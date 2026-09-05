use crate::{auth::AuthContext, state::AppState};
use axum::{
    Extension, Json, Router,
    extract::{
        FromRequestParts, Path, Query, State,
        rejection::{JsonRejection, QueryRejection},
    },
    http::{StatusCode, request::Parts},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use chrono::{DateTime, SecondsFormat, Utc};
use serde::{Deserialize, Serialize, Serializer};
use serde_json::json;
use std::sync::Arc;

const FEED_PATH: &str = "/api/notifications";
const REGISTER_TOKEN_PATH: &str = "/api/notifications/register-token";
const UNREGISTER_TOKEN_PATH: &str = "/api/notifications/unregister-token";
const READ_ONE_PATH: &str = "/api/notifications/{id}/read";
const READ_ALL_PATH: &str = "/api/notifications/read-all";
const DELETE_READ_PATH: &str = "/api/notifications/read";
const DELETE_ONE_PATH: &str = "/api/notifications/{id}";
const DEFAULT_PLATFORM: &str = "android";
const DEFAULT_FEED: i64 = 30;
const MAX_FEED: i64 = 50;
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

const UNREAD_COUNT_SQL: &str = r#"
    SELECT COUNT(*)::bigint
    FROM web_notifications
    WHERE lower(username) = lower($1) AND read_at IS NULL
"#;

const FEED_SQL: &str = r#"
    SELECT id, title, body, category, link, app_target, notif_id, created_at,
           read_at IS NOT NULL AS read
    FROM web_notifications
    WHERE lower(username) = lower($1)
    ORDER BY created_at DESC, id DESC
    LIMIT $2
"#;

const READ_ONE_SQL: &str = r#"
    UPDATE web_notifications SET read_at = CURRENT_TIMESTAMP
    WHERE id = $1 AND lower(username) = lower($2) AND read_at IS NULL
"#;

const READ_ALL_SQL: &str = r#"
    UPDATE web_notifications SET read_at = CURRENT_TIMESTAMP
    WHERE lower(username) = lower($1) AND read_at IS NULL
"#;

const DELETE_READ_SQL: &str = r#"
    DELETE FROM web_notifications
    WHERE lower(username) = lower($1) AND read_at IS NOT NULL
"#;

const DELETE_ONE_SQL: &str = r#"
    DELETE FROM web_notifications
    WHERE id = $1 AND lower(username) = lower($2)
"#;

/// The caller must place this router behind `auth::require_auth`.
///
/// Both handlers require `Extension<AuthContext>` and derive the username only
/// from that authenticated context. Request JSON can never choose the owner of
/// a push token.
pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(FEED_PATH, get(feed))
        .route(REGISTER_TOKEN_PATH, post(register_token))
        .route(UNREGISTER_TOKEN_PATH, post(unregister_token))
        .route(READ_ONE_PATH, post(read_one))
        .route(READ_ALL_PATH, post(read_all))
        .route(DELETE_READ_PATH, axum::routing::delete(delete_read))
        .route(DELETE_ONE_PATH, axum::routing::delete(delete_one))
}

#[derive(Clone, Copy, Debug, Default, Deserialize, Eq, PartialEq)]
#[serde(default)]
struct FeedQuery {
    limit: Option<i64>,
}

impl FeedQuery {
    fn take(self) -> i64 {
        self.limit.unwrap_or(DEFAULT_FEED).clamp(1, MAX_FEED)
    }
}

#[derive(Debug, Serialize, sqlx::FromRow)]
#[serde(rename_all = "camelCase")]
struct NotificationItem {
    id: i64,
    title: String,
    body: String,
    category: String,
    link: String,
    app_target: String,
    notif_id: String,
    #[serde(serialize_with = "serialize_dotnet_utc")]
    created_at: DateTime<Utc>,
    read: bool,
}

#[derive(Debug, Serialize)]
struct NotificationFeed {
    unread: i64,
    items: Vec<NotificationItem>,
}

/// An integer extractor whose rejection mirrors ASP.NET's `{id:long}` route constraint.
struct NotificationId(i64);

