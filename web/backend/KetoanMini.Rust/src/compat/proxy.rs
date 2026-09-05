use crate::{network, state::AppState};
use anyhow::{Context, Result, bail};
use axum::{
    body::Body,
    extract::{ConnectInfo, State},
    http::{HeaderMap, HeaderName, HeaderValue, Request, Response, StatusCode, Uri, header},
    response::IntoResponse,
};
use hyper_util::{
    client::legacy::{Client, connect::HttpConnector},
    rt::TokioExecutor,
};
use serde_json::json;
use std::{net::SocketAddr, sync::Arc, time::Duration};

#[derive(Clone)]
pub struct CompatProxy {
    base: Uri,
    client: Client<HttpConnector, Body>,
}

impl CompatProxy {
    pub fn new(base: Uri) -> Result<Self> {
        if base.scheme_str() != Some("http") {
            bail!("compatibility upstream must use HTTP on the local machine");
        }
        let authority = base
            .authority()
            .context("compatibility upstream is missing host and port")?;
        let host = authority.host();
        let host_is_loopback = host
            .parse::<std::net::IpAddr>()
            .is_ok_and(|ip| ip.is_loopback());
        if host != "localhost" && !host_is_loopback {
            bail!("compatibility upstream must resolve explicitly to loopback");
        }
        if base.path() != "/" && !base.path().is_empty() {
            bail!("compatibility upstream must not contain a path");
        }

        let mut connector = HttpConnector::new();
        connector.enforce_http(true);
        connector.set_connect_timeout(Some(Duration::from_secs(3)));
        let client = Client::builder(TokioExecutor::new())
            .pool_idle_timeout(Duration::from_secs(90))
            .pool_max_idle_per_host(64)
            .build(connector);

        Ok(Self { base, client })
    }

    async fn forward(
        &self,
        peer: SocketAddr,
        mut request: Request<Body>,
    ) -> Result<Response<Body>> {
        rewrite_request(&self.base, peer, &mut request)?;
        let mut response = self
            .client
            .request(request)
            .await
            .context("compatibility upstream request failed")?;

        strip_hop_by_hop(response.headers_mut());

        let (parts, body) = response.into_parts();
        Ok(Response::from_parts(parts, Body::new(body)))
    }
}

pub async fn fallback(
    State(state): State<Arc<AppState>>,
    ConnectInfo(peer): ConnectInfo<SocketAddr>,
    request: Request<Body>,
) -> Response<Body> {
    let method = request.method().clone();
    let Some(proxy) = &state.compat else {
        return if request.uri().path().starts_with("/api/") {
            (
                StatusCode::NOT_IMPLEMENTED,
                axum::Json(json!({
                    "message": "API này chưa được chuyển sang tiến trình Rust."
                })),
            )
                .into_response()
        } else {
            StatusCode::NOT_FOUND.into_response()
        };
    };

    match proxy.forward(peer, request).await {
        Ok(response) => response,
        Err(error) => {
            // Deliberately omit URI/query/headers because bearer tokens and cookies are secret.
            tracing::error!(%method, %error, "compatibility upstream unavailable");
            (
                StatusCode::BAD_GATEWAY,
                axum::Json(json!({
                    "message": "Backend tương thích tạm thời không sẵn sàng."
                })),
            )
                .into_response()
        }
    }
}

