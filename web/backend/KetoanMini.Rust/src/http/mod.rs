mod routes;

use crate::{auth, compat, state::AppState};
use axum::{
    Router,
    body::Body,
    http::{HeaderName, HeaderValue, Request, header},
    middleware,
    response::Response,
};
use std::sync::Arc;
use tower_http::{
    catch_panic::CatchPanicLayer,
    set_header::SetResponseHeaderLayer,
    trace::{DefaultOnResponse, TraceLayer},
};
use tracing::Level;

pub fn build_router(state: Arc<AppState>) -> Router {
    let protected = Router::new()
        .merge(routes::app_config::router())
        .merge(routes::auth::router())
        .merge(routes::bank_accounts::router())
        .merge(routes::directory::router())
        .merge(routes::gia_cong::router())
        .merge(routes::help::router())
        .merge(routes::notifications::router())
        .merge(routes::penalty_refunds::router())
        .merge(routes::portal::router())
        .merge(routes::preferences::router())
        .merge(routes::schedule::router())
        .merge(routes::surveys::router())
        .merge(routes::talent::router())
        .merge(routes::worklist::router())
        .route_layer(middleware::from_fn_with_state(
            state.clone(),
            auth::require_auth,
        ));

    Router::new()
        .merge(routes::system::router())
        .merge(protected)
        .fallback(compat::fallback)
        .with_state(state)
        .layer(SetResponseHeaderLayer::if_not_present(
            header::X_CONTENT_TYPE_OPTIONS,
            HeaderValue::from_static("nosniff"),
        ))
        .layer(SetResponseHeaderLayer::if_not_present(
            header::X_FRAME_OPTIONS,
            HeaderValue::from_static("DENY"),
        ))
        .layer(SetResponseHeaderLayer::if_not_present(
            header::REFERRER_POLICY,
            HeaderValue::from_static("strict-origin-when-cross-origin"),
        ))
        .layer(SetResponseHeaderLayer::if_not_present(
            HeaderName::from_static("content-security-policy"),
            HeaderValue::from_static(
                "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; img-src 'self' data: blob: https:; media-src 'self' blob: https:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'wasm-unsafe-eval'; worker-src 'self' blob:; frame-src 'self' blob:; connect-src 'self' https: wss:; form-action 'self'",
            ),
        ))
        .layer(CatchPanicLayer::new())
        .layer(middleware::from_fn(dotnet_json_content_type))
        .layer(
            TraceLayer::new_for_http()
                // Default request tracing includes URI/query. Do not log it because hub query tokens
                // and opaque QR values can be credentials.
                .make_span_with(|request: &axum::http::Request<_>| {
                    tracing::span!(Level::INFO, "http.request", method = %request.method())
                })
                .on_response(DefaultOnResponse::new().level(Level::INFO)),
        )
}

async fn dotnet_json_content_type(request: Request<Body>, next: middleware::Next) -> Response {
    let mut response = next.run(request).await;
    if response
        .headers()
        .get(header::CONTENT_TYPE)
        .is_some_and(|value| value == "application/json")
    {
        response.headers_mut().insert(
            header::CONTENT_TYPE,
            HeaderValue::from_static("application/json; charset=utf-8"),
        );
    }
    response
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::auth::{AuthService, AuthSettings};
    use axum::{
        body::Body,
        extract::ConnectInfo,
        http::{Request, StatusCode},
    };
    use http_body_util::BodyExt;
    use sqlx::postgres::PgPoolOptions;
    use std::net::SocketAddr;
    use tower::ServiceExt;

    fn state() -> Arc<AppState> {
        let pool = PgPoolOptions::new()
            .max_connections(1)
            .connect_lazy("postgres://postgres@127.0.0.1:1/ketoanmini")
            .unwrap();
        let auth = AuthService::new(AuthSettings {
            jwt_key: b"test-only-key-with-at-least-thirty-two-bytes".to_vec(),
            issuer: "KetoanMini.Web".to_owned(),
            audience: "KetoanMini.Web".to_owned(),
            web_expire_hours: 168,
            session_idle_days: 7,
            cookie_auth: true,
        })
        .unwrap();
        Arc::new(AppState::new(pool, None, auth))
    }

    #[tokio::test]
    async fn info_is_contract_compatible_and_has_security_headers() {
        let response = build_router(state())
            .oneshot(
                Request::builder()
                    .uri("/api/info")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(
            response.headers()[header::X_CONTENT_TYPE_OPTIONS],
            "nosniff"
        );
        assert_eq!(response.headers()[header::X_FRAME_OPTIONS], "DENY");
        assert_eq!(
            response.headers()[header::CONTENT_TYPE],
            "application/json; charset=utf-8"
        );
        assert!(response.headers().contains_key("content-security-policy"));
        let body = response.into_body().collect().await.unwrap().to_bytes();
        let value: serde_json::Value = serde_json::from_slice(&body).unwrap();
        assert_eq!(
            value,
            serde_json::json!({"app": "KetoanMini Web API", "status": "ok"})
        );
    }

    #[tokio::test]
    async fn public_health_is_hidden_before_touching_the_database() {
        let response = build_router(state())
            .oneshot(
                Request::builder()
                    .uri("/api/health")
                    .extension(ConnectInfo(
                        "203.0.113.10:12345".parse::<SocketAddr>().unwrap(),
                    ))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::NOT_FOUND);
    }

    #[tokio::test]
    async fn unported_api_is_explicit_without_compatibility_mode() {
        let response = build_router(state())
            .oneshot(
                Request::builder()
                    .uri("/api/not-ported")
                    .extension(ConnectInfo(
                        "127.0.0.1:12345".parse::<SocketAddr>().unwrap(),
                    ))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::NOT_IMPLEMENTED);
    }

    #[tokio::test]
    async fn native_routes_require_authentication_before_database_access() {
        let response = build_router(state())
            .oneshot(
                Request::builder()
                    .uri("/api/preferences")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
    }

    #[tokio::test]
    async fn unsafe_cookie_request_requires_csrf_before_token_or_database_access() {
        let response = build_router(state())
            .oneshot(
                Request::builder()
                    .method("PUT")
                    .uri("/api/preferences")
                    .header(header::COOKIE, "km_auth=invalid")
                    .header(header::CONTENT_TYPE, "application/json")
                    .body(Body::from("{}"))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::FORBIDDEN);
        let body = response.into_body().collect().await.unwrap().to_bytes();
        let value: serde_json::Value = serde_json::from_slice(&body).unwrap();
        assert_eq!(value["code"], "csrf_required");
    }
}
