mod claims;
mod middleware;
pub mod password;
pub mod permissions;
pub mod roles;

pub use claims::{AuthService, AuthSettings, TokenIdentity, TokenSource};
pub use middleware::{AuthContext, require_auth};

pub const AUTH_COOKIE: &str = "km_auth";
pub const CSRF_COOKIE: &str = "km_csrf";
pub const CSRF_HEADER: &str = "x-csrf-token";
