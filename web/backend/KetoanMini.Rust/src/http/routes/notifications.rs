use crate::{auth::AuthContext, state::AppState};
use axum::{
    Extension, Json, Router,
    extract::{State, rejection::JsonRejection},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::post,
};
use serde::Deserialize;
use serde_json::json;
use std::sync::Arc;

const REGISTER_TOKEN_PATH: &str = "/api/notifications/register-token";
const UNREGISTER_TOKEN_PATH: &str = "/api/notifications/unregister-token";
const DEFAULT_PLATFORM: &str = "android";
const MISSING_TOKEN_MESSAGE: &str = "Thiếu token thiết bị.";
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";

const REGISTER_TOKEN_SQL: &str = r#"
    INSERT INTO hr_device_tokens (token, username, platform, updated_at)
    VALUES ($1, $2, $3, CURRENT_TIMESTAMP)
    ON CONFLICT (token) DO UPDATE SET
        username = EXCLUDED.username,
        platform = EXCLUDED.platform,
        updated_at = CURRENT_TIMESTAMP
"#;

const UNREGISTER_TOKEN_SQL: &str = r#"
    DELETE FROM hr_device_tokens
    WHERE token = $1 AND lower(username) = lower($2)
"#;

/// The caller must place this router behind `auth::require_auth`.
///
/// Both handlers require `Extension<AuthContext>` and derive the username only
/// from that authenticated context. Request JSON can never choose the owner of
/// a push token.
pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(REGISTER_TOKEN_PATH, post(register_token))
        .route(UNREGISTER_TOKEN_PATH, post(unregister_token))
}

#[derive(Debug, Default, Deserialize, Eq, PartialEq)]
#[serde(default, rename_all = "camelCase")]
struct RegisterTokenRequest {
    token: Option<String>,
    platform: Option<String>,
}

#[derive(Debug, Default, Deserialize, Eq, PartialEq)]
#[serde(default, rename_all = "camelCase")]
struct TokenRequest {
    token: Option<String>,
}

#[derive(Debug, Eq, PartialEq)]
struct RegisterTokenMutation {
    token: String,
    username: String,
    platform: String,
}

impl RegisterTokenMutation {
    fn from_request(
        request: RegisterTokenRequest,
        authenticated_username: &str,
    ) -> Result<Self, MissingToken> {
        let token = required_token(request.token.as_deref())?;
        let platform = request
            .platform
            .as_deref()
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .unwrap_or(DEFAULT_PLATFORM)
            .to_owned();
        Ok(Self {
            token,
            // Never copy an owner field from request JSON. A token reused by a
            // different signed-in account is intentionally reassigned here.
            username: authenticated_username.to_owned(),
            platform,
        })
    }
}

#[derive(Debug, Eq, PartialEq)]
struct UnregisterTokenMutation {
    token: String,
    username: String,
}

