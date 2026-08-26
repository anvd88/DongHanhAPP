use anyhow::{Context, Result, bail};
use jsonwebtoken::{Algorithm, DecodingKey, EncodingKey, Header, Validation, decode, encode};
use serde::{Deserialize, Deserializer, Serialize};
use std::collections::BTreeSet;
use uuid::Uuid;

#[cfg(test)]
const NAME_IDENTIFIER_CLAIM: &str =
    "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
#[cfg(test)]
const NAME_CLAIM: &str = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";
#[cfg(test)]
const ROLE_CLAIM: &str = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

#[derive(Clone)]
pub struct AuthSettings {
    pub jwt_key: Vec<u8>,
    pub issuer: String,
    pub audience: String,
    pub web_expire_hours: i64,
    pub session_idle_days: i32,
    pub cookie_auth: bool,
}

#[derive(Clone)]
pub struct AuthService {
    encoding_key: EncodingKey,
    decoding_key: DecodingKey,
    validation: Validation,
    settings: AuthSettings,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum TokenSource {
    Bearer,
    Cookie,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct TokenIdentity {
    pub user_id: Option<Uuid>,
    pub username: String,
    pub full_name: String,
    pub roles: Vec<String>,
    pub sid: Option<String>,
    pub expires_at_unix: i64,
}

#[derive(Debug, Deserialize)]
struct DecodedClaims {
    exp: i64,
    #[serde(
        default,
        rename = "nameid",
        alias = "sub",
        alias = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    )]
    user_id: Option<String>,
    #[serde(
        default,
        rename = "unique_name",
        alias = "name",
        alias = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"
    )]
    username: Option<String>,
    #[serde(default, rename = "fullName")]
    full_name: Option<String>,
    #[serde(default, deserialize_with = "one_or_many", rename = "role")]
    roles: Vec<String>,
    #[serde(
        default,
        deserialize_with = "one_or_many",
        rename = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    )]
    roles_uri: Vec<String>,
    #[serde(default)]
    sid: Option<String>,
}

#[derive(Serialize)]
struct IssuedClaims<'a> {
    iss: &'a str,
    aud: &'a str,
    exp: i64,
    nbf: i64,
    iat: i64,
    #[serde(rename = "nameid", skip_serializing_if = "Option::is_none")]
    user_id: Option<String>,
    #[serde(rename = "unique_name")]
    username: &'a str,
    #[serde(rename = "fullName")]
    full_name: &'a str,
    #[serde(skip_serializing_if = "Option::is_none")]
    sid: Option<&'a str>,
}

impl AuthService {
    pub fn new(settings: AuthSettings) -> Result<Self> {
        if settings.jwt_key.len() < 32 {
            bail!("JWT signing key must contain at least 32 bytes");
        }
        if settings.issuer.trim().is_empty() || settings.audience.trim().is_empty() {
            bail!("JWT issuer and audience must not be empty");
        }
        if settings.web_expire_hours < 1 {
            bail!("Jwt__WebExpireHours must be at least 1");
        }
        if settings.session_idle_days < 0 {
            bail!("Security__SessionIdleDays cannot be negative");
        }

        let mut validation = Validation::new(Algorithm::HS256);
        validation.leeway = 300;
        validation.set_issuer(&[settings.issuer.as_str()]);
        validation.set_audience(&[settings.audience.as_str()]);
        validation.set_required_spec_claims(&["exp", "iss", "aud"]);

        Ok(Self {
            encoding_key: EncodingKey::from_secret(&settings.jwt_key),
            decoding_key: DecodingKey::from_secret(&settings.jwt_key),
            validation,
            settings,
        })
    }

    pub fn settings(&self) -> &AuthSettings {
        &self.settings
    }

    pub fn decode(&self, token: &str) -> Result<TokenIdentity> {
        let mut claims = decode::<DecodedClaims>(token, &self.decoding_key, &self.validation)
            .context("JWT validation failed")?
            .claims;

        let username = claims.username.take().unwrap_or_default().trim().to_owned();
        if username.is_empty() {
            bail!("JWT does not contain a username");
        }

        let mut unique_roles = BTreeSet::new();
        for role in claims.roles.into_iter().chain(claims.roles_uri) {
            let role = role.trim();
            if !role.is_empty() {
                unique_roles.insert(role.to_owned());
            }
        }

        Ok(TokenIdentity {
            user_id: claims
                .user_id
                .as_deref()
                .and_then(|value| Uuid::parse_str(value.trim()).ok()),
            username,
            full_name: claims.full_name.unwrap_or_default(),
            roles: unique_roles.into_iter().collect(),
            sid: claims
                .sid
                .map(|value| value.trim().to_owned())
                .filter(|value| !value.is_empty()),
            expires_at_unix: claims.exp,
        })
    }

