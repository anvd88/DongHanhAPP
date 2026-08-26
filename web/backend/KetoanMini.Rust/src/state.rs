use crate::{auth::AuthService, compat::CompatProxy};
use sqlx::PgPool;
use std::time::Instant;

pub struct AppState {
    pub pool: PgPool,
    pub compat: Option<CompatProxy>,
    pub auth: AuthService,
    pub started_at: Instant,
}

impl AppState {
    pub fn new(pool: PgPool, compat: Option<CompatProxy>, auth: AuthService) -> Self {
        Self {
            pool,
            compat,
            auth,
            started_at: Instant::now(),
        }
    }
}
