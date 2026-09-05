use crate::{auth::AuthContext, state::AppState};
use axum::{
    Extension, Json, Router,
    extract::{State, rejection::JsonRejection},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
};
use serde::{Deserialize, Serialize};
use sqlx::PgPool;
use std::{collections::HashMap, sync::Arc};
use uuid::Uuid;

const WATER_REMINDER_ENABLED: &str = "waterReminderEnabled";
const EYE_REMINDER_ENABLED: &str = "eyeReminderEnabled";
const KEEP_CREATE_VOUCHER_OPEN: &str = "keepCreateVoucherOpen";
const NOTIFICATION_GROUPS_PATH: &str = "/api/preferences/notifications";
const NOTIFY_PREFIX: &str = "notifyGroup.";
const DELIVERY_GROUP: &str = "delivery";
const COLLECTION_GROUP: &str = "collection";
const ACCOUNTING_GROUP: &str = "accounting";
const WORK_GROUP: &str = "work";
const PEOPLE_GROUP: &str = "people";

const LOAD_SQL: &str = r#"
    SELECT preference_key, preference_value
    FROM web_user_preferences
    WHERE user_id = $1
      AND preference_key IN ($2, $3, $4)
"#;

const UPSERT_SQL: &str = r#"
    INSERT INTO web_user_preferences (user_id, preference_key, preference_value, updated_at)
    VALUES ($1, $2, $3, CURRENT_TIMESTAMP)
    ON CONFLICT (user_id, preference_key) DO UPDATE SET
        preference_value = EXCLUDED.preference_value,
        updated_at = EXCLUDED.updated_at
"#;

const LOAD_NOTIFICATION_GROUPS_SQL: &str = r#"
    SELECT preference_key, preference_value
    FROM web_user_preferences
    WHERE user_id = $1 AND preference_key LIKE 'notifyGroup.%'
"#;

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(
            "/api/preferences",
            get(get_preferences).put(put_preferences),
        )
        .route(
            NOTIFICATION_GROUPS_PATH,
            get(get_notification_groups).put(put_notification_groups),
        )
}

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq)]
#[serde(rename_all = "camelCase")]
struct UserPreferencePatchRequest {
    water_reminder_enabled: Option<bool>,
    eye_reminder_enabled: Option<bool>,
    keep_create_voucher_open: Option<bool>,
}

