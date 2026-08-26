use anyhow::{Context, Result, bail};
use sqlx::{
    PgPool,
    postgres::{PgConnectOptions, PgPoolOptions, PgSslMode},
};
use std::{collections::HashMap, str::FromStr, time::Duration};

pub struct DatabaseSettings {
    pub options: PgConnectOptions,
    pub min_connections: u32,
    pub max_connections: u32,
    pub acquire_timeout: Duration,
}

pub fn create_pool(settings: &DatabaseSettings) -> PgPool {
    PgPoolOptions::new()
        .min_connections(settings.min_connections)
        .max_connections(settings.max_connections)
        .acquire_timeout(settings.acquire_timeout)
        .idle_timeout(Some(Duration::from_secs(10 * 60)))
        .max_lifetime(Some(Duration::from_secs(30 * 60)))
        .connect_lazy_with(settings.options.clone())
}

pub fn parse_database_source(raw: &str) -> Result<PgConnectOptions> {
    let trimmed = raw.trim();
    if trimmed.starts_with("postgres://") || trimmed.starts_with("postgresql://") {
        return PgConnectOptions::from_str(trimmed)
            .map_err(|_| anyhow::anyhow!("invalid PostgreSQL URL"));
    }
    options_from_npgsql(trimmed)
}

fn options_from_npgsql(raw: &str) -> Result<PgConnectOptions> {
    let values = parse_ado_pairs(raw)?;
    let get = |keys: &[&str]| {
        keys.iter()
            .find_map(|key| values.get(&normalize_key(key)).map(String::as_str))
    };

    let host = get(&["host", "server"]).context("Npgsql connection string is missing Host")?;
    let database = get(&["database", "initial catalog"])
        .context("Npgsql connection string is missing Database")?;
    let username = get(&["username", "user id", "userid"])
        .context("Npgsql connection string is missing Username")?;
    let password = get(&["password"]);
    let port = get(&["port"])
        .map(str::parse)
        .transpose()
        .context("Npgsql Port is invalid")?
        .unwrap_or(5432);
    let ssl_mode = match get(&["ssl mode", "sslmode"])
        .unwrap_or("prefer")
        .trim()
        .to_ascii_lowercase()
        .replace(' ', "")
        .as_str()
    {
        "disable" => PgSslMode::Disable,
        "allow" => PgSslMode::Allow,
        "prefer" => PgSslMode::Prefer,
        "require" => PgSslMode::Require,
        "verifyca" => PgSslMode::VerifyCa,
        "verifyfull" => PgSslMode::VerifyFull,
        mode => bail!("unsupported Npgsql SSL Mode '{mode}'"),
    };

    let mut options = PgConnectOptions::new()
        .host(host)
        .port(port)
        .database(database)
        .username(username)
        .ssl_mode(ssl_mode)
        .application_name("KetoanMini.Rust");
    if let Some(password) = password {
        options = options.password(password);
    }
    Ok(options)
}

fn parse_ado_pairs(raw: &str) -> Result<HashMap<String, String>> {
    let mut pairs = HashMap::new();
    let mut segment = String::new();
    let mut quote = None;
    let mut chars = raw.chars().peekable();

    while let Some(ch) = chars.next() {
        match quote {
            Some(active) if ch == active => {
                if chars.peek() == Some(&active) {
                    segment.push(ch);
                    chars.next();
                } else {
                    quote = None;
                }
            }
            Some(_) => segment.push(ch),
            None if ch == '\'' || ch == '"' => quote = Some(ch),
            None if ch == ';' => {
                insert_pair(&mut pairs, &segment)?;
                segment.clear();
            }
            None => segment.push(ch),
        }
    }
    if quote.is_some() {
        bail!("unterminated quote in Npgsql connection string");
    }
    insert_pair(&mut pairs, &segment)?;
    Ok(pairs)
}

fn insert_pair(target: &mut HashMap<String, String>, segment: &str) -> Result<()> {
    if segment.trim().is_empty() {
        return Ok(());
    }
    let (key, value) = segment
        .split_once('=')
        .context("invalid Npgsql connection-string segment")?;
    target.insert(normalize_key(key), value.trim().to_owned());
    Ok(())
}

fn normalize_key(key: &str) -> String {
    key.trim().to_ascii_lowercase().replace('_', " ")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_quoted_npgsql_values() {
        let parsed = parse_ado_pairs(
            "Host=127.0.0.1;Port=5432;Database=ketoan;Username=api;Password=\"a;b=c\";",
        )
        .unwrap();

        assert_eq!(parsed["host"], "127.0.0.1");
        assert_eq!(parsed["database"], "ketoan");
        assert_eq!(parsed["password"], "a;b=c");
    }

    #[test]
    fn rejects_unterminated_quotes_without_echoing_the_secret() {
        let error = parse_ado_pairs("Host=localhost;Password='unterminated").unwrap_err();
        assert_eq!(
            error.to_string(),
            "unterminated quote in Npgsql connection string"
        );
    }

    #[test]
    fn malformed_sources_never_echo_credentials() {
        let error = parse_database_source("postgres://user:secret with spaces@[").unwrap_err();
        assert_eq!(error.to_string(), "invalid PostgreSQL URL");
        assert!(!error.to_string().contains("secret"));

        let error = parse_ado_pairs("Host=localhost;super-secret-without-equals").unwrap_err();
        assert_eq!(
            error.to_string(),
            "invalid Npgsql connection-string segment"
        );
        assert!(!error.to_string().contains("secret"));
    }
}
