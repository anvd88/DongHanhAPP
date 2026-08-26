//! Native penalty-refund routes, ported from `PenaltyRefundEndpoints.cs`.
//!
//! Mount this router behind `auth::require_auth`. The route-local layers keep
//! the original separation of duties: every endpoint needs `penalty.read`,
//! approval/rejection additionally need `payout.approve`, and marking a cash
//! refund paid needs `payout.pay`. Queue-wide reads are checked in the handler
//! because they also depend on active membership of an accounting department.

use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    body::Body,
    extract::{
        DefaultBodyLimit, FromRequestParts, Path, Query, State,
        rejection::{JsonRejection, QueryRejection},
    },
    http::{Request, StatusCode, request::Parts},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use chrono::{DateTime, NaiveDate, SecondsFormat, Utc};
use serde::{Deserialize, Deserializer, Serialize, Serializer, de};
use serde_json::json;
use sqlx::{FromRow, PgConnection, PgPool};
use std::{fmt, sync::Arc};
use uuid::Uuid;

const MAX_JSON_BODY_BYTES: usize = 16 * 1024 * 1024;
const PAYLOAD_TOO_LARGE_MESSAGE: &str = "Payload vượt giới hạn 16777216 byte.";
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";
const INVALID_SCOPE_MESSAGE: &str = "Phạm vi danh sách không hợp lệ.";
const ALREADY_PROCESSED_MESSAGE: &str = "Khoản hoàn không tồn tại hoặc đã được xử lý.";
const NOT_CASH_APPROVED_MESSAGE: &str =
    "Chỉ đánh dấu đã chi cho khoản đã duyệt hình thức tiền mặt.";
const EMPLOYEE_TIME_ZONE: &str = "Asia/Ho_Chi_Minh";

const LIST_PATH: &str = "/api/penalty-refunds";
const APPROVE_PATH: &str = "/api/penalty-refunds/{id}/approve";
const REJECT_PATH: &str = "/api/penalty-refunds/{id}/reject";
const MARK_PAID_PATH: &str = "/api/penalty-refunds/{id}/mark-paid";

const LIST_MINE_SQL: &str = r#"
    SELECT r.id, r.refund_no, r.employee_id, r.penalty_no, r.appeal_request_no,
           r.amount::text AS amount, r.reason, r.status, r.payout_method,
           r.applied_period, r.created_by, r.approved_by, r.note,
           r.created_at, r.decided_at,
           e.full_name AS emp_name, e.employee_code
    FROM hr_penalty_refunds r
    JOIN hr_employees e ON e.id = r.employee_id
    WHERE r.employee_id = $1
    ORDER BY r.created_at DESC
"#;

const LIST_QUEUE_SQL: &str = r#"
    SELECT r.id, r.refund_no, r.employee_id, r.penalty_no, r.appeal_request_no,
           r.amount::text AS amount, r.reason, r.status, r.payout_method,
           r.applied_period, r.created_by, r.approved_by, r.note,
           r.created_at, r.decided_at,
           e.full_name AS emp_name, e.employee_code
    FROM hr_penalty_refunds r
    JOIN hr_employees e ON e.id = r.employee_id
    WHERE r.status IN ('PendingAccounting', 'Approved')
    ORDER BY r.created_at DESC
"#;

const LIST_ALL_SQL: &str = r#"
    SELECT r.id, r.refund_no, r.employee_id, r.penalty_no, r.appeal_request_no,
           r.amount::text AS amount, r.reason, r.status, r.payout_method,
           r.applied_period, r.created_by, r.approved_by, r.note,
           r.created_at, r.decided_at,
           e.full_name AS emp_name, e.employee_code
    FROM hr_penalty_refunds r
    JOIN hr_employees e ON e.id = r.employee_id
    ORDER BY r.created_at DESC
"#;

const APPROVE_SQL: &str = r#"
    UPDATE hr_penalty_refunds
    SET status = 'Approved',
        payout_method = $2,
        approved_by = $3,
        note = $4,
        decided_at = CURRENT_TIMESTAMP
    WHERE id = $1 AND status = 'PendingAccounting'
"#;

const REJECT_SQL: &str = r#"
    UPDATE hr_penalty_refunds
    SET status = 'Rejected',
        approved_by = $2,
        note = $3,
        decided_at = CURRENT_TIMESTAMP
    WHERE id = $1 AND status = 'PendingAccounting'
"#;