fn rewrite_request(base: &Uri, peer: SocketAddr, request: &mut Request<Body>) -> Result<()> {
    let path_and_query = request
        .uri()
        .path_and_query()
        .map(|value| value.as_str())
        .unwrap_or("/");
    let authority = base.authority().context("upstream authority missing")?;
    *request.uri_mut() = Uri::builder()
        .scheme(base.scheme().expect("validated upstream scheme").clone())
        .authority(authority.clone())
        .path_and_query(path_and_query)
        .build()
        .context("cannot build compatibility upstream URI")?;

    strip_hop_by_hop(request.headers_mut());
    let original_host = request.headers().get(header::HOST).cloned();
    request.headers_mut().insert(
        header::HOST,
        HeaderValue::from_str(authority.as_str()).context("invalid upstream authority")?,
    );
    if let Some(original_host) = original_host {
        request
            .headers_mut()
            .insert(HeaderName::from_static("x-forwarded-host"), original_host);
    }
    let forwarded_for = network::forwarded_for(peer.ip(), request.headers());
    let forwarded_proto = network::forwarded_proto(peer.ip(), request.headers());
    request.headers_mut().insert(
        HeaderName::from_static("x-forwarded-for"),
        HeaderValue::from_str(&forwarded_for).context("invalid forwarded client address")?,
    );
    request.headers_mut().insert(
        HeaderName::from_static("x-forwarded-proto"),
        HeaderValue::from_static(forwarded_proto),
    );
    request.headers_mut().remove(header::FORWARDED);
    Ok(())
}

