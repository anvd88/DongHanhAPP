use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    body::Body,
    extract::{
        FromRequestParts, Path, Query, State,
        rejection::{JsonRejection, QueryRejection},
    },
    http::{Request, StatusCode, request::Parts},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use chrono::NaiveDate;
use serde::{Deserialize, Serialize};
use serde_json::json;
use sqlx::{FromRow, PgConnection, PgPool};
use std::sync::Arc;
use uuid::Uuid;

const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";
const MISSING_ACCOUNT_NUMBER_MESSAGE: &str = "Vui lòng nhập số tài khoản.";
const EMPLOYEE_TIME_ZONE: &str = "Asia/Ho_Chi_Minh";

#[derive(Clone, Copy, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct BankInfo {
    code: &'static str,
    name: &'static str,
    short_name: &'static str,
}

const BANKS: [BankInfo; 2] = [
    BankInfo {
        code: "vietcombank",
        name: "Ngân hàng TMCP Ngoại thương Việt Nam",
        short_name: "Vietcombank",
    },
    BankInfo {
        code: "sacombank",
        name: "Ngân hàng TMCP Sài Gòn Thương Tín",
        short_name: "Sacombank",
    },
];

/// Native employee bank-account routes.
///
/// The caller must still place this router behind `auth::require_auth`. The
/// route-local layer mirrors the .NET group's `hr.self.access` policy and runs
/// before path, query, or JSON binding reaches a handler.
pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route("/api/bank-accounts/banks", get(list_banks))
        .route(
            "/api/bank-accounts",
            get(list_accounts).post(create_account),
        )
        .route(
            "/api/bank-accounts/{id}",
            axum::routing::put(update_account).delete(delete_account),
        )
        .route("/api/bank-accounts/{id}/default", post(set_default_account))
        .route_layer(middleware::from_fn(require_hr_self_access))
}

async fn require_hr_self_access(request: Request<Body>, next: Next) -> Response {
    let Some(auth) = request.extensions().get::<AuthContext>() else {
        return StatusCode::UNAUTHORIZED.into_response();
    };
    if !auth.permissions.contains(permissions::HR_SELF_ACCESS) {
        return StatusCode::FORBIDDEN.into_response();
    }
    next.run(request).await
}