const MARK_PAID_SQL: &str = r#"
    UPDATE hr_penalty_refunds
    SET status = 'Paid', decided_at = CURRENT_TIMESTAMP
    WHERE id = $1 AND status = 'Approved' AND payout_method = 'cash'
"#;

const IS_ACCOUNTING_SQL: &str = r#"
    SELECT EXISTS(
        SELECT 1
        FROM hr_employees e
        JOIN hr_departments d ON d.id = e.department_id
        WHERE lower(e.username) = lower($1)
          AND e.status = 'Active'
          AND d.is_accounting = TRUE
    )
"#;

const AUDIT_SQL: &str = r#"
    INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
    VALUES (CURRENT_TIMESTAMP, $1, $2, 'PenaltyRefund', $3, $4)
"#;

const AUDIT_APPROVE: &str = "Duyệt hoàn tiền phạt";
const AUDIT_REJECT: &str = "Từ chối hoàn tiền phạt";
const AUDIT_MARK_PAID: &str = "Chi tiền mặt hoàn phạt";

#[cfg(test)]
const ROUTE_CONTRACTS: &[(&str, &str)] = &[
    ("GET", LIST_PATH),
    ("POST", APPROVE_PATH),
    ("POST", REJECT_PATH),
    ("POST", MARK_PAID_PATH),
];

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(LIST_PATH, get(list_refunds))
        .route(
            APPROVE_PATH,
            post(approve_refund).route_layer(middleware::from_fn(require_payout_approve)),
        )
        .route(
            REJECT_PATH,
            post(reject_refund).route_layer(middleware::from_fn(require_payout_approve)),
        )
        .route(
            MARK_PAID_PATH,
            post(mark_paid).route_layer(middleware::from_fn(require_payout_pay)),
        )
        .route_layer(middleware::from_fn(require_penalty_read))
        .layer(DefaultBodyLimit::max(MAX_JSON_BODY_BYTES))
}

async fn require_penalty_read(request: Request<Body>, next: Next) -> Response {
    require_permission(request, next, permissions::PENALTY_READ).await
}

async fn require_payout_approve(request: Request<Body>, next: Next) -> Response {
    require_permission(request, next, permissions::PAYOUT_APPROVE).await
}

async fn require_payout_pay(request: Request<Body>, next: Next) -> Response {
    require_permission(request, next, permissions::PAYOUT_PAY).await
}

async fn require_permission(
    request: Request<Body>,
    next: Next,
    required: &'static str,
) -> Response {
    let Some(auth) = request.extensions().get::<AuthContext>() else {
        return StatusCode::UNAUTHORIZED.into_response();
    };
    if !auth.permissions.contains(required) {
        return StatusCode::FORBIDDEN.into_response();
    }
    next.run(request).await
}

#[derive(Debug, Default, Deserialize)]
struct ListRefundsQuery {
    #[serde(alias = "Scope")]
    scope: Option<String>,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum RefundScope {
    Mine,
    Queue,
    All,
}

fn parse_scope(value: Option<&str>) -> Result<RefundScope, ()> {
    let value = value.unwrap_or_default().trim();
    if value.is_empty() || value.eq_ignore_ascii_case("mine") {
        Ok(RefundScope::Mine)
    } else if value.eq_ignore_ascii_case("queue") {
        Ok(RefundScope::Queue)
    } else if value.eq_ignore_ascii_case("all") {
        Ok(RefundScope::All)
    } else {
        Err(())
    }
}

#[derive(Debug, Default, Eq, PartialEq)]
struct ApproveRefundRequest {
    payout_method: Option<String>,
    note: Option<String>,
}

/// ASP.NET's web JSON defaults match DTO property names case-insensitively.
/// Serde's derive only covers exact camel/Pascal case, so use a tiny visitor to
/// retain compatibility with older clients that sent arbitrary casing.
impl<'de> Deserialize<'de> for ApproveRefundRequest {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        struct RequestVisitor;

        impl<'de> de::Visitor<'de> for RequestVisitor {
            type Value = ApproveRefundRequest;

            fn expecting(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
                formatter.write_str("an approve-refund JSON object")
            }

            fn visit_map<A>(self, mut map: A) -> Result<Self::Value, A::Error>
            where
                A: de::MapAccess<'de>,
            {
                let mut request = ApproveRefundRequest::default();
                while let Some(key) = map.next_key::<String>()? {
                    if key.eq_ignore_ascii_case("payoutMethod") {
                        request.payout_method = map.next_value()?;
                    } else if key.eq_ignore_ascii_case("note") {
                        request.note = map.next_value()?;
                    } else {
                        let _: de::IgnoredAny = map.next_value()?;
                    }
                }
                Ok(request)
            }
        }