impl<S> FromRequestParts<S> for NotificationId
where
    S: Send + Sync,
{
    type Rejection = StatusCode;

    async fn from_request_parts(parts: &mut Parts, state: &S) -> Result<Self, Self::Rejection> {
        let Path(raw) = Path::<String>::from_request_parts(parts, state)
            .await
            .map_err(|_| StatusCode::NOT_FOUND)?;
        raw.parse::<i64>()
            .map(Self)
            .map_err(|_| StatusCode::NOT_FOUND)
    }
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

async fn feed(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    query: Result<Query<FeedQuery>, QueryRejection>,
) -> Response {
    let take = match query {
        Ok(Query(query)) => query.take(),
        Err(rejection) => return rejection.status().into_response(),
    };

    let unread = sqlx::query_scalar::<_, i64>(UNREAD_COUNT_SQL)
        .bind(&auth.username)
        .fetch_one(&state.pool)
        .await;
    let unread = match unread {
        Ok(unread) => unread,
        Err(error) => return database_failure("count unread web notifications", error),
    };

    let items = sqlx::query_as::<_, NotificationItem>(FEED_SQL)
        .bind(&auth.username)
        .bind(take)
        .fetch_all(&state.pool)
        .await;
    match items {
        Ok(items) => Json(NotificationFeed { unread, items }).into_response(),
        Err(error) => database_failure("load web notification feed", error),
    }
}

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

async fn read_one(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    NotificationId(id): NotificationId,
) -> Response {
    empty_notification_mutation(
        sqlx::query(READ_ONE_SQL)
            .bind(id)
            .bind(&auth.username)
            .execute(&state.pool)
            .await,
        "mark one web notification as read",
    )
}

async fn read_all(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    empty_notification_mutation(
        sqlx::query(READ_ALL_SQL)
            .bind(&auth.username)
            .execute(&state.pool)
            .await,
        "mark all web notifications as read",
    )
}

async fn delete_read(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    empty_notification_mutation(
        sqlx::query(DELETE_READ_SQL)
            .bind(&auth.username)
            .execute(&state.pool)
            .await,
        "delete read web notifications",
    )
}

async fn delete_one(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    NotificationId(id): NotificationId,
) -> Response {
    empty_notification_mutation(
        sqlx::query(DELETE_ONE_SQL)
            .bind(id)
            .bind(&auth.username)
            .execute(&state.pool)
            .await,
        "delete one web notification",
    )
}

fn empty_notification_mutation(
    result: Result<sqlx::postgres::PgQueryResult, sqlx::Error>,
    operation: &'static str,
) -> Response {
    match result {
        // The .NET contract deliberately treats zero affected rows as an idempotent success.
        Ok(_) => StatusCode::NO_CONTENT.into_response(),
        Err(error) => database_failure(operation, error),
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

fn serialize_dotnet_utc<S>(value: &DateTime<Utc>, serializer: S) -> Result<S::Ok, S::Error>
where
    S: Serializer,
{
    serializer.serialize_str(&value.to_rfc3339_opts(SecondsFormat::Millis, true))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn compact_sql(sql: &str) -> String {
        sql.split_whitespace().collect::<Vec<_>>().join(" ")
    }

    #[test]
    fn route_contract_contains_the_complete_notification_group() {
        assert_eq!(
            [
                ("GET", FEED_PATH),
                ("POST", REGISTER_TOKEN_PATH),
                ("POST", UNREGISTER_TOKEN_PATH),
                ("POST", READ_ONE_PATH),
                ("POST", READ_ALL_PATH),
                ("DELETE", DELETE_READ_PATH),
                ("DELETE", DELETE_ONE_PATH),
            ],
            [
                ("GET", "/api/notifications"),
                ("POST", "/api/notifications/register-token"),
                ("POST", "/api/notifications/unregister-token"),
                ("POST", "/api/notifications/{id}/read"),
                ("POST", "/api/notifications/read-all"),
                ("DELETE", "/api/notifications/read"),
                ("DELETE", "/api/notifications/{id}"),
            ]
        );
    }

    #[test]
    fn feed_limit_matches_dotnet_clamping() {
        assert_eq!(FeedQuery::default().take(), 30);
        assert_eq!(FeedQuery { limit: Some(0) }.take(), 1);
        assert_eq!(FeedQuery { limit: Some(500) }.take(), 50);
    }

    #[test]
    fn feed_json_matches_camel_case_and_millisecond_utc_contract() {
        let created_at = DateTime::parse_from_rfc3339("2026-08-28T01:02:03.987654Z")
            .unwrap()
            .with_timezone(&Utc);
        let value = serde_json::to_value(NotificationFeed {
            unread: 1,
            items: vec![NotificationItem {
                id: 7,
                title: "Tiêu đề".to_owned(),
                body: "Nội dung".to_owned(),
                category: "task".to_owned(),
                link: "/tasks/7".to_owned(),
                app_target: "task:7".to_owned(),
                notif_id: "task:7:created".to_owned(),
                created_at,
                read: false,
            }],
        })
        .unwrap();

        assert_eq!(value["items"][0]["appTarget"], "task:7");
        assert_eq!(value["items"][0]["notifId"], "task:7:created");
        assert_eq!(value["items"][0]["createdAt"], "2026-08-28T01:02:03.987Z");
        assert_eq!(value["items"][0]["read"], false);
    }

    #[test]
    fn every_feed_mutation_is_scoped_to_the_authenticated_owner() {
        for sql in [READ_ONE_SQL, READ_ALL_SQL, DELETE_READ_SQL, DELETE_ONE_SQL] {
            let sql = compact_sql(sql);
            assert!(sql.contains("lower(username) = lower($"), "{sql}");
        }
        assert!(compact_sql(DELETE_READ_SQL).contains("read_at IS NOT NULL"));
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