    pub fn renew_web_token(
        &self,
        identity: &TokenIdentity,
        now_unix: i64,
    ) -> Result<(String, i64)> {
        let expires_at = now_unix
            .checked_add(self.settings.web_expire_hours.saturating_mul(3_600))
            .context("web JWT expiration overflow")?;
        let claims = IssuedClaims {
            iss: &self.settings.issuer,
            aud: &self.settings.audience,
            exp: expires_at,
            nbf: now_unix,
            iat: now_unix,
            user_id: identity.user_id.map(|id| id.to_string()),
            username: &identity.username,
            full_name: &identity.full_name,
            sid: identity.sid.as_deref(),
        };
        let token = encode(&Header::new(Algorithm::HS256), &claims, &self.encoding_key)
            .context("cannot sign renewed web JWT")?;
        Ok((token, expires_at))
    }
}

fn one_or_many<'de, D>(deserializer: D) -> std::result::Result<Vec<String>, D::Error>
where
    D: Deserializer<'de>,
{
    #[derive(Deserialize)]
    #[serde(untagged)]
    enum Value {
        One(String),
        Many(Vec<String>),
    }

    Ok(match Option::<Value>::deserialize(deserializer)? {
        Some(Value::One(value)) => vec![value],
        Some(Value::Many(values)) => values,
        None => Vec::new(),
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn service() -> AuthService {
        AuthService::new(AuthSettings {
            jwt_key: b"test-only-key-with-at-least-thirty-two-bytes".to_vec(),
            issuer: "KetoanMini.Web".to_owned(),
            audience: "KetoanMini.Web".to_owned(),
            web_expire_hours: 168,
            session_idle_days: 7,
            cookie_auth: true,
        })
        .unwrap()
    }

    fn sign(value: serde_json::Value, algorithm: Algorithm) -> String {
        encode(
            &Header::new(algorithm),
            &value,
            &EncodingKey::from_secret(b"test-only-key-with-at-least-thirty-two-bytes"),
        )
        .unwrap()
    }

    #[test]
    fn accepts_dotnet_short_claim_names_and_role_arrays() {
        let now = jsonwebtoken::get_current_timestamp() as i64;
        let token = sign(
            json!({
                "iss": "KetoanMini.Web",
                "aud": "KetoanMini.Web",
                "exp": now + 3600,
                "nameid": Uuid::nil().to_string(),
                "unique_name": "alice",
                "role": ["Employee", "Warehouse"],
                "fullName": "Alice",
                "sid": "app:alice"
            }),
            Algorithm::HS256,
        );

        let identity = service().decode(&token).unwrap();
        assert_eq!(identity.username, "alice");
        assert_eq!(identity.roles, ["Employee", "Warehouse"]);
        assert_eq!(identity.user_id, Some(Uuid::nil()));
    }

    #[test]
    fn rejects_wrong_issuer_and_wrong_algorithm() {
        let now = jsonwebtoken::get_current_timestamp() as i64;
        let wrong_issuer = sign(
            json!({
                "iss": "Other",
                "aud": "KetoanMini.Web",
                "exp": now + 3600,
                "unique_name": "alice"
            }),
            Algorithm::HS256,
        );
        assert!(service().decode(&wrong_issuer).is_err());

        let wrong_algorithm = encode(
            &Header::new(Algorithm::HS384),
            &json!({
                "iss": "KetoanMini.Web",
                "aud": "KetoanMini.Web",
                "exp": now + 3600,
                "unique_name": "alice"
            }),
            &EncodingKey::from_secret(b"test-only-key-with-at-least-thirty-two-bytes"),
        )
        .unwrap();
        assert!(service().decode(&wrong_algorithm).is_err());
    }

    #[test]
    fn renewed_token_drops_stale_role_claims() {
        let service = service();
        let now = jsonwebtoken::get_current_timestamp() as i64;
        let identity = TokenIdentity {
            user_id: Some(Uuid::nil()),
            username: "alice".to_owned(),
            full_name: "Alice".to_owned(),
            roles: vec!["Admin".to_owned()],
            sid: Some("web:alice".to_owned()),
            expires_at_unix: 0,
        };
        let (token, _) = service.renew_web_token(&identity, now).unwrap();
        let decoded = service.decode(&token).unwrap();
        assert!(decoded.roles.is_empty());
        assert_eq!(decoded.sid.as_deref(), Some("web:alice"));
    }

    #[test]
    fn constants_match_dotnet_claim_types() {
        assert!(NAME_IDENTIFIER_CLAIM.ends_with("/nameidentifier"));
        assert!(NAME_CLAIM.ends_with("/name"));
        assert!(ROLE_CLAIM.ends_with("/role"));
    }
}