        deserializer.deserialize_map(RequestVisitor)
    }
}

/// A UUID extractor whose rejection mirrors ASP.NET's `{id:guid}` constraint.
struct RefundId(Uuid);

impl<S> FromRequestParts<S> for RefundId
where
    S: Send + Sync,
{
    type Rejection = StatusCode;

    async fn from_request_parts(parts: &mut Parts, state: &S) -> Result<Self, Self::Rejection> {
        let Path(raw) = Path::<String>::from_request_parts(parts, state)
            .await
            .map_err(|_| StatusCode::NOT_FOUND)?;
        Uuid::parse_str(&raw)
            .map(Self)
            .map_err(|_| StatusCode::NOT_FOUND)
    }
}

#[derive(Debug, FromRow)]
struct RefundRow {
    id: Uuid,
    refund_no: Option<String>,
    employee_id: Uuid,
    penalty_no: Option<String>,
    appeal_request_no: Option<String>,
    amount: Option<String>,
    reason: Option<String>,
    status: Option<String>,
    payout_method: Option<String>,
    applied_period: Option<String>,
    created_by: Option<String>,
    approved_by: Option<String>,
    note: Option<String>,
    created_at: DateTime<Utc>,
    decided_at: Option<DateTime<Utc>>,
    emp_name: Option<String>,
    employee_code: Option<String>,
}

#[derive(Debug, Eq, PartialEq)]
struct RefundDto {
    id: Uuid,
    refund_no: String,
    employee_id: Uuid,
    employee_name: String,
    employee_code: String,
    penalty_no: String,
    appeal_request_no: String,
    amount: String,
    reason: String,
    status: String,
    payout_method: String,
    applied_period: String,
    created_by: String,
    approved_by: String,
    note: String,
    created_at: DateTime<Utc>,
    decided_at: Option<DateTime<Utc>>,
}

impl TryFrom<RefundRow> for RefundDto {
    type Error = InvalidStoredAmount;

    fn try_from(row: RefundRow) -> Result<Self, Self::Error> {
        let amount = row.amount.unwrap_or_else(|| "0".to_owned());
        if !is_json_decimal(&amount) {
            return Err(InvalidStoredAmount);
        }
        Ok(Self {
            id: row.id,
            refund_no: row.refund_no.unwrap_or_default(),
            employee_id: row.employee_id,
            employee_name: row.emp_name.unwrap_or_default(),
            employee_code: row.employee_code.unwrap_or_default(),
            penalty_no: row.penalty_no.unwrap_or_default(),
            appeal_request_no: row.appeal_request_no.unwrap_or_default(),
            amount,
            reason: row.reason.unwrap_or_default(),
            status: row.status.unwrap_or_default(),
            payout_method: row.payout_method.unwrap_or_default(),
            applied_period: row.applied_period.unwrap_or_default(),
            created_by: row.created_by.unwrap_or_default(),
            approved_by: row.approved_by.unwrap_or_default(),
            note: row.note.unwrap_or_default(),
            created_at: row.created_at,
            decided_at: row.decided_at,
        })
    }
}

#[derive(Clone, Copy, Debug)]
struct InvalidStoredAmount;

async fn list_refunds(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    query: Result<Query<ListRefundsQuery>, QueryRejection>,
) -> Response {
    let Query(query) = match query {
        Ok(query) => query,
        Err(_) => return StatusCode::BAD_REQUEST.into_response(),
    };

    // The .NET handler opens its connection before normalizing the scope.
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for refund list", error),
    };
    let scope = match parse_scope(query.scope.as_deref()) {
        Ok(scope) => scope,
        Err(()) => return bad_request(INVALID_SCOPE_MESSAGE),
    };

    let rows = match scope {
        RefundScope::Mine => {
            let employee_id = match ensure_employee_for_user(&mut connection, &auth.username).await
            {
                Ok(employee_id) => employee_id,
                Err(error) => return database_failure("resolve employee for refund list", error),
            };
            sqlx::query_as::<_, RefundRow>(LIST_MINE_SQL)
                .bind(employee_id)
                .fetch_all(&mut *connection)
                .await
        }
        RefundScope::Queue | RefundScope::All => {
            if !auth.permissions.contains(permissions::PAYOUT_READ) {
                return StatusCode::FORBIDDEN.into_response();
            }
            match is_accounting(&mut connection, &auth.username).await {
                Ok(true) => {}
                Ok(false) => return StatusCode::FORBIDDEN.into_response(),
                Err(error) => {
                    return database_failure("authorize accounting refund list", error);
                }
            }
            let sql = match scope {
                RefundScope::Queue => LIST_QUEUE_SQL,
                RefundScope::All => LIST_ALL_SQL,
                RefundScope::Mine => unreachable!(),
            };
            sqlx::query_as::<_, RefundRow>(sql)
                .fetch_all(&mut *connection)
                .await
        }
    };

    let rows = match rows {
        Ok(rows) => rows,
        Err(error) => return database_failure("list penalty refunds", error),
    };
    let refunds = match rows
        .into_iter()
        .map(RefundDto::try_from)
        .collect::<Result<Vec<_>, _>>()
    {
        Ok(refunds) => refunds,
        Err(_) => return stored_data_failure("serialize penalty-refund amount"),
    };
    refund_list_response(&refunds)
}