fn strip_hop_by_hop(headers: &mut HeaderMap) {
    let connection_tokens: Vec<HeaderName> = headers
        .get_all(header::CONNECTION)
        .iter()
        .filter_map(|value| value.to_str().ok())
        .flat_map(|value| value.split(','))
        .filter_map(|name| HeaderName::from_bytes(name.trim().as_bytes()).ok())
        .collect();
    for name in connection_tokens {
        headers.remove(name);
    }

    for name in [
        HeaderName::from_static("keep-alive"),
        header::PROXY_AUTHENTICATE,
        header::PROXY_AUTHORIZATION,
        header::TE,
        header::TRAILER,
        header::TRANSFER_ENCODING,
    ] {
        headers.remove(name);
    }
    headers.remove("proxy-connection");
    headers.remove(header::CONNECTION);
    headers.remove(header::UPGRADE);
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::{Json, Router, body::Bytes};
    use http_body_util::BodyExt;
    use serde_json::Value;
    use std::convert::Infallible;
    use tokio_stream::wrappers::ReceiverStream;

    async fn echo(request: Request<Body>) -> Response<Body> {
        let (parts, body) = request.into_parts();
        let body = body.collect().await.unwrap().to_bytes();
        let value = json!({
            "method": parts.method.as_str(),
            "pathAndQuery": parts.uri.path_and_query().unwrap().as_str(),
            "authorization": header_text(&parts.headers, header::AUTHORIZATION),
            "cookie": header_text(&parts.headers, header::COOKIE),
            "csrf": header_text(&parts.headers, HeaderName::from_static("x-csrf-token")),
            "forwardedFor": header_text(&parts.headers, HeaderName::from_static("x-forwarded-for")),
            "forwardedProto": header_text(&parts.headers, HeaderName::from_static("x-forwarded-proto")),
            "forwardedHost": header_text(&parts.headers, HeaderName::from_static("x-forwarded-host")),
            "body": String::from_utf8_lossy(&body),
        });
        let mut response = Json(value).into_response();
        response.headers_mut().append(
            header::SET_COOKIE,
            HeaderValue::from_static("km_auth=renewed; Path=/; HttpOnly; SameSite=Lax"),
        );
        response
    }

    fn header_text(headers: &HeaderMap, name: HeaderName) -> Option<&str> {
        headers.get(name).and_then(|value| value.to_str().ok())
    }

    async fn spawn_upstream() -> (Uri, tokio::task::JoinHandle<()>) {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        let handle = tokio::spawn(async move {
            axum::serve(listener, Router::new().fallback(echo))
                .await
                .unwrap();
        });
        (format!("http://{address}").parse().unwrap(), handle)
    }

    async fn sse() -> Response<Body> {
        let (sender, receiver) = tokio::sync::mpsc::channel::<Result<Bytes, Infallible>>(2);
        tokio::spawn(async move {
            sender
                .send(Ok(Bytes::from_static(
                    b"id: 1\nevent: invalidated\ndata: {}\n\n",
                )))
                .await
                .unwrap();
            tokio::time::sleep(Duration::from_millis(400)).await;
            sender
                .send(Ok(Bytes::from_static(b": heartbeat\n\n")))
                .await
                .unwrap();
        });
        Response::builder()
            .header(header::CONTENT_TYPE, "text/event-stream")
            .header("x-accel-buffering", "no")
            .body(Body::from_stream(ReceiverStream::new(receiver)))
            .unwrap()
    }

    #[tokio::test]
    async fn forwards_sse_chunks_without_buffering_and_preserves_cursor_header() {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        let handle = tokio::spawn(async move {
            axum::serve(
                listener,
                Router::new().route("/api/realtime/stream", axum::routing::get(sse)),
            )
            .await
            .unwrap();
        });
        let proxy = CompatProxy::new(format!("http://{address}").parse().unwrap()).unwrap();
        let request = Request::builder()
            .uri("/api/realtime/stream?after=0")
            .header("last-event-id", "0")
            .header(header::AUTHORIZATION, "Bearer token")
            .body(Body::empty())
            .unwrap();
        let response = proxy
            .forward("127.0.0.1:40000".parse().unwrap(), request)
            .await
            .unwrap();
        assert_eq!(
            response.headers()[header::CONTENT_TYPE],
            "text/event-stream"
        );
        let mut body = response.into_body();
        let first = tokio::time::timeout(Duration::from_millis(150), body.frame())
            .await
            .expect("proxy buffered the first SSE chunk")
            .unwrap()
            .unwrap();
        assert!(first.into_data().unwrap().starts_with(b"id: 1"));
        let second = tokio::time::timeout(Duration::from_secs(1), body.frame())
            .await
            .unwrap()
            .unwrap()
            .unwrap()
            .into_data()
            .unwrap();
        assert!(second.starts_with(b": heartbeat"));
        handle.abort();
    }

    #[tokio::test]
    async fn streams_android_and_web_credentials_without_trusting_spoofed_proxy_headers() {
        let (upstream, handle) = spawn_upstream().await;
        let proxy = CompatProxy::new(upstream).unwrap();
        let request = Request::builder()
            .method("POST")
            .uri("/api/auth/verify-password?opaque=value")
            .header(header::HOST, "rust.example.test")
            .header(header::AUTHORIZATION, "Bearer android-token")
            .header(header::COOKIE, "km_auth=web-token; km_csrf=csrf-value")
            .header("x-csrf-token", "csrf-value")
            .header("x-forwarded-for", "203.0.113.55")
            .header("x-forwarded-proto", "https")
            .body(Body::from(Bytes::from_static(
                br#"{"password":"not-logged"}"#,
            )))
            .unwrap();

        let response = proxy
            .forward("192.168.1.25:43210".parse().unwrap(), request)
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(
            response.headers().get(header::SET_COOKIE).unwrap(),
            "km_auth=renewed; Path=/; HttpOnly; SameSite=Lax"
        );
        let body = response.into_body().collect().await.unwrap().to_bytes();
        let value: Value = serde_json::from_slice(&body).unwrap();
        assert_eq!(value["method"], "POST");
        assert_eq!(
            value["pathAndQuery"],
            "/api/auth/verify-password?opaque=value"
        );
        assert_eq!(value["authorization"], "Bearer android-token");
        assert_eq!(value["cookie"], "km_auth=web-token; km_csrf=csrf-value");
        assert_eq!(value["csrf"], "csrf-value");
        assert_eq!(value["forwardedFor"], "192.168.1.25");
        assert_eq!(value["forwardedProto"], "http");
        assert_eq!(value["forwardedHost"], "rust.example.test");
        assert_eq!(value["body"], r#"{"password":"not-logged"}"#);

        handle.abort();
    }

    #[test]
    fn refuses_remote_or_path_scoped_upstreams() {
        assert!(CompatProxy::new("http://example.com:5239".parse().unwrap()).is_err());
        assert!(CompatProxy::new("https://127.0.0.1:5443".parse().unwrap()).is_err());
        assert!(CompatProxy::new("http://127.0.0.1:5239/api".parse().unwrap()).is_err());
    }
}
