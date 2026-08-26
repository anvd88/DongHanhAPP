//! Native `/api/help` endpoints, ported from `HelpEndpoints.cs`.
//!
//! The router containing this module must remain behind `auth::require_auth`.
//! Every handler also enforces the original `portal.read` group policy itself;
//! mutating handlers additionally require `portal.manage`.

use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    extract::{Path, State, rejection::JsonRejection},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
};
use chrono::{DateTime, SecondsFormat, Utc};
use serde::{Deserialize, Serialize, Serializer};
use serde_json::json;
use sqlx::{FromRow, PgPool};
use std::{collections::BTreeSet, sync::Arc};
use uuid::Uuid;

const FAQS_PATH: &str = "/api/help/faqs";
const FAQ_BY_ID_PATH: &str = "/api/help/faqs/{id}";
const STATUS_PATH: &str = "/api/help/status";

const LIST_ALL_SQL: &str = r#"
    SELECT id, category, question, answer, order_no, is_published
    FROM help_faqs
    ORDER BY category, order_no, updated_at DESC
"#;

const LIST_PUBLISHED_SQL: &str = r#"
    SELECT id, category, question, answer, order_no, is_published
    FROM help_faqs
    WHERE is_published = TRUE
    ORDER BY category, order_no, updated_at DESC
"#;

const INSERT_SQL: &str = r#"
    INSERT INTO help_faqs
        (id, category, question, answer, order_no, is_published, updated_by)
    VALUES ($1, $2, $3, $4, $5, $6, $7)
"#;

const UPDATE_SQL: &str = r#"
    UPDATE help_faqs
    SET category = $2,
        question = $3,
        answer = $4,
        order_no = $5,
        is_published = $6,
        updated_by = $7,
        updated_at = CURRENT_TIMESTAMP
    WHERE id = $1
"#;

const DELETE_SQL: &str = "DELETE FROM help_faqs WHERE id = $1";

const AUDIT_SQL: &str = r#"
    INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
    VALUES (CURRENT_TIMESTAMP, $1, $2, $3, $4, $5)
"#;

const AUDIT_CREATE: &str = "Tạo FAQ";
const AUDIT_UPDATE: &str = "Sửa FAQ";
const AUDIT_DELETE: &str = "Xóa FAQ";
const AUDIT_ENTITY: &str = "Faq";
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";

#[cfg(test)]
const ROUTE_CONTRACTS: &[(&str, &str)] = &[
    ("GET", FAQS_PATH),
    ("POST", FAQS_PATH),
    ("PUT", FAQ_BY_ID_PATH),
    ("DELETE", FAQ_BY_ID_PATH),
    ("GET", STATUS_PATH),
];

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(FAQS_PATH, get(list_faqs).post(create_faq))
        .route(
            FAQ_BY_ID_PATH,
            axum::routing::put(update_faq).delete(delete_faq),
        )
        .route(STATUS_PATH, get(service_status))
}

#[derive(Debug, Deserialize, Eq, PartialEq)]
#[serde(rename_all = "camelCase")]
struct FaqRequest {
    category: Option<String>,
    question: Option<String>,
    answer: Option<String>,
    order_no: Option<i32>,
    is_published: Option<bool>,
}

#[derive(Debug, Eq, PartialEq)]
struct FaqValues {
    category: String,
    question: String,
    answer: String,
    order_no: i32,
    is_published: bool,
}

impl FaqRequest {
    fn for_create(mut self) -> Result<FaqValues, &'static str> {
        let question = self.question.take().unwrap_or_default();
        if question.trim().is_empty() {
            return Err("Thiếu câu hỏi.");
        }
        Ok(self.into_values(question.trim().to_owned()))
    }

    fn for_update(self) -> FaqValues {
        let question = self
            .question
            .as_deref()
            .unwrap_or_default()
            .trim()
            .to_owned();
        self.into_values(question)
    }

    fn into_values(self, question: String) -> FaqValues {
        FaqValues {
            category: self.category.unwrap_or_default(),
            question,
            answer: self.answer.unwrap_or_default(),
            order_no: self.order_no.unwrap_or_default(),
            is_published: self.is_published.unwrap_or(true),
        }
    }
}

#[derive(Debug, FromRow)]
struct FaqRow {
    id: Uuid,
    category: String,
    question: String,
    answer: String,
    order_no: i32,
    is_published: bool,
}

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct FaqDto {
    id: Uuid,
    category: String,
    question: String,
    answer: String,
    order_no: i32,
    is_published: bool,
}

