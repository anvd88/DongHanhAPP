use crate::{
    auth::AuthSettings,
    db::{DatabaseSettings, parse_database_source},
};
use anyhow::{Context, Result};
use http::Uri;
use std::{env, net::SocketAddr, time::Duration};

pub struct Settings {
    pub bind: SocketAddr,
    pub database: DatabaseSettings,
    pub auth: AuthSettings,
    pub compat_upstream: Option<Uri>,
}

impl Settings {
    pub fn from_env() -> Result<Self> {
        let bind = env::var("KETOANMINI_RUST_BIND")
            .unwrap_or_else(|_| "127.0.0.1:5240".to_owned())
            .parse()
            .context("KETOANMINI_RUST_BIND must be an IP:port pair")?;

        let source = env::var("KETOANMINI_DATABASE_URL")
            .or_else(|_| env::var("DATABASE_URL"))
            .or_else(|_| env::var("ConnectionStrings__KetoanMini"))
            .context("set KETOANMINI_DATABASE_URL or reuse ConnectionStrings__KetoanMini")?;
        let max_connections = parse_env("KETOANMINI_DB_MAX_CONNECTIONS", 20_u32)?;
        let min_connections = parse_env("KETOANMINI_DB_MIN_CONNECTIONS", 0_u32)?;
        if min_connections > max_connections {
            anyhow::bail!("KETOANMINI_DB_MIN_CONNECTIONS cannot exceed the maximum");
        }
        let acquire_timeout_ms = parse_env("KETOANMINI_DB_ACQUIRE_TIMEOUT_MS", 3_000_u64)?;
        let database = DatabaseSettings {
            options: parse_database_source(&source)?,
            min_connections,
            max_connections,
            acquire_timeout: Duration::from_millis(acquire_timeout_ms),
        };

        let jwt_key = env::var("KETOANMINI_JWT_KEY")
            .or_else(|_| env::var("Jwt__Key"))
            .context("set Jwt__Key (or KETOANMINI_JWT_KEY) for the Rust process")?;
        let insecure_default =
            "doi-chuoi-bi-mat-nay-thanh-mot-gia-tri-ngau-nhien-dai-it-nhat-32-ky-tu";
        if jwt_key.len() < 32 || jwt_key == insecure_default {
            anyhow::bail!("JWT signing key is missing, too short, or still the insecure default");
        }
        let auth = AuthSettings {
            jwt_key: jwt_key.into_bytes(),
            issuer: env::var("Jwt__Issuer").unwrap_or_else(|_| "KetoanMini.Web".to_owned()),
            audience: env::var("Jwt__Audience").unwrap_or_else(|_| "KetoanMini.Web".to_owned()),
            web_expire_hours: parse_env("Jwt__WebExpireHours", 168_i64)?,
            session_idle_days: parse_env("Security__SessionIdleDays", 7_i32)?,
            cookie_auth: parse_bool_env("Security__CookieAuth", true)?,
        };

        let compat_upstream = match env::var("KETOANMINI_COMPAT_UPSTREAM") {
            Ok(raw) if !raw.trim().is_empty() => Some(
                raw.parse()
                    .context("KETOANMINI_COMPAT_UPSTREAM must be an absolute HTTP URL")?,
            ),
            Ok(_) | Err(env::VarError::NotPresent) => None,
            Err(error) => return Err(error).context("cannot read KETOANMINI_COMPAT_UPSTREAM"),
        };

        Ok(Self {
            bind,
            database,
            auth,
            compat_upstream,
        })
    }
}

fn parse_bool_env(name: &str, default: bool) -> Result<bool> {
    match env::var(name) {
        Ok(raw) => match raw.trim().to_ascii_lowercase().as_str() {
            "1" | "true" | "yes" | "on" => Ok(true),
            "0" | "false" | "no" | "off" => Ok(false),
            _ => anyhow::bail!("{name} has an invalid boolean value"),
        },
        Err(env::VarError::NotPresent) => Ok(default),
        Err(error) => Err(error).with_context(|| format!("cannot read {name}")),
    }
}

fn parse_env<T>(name: &str, default: T) -> Result<T>
where
    T: std::str::FromStr,
    T::Err: std::error::Error + Send + Sync + 'static,
{
    match env::var(name) {
        Ok(raw) => raw
            .parse()
            .with_context(|| format!("{name} has an invalid value")),
        Err(env::VarError::NotPresent) => Ok(default),
        Err(error) => Err(error).with_context(|| format!("cannot read {name}")),
    }
}