async fn approve_refund(
    RefundId(id): RefundId,
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<ApproveRefundRequest>, JsonRejection>,
) -> Response {
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection to approve refund", error),
    };
    match is_accounting(&mut connection, &auth.username).await {
        Ok(true) => {}
        Ok(false) => return StatusCode::FORBIDDEN.into_response(),
        Err(error) => return database_failure("authorize refund approval", error),
    }

    let payout_method = normalize_payout_method(request.payout_method.as_deref());
    let result = sqlx::query(APPROVE_SQL)
        .bind(id)
        .bind(payout_method)
        .bind(&auth.username)
        .bind(request.note.unwrap_or_default())
        .execute(&mut *connection)
        .await;
    let affected = match result {
        Ok(result) => result.rows_affected(),
        Err(error) => return database_failure("approve penalty refund", error),
    };
    if affected == 0 {
        return bad_request(ALREADY_PROCESSED_MESSAGE);
    }

    // The UPDATE has committed before audit. The existing table trigger emits
    // realtime scope `hr`; duplicating that event in-process would notify twice.
    drop(connection);
    record_audit(&state.pool, &auth.username, id, AUDIT_APPROVE).await;
    StatusCode::NO_CONTENT.into_response()
}

async fn reject_refund(
    RefundId(id): RefundId,
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<ApproveRefundRequest>, JsonRejection>,
) -> Response {
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection to reject refund", error),
    };
    match is_accounting(&mut connection, &auth.username).await {
        Ok(true) => {}
        Ok(false) => return StatusCode::FORBIDDEN.into_response(),
        Err(error) => return database_failure("authorize refund rejection", error),
    }

    let result = sqlx::query(REJECT_SQL)
        .bind(id)
        .bind(&auth.username)
        .bind(request.note.unwrap_or_default())
        .execute(&mut *connection)
        .await;
    let affected = match result {
        Ok(result) => result.rows_affected(),
        Err(error) => return database_failure("reject penalty refund", error),
    };
    if affected == 0 {
        return bad_request(ALREADY_PROCESSED_MESSAGE);
    }

    drop(connection);
    record_audit(&state.pool, &auth.username, id, AUDIT_REJECT).await;
    StatusCode::NO_CONTENT.into_response()
}

async fn mark_paid(
    RefundId(id): RefundId,
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection to pay refund", error),
    };
    match is_accounting(&mut connection, &auth.username).await {
        Ok(true) => {}
        Ok(false) => return StatusCode::FORBIDDEN.into_response(),
        Err(error) => return database_failure("authorize refund cash payment", error),
    }

    let result = sqlx::query(MARK_PAID_SQL)
        .bind(id)
        .execute(&mut *connection)
        .await;
    let affected = match result {
        Ok(result) => result.rows_affected(),
        Err(error) => return database_failure("mark penalty refund paid", error),
    };
    if affected == 0 {
        return bad_request(NOT_CASH_APPROVED_MESSAGE);
    }

    drop(connection);
    record_audit(&state.pool, &auth.username, id, AUDIT_MARK_PAID).await;
    StatusCode::NO_CONTENT.into_response()
}

async fn is_accounting(connection: &mut PgConnection, username: &str) -> Result<bool, sqlx::Error> {
    sqlx::query_scalar::<_, bool>(IS_ACCOUNTING_SQL)
        .bind(username)
        .fetch_one(connection)
        .await
}

#[derive(FromRow)]
struct EmployeeSeedRow {
    id: Uuid,
    full_name: Option<String>,
    email: Option<String>,
    account_date: Option<NaiveDate>,
}

