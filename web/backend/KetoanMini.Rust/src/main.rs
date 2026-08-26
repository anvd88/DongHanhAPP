#![forbid(unsafe_code)]

use anyhow::Context;
use ketoanmini_server::{
    AppState, auth::AuthService, build_router, compat::CompatProxy, config::Settings, db,
};
use std::sync::Arc;
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    init_tracing();

    let settings = Settings::from_env()?;
    let pool = db::create_pool(&settings.database);
    ketoanmini_server::schema::check_compatibility(&pool).await?;
    let auth = AuthService::new(settings.auth)?;
    let compat = settings
        .compat_upstream
        .clone()
        .map(CompatProxy::new)
        .transpose()?;
    let state = Arc::new(AppState::new(pool, compat, auth));

    let listener = tokio::net::TcpListener::bind(settings.bind)
        .await
        .with_context(|| format!("cannot bind KetoanMini Rust server to {}", settings.bind))?;

    tracing::info!(
        bind = %settings.bind,
        compatibility_mode = state.compat.is_some(),
        "KetoanMini Rust process is listening"
    );

    axum::serve(
        listener,
        build_router(state).into_make_service_with_connect_info::<std::net::SocketAddr>(),
    )
    .with_graceful_shutdown(shutdown_signal())
    .await
    .context("KetoanMini Rust HTTP server stopped unexpectedly")?;

    Ok(())
}

fn init_tracing() {
    tracing_subscriber::registry()
        .with(
            tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| {
                "ketoanmini_server=info,tower_http=info,hyper_util=warn".into()
            }),
        )
        .with(tracing_subscriber::fmt::layer())
        .init();
}

async fn shutdown_signal() {
    if let Err(error) = tokio::signal::ctrl_c().await {
        tracing::error!(%error, "cannot install Ctrl+C handler");
    }
    tracing::info!("graceful shutdown requested");
}