async fn list_banks() -> Response {
    Json(BANKS).into_response()
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ListAccountsQuery {
    employee_id: Option<Uuid>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct SaveBankAccountRequest {
    employee_id: Option<Uuid>,
    bank: Option<String>,
    account_number: Option<String>,
    account_holder: Option<String>,
    branch: Option<String>,
    is_default: bool,
    note: Option<String>,
}

#[derive(FromRow)]
struct BankAccountRow {
    id: Uuid,
    employee_id: Uuid,
    bank: Option<String>,
    account_number: Option<String>,
    account_holder: Option<String>,
    branch: Option<String>,
    is_default: Option<bool>,
    note: Option<String>,
    emp_name: Option<String>,
    employee_code: Option<String>,
}

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct BankAccountResponse {
    id: Uuid,
    employee_id: Uuid,
    employee_name: String,
    employee_code: String,
    bank: String,
    account_number: String,
    account_holder: String,
    branch: String,
    is_default: bool,
    note: String,
}

impl From<BankAccountRow> for BankAccountResponse {
    fn from(row: BankAccountRow) -> Self {
        Self {
            id: row.id,
            employee_id: row.employee_id,
            employee_name: row.emp_name.unwrap_or_default(),
            employee_code: row.employee_code.unwrap_or_default(),
            bank: row.bank.unwrap_or_default(),
            account_number: row.account_number.unwrap_or_default(),
            account_holder: row.account_holder.unwrap_or_default(),
            branch: row.branch.unwrap_or_default(),
            is_default: row.is_default.unwrap_or(false),
            note: row.note.unwrap_or_default(),
        }
    }
}

#[derive(Serialize)]
struct CreatedResponse {
    id: Uuid,
}

async fn list_accounts(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    query: Result<Query<ListAccountsQuery>, QueryRejection>,
) -> Response {
    let Query(query) = match query {
        Ok(query) => query,
        Err(_) => return StatusCode::BAD_REQUEST.into_response(),
    };

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection to list bank accounts", error),
    };
    let employee_id = match resolve_employee(&mut connection, &auth, query.employee_id).await {
        Ok(Some(employee_id)) => employee_id,
        Ok(None) => return StatusCode::FORBIDDEN.into_response(),
        Err(error) => return database_failure("resolve employee for bank-account list", error),
    };

    let rows = sqlx::query_as::<_, BankAccountRow>(
        r#"
        SELECT b.id, b.employee_id, b.bank, b.account_number, b.account_holder, b.branch,
               b.is_default, b.note,
               e.full_name AS emp_name, e.employee_code
        FROM hr_bank_accounts b
        JOIN hr_employees e ON e.id = b.employee_id
        WHERE b.employee_id = $1
        ORDER BY b.is_default DESC, b.created_at
        "#,
    )
    .bind(employee_id)
    .fetch_all(&mut *connection)
    .await;

    match rows {
        Ok(rows) => Json(
            rows.into_iter()
                .map(BankAccountResponse::from)
                .collect::<Vec<_>>(),
        )
        .into_response(),
        Err(error) => database_failure("list employee bank accounts", error),
    }
}

async fn create_account(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<SaveBankAccountRequest>, JsonRejection>,
) -> Response {
    let request = match parse_json(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };

    // Resolve (and, for self-service, lazily create) the employee before
    // validating the account number, matching the existing handler ordering.
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection to create bank account", error),
    };
    let employee_id = match resolve_employee(&mut connection, &auth, request.employee_id).await {
        Ok(Some(employee_id)) => employee_id,
        Ok(None) => return StatusCode::FORBIDDEN.into_response(),
        Err(error) => return database_failure("resolve employee for bank-account creation", error),
    };

    let account_number = match normalize_account_number(request.account_number.as_deref()) {
        Ok(account_number) => account_number,
        Err(ValidationError::MissingAccountNumber) => return missing_account_number(),
    };

    let mut account_holder = trim_or_empty(request.account_holder.as_deref());
    if account_holder.is_empty() {
        let full_name = sqlx::query_scalar::<_, Option<String>>(
            "SELECT full_name FROM hr_employees WHERE id = $1",
        )
        .bind(employee_id)
        .fetch_optional(&mut *connection)
        .await;
        account_holder = match full_name {
            Ok(full_name) => full_name.flatten().unwrap_or_default().to_uppercase(),
            Err(error) => return database_failure("load employee bank-account holder", error),
        };
    }

    let bank = normalize_bank(request.bank.as_deref());
    let branch = trim_or_empty(request.branch.as_deref());
    // Notes are deliberately not trimmed in the original API.
    let note = request.note.unwrap_or_default();
    drop(connection);

    let mut transaction = match state.pool.begin().await {
        Ok(transaction) => transaction,
        Err(error) => return database_failure("begin bank-account creation", error),
    };

    // Serialize all default-selection changes for one employee. Existing .NET
    // schema installations have no partial unique index, so the row lock is the
    // only way to prevent concurrent requests from creating two defaults.
    if let Err(error) = lock_employee(&mut transaction, employee_id).await {
        return database_failure("lock employee for bank-account creation", error);
    }

    let count = sqlx::query_scalar::<_, i64>(
        "SELECT COUNT(*) FROM hr_bank_accounts WHERE employee_id = $1",
    )
    .bind(employee_id)
    .fetch_one(&mut *transaction)
    .await;
    let count = match count {
        Ok(count) => count,
        Err(error) => return database_failure("count employee bank accounts", error),
    };
    let make_default = should_make_default(request.is_default, count);

    if make_default && let Err(error) = clear_default(&mut transaction, employee_id).await {
        return database_failure("clear previous default bank account", error);
    }

    let id = Uuid::new_v4();
    let insert = sqlx::query(
        r#"
        INSERT INTO hr_bank_accounts
            (id, employee_id, bank, account_number, account_holder, branch, is_default, note)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
        "#,
    )
    .bind(id)
    .bind(employee_id)
    .bind(bank)
    .bind(account_number)
    .bind(account_holder)
    .bind(branch)
    .bind(make_default)
    .bind(note)
    .execute(&mut *transaction)
    .await;
    if let Err(error) = insert {
        return database_failure("insert employee bank account", error);
    }

    if let Err(error) = transaction.commit().await {
        return database_failure("commit bank-account creation", error);
    }

    record_audit(&state.pool, &auth.username, "Thêm tài khoản ngân hàng", id).await;
    Json(CreatedResponse { id }).into_response()
}