impl UserPreferencePatchRequest {
    fn entries(self) -> [(&'static str, Option<bool>); 3] {
        [
            (WATER_REMINDER_ENABLED, self.water_reminder_enabled),
            (EYE_REMINDER_ENABLED, self.eye_reminder_enabled),
            (KEEP_CREATE_VOUCHER_OPEN, self.keep_create_voucher_open),
        ]
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct UserPreferencesDto {
    water_reminder_enabled: bool,
    eye_reminder_enabled: bool,
    keep_create_voucher_open: bool,
}

impl UserPreferencesDto {
    fn from_values(values: &PreferenceValues) -> Self {
        Self {
            water_reminder_enabled: parse_bool(values, WATER_REMINDER_ENABLED, true),
            eye_reminder_enabled: parse_bool(values, EYE_REMINDER_ENABLED, true),
            keep_create_voucher_open: parse_bool(values, KEEP_CREATE_VOUCHER_OPEN, false),
        }
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq, Serialize)]
struct NotificationGroupValues {
    delivery: bool,
    collection: bool,
    accounting: bool,
    work: bool,
    people: bool,
}

impl Default for NotificationGroupValues {
    fn default() -> Self {
        Self {
            delivery: true,
            collection: true,
            accounting: true,
            work: true,
            people: true,
        }
    }
}

impl NotificationGroupValues {
    fn apply(&mut self, group: &str, enabled: bool) {
        match group {
            DELIVERY_GROUP => self.delivery = enabled,
            COLLECTION_GROUP => self.collection = enabled,
            ACCOUNTING_GROUP => self.accounting = enabled,
            WORK_GROUP => self.work = enabled,
            PEOPLE_GROUP => self.people = enabled,
            _ => {}
        }
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq, Serialize)]
struct NotificationGroupResponse {
    groups: NotificationGroupValues,
}

#[derive(Debug, Default, Deserialize, Eq, PartialEq)]
#[serde(default, rename_all = "camelCase")]
struct NotificationGroupPatch {
    #[serde(alias = "Groups")]
    groups: Option<HashMap<String, bool>>,
}

async fn get_preferences(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let Some(user_id) = auth.user_id else {
        return StatusCode::UNAUTHORIZED.into_response();
    };

    match load_preferences(&state.pool, user_id).await {
        Ok(preferences) => Json(preferences).into_response(),
        Err(error) => {
            tracing::error!(%error, "could not load user preferences");
            database_unavailable().into_response()
        }
    }
}

async fn put_preferences(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<UserPreferencePatchRequest>, JsonRejection>,
) -> Response {
    let Some(user_id) = auth.user_id else {
        return StatusCode::UNAUTHORIZED.into_response();
    };
    let patch = match payload {
        Ok(Json(patch)) => patch,
        Err(rejection) => {
            // ASP.NET model binding reports a syntactically valid JSON value with the wrong DTO
            // shape as 400, while Axum's default JsonDataError status is 422.
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            return status.into_response();
        }
    };

    if let Err(error) = save_preferences(&state.pool, user_id, patch).await {
        tracing::error!(%error, "could not save user preferences");
        return database_unavailable().into_response();
    }

    match load_preferences(&state.pool, user_id).await {
        Ok(preferences) => Json(preferences).into_response(),
        Err(error) => {
            tracing::error!(%error, "could not reload saved user preferences");
            database_unavailable().into_response()
        }
    }
}

async fn get_notification_groups(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let Some(user_id) = auth.user_id else {
        return StatusCode::UNAUTHORIZED.into_response();
    };

    match load_notification_groups(&state.pool, user_id).await {
        Ok(groups) => Json(NotificationGroupResponse { groups }).into_response(),
        Err(error) => {
            tracing::error!(%error, "could not load notification-group preferences");
            database_unavailable().into_response()
        }
    }
}

async fn put_notification_groups(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<NotificationGroupPatch>, JsonRejection>,
) -> Response {
    let Some(user_id) = auth.user_id else {
        return StatusCode::UNAUTHORIZED.into_response();
    };
    let patch = match payload {
        Ok(Json(patch)) => patch,
        Err(rejection) => {
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            return status.into_response();
        }
    };

    if let Err(error) = save_notification_groups(&state.pool, user_id, patch).await {
        tracing::error!(%error, "could not save notification-group preferences");
        return database_unavailable().into_response();
    }

    match load_notification_groups(&state.pool, user_id).await {
        Ok(groups) => Json(NotificationGroupResponse { groups }).into_response(),
        Err(error) => {
            tracing::error!(%error, "could not reload notification-group preferences");
            database_unavailable().into_response()
        }
    }
}

fn database_unavailable() -> (StatusCode, Json<serde_json::Value>) {
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(serde_json::json!({
            "message": "Khong ket noi duoc co so du lieu PostgreSQL."
        })),
    )
}

async fn save_preferences(
    pool: &PgPool,
    user_id: Uuid,
    patch: UserPreferencePatchRequest,
) -> Result<(), sqlx::Error> {
    // The legacy endpoint performs one upsert per supplied field without a surrounding
    // transaction. Preserve that partial-update behavior for compatibility.
    let mut connection = pool.acquire().await?;
    for (key, value) in patch.entries() {
        let Some(value) = value else {
            continue;
        };

        sqlx::query(UPSERT_SQL)
            .bind(user_id)
            .bind(key)
            .bind(if value { "true" } else { "false" })
            .execute(&mut *connection)
            .await?;
    }
    Ok(())
}

async fn load_preferences(pool: &PgPool, user_id: Uuid) -> Result<UserPreferencesDto, sqlx::Error> {
    let rows = sqlx::query_as::<_, (String, String)>(LOAD_SQL)
        .bind(user_id)
        .bind(WATER_REMINDER_ENABLED)
        .bind(EYE_REMINDER_ENABLED)
        .bind(KEEP_CREATE_VOUCHER_OPEN)
        .fetch_all(pool)
        .await?;

    let values = rows
        .into_iter()
        .map(|(key, value)| (normalize_key(&key), value))
        .collect();
    Ok(UserPreferencesDto::from_values(&values))
}

async fn save_notification_groups(
    pool: &PgPool,
    user_id: Uuid,
    patch: NotificationGroupPatch,
) -> Result<(), sqlx::Error> {
    // Match .NET: unknown keys are ignored, known keys are applied independently without wrapping
    // the whole patch in a transaction.
    let mut connection = pool.acquire().await?;
    for (group, enabled) in patch.groups.unwrap_or_default() {
        let Some(key) = notification_group_preference_key(&group) else {
            continue;
        };
        sqlx::query(UPSERT_SQL)
            .bind(user_id)
            .bind(key)
            .bind(if enabled { "true" } else { "false" })
            .execute(&mut *connection)
            .await?;
    }
    Ok(())
}

async fn load_notification_groups(
    pool: &PgPool,
    user_id: Uuid,
) -> Result<NotificationGroupValues, sqlx::Error> {
    let rows = sqlx::query_as::<_, (String, String)>(LOAD_NOTIFICATION_GROUPS_SQL)
        .bind(user_id)
        .fetch_all(pool)
        .await?;

    let mut groups = NotificationGroupValues::default();
    for (key, value) in rows {
        let Some(group) = key.strip_prefix(NOTIFY_PREFIX) else {
            continue;
        };
        if notification_group_preference_key(group).is_some() {
            groups.apply(group, !value.eq_ignore_ascii_case("false"));
        }
    }
    Ok(groups)
}

fn notification_group_preference_key(group: &str) -> Option<&'static str> {
    match group {
        DELIVERY_GROUP => Some("notifyGroup.delivery"),
        COLLECTION_GROUP => Some("notifyGroup.collection"),
        ACCOUNTING_GROUP => Some("notifyGroup.accounting"),
        WORK_GROUP => Some("notifyGroup.work"),
        PEOPLE_GROUP => Some("notifyGroup.people"),
        _ => None,
    }
}

type PreferenceValues = HashMap<String, String>;

fn normalize_key(key: &str) -> String {
    key.to_ascii_lowercase()
}

fn parse_bool(values: &PreferenceValues, key: &str, default_value: bool) -> bool {
    let Some(raw) = values.get(&normalize_key(key)) else {
        return default_value;
    };

    let raw = raw.trim();
    if raw.eq_ignore_ascii_case("true") {
        true
    } else if raw.eq_ignore_ascii_case("false") {
        false
    } else {
        default_value
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn missing_or_invalid_values_use_legacy_defaults() {
        let mut values = PreferenceValues::new();
        values.insert(normalize_key(WATER_REMINDER_ENABLED), "invalid".to_owned());
        values.insert(normalize_key(EYE_REMINDER_ENABLED), " FALSE ".to_owned());
        values.insert(normalize_key(KEEP_CREATE_VOUCHER_OPEN), "TrUe".to_owned());

        assert_eq!(
            UserPreferencesDto::from_values(&values),
            UserPreferencesDto {
                water_reminder_enabled: true,
                eye_reminder_enabled: false,
                keep_create_voucher_open: true,
            }
        );
    }

    #[test]
    fn response_uses_the_existing_camel_case_contract() {
        let value = serde_json::to_value(UserPreferencesDto {
            water_reminder_enabled: true,
            eye_reminder_enabled: false,
            keep_create_voucher_open: true,
        })
        .unwrap();

        assert_eq!(
            value,
            json!({
                "waterReminderEnabled": true,
                "eyeReminderEnabled": false,
                "keepCreateVoucherOpen": true
            })
        );
    }

    #[test]
    fn patch_is_partial_and_uses_camel_case_input() {
        let patch: UserPreferencePatchRequest = serde_json::from_value(json!({
            "waterReminderEnabled": false
        }))
        .unwrap();

        assert_eq!(patch.water_reminder_enabled, Some(false));
        assert_eq!(patch.eye_reminder_enabled, None);
        assert_eq!(patch.keep_create_voucher_open, None);
    }

    #[test]
    fn notification_groups_default_on_and_serialize_in_the_existing_shape() {
        let value = serde_json::to_value(NotificationGroupResponse {
            groups: NotificationGroupValues::default(),
        })
        .unwrap();

        assert_eq!(
            value,
            json!({
                "groups": {
                    "delivery": true,
                    "collection": true,
                    "accounting": true,
                    "work": true,
                    "people": true
                }
            })
        );
    }

    #[test]
    fn notification_group_patch_ignores_unknown_keys_and_keeps_known_key_names() {
        let patch: NotificationGroupPatch = serde_json::from_value(json!({
            "groups": {
                "delivery": false,
                "security": false,
                "unknown": true
            }
        }))
        .unwrap();
        let groups = patch.groups.unwrap();
        assert_eq!(groups.get("delivery"), Some(&false));
        assert_eq!(
            notification_group_preference_key("delivery"),
            Some("notifyGroup.delivery")
        );
        assert_eq!(notification_group_preference_key("security"), None);
        assert_eq!(notification_group_preference_key("unknown"), None);
    }
}
