use axum::{
    body::Body,
    extract::ConnectInfo,
    http::{Method, Request, StatusCode},
};
use ketoanmini_server::{
    AppState,
    auth::{AuthService, AuthSettings},
    build_router,
};
use serde_json::Value;
use sqlx::postgres::PgPoolOptions;
use std::{collections::BTreeSet, net::SocketAddr, sync::Arc};
use tower::ServiceExt;

const OPENAPI_BASELINE: &str = include_str!("../../../docs/openapi.baseline.json");

/// Operations intentionally owned by Rust. Everything else in the OpenAPI baseline must reach the
/// compatibility fallback, otherwise adding one Axum path can accidentally turn a still-.NET verb
/// on the same path into `405 Method Not Allowed` instead of proxying it.
const NATIVE_OPERATIONS: &[(&str, &str)] = &[
    ("GET", "/api/app-config"),
    ("PUT", "/api/app-config"),
    ("GET", "/api/auth/access-profile"),
    ("GET", "/api/auth/me"),
    ("GET", "/api/bank-accounts"),
    ("POST", "/api/bank-accounts"),
    ("DELETE", "/api/bank-accounts/{id}"),
    ("PUT", "/api/bank-accounts/{id}"),
    ("POST", "/api/bank-accounts/{id}/default"),
    ("GET", "/api/bank-accounts/banks"),
    ("GET", "/api/directory"),
    ("GET", "/api/directory/org-chart"),
    ("GET", "/api/giacong"),
    ("POST", "/api/giacong"),
    ("DELETE", "/api/giacong/{id}"),
    ("GET", "/api/giacong/{id}"),
    ("PUT", "/api/giacong/{id}"),
    ("GET", "/api/giacong/report"),
    ("GET", "/api/health"),
    ("GET", "/api/help/faqs"),
    ("POST", "/api/help/faqs"),
    ("DELETE", "/api/help/faqs/{id}"),
    ("PUT", "/api/help/faqs/{id}"),
    ("GET", "/api/help/status"),
    ("GET", "/api/info"),
    ("GET", "/api/notifications"),
    ("DELETE", "/api/notifications/{id}"),
    ("POST", "/api/notifications/{id}/read"),
    ("DELETE", "/api/notifications/read"),
    ("POST", "/api/notifications/read-all"),
    ("POST", "/api/notifications/register-token"),
    ("POST", "/api/notifications/unregister-token"),
    ("GET", "/api/penalty-refunds"),
    ("POST", "/api/penalty-refunds/{id}/approve"),
    ("POST", "/api/penalty-refunds/{id}/mark-paid"),
    ("POST", "/api/penalty-refunds/{id}/reject"),
    ("GET", "/api/penalties/types"),
    ("GET", "/api/portal/about"),
    ("PUT", "/api/portal/about"),
    ("GET", "/api/portal/feed"),
    ("GET", "/api/portal/posts"),
    ("POST", "/api/portal/posts"),
    ("DELETE", "/api/portal/posts/{id}"),
    ("PUT", "/api/portal/posts/{id}"),
    ("GET", "/api/preferences"),
    ("PUT", "/api/preferences"),
    ("GET", "/api/preferences/notifications"),
    ("PUT", "/api/preferences/notifications"),
    ("GET", "/api/roles/catalog"),
    ("GET", "/api/schedule/ical"),
    ("GET", "/api/surveys"),
    ("POST", "/api/surveys"),
    ("DELETE", "/api/surveys/{id}"),
    ("GET", "/api/surveys/{id}"),
    ("POST", "/api/surveys/{id}/close"),
    ("POST", "/api/surveys/{id}/respond"),
    ("GET", "/api/surveys/{id}/results"),
    ("GET", "/api/surveys/active"),
    ("GET", "/api/talent/benefits"),
    ("GET", "/api/talent/onboarding"),
    ("POST", "/api/talent/onboarding/{id}/complete"),
    ("GET", "/api/talent/performance"),
    ("PUT", "/api/talent/performance/goals/{id}"),
    ("PUT", "/api/talent/performance/reviews/{id}/self"),
    ("GET", "/api/talent/training"),
    ("PUT", "/api/talent/training/{id}/progress"),
    ("POST", "/api/talent/training/{id}/quiz"),
    ("GET", "/api/worklist"),
];

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

fn operations() -> Vec<(String, String)> {
    let document: Value = serde_json::from_str(OPENAPI_BASELINE).unwrap();
    let mut operations = Vec::new();
    for (path, item) in document["paths"].as_object().unwrap() {
        for method in [
            "get", "post", "put", "delete", "patch", "head", "options", "trace",
        ] {
            if item.get(method).is_some() {
                operations.push((method.to_ascii_uppercase(), path.clone()));
            }
        }
    }
    operations.sort();
    operations
}

fn concrete_path(template: &str) -> String {
    let mut result = template.to_owned();
    while let Some(open) = result.find('{') {
        let close = result[open..]
            .find('}')
            .map(|offset| open + offset)
            .unwrap();
        result.replace_range(open..=close, "1");
    }
    result
}

#[tokio::test]
async fn axum_native_surface_matches_openapi_and_every_other_operation_still_proxies() {
    let expected = NATIVE_OPERATIONS
        .iter()
        .map(|(method, path)| ((*method).to_owned(), (*path).to_owned()))
        .collect::<BTreeSet<_>>();
    assert_eq!(
        expected.len(),
        NATIVE_OPERATIONS.len(),
        "duplicate native contract"
    );

    let baseline = operations();
    let baseline_set = baseline.iter().cloned().collect::<BTreeSet<_>>();
    let missing_from_openapi = expected.difference(&baseline_set).collect::<Vec<_>>();
    assert!(
        missing_from_openapi.is_empty(),
        "native contracts absent from OpenAPI: {missing_from_openapi:?}"
    );

    let router = build_router(state());
    let mut actual_native = BTreeSet::new();
    let mut blocked_compatibility = Vec::new();
    for (method, template) in baseline {
        let response = router
            .clone()
            .oneshot(
                Request::builder()
                    .method(Method::from_bytes(method.as_bytes()).unwrap())
                    .uri(concrete_path(&template))
                    .extension(ConnectInfo(
                        "127.0.0.1:40000".parse::<SocketAddr>().unwrap(),
                    ))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();

        let operation = (method, template);
        if response.status() == StatusCode::NOT_IMPLEMENTED {
            continue;
        }
        if expected.contains(&operation) {
            actual_native.insert(operation);
        } else {
            blocked_compatibility.push((operation, response.status()));
        }
    }

    assert!(
        blocked_compatibility.is_empty(),
        "unported OpenAPI operations no longer reach compatibility fallback: {blocked_compatibility:?}"
    );
    assert_eq!(actual_native, expected);
    assert_eq!(actual_native.len(), 68);
}