/// Mirrors `HrEndpoints.EnsureEmployeeForUser`, including its lazy profile
/// creation and conflict-safe reread for simultaneous first access.
async fn ensure_employee_for_user(
    connection: &mut PgConnection,
    username: &str,
) -> Result<Uuid, sqlx::Error> {
    if let Some(existing) =
        sqlx::query_scalar::<_, Uuid>("SELECT id FROM hr_employees WHERE username = $1 LIMIT 1")
            .bind(username)
            .fetch_optional(&mut *connection)
            .await?
    {
        return Ok(existing);
    }

    let seed = sqlx::query_as::<_, EmployeeSeedRow>(
        r#"
        SELECT id, full_name, email,
               (created_at AT TIME ZONE $2)::date AS account_date
        FROM app_users
        WHERE username = $1 AND is_deleted = FALSE
        LIMIT 1
        "#,
    )
    .bind(username)
    .bind(EMPLOYEE_TIME_ZONE)
    .fetch_optional(&mut *connection)
    .await?;

    let mut user_id = None;
    let mut full_name = username.to_owned();
    let mut email = String::new();
    let mut hire_date = None;
    if let Some(seed) = seed {
        user_id = Some(seed.id);
        if seed
            .full_name
            .as_deref()
            .is_some_and(|value| !value.trim().is_empty())
        {
            full_name = seed.full_name.unwrap_or_default();
        }
        email = seed.email.unwrap_or_default();
        hire_date = seed.account_date;
    }

    let id = Uuid::new_v4();
    let sequence = sqlx::query_scalar::<_, i64>("SELECT nextval('hr_employee_code_seq')")
        .fetch_one(&mut *connection)
        .await?;
    let employee_code = format!("NV{sequence:04}");
    sqlx::query(
        r#"
        INSERT INTO hr_employees
            (id, employee_code, user_id, username, full_name, email, hire_date, status)
        VALUES ($1, $2, $3, $4, $5, $6, $7, 'Active')
        ON CONFLICT (username) WHERE username <> '' DO NOTHING
        "#,
    )
    .bind(id)
    .bind(employee_code)
    .bind(user_id)
    .bind(username)
    .bind(full_name)
    .bind(email)
    .bind(hire_date)
    .execute(&mut *connection)
    .await?;

    Ok(
        sqlx::query_scalar::<_, Uuid>("SELECT id FROM hr_employees WHERE username = $1 LIMIT 1")
            .bind(username)
            .fetch_optional(&mut *connection)
            .await?
            .unwrap_or(id),
    )
}

