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
    rt::{TokioExecutor, TokioIo},
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
        let is_upgrade = wants_upgrade(request.headers());
        let downstream_upgrade = is_upgrade.then(|| hyper::upgrade::on(&mut request));

        rewrite_request(&self.base, peer, &mut request)?;
        let mut response = self
            .client
            .request(request)
            .await
            .context("compatibility upstream request failed")?;

        if is_upgrade && response.status() == StatusCode::SWITCHING_PROTOCOLS {
            let upstream_upgrade = hyper::upgrade::on(&mut response);
            let downstream_upgrade = downstream_upgrade.expect("upgrade future must exist");
            tokio::spawn(async move {
                let Ok((downstream, upstream)) =
                    tokio::try_join!(downstream_upgrade, upstream_upgrade)
                else {
                    tracing::warn!("compatibility WebSocket upgrade failed");
                    return;
                };
                let mut downstream = TokioIo::new(downstream);
                let mut upstream = TokioIo::new(upstream);
                if let Err(error) =
                    tokio::io::copy_bidirectional(&mut downstream, &mut upstream).await
                {
                    tracing::debug!(%error, "compatibility WebSocket tunnel closed");
                }
            });
        } else {
            strip_hop_by_hop(response.headers_mut(), false);
        }

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
        return if request.uri().path().starts_with("/api/")
            || request.uri().path().starts_with("/hubs/")
        {
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
            // Deliberately omit URI/query/headers: SignalR query tokens and bearer/cookies are secret.
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

    let is_upgrade = wants_upgrade(request.headers());
    strip_hop_by_hop(request.headers_mut(), is_upgrade);
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

fn wants_upgrade(headers: &HeaderMap) -> bool {
    headers.contains_key(header::UPGRADE)
        && headers
            .get_all(header::CONNECTION)
            .iter()
            .filter_map(|value| value.to_str().ok())
            .flat_map(|value| value.split(','))
            .any(|token| token.trim().eq_ignore_ascii_case("upgrade"))
}

fn strip_hop_by_hop(headers: &mut HeaderMap, keep_upgrade: bool) {
    let connection_tokens: Vec<HeaderName> = headers
        .get_all(header::CONNECTION)
        .iter()
        .filter_map(|value| value.to_str().ok())
        .flat_map(|value| value.split(','))
        .filter_map(|name| HeaderName::from_bytes(name.trim().as_bytes()).ok())
        .collect();
    for name in connection_tokens {
        if !(keep_upgrade && name == header::UPGRADE) {
            headers.remove(name);
        }
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
    if !keep_upgrade {
        headers.remove(header::CONNECTION);
        headers.remove(header::UPGRADE);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::{Json, Router, body::Bytes};
    use http_body_util::BodyExt;
    use serde_json::Value;

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