impl From<FaqRow> for FaqDto {
    fn from(row: FaqRow) -> Self {
        Self {
            id: row.id,
            category: row.category,
            question: row.question,
            answer: row.answer,
            order_no: row.order_no,
            is_published: row.is_published,
        }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct HelpStatusDto {
    db: &'static str,
    #[serde(serialize_with = "serialize_dotnet_utc")]
    server_time: DateTime<Utc>,
}

fn serialize_dotnet_utc<S>(value: &DateTime<Utc>, serializer: S) -> Result<S::Ok, S::Error>
where
    S: Serializer,
{
    serializer.serialize_str(&value.to_rfc3339_opts(SecondsFormat::Millis, true))
}

async fn list_faqs(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let Some(query) = faq_list_query(&auth.permissions) else {
        return StatusCode::FORBIDDEN.into_response();
    };
    let rows = match sqlx::query_as::<_, FaqRow>(query)
        .fetch_all(&state.pool)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("list help FAQs", error),
    };
    Json(rows.into_iter().map(FaqDto::from).collect::<Vec<_>>()).into_response()
}

async fn create_faq(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<FaqRequest>, JsonRejection>,
) -> Response {
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };
    let values = match request.for_create() {
        Ok(values) => values,
        Err(message) => {
            return (StatusCode::BAD_REQUEST, Json(json!({ "message": message }))).into_response();
        }
    };

    let id = Uuid::new_v4();
    if let Err(error) = sqlx::query(INSERT_SQL)
        .bind(id)
        .bind(&values.category)
        .bind(&values.question)
        .bind(&values.answer)
        .bind(values.order_no)
        .bind(values.is_published)
        .bind(&auth.username)
        .execute(&state.pool)
        .await
    {
        return database_failure("create help FAQ", error);
    }

    // PostgreSQL's existing help_faqs trigger publishes realtime scope `data`.
    // RecordAudit is deliberately best-effort in .NET; keep it out of the FAQ
    // transaction and never turn an otherwise successful write into a failure.
    record_audit(
        &state.pool,
        &auth.username,
        AUDIT_CREATE,
        id,
        &values.question,
    )
    .await;
    (StatusCode::OK, Json(json!({ "id": id }))).into_response()
}

async fn update_faq(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
    payload: Result<Json<FaqRequest>, JsonRejection>,
) -> Response {
    let Some(id) = parse_faq_id(&id) else {
        // Match the ASP.NET `{id:guid}` route constraint.
        return StatusCode::NOT_FOUND.into_response();
    };
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };
    let values = request.for_update();

    let result = match sqlx::query(UPDATE_SQL)
        .bind(id)
        .bind(&values.category)
        .bind(&values.question)
        .bind(&values.answer)
        .bind(values.order_no)
        .bind(values.is_published)
        .bind(&auth.username)
        .execute(&state.pool)
        .await
    {
        Ok(result) => result,
        Err(error) => return database_failure("update help FAQ", error),
    };
    if result.rows_affected() == 0 {
        return StatusCode::NOT_FOUND.into_response();
    }

    record_audit(&state.pool, &auth.username, AUDIT_UPDATE, id, "").await;
    StatusCode::NO_CONTENT.into_response()
}

async fn delete_faq(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
) -> Response {
    let Some(id) = parse_faq_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let result = match sqlx::query(DELETE_SQL).bind(id).execute(&state.pool).await {
        Ok(result) => result,
        Err(error) => return database_failure("delete help FAQ", error),
    };
    if result.rows_affected() == 0 {
        return StatusCode::NOT_FOUND.into_response();
    }

    record_audit(&state.pool, &auth.username, AUDIT_DELETE, id, "").await;
    StatusCode::NO_CONTENT.into_response()
}

async fn service_status(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    if !may_read(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let db = match state.pool.acquire().await {
        Ok(connection) => {
            drop(connection);
            "ok"
        }
        Err(error) => {
            tracing::warn!(%error, "help status database check failed");
            "error"
        }
    };
    Json(HelpStatusDto {
        db,
        server_time: Utc::now(),
    })
    .into_response()
}

fn may_read(permission_set: &BTreeSet<String>) -> bool {
    permission_set.contains(permissions::PORTAL_READ)
}

fn may_manage(permission_set: &BTreeSet<String>) -> bool {
    may_read(permission_set) && permission_set.contains(permissions::PORTAL_MANAGE)
}

fn faq_list_query(permission_set: &BTreeSet<String>) -> Option<&'static str> {
    if !may_read(permission_set) {
        None
    } else if may_manage(permission_set) {
        Some(LIST_ALL_SQL)
    } else {
        Some(LIST_PUBLISHED_SQL)
    }
}

fn parse_faq_id(raw: &str) -> Option<Uuid> {
    Uuid::parse_str(raw).ok()
}

fn json_payload(
    payload: Result<Json<FaqRequest>, JsonRejection>,
) -> Result<FaqRequest, StatusCode> {
    match payload {
        Ok(Json(request)) => Ok(request),
        Err(rejection) => {
            // ASP.NET model binding uses 400 for a valid JSON document whose
            // field type does not match the DTO; Axum normally uses 422.
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(status)
        }
    }
}

