#![forbid(unsafe_code)]

pub mod auth;
pub mod compat;
pub mod config;
pub mod db;
pub mod http;
pub mod network;
pub mod schema;
pub mod state;

pub use http::build_router;
pub use state::AppState;