/// A UUID path extractor whose rejection is 404, matching ASP.NET's
/// `{id:guid}` route constraint. It also rejects before JSON body extraction.
struct AccountId(Uuid);

impl<S> FromRequestParts<S> for AccountId
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

async fn update_account(
    AccountId(id): AccountId,
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<SaveBankAccountRequest>, JsonRejection>,
) -> Response {
    let request = match parse_json(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };

    let mut transaction = match state.pool.begin().await {
        Ok(transaction) => transaction,
        Err(error) => return database_failure("begin bank-account update", error),
    };
    let employee_id = match lock_account_employee(&mut transaction, id).await {
        Ok(Some(employee_id)) => employee_id,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(error) => return database_failure("find bank-account owner for update", error),
    };
    let allowed = match can_manage(&mut transaction, &auth, employee_id).await {
        Ok(allowed) => allowed,
        Err(error) => return database_failure("authorize bank-account update", error),
    };
    if !allowed {
        return StatusCode::FORBIDDEN.into_response();
    }

    let account_number = match normalize_account_number(request.account_number.as_deref()) {
        Ok(account_number) => account_number,
        Err(ValidationError::MissingAccountNumber) => return missing_account_number(),
    };
    if request.is_default
        && let Err(error) = clear_default(&mut transaction, employee_id).await
    {
        return database_failure("clear previous default during bank-account update", error);
    }

    let update = sqlx::query(
        r#"
        UPDATE hr_bank_accounts
        SET bank = $2,
            account_number = $3,
            account_holder = $4,
            branch = $5,
            is_default = $6,
            note = $7,
            updated_at = CURRENT_TIMESTAMP
        WHERE id = $1
        "#,
    )
    .bind(id)
    .bind(normalize_bank(request.bank.as_deref()))
    .bind(account_number)
    .bind(trim_or_empty(request.account_holder.as_deref()))
    .bind(trim_or_empty(request.branch.as_deref()))
    .bind(request.is_default)
    .bind(request.note.unwrap_or_default())
    .execute(&mut *transaction)
    .await;
    let affected = match update {
        Ok(result) => result.rows_affected(),
        Err(error) => return database_failure("update employee bank account", error),
    };
    if affected == 0 {
        return StatusCode::NOT_FOUND.into_response();
    }
    if let Err(error) = transaction.commit().await {
        return database_failure("commit bank-account update", error);
    }

    record_audit(
        &state.pool,
        &auth.username,
        "Cập nhật tài khoản ngân hàng",
        id,
    )
    .await;
    StatusCode::NO_CONTENT.into_response()
}

async fn set_default_account(
    AccountId(id): AccountId,
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let mut transaction = match state.pool.begin().await {
        Ok(transaction) => transaction,
        Err(error) => return database_failure("begin default bank-account change", error),
    };
    let employee_id = match lock_account_employee(&mut transaction, id).await {
        Ok(Some(employee_id)) => employee_id,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(error) => return database_failure("find bank-account owner for default change", error),
    };
    let allowed = match can_manage(&mut transaction, &auth, employee_id).await {
        Ok(allowed) => allowed,
        Err(error) => return database_failure("authorize default bank-account change", error),
    };
    if !allowed {
        return StatusCode::FORBIDDEN.into_response();
    }

    if let Err(error) = clear_default(&mut transaction, employee_id).await {
        return database_failure("clear previous default bank account", error);
    }
    let update = sqlx::query(
        "UPDATE hr_bank_accounts SET is_default = TRUE, updated_at = CURRENT_TIMESTAMP WHERE id = $1",
    )
    .bind(id)
    .execute(&mut *transaction)
    .await;
    if let Err(error) = update {
        return database_failure("set default employee bank account", error);
    }
    if let Err(error) = transaction.commit().await {
        return database_failure("commit default bank-account change", error);
    }

    record_audit(
        &state.pool,
        &auth.username,
        "Đặt tài khoản ngân hàng mặc định",
        id,
    )
    .await;
    StatusCode::NO_CONTENT.into_response()
}

