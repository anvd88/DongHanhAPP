use crate::{network, state::AppState};
use axum::{
    Json, Router,
    extract::{ConnectInfo, State},
    http::{HeaderMap, StatusCode},
    response::{IntoResponse, Response},
    routing::get,
};
use serde::Serialize;
use std::{net::SocketAddr, sync::Arc};

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route("/api/info", get(info))
        .route("/api/health", get(health))
}

#[derive(Serialize)]
struct InfoResponse {
    app: &'static str,
    status: &'static str,
}

#[derive(Serialize)]
struct HealthResponse {
    db: &'static str,
}

async fn info() -> Json<InfoResponse> {
    Json(InfoResponse {
        app: "KetoanMini Web API",
        status: "ok",
    })
}

async fn health(
    State(state): State<Arc<AppState>>,
    ConnectInfo(peer): ConnectInfo<SocketAddr>,
    headers: HeaderMap,
) -> Response {
    let client_ip = network::client_ip(peer.ip(), &headers);
    if !network::is_internal(client_ip) {
        return StatusCode::NOT_FOUND.into_response();
    }

    match sqlx::query_scalar::<_, i32>("SELECT 1")
        .fetch_one(&state.pool)
        .await
    {
        Ok(1) => Json(HealthResponse { db: "connected" }).into_response(),
        Ok(_) | Err(_) => (
            StatusCode::SERVICE_UNAVAILABLE,
            Json(HealthResponse { db: "error" }),
        )
            .into_response(),
    }
}