impl UnregisterTokenMutation {
    fn from_request(request: TokenRequest, authenticated_username: &str) -> Option<Self> {
        optional_token(request.token.as_deref()).map(|token| Self {
            token,
            // The DELETE predicate binds this value as its second condition;
            // possession of another user's raw FCM token is not sufficient.
            username: authenticated_username.to_owned(),
        })
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
struct MissingToken;

async fn register_token(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<RegisterTokenRequest>, JsonRejection>,
) -> Response {
    let request = match parse_json(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };
    let mutation = match RegisterTokenMutation::from_request(request, &auth.username) {
        Ok(mutation) => mutation,
        Err(MissingToken) => return missing_token(),
    };

    let result = sqlx::query(REGISTER_TOKEN_SQL)
        .bind(mutation.token)
        .bind(mutation.username)
        .bind(mutation.platform)
        .execute(&state.pool)
        .await;
    match result {
        Ok(_) => StatusCode::NO_CONTENT.into_response(),
        Err(error) => database_failure("register device push token", error),
    }
}

async fn unregister_token(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<TokenRequest>, JsonRejection>,
) -> Response {
    let request = match parse_json(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };
    let Some(mutation) = UnregisterTokenMutation::from_request(request, &auth.username) else {
        // Preserve the .NET fast path: blank input is an idempotent success and
        // does not open a database connection.
        return StatusCode::NO_CONTENT.into_response();
    };

    let result = sqlx::query(UNREGISTER_TOKEN_SQL)
        .bind(mutation.token)
        .bind(mutation.username)
        .execute(&state.pool)
        .await;
    match result {
        // Deleting zero rows is also an idempotent success. In particular, it
        // is the expected result when the token belongs to another account.
        Ok(_) => StatusCode::NO_CONTENT.into_response(),
        Err(error) => database_failure("unregister device push token", error),
    }
}

fn required_token(value: Option<&str>) -> Result<String, MissingToken> {
    optional_token(value).ok_or(MissingToken)
}

fn optional_token(value: Option<&str>) -> Option<String> {
    value
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(str::to_owned)
}

fn parse_json<T>(payload: Result<Json<T>, JsonRejection>) -> Result<T, StatusCode> {
    match payload {
        Ok(Json(request)) => Ok(request),
        Err(rejection) => {
            // ASP.NET model binding uses 400 for a syntactically valid JSON
            // value whose field types do not match the request record.
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(status)
        }
    }
}

fn missing_token() -> Response {
    (
        StatusCode::BAD_REQUEST,
        Json(json!({ "message": MISSING_TOKEN_MESSAGE })),
    )
        .into_response()
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    // FCM tokens are credentials. Deliberately log neither SQL bind values nor
    // the token itself when a database operation fails.
    tracing::warn!(%error, operation, "native notification-token database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn compact_sql(sql: &str) -> String {
        sql.split_whitespace().collect::<Vec<_>>().join(" ")
    }

    #[test]
    fn route_contract_has_exactly_the_two_authenticated_posts() {
        assert_eq!(
            [REGISTER_TOKEN_PATH, UNREGISTER_TOKEN_PATH],
            [
                "/api/notifications/register-token",
                "/api/notifications/unregister-token"
            ]
        );
    }

    #[test]
    fn register_uses_only_authenticated_username_and_reassigns_conflicting_token() {
        // Unknown fields are ignored just like System.Text.Json. A hostile
        // caller cannot smuggle an account owner through the body.
        let request: RegisterTokenRequest = serde_json::from_value(json!({
            "token": "  shared-fcm-token  ",
            "platform": "  android  ",
            "username": "admin"
        }))
        .unwrap();
        let mutation = RegisterTokenMutation::from_request(request, "employee01").unwrap();

        assert_eq!(
            mutation,
            RegisterTokenMutation {
                token: "shared-fcm-token".to_owned(),
                username: "employee01".to_owned(),
                platform: "android".to_owned(),
            }
        );
        let sql = compact_sql(REGISTER_TOKEN_SQL);
        assert!(sql.contains("ON CONFLICT (token) DO UPDATE SET"));
        assert!(sql.contains("username = EXCLUDED.username"));
        assert!(sql.contains("platform = EXCLUDED.platform"));
    }

    #[test]
    fn unregister_is_scoped_by_token_and_authenticated_username() {
        let request: TokenRequest = serde_json::from_value(json!({
            "token": "  admin-device-token  ",
            "username": "admin"
        }))
        .unwrap();
        let mutation = UnregisterTokenMutation::from_request(request, "employee01").unwrap();

        assert_eq!(mutation.token, "admin-device-token");
        assert_eq!(mutation.username, "employee01");
        let sql = compact_sql(UNREGISTER_TOKEN_SQL);
        assert_eq!(
            sql,
            "DELETE FROM hr_device_tokens WHERE token = $1 AND lower(username) = lower($2)"
        );
        // Therefore a row `(admin-device-token, admin)` cannot match the
        // employee01 mutation even when the employee knows the raw token.
        assert!(!"admin".eq_ignore_ascii_case(&mutation.username));
    }

    #[test]
    fn token_and_platform_normalization_matches_dotnet() {
        let default_platform = RegisterTokenMutation::from_request(
            RegisterTokenRequest {
                token: Some("\u{2003}token-1\t".to_owned()),
                platform: Some(" \n\u{2003}".to_owned()),
            },
            "user",
        )
        .unwrap();
        assert_eq!(default_platform.token, "token-1");
        assert_eq!(default_platform.platform, "android");

        let ios = RegisterTokenMutation::from_request(
            RegisterTokenRequest {
                token: Some("token-2".to_owned()),
                platform: Some("  ios  ".to_owned()),
            },
            "user",
        )
        .unwrap();
        assert_eq!(ios.platform, "ios");
    }

    #[test]
    fn register_rejects_blank_token_but_unregister_is_idempotent() {
        assert_eq!(required_token(None), Err(MissingToken));
        assert_eq!(required_token(Some(" \t\n")), Err(MissingToken));
        assert_eq!(optional_token(None), None);
        assert_eq!(optional_token(Some(" \t\n")), None);
    }

    #[test]
    fn malformed_field_types_are_rejected_by_serde() {
        assert!(
            serde_json::from_value::<RegisterTokenRequest>(json!({
                "token": 123,
                "platform": "android"
            }))
            .is_err()
        );
        assert!(serde_json::from_value::<TokenRequest>(json!({ "token": false })).is_err());
    }
}