fn normalize_payout_method(value: Option<&str>) -> &'static str {
    if value == Some("cash") {
        "cash"
    } else {
        "payroll"
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum RefundJsonError {
    PayloadTooLarge,
    Status(StatusCode),
}

impl IntoResponse for RefundJsonError {
    fn into_response(self) -> Response {
        match self {
            Self::PayloadTooLarge => (
                StatusCode::PAYLOAD_TOO_LARGE,
                Json(json!({ "message": PAYLOAD_TOO_LARGE_MESSAGE })),
            )
                .into_response(),
            Self::Status(status) => status.into_response(),
        }
    }
}

fn json_payload<T>(payload: Result<Json<T>, JsonRejection>) -> Result<T, RefundJsonError> {
    match payload {
        Ok(Json(request)) => Ok(request),
        Err(rejection) => {
            if rejection.status() == StatusCode::PAYLOAD_TOO_LARGE {
                return Err(RefundJsonError::PayloadTooLarge);
            }
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(RefundJsonError::Status(status))
        }
    }
}

fn bad_request(message: &'static str) -> Response {
    (StatusCode::BAD_REQUEST, Json(json!({ "message": message }))).into_response()
}

async fn record_audit(pool: &PgPool, username: &str, id: Uuid, action: &'static str) {
    let details = format!("{action} (web).");
    if let Err(error) = sqlx::query(AUDIT_SQL)
        .bind(username)
        .bind(action)
        .bind(id.to_string())
        .bind(details)
        .execute(pool)
        .await
    {
        // `Database.RecordAudit` is intentionally best-effort in the existing
        // backend and must not roll back an already committed refund decision.
        tracing::warn!(%error, action, refund_id = %id, "could not record penalty-refund audit");
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native penalty-refund database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

fn stored_data_failure(operation: &'static str) -> Response {
    tracing::warn!(operation, "invalid stored penalty-refund data");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

/// `numeric(18,2)` is read as text so even values above JavaScript/Rust's
/// exactly representable f64 range stay byte-for-byte decimal numbers. This
/// validator keeps the trusted database string inside JSON's number grammar.
fn is_json_decimal(value: &str) -> bool {
    let bytes = value.as_bytes();
    let mut index = usize::from(bytes.first() == Some(&b'-'));
    if index == bytes.len() {
        return false;
    }

    if bytes[index] == b'0' {
        index += 1;
        if bytes.get(index).is_some_and(u8::is_ascii_digit) {
            return false;
        }
    } else if bytes[index].is_ascii_digit() {
        while bytes.get(index).is_some_and(u8::is_ascii_digit) {
            index += 1;
        }
    } else {
        return false;
    }

    if bytes.get(index) == Some(&b'.') {
        index += 1;
        let fraction_start = index;
        while bytes.get(index).is_some_and(u8::is_ascii_digit) {
            index += 1;
        }
        if index == fraction_start {
            return false;
        }
    }
    index == bytes.len()
}

fn refund_list_response(refunds: &[RefundDto]) -> Response {
    match encode_refund_list(refunds) {
        Ok(body) => (
            [(axum::http::header::CONTENT_TYPE, "application/json")],
            body,
        )
            .into_response(),
        Err(error) => {
            tracing::warn!(%error, "could not encode penalty-refund response");
            stored_data_failure("encode penalty-refund response")
        }
    }
}

/// Manual only at the `amount` slot: Serde JSON without `arbitrary_precision`
/// would round `numeric(18,2)` through f64. Every string/date/UUID still goes
/// through Serde's normal escaping and representation.
fn encode_refund_list(refunds: &[RefundDto]) -> Result<Vec<u8>, serde_json::Error> {
    let mut output = Vec::new();
    output.push(b'[');
    for (index, refund) in refunds.iter().enumerate() {
        if index != 0 {
            output.push(b',');
        }
        output.extend_from_slice(b"{\"id\":");
        write_json(&mut output, &refund.id)?;
        output.extend_from_slice(b",\"refundNo\":");
        write_json(&mut output, &refund.refund_no)?;
        output.extend_from_slice(b",\"employeeId\":");
        write_json(&mut output, &refund.employee_id)?;
        output.extend_from_slice(b",\"employeeName\":");
        write_json(&mut output, &refund.employee_name)?;
        output.extend_from_slice(b",\"employeeCode\":");
        write_json(&mut output, &refund.employee_code)?;
        output.extend_from_slice(b",\"penaltyNo\":");
        write_json(&mut output, &refund.penalty_no)?;
        output.extend_from_slice(b",\"appealRequestNo\":");
        write_json(&mut output, &refund.appeal_request_no)?;
        output.extend_from_slice(b",\"amount\":");
        output.extend_from_slice(refund.amount.as_bytes());
        output.extend_from_slice(b",\"reason\":");
        write_json(&mut output, &refund.reason)?;
        output.extend_from_slice(b",\"status\":");
        write_json(&mut output, &refund.status)?;
        output.extend_from_slice(b",\"payoutMethod\":");
        write_json(&mut output, &refund.payout_method)?;
        output.extend_from_slice(b",\"appliedPeriod\":");
        write_json(&mut output, &refund.applied_period)?;
        output.extend_from_slice(b",\"createdBy\":");
        write_json(&mut output, &refund.created_by)?;
        output.extend_from_slice(b",\"approvedBy\":");
        write_json(&mut output, &refund.approved_by)?;
        output.extend_from_slice(b",\"note\":");
        write_json(&mut output, &refund.note)?;
        output.extend_from_slice(b",\"createdAt\":");
        write_json(&mut output, &DotNetUtc(&refund.created_at))?;
        if let Some(decided_at) = &refund.decided_at {
            output.extend_from_slice(b",\"decidedAt\":");
            write_json(&mut output, &DotNetUtc(decided_at))?;
        }
        output.push(b'}');
    }
    output.push(b']');
    Ok(output)
}

fn write_json<T>(output: &mut Vec<u8>, value: &T) -> Result<(), serde_json::Error>
where
    T: Serialize + ?Sized,
{
    serde_json::to_writer(output, value)
}

struct DotNetUtc<'a>(&'a DateTime<Utc>);

impl Serialize for DotNetUtc<'_> {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_str(&self.0.to_rfc3339_opts(SecondsFormat::Millis, true))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::auth::{AuthService, AuthSettings, TokenSource};
    use axum::{
        body::Body,
        http::{Request, header},
    };
    use chrono::TimeZone;
    use serde_json::Value;
    use sqlx::postgres::PgPoolOptions;
    use std::collections::BTreeSet;
    use tower::ServiceExt;

    fn test_state() -> Arc<AppState> {
        let pool = PgPoolOptions::new()
            .max_connections(1)
            .connect_lazy("postgres://postgres@127.0.0.1:1/ketoanmini")
            .unwrap();
        let auth = AuthService::new(AuthSettings {
            jwt_key: b"test-only-key-with-at-least-thirty-two-bytes".to_vec(),
            issuer: "KetoanMini.Web".to_owned(),
            audience: "KetoanMini.Web".to_owned(),
            web_expire_hours: 168,
            session_idle_days: 7,
            cookie_auth: true,
        })
        .unwrap();
        Arc::new(AppState::new(pool, None, auth))
    }

    fn auth_context(permission_values: &[&str]) -> AuthContext {
        AuthContext {
            user_id: Some(Uuid::nil()),
            username: "accountant".to_owned(),
            full_name: "Accountant".to_owned(),
            sid: Some("app:accountant".to_owned()),
            roles: vec![],
            permissions: permission_values
                .iter()
                .map(|value| (*value).to_owned())
                .collect::<BTreeSet<_>>(),
            source: TokenSource::Bearer,
            account_state_verified: true,
            session_alive: true,
        }
    }

    #[test]
    fn route_matrix_matches_all_four_dotnet_endpoints() {
        assert_eq!(
            ROUTE_CONTRACTS,
            &[
                ("GET", "/api/penalty-refunds"),
                ("POST", "/api/penalty-refunds/{id}/approve"),
                ("POST", "/api/penalty-refunds/{id}/reject"),
                ("POST", "/api/penalty-refunds/{id}/mark-paid"),
            ]
        );
    }

    #[tokio::test]
    async fn route_layers_fail_closed_before_any_database_access() {
        let unauthenticated = router()
            .with_state(test_state())
            .oneshot(
                Request::builder()
                    .uri(LIST_PATH)
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(unauthenticated.status(), StatusCode::UNAUTHORIZED);

        let no_penalty_read = router()
            .with_state(test_state())
            .oneshot(
                Request::builder()
                    .uri(LIST_PATH)
                    .extension(auth_context(&[]))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(no_penalty_read.status(), StatusCode::FORBIDDEN);

        let approve_without_duty = router()
            .with_state(test_state())
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri(APPROVE_PATH.replace("{id}", &Uuid::nil().to_string()))
                    .header(header::CONTENT_TYPE, "application/json")
                    .extension(auth_context(&[permissions::PENALTY_READ]))
                    .body(Body::from("{}"))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(approve_without_duty.status(), StatusCode::FORBIDDEN);

        let pay_with_approve_only = router()
            .with_state(test_state())
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri(MARK_PAID_PATH.replace("{id}", &Uuid::nil().to_string()))
                    .extension(auth_context(&[
                        permissions::PENALTY_READ,
                        permissions::PAYOUT_APPROVE,
                    ]))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(pay_with_approve_only.status(), StatusCode::FORBIDDEN);
    }

    #[test]
    fn scopes_default_normalize_and_reject_exactly_like_dotnet() {
        assert_eq!(parse_scope(None), Ok(RefundScope::Mine));
        assert_eq!(parse_scope(Some("   ")), Ok(RefundScope::Mine));
        assert_eq!(parse_scope(Some(" MINE ")), Ok(RefundScope::Mine));
        assert_eq!(parse_scope(Some(" Queue ")), Ok(RefundScope::Queue));
        assert_eq!(parse_scope(Some("ALL")), Ok(RefundScope::All));
        assert_eq!(parse_scope(Some("department")), Err(()));
    }

    #[test]
    fn request_fields_are_case_insensitive_and_unknown_fields_are_ignored() {
        let request: ApproveRefundRequest = serde_json::from_value(json!({
            "PAYOUTMETHOD": "cash",
            "NoTe": "Đã kiểm tra",
            "futureField": { "anything": true }
        }))
        .unwrap();
        assert_eq!(
            request,
            ApproveRefundRequest {
                payout_method: Some("cash".to_owned()),
                note: Some("Đã kiểm tra".to_owned()),
            }
        );
        assert!(serde_json::from_value::<ApproveRefundRequest>(json!(null)).is_err());
        assert!(serde_json::from_value::<ApproveRefundRequest>(json!({ "note": 42 })).is_err());
    }

    #[test]
    fn payout_selection_is_deliberately_case_sensitive_and_untrimmed() {
        assert_eq!(normalize_payout_method(Some("cash")), "cash");
        assert_eq!(normalize_payout_method(Some("Cash")), "payroll");
        assert_eq!(normalize_payout_method(Some(" cash ")), "payroll");
        assert_eq!(normalize_payout_method(None), "payroll");
    }

    #[test]
    fn mutation_sql_uses_atomic_compare_and_set_workflow_guards() {
        assert!(APPROVE_SQL.contains("status = 'PendingAccounting'"));
        assert!(REJECT_SQL.contains("status = 'PendingAccounting'"));
        assert!(MARK_PAID_SQL.contains("status = 'Approved'"));
        assert!(MARK_PAID_SQL.contains("payout_method = 'cash'"));
        assert!(LIST_MINE_SQL.contains("r.employee_id = $1"));
        assert!(LIST_QUEUE_SQL.contains("'PendingAccounting', 'Approved'"));
        assert!(IS_ACCOUNTING_SQL.contains("e.status = 'Active'"));
        assert!(IS_ACCOUNTING_SQL.contains("d.is_accounting = TRUE"));
    }

    #[test]
    fn exact_decimal_validator_accepts_database_money_and_blocks_json_injection() {
        for valid in ["0", "0.00", "400000.50", "9999999999999999.99", "-12.30"] {
            assert!(is_json_decimal(valid), "{valid}");
        }
        for invalid in [
            "",
            "-",
            ".5",
            "1.",
            "+1",
            "01.00",
            "NaN",
            "1e2",
            "0],\"admin\":true",
        ] {
            assert!(!is_json_decimal(invalid), "{invalid}");
        }
    }

    #[test]
    fn list_json_preserves_decimal_precision_timestamp_and_null_omission() {
        let created_at = Utc.with_ymd_and_hms(2026, 8, 24, 3, 4, 5).unwrap()
            + chrono::Duration::microseconds(123_456);
        let refund = RefundDto {
            id: Uuid::nil(),
            refund_no: "HP00001".to_owned(),
            employee_id: Uuid::from_u128(1),
            employee_name: "Nguyễn \"An\"".to_owned(),
            employee_code: "NV0001".to_owned(),
            penalty_no: "P00001".to_owned(),
            appeal_request_no: "KN00001".to_owned(),
            amount: "9999999999999999.99".to_owned(),
            reason: "Điều chỉnh".to_owned(),
            status: "PendingAccounting".to_owned(),
            payout_method: String::new(),
            applied_period: String::new(),
            created_by: "admin".to_owned(),
            approved_by: String::new(),
            note: String::new(),
            created_at,
            decided_at: None,
        };

        let bytes = encode_refund_list(&[refund]).unwrap();
        let raw = String::from_utf8(bytes.clone()).unwrap();
        assert!(raw.contains("\"amount\":9999999999999999.99"));
        assert!(raw.contains("\"createdAt\":\"2026-08-24T03:04:05.123Z\""));
        assert!(!raw.contains("decidedAt"));

        let value: Value = serde_json::from_slice(&bytes).unwrap();
        assert_eq!(value[0]["employeeName"], "Nguyễn \"An\"");
        assert_eq!(value[0]["refundNo"], "HP00001");
    }

    #[test]
    fn audit_literals_and_payload_limit_match_existing_contract() {
        assert_eq!(MAX_JSON_BODY_BYTES, 16_777_216);
        assert_eq!(
            PAYLOAD_TOO_LARGE_MESSAGE,
            "Payload vượt giới hạn 16777216 byte."
        );
        assert_eq!(
            (AUDIT_APPROVE, AUDIT_REJECT, AUDIT_MARK_PAID),
            (
                "Duyệt hoàn tiền phạt",
                "Từ chối hoàn tiền phạt",
                "Chi tiền mặt hoàn phạt",
            )
        );
        assert!(AUDIT_SQL.contains("'PenaltyRefund'"));
    }

    #[test]
    fn select_projection_stays_in_sync_across_all_three_scopes() {
        for query in [LIST_MINE_SQL, LIST_QUEUE_SQL, LIST_ALL_SQL] {
            for column in [
                "r.refund_no",
                "r.employee_id",
                "r.penalty_no",
                "r.appeal_request_no",
                "r.amount::text",
                "r.created_at",
                "r.decided_at",
                "e.full_name AS emp_name",
                "e.employee_code",
            ] {
                assert!(query.contains(column), "missing {column}");
            }
        }
    }
}