async fn record_audit(
    pool: &PgPool,
    username: &str,
    action: &'static str,
    entity_id: Uuid,
    details: &str,
) {
    // The existing audit_logs trigger publishes realtime scope `audit`.
    if let Err(error) = sqlx::query(AUDIT_SQL)
        .bind(username)
        .bind(action)
        .bind(AUDIT_ENTITY)
        .bind(entity_id.to_string())
        .bind(details)
        .execute(pool)
        .await
    {
        tracing::warn!(%error, action, "could not record help FAQ audit event");
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native help database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({
            "message": DATABASE_UNAVAILABLE_MESSAGE
        })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn route_contract_contains_exactly_the_five_dotnet_endpoints() {
        let expected: &[(&str, &str)] = &[
            ("GET", "/api/help/faqs"),
            ("POST", "/api/help/faqs"),
            ("PUT", "/api/help/faqs/{id}"),
            ("DELETE", "/api/help/faqs/{id}"),
            ("GET", "/api/help/status"),
        ];
        assert_eq!(ROUTE_CONTRACTS, expected);
    }

    #[test]
    fn group_and_mutation_permissions_are_both_enforced() {
        let none = BTreeSet::new();
        assert!(!may_read(&none));
        assert!(!may_manage(&none));
        assert_eq!(faq_list_query(&none), None);

        let read = BTreeSet::from([permissions::PORTAL_READ.to_owned()]);
        assert!(may_read(&read));
        assert!(!may_manage(&read));
        assert_eq!(faq_list_query(&read), Some(LIST_PUBLISHED_SQL));

        let manage_only = BTreeSet::from([permissions::PORTAL_MANAGE.to_owned()]);
        assert!(!may_read(&manage_only));
        assert!(!may_manage(&manage_only));
        assert_eq!(faq_list_query(&manage_only), None);

        let both = BTreeSet::from([
            permissions::PORTAL_READ.to_owned(),
            permissions::PORTAL_MANAGE.to_owned(),
        ]);
        assert!(may_read(&both));
        assert!(may_manage(&both));
        assert_eq!(faq_list_query(&both), Some(LIST_ALL_SQL));
    }

    #[test]
    fn create_request_requires_and_trims_question_with_dotnet_defaults() {
        let request: FaqRequest = serde_json::from_value(json!({
            "question": "  Làm sao chấm công offline?  "
        }))
        .unwrap();
        assert_eq!(
            request.for_create().unwrap(),
            FaqValues {
                category: String::new(),
                question: "Làm sao chấm công offline?".to_owned(),
                answer: String::new(),
                order_no: 0,
                is_published: true,
            }
        );

        let missing: FaqRequest = serde_json::from_value(json!({})).unwrap();
        assert_eq!(missing.for_create(), Err("Thiếu câu hỏi."));
        let blank: FaqRequest = serde_json::from_value(json!({ "question": " \t\n " })).unwrap();
        assert_eq!(blank.for_create(), Err("Thiếu câu hỏi."));
    }

    #[test]
    fn update_allows_blank_question_and_preserves_untrimmed_other_text() {
        let request: FaqRequest = serde_json::from_value(json!({
            "category": "  Chấm công  ",
            "question": null,
            "answer": "  Nội dung  ",
            "orderNo": -2,
            "isPublished": false
        }))
        .unwrap();
        assert_eq!(
            request.for_update(),
            FaqValues {
                category: "  Chấm công  ".to_owned(),
                question: String::new(),
                answer: "  Nội dung  ".to_owned(),
                order_no: -2,
                is_published: false,
            }
        );
    }

    #[test]
    fn faq_response_uses_camel_case_wire_names() {
        let id = Uuid::parse_str("00112233-4455-6677-8899-aabbccddeeff").unwrap();
        let dto = FaqDto {
            id,
            category: "Chấm công".to_owned(),
            question: "Hỏi".to_owned(),
            answer: "Đáp".to_owned(),
            order_no: 7,
            is_published: true,
        };
        assert_eq!(
            serde_json::to_value(dto).unwrap(),
            json!({
                "id": "00112233-4455-6677-8899-aabbccddeeff",
                "category": "Chấm công",
                "question": "Hỏi",
                "answer": "Đáp",
                "orderNo": 7,
                "isPublished": true
            })
        );
    }

    #[test]
    fn status_response_uses_the_expected_wire_shape() {
        let server_time = DateTime::parse_from_rfc3339("2026-08-24T03:04:05Z")
            .unwrap()
            .with_timezone(&Utc);
        let value = serde_json::to_value(HelpStatusDto {
            db: "ok",
            server_time,
        })
        .unwrap();
        assert_eq!(
            value,
            json!({
                "db": "ok",
                "serverTime": "2026-08-24T03:04:05.000Z"
            })
        );
        assert_eq!(
            DATABASE_UNAVAILABLE_MESSAGE,
            "Khong ket noi duoc co so du lieu PostgreSQL."
        );
    }

    #[test]
    fn guid_route_constraint_and_audit_literals_match_dotnet() {
        assert!(parse_faq_id("00112233-4455-6677-8899-aabbccddeeff").is_some());
        assert!(parse_faq_id("not-a-guid").is_none());
        assert_eq!(
            (AUDIT_CREATE, AUDIT_UPDATE, AUDIT_DELETE, AUDIT_ENTITY),
            ("Tạo FAQ", "Sửa FAQ", "Xóa FAQ", "Faq")
        );
    }
}