async fn delete_account(
    AccountId(id): AccountId,
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let mut transaction = match state.pool.begin().await {
        Ok(transaction) => transaction,
        Err(error) => return database_failure("begin bank-account deletion", error),
    };
    let employee_id = match lock_account_employee(&mut transaction, id).await {
        Ok(Some(employee_id)) => employee_id,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(error) => return database_failure("find bank-account owner for deletion", error),
    };
    let allowed = match can_manage(&mut transaction, &auth, employee_id).await {
        Ok(allowed) => allowed,
        Err(error) => return database_failure("authorize bank-account deletion", error),
    };
    if !allowed {
        return StatusCode::FORBIDDEN.into_response();
    }

    let was_default =
        sqlx::query_scalar::<_, bool>("SELECT is_default FROM hr_bank_accounts WHERE id = $1")
            .bind(id)
            .fetch_optional(&mut *transaction)
            .await;
    let was_default = match was_default {
        Ok(value) => value.unwrap_or(false),
        Err(error) => return database_failure("read deleted bank-account default state", error),
    };

    if let Err(error) = sqlx::query("DELETE FROM hr_bank_accounts WHERE id = $1")
        .bind(id)
        .execute(&mut *transaction)
        .await
    {
        return database_failure("delete employee bank account", error);
    }

    if was_default {
        let promote = sqlx::query(
            r#"
            UPDATE hr_bank_accounts
            SET is_default = TRUE
            WHERE id = (
                SELECT id
                FROM hr_bank_accounts
                WHERE employee_id = $1
                ORDER BY created_at
                LIMIT 1
            )
            "#,
        )
        .bind(employee_id)
        .execute(&mut *transaction)
        .await;
        if let Err(error) = promote {
            return database_failure("promote replacement default bank account", error);
        }
    }
    if let Err(error) = transaction.commit().await {
        return database_failure("commit bank-account deletion", error);
    }

    record_audit(&state.pool, &auth.username, "Xóa tài khoản ngân hàng", id).await;
    StatusCode::NO_CONTENT.into_response()
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum EmployeeSelection {
    SelfProfile,
    Delegated(Uuid),
    Forbidden,
}

fn select_employee(employee_id: Option<Uuid>, can_manage_payroll: bool) -> EmployeeSelection {
    match employee_id {
        Some(employee_id) if !employee_id.is_nil() && can_manage_payroll => {
            EmployeeSelection::Delegated(employee_id)
        }
        Some(employee_id) if !employee_id.is_nil() => EmployeeSelection::Forbidden,
        _ => EmployeeSelection::SelfProfile,
    }
}

async fn resolve_employee(
    connection: &mut PgConnection,
    auth: &AuthContext,
    employee_id: Option<Uuid>,
) -> Result<Option<Uuid>, sqlx::Error> {
    match select_employee(
        employee_id,
        auth.permissions.contains(permissions::PAYROLL_MANAGE),
    ) {
        EmployeeSelection::Delegated(employee_id) => Ok(Some(employee_id)),
        EmployeeSelection::Forbidden => Ok(None),
        EmployeeSelection::SelfProfile => ensure_employee_for_user(connection, &auth.username)
            .await
            .map(Some),
    }
}

#[derive(FromRow)]
struct EmployeeSeedRow {
    id: Uuid,
    full_name: Option<String>,
    email: Option<String>,
    account_date: Option<NaiveDate>,
}

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

/// Lock only the employee row, not the account row. Every mutating route takes
/// this same lock first, avoiding lock-order inversions while serializing all
/// default changes for one employee.
async fn lock_account_employee(
    connection: &mut PgConnection,
    account_id: Uuid,
) -> Result<Option<Uuid>, sqlx::Error> {
    sqlx::query_scalar::<_, Uuid>(
        r#"
        SELECT b.employee_id
        FROM hr_bank_accounts b
        JOIN hr_employees e ON e.id = b.employee_id
        WHERE b.id = $1
        FOR UPDATE OF e
        "#,
    )
    .bind(account_id)
    .fetch_optional(connection)
    .await
}

async fn lock_employee(
    connection: &mut PgConnection,
    employee_id: Uuid,
) -> Result<(), sqlx::Error> {
    let _ = sqlx::query_scalar::<_, Uuid>("SELECT id FROM hr_employees WHERE id = $1 FOR UPDATE")
        .bind(employee_id)
        .fetch_optional(connection)
        .await?;
    Ok(())
}

async fn can_manage(
    connection: &mut PgConnection,
    auth: &AuthContext,
    employee_id: Uuid,
) -> Result<bool, sqlx::Error> {
    if auth.permissions.contains(permissions::PAYROLL_MANAGE) {
        return Ok(true);
    }
    let owner =
        sqlx::query_scalar::<_, Option<String>>("SELECT username FROM hr_employees WHERE id = $1")
            .bind(employee_id)
            .fetch_optional(connection)
            .await?
            .flatten();
    Ok(can_manage_identity(false, owner.as_deref(), &auth.username))
}

fn can_manage_identity(
    can_manage_payroll: bool,
    owner_username: Option<&str>,
    authenticated_username: &str,
) -> bool {
    can_manage_payroll
        // Usernames generated by the system are ASCII. For legacy non-ASCII
        // usernames, accepting only exact code points is deliberately
        // fail-closed instead of introducing a Unicode case-fold collision.
        || owner_username
            .is_some_and(|owner| owner.eq_ignore_ascii_case(authenticated_username))
}

async fn clear_default(
    connection: &mut PgConnection,
    employee_id: Uuid,
) -> Result<(), sqlx::Error> {
    sqlx::query(
        "UPDATE hr_bank_accounts SET is_default = FALSE WHERE employee_id = $1 AND is_default = TRUE",
    )
    .bind(employee_id)
    .execute(connection)
    .await?;
    Ok(())
}

fn normalize_bank(code: Option<&str>) -> String {
    match code {
        // Preserve the .NET ordering: membership is checked before Trim().
        Some(code) if BANKS.iter().any(|bank| bank.code == code) => code.trim().to_owned(),
        _ => BANKS[0].code.to_owned(),
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ValidationError {
    MissingAccountNumber,
}

fn normalize_account_number(value: Option<&str>) -> Result<String, ValidationError> {
    let value = value.unwrap_or_default().trim();
    if value.is_empty() {
        Err(ValidationError::MissingAccountNumber)
    } else {
        Ok(value.to_owned())
    }
}

fn trim_or_empty(value: Option<&str>) -> String {
    value.unwrap_or_default().trim().to_owned()
}

fn should_make_default(requested_default: bool, current_count: i64) -> bool {
    requested_default || current_count == 0
}

fn parse_json(
    payload: Result<Json<SaveBankAccountRequest>, JsonRejection>,
) -> Result<SaveBankAccountRequest, StatusCode> {
    match payload {
        Ok(Json(request)) => Ok(request),
        Err(rejection) => {
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(status)
        }
    }
}

fn missing_account_number() -> Response {
    (
        StatusCode::BAD_REQUEST,
        Json(json!({ "message": MISSING_ACCOUNT_NUMBER_MESSAGE })),
    )
        .into_response()
}

async fn record_audit(pool: &PgPool, username: &str, action: &str, id: Uuid) {
    let details = format!("{action} (web).");
    if let Err(error) = sqlx::query(
        r#"
        INSERT INTO audit_logs
            (occurred_at, username, action, entity, entity_name, details)
        VALUES (CURRENT_TIMESTAMP, $1, $2, 'BankAccount', $3, $4)
        "#,
    )
    .bind(username)
    .bind(action)
    .bind(id.to_string())
    .bind(details)
    .execute(pool)
    .await
    {
        // Audit is explicitly best-effort in the existing API and must never
        // turn an already committed business operation into a failure.
        tracing::warn!(%error, action, account_id = %id, "could not record bank-account audit");
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native bank-account database operation failed");
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

    #[test]
    fn bank_catalog_keeps_the_existing_json_contract() {
        assert_eq!(
            serde_json::to_value(BANKS).unwrap(),
            json!([
                {
                    "code": "vietcombank",
                    "name": "Ngân hàng TMCP Ngoại thương Việt Nam",
                    "shortName": "Vietcombank"
                },
                {
                    "code": "sacombank",
                    "name": "Ngân hàng TMCP Sài Gòn Thương Tín",
                    "shortName": "Sacombank"
                }
            ])
        );
    }

    #[test]
    fn bank_normalization_checks_membership_before_trimming() {
        assert_eq!(normalize_bank(Some("sacombank")), "sacombank");
        assert_eq!(normalize_bank(Some("SACOMBANK")), "vietcombank");
        assert_eq!(normalize_bank(Some("sacombank ")), "vietcombank");
        assert_eq!(normalize_bank(None), "vietcombank");
    }

    #[test]
    fn account_number_validation_uses_unicode_whitespace_and_trims() {
        assert_eq!(
            normalize_account_number(Some("  0123456789\u{2003}")),
            Ok("0123456789".to_owned())
        );
        assert_eq!(
            normalize_account_number(Some(" \t\u{2003}\n")),
            Err(ValidationError::MissingAccountNumber)
        );
        assert_eq!(
            normalize_account_number(None),
            Err(ValidationError::MissingAccountNumber)
        );
    }

    #[test]
    fn delegated_employee_selection_requires_payroll_manage() {
        let target = Uuid::new_v4();
        assert_eq!(
            select_employee(Some(target), false),
            EmployeeSelection::Forbidden
        );
        assert_eq!(
            select_employee(Some(target), true),
            EmployeeSelection::Delegated(target)
        );
        assert_eq!(
            select_employee(Some(Uuid::nil()), false),
            EmployeeSelection::SelfProfile
        );
        assert_eq!(select_employee(None, false), EmployeeSelection::SelfProfile);
    }

    #[test]
    fn ownership_is_self_only_unless_payroll_manage_is_present() {
        assert!(can_manage_identity(false, Some("NhanVien01"), "nhanvien01"));
        assert!(!can_manage_identity(false, Some("other"), "nhanvien01"));
        assert!(!can_manage_identity(false, None, "nhanvien01"));
        assert!(can_manage_identity(true, None, "nhanvien01"));
    }

    #[test]
    fn first_account_is_always_default_but_later_accounts_follow_request() {
        assert!(should_make_default(false, 0));
        assert!(should_make_default(true, 4));
        assert!(!should_make_default(false, 1));
    }

    #[test]
    fn save_request_defaults_match_dotnet_model_binding() {
        let request: SaveBankAccountRequest = serde_json::from_value(json!({
            "accountNumber": "123"
        }))
        .unwrap();
        assert_eq!(request.account_number.as_deref(), Some("123"));
        assert_eq!(request.employee_id, None);
        assert!(!request.is_default);
        assert!(request.note.is_none());

        assert!(
            serde_json::from_value::<SaveBankAccountRequest>(json!({
                "accountNumber": "123",
                "isDefault": null
            }))
            .is_err()
        );
    }

    #[test]
    fn account_response_has_no_nullable_fields() {
        let id = Uuid::new_v4();
        let employee_id = Uuid::new_v4();
        let response = BankAccountResponse::from(BankAccountRow {
            id,
            employee_id,
            bank: None,
            account_number: None,
            account_holder: None,
            branch: None,
            is_default: None,
            note: None,
            emp_name: None,
            employee_code: None,
        });

        assert_eq!(
            serde_json::to_value(response).unwrap(),
            json!({
                "id": id,
                "employeeId": employee_id,
                "employeeName": "",
                "employeeCode": "",
                "bank": "",
                "accountNumber": "",
                "accountHolder": "",
                "branch": "",
                "isDefault": false,
                "note": ""
            })
        );
    }
}
