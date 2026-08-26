use crate::{
    auth::{AuthContext, permissions, roles},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    extract::State,
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
};
use chrono::{DateTime, Duration, NaiveDate, SecondsFormat, Utc};
use serde::{Serialize, Serializer};
use serde_json::json;
use sqlx::{FromRow, PgConnection};
use std::{cmp::Ordering, sync::Arc};
use uuid::Uuid;

const DOC_WINDOW_DAYS: i32 = 30;
const CONTRACT_WINDOW_DAYS: i32 = 45;
const FINAL_APPROVAL_QUEUE: &str = permissions::REQUESTS_MANAGE;
const LEGACY_FINAL_APPROVAL_QUEUE: &str = roles::ADMIN;

const APPROVALS_SQL: &str = r#"
    SELECT r.id, r.request_no, r.title, r.req_type, r.due_at
    FROM hr_requests r
    WHERE r.status='Pending' AND EXISTS (
        SELECT 1 FROM hr_request_approvals a
        WHERE a.request_id=r.id AND a.step_no=r.current_step AND a.status='Pending'
          AND ((lower(a.approver_username)=lower($1) AND $2)
               OR (lower(a.approver_role) IN (lower($4), lower($5)) AND $3))
    )
    ORDER BY r.due_at NULLS LAST, r.created_at
    LIMIT 100
"#;

const PAYSLIPS_SQL: &str = r#"
    SELECT p.id, p.period
    FROM hr_payslips p JOIN hr_employees e ON e.id=p.employee_id
    WHERE e.username=$1 AND p.published=TRUE AND p.acknowledged_at IS NULL
    ORDER BY p.period DESC LIMIT 24
"#;

const DOCUMENTS_SQL: &str = r#"
    SELECT d.id, d.title, d.doc_type, d.expires_at
    FROM hr_documents d JOIN hr_employees e ON e.id=d.employee_id
    WHERE e.username=$1 AND d.expires_at IS NOT NULL
      AND d.expires_at <= CURRENT_DATE + $2::int
    ORDER BY d.expires_at LIMIT 50
"#;

const CONTRACTS_SQL: &str = r#"
    SELECT c.id, c.contract_no, c.contract_type, c.end_date
    FROM hr_contracts c JOIN hr_employees e ON e.id=c.employee_id
    WHERE e.username=$1 AND c.end_date IS NOT NULL
      AND c.end_date <= CURRENT_DATE + $2::int
      AND c.status <> 'Ended'
    ORDER BY c.end_date LIMIT 20
"#;

const NOTICE_SQL: &str = "SELECT announcement, announcement_level FROM app_config WHERE id=1";

/// The caller must place this router behind `auth::require_auth`. The handler additionally enforces
/// the same `requests.self` policy as the .NET endpoint before touching PostgreSQL.
pub fn router() -> Router<Arc<AppState>> {
    Router::new().route("/api/worklist", get(get_worklist))
}

#[derive(Debug, FromRow)]
struct ApprovalRow {
    id: Uuid,
    request_no: Option<String>,
    title: Option<String>,
    #[allow(dead_code)]
    req_type: Option<String>,
    due_at: Option<DateTime<Utc>>,
}

#[derive(Debug, FromRow)]
struct PayslipRow {
    id: Uuid,
    period: Option<String>,
}

#[derive(Debug, FromRow)]
struct DocumentRow {
    id: Uuid,
    title: Option<String>,
    doc_type: Option<String>,
    expires_at: NaiveDate,
}

#[derive(Debug, FromRow)]
struct ContractRow {
    id: Uuid,
    contract_no: Option<String>,
    contract_type: Option<String>,
    end_date: NaiveDate,
}

#[derive(Debug, FromRow)]
struct NoticeRow {
    announcement: Option<String>,
    announcement_level: Option<String>,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct WorklistItem {
    key: String,
    kind: String,
    title: String,
    description: String,
    priority: String,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_utc_millis"
    )]
    due_at: Option<DateTime<Utc>>,
    route: String,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct WorklistSummary {
    total: i32,
    approvals: i32,
    payslips: i32,
    documents: i32,
    contracts: i32,
    notices: i32,
    overdue: i32,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct WorklistResult {
    items: Vec<WorklistItem>,
    summary: WorklistSummary,
}

async fn get_worklist(
    Extension(auth): Extension<AuthContext>,
    State(state): State<Arc<AppState>>,
) -> Response {
    if !auth.permissions.contains(permissions::REQUESTS_SELF) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let can_approve = auth.permissions.contains(permissions::REQUESTS_APPROVE);
    let can_manage = auth.permissions.contains(permissions::REQUESTS_MANAGE);
    let now = Utc::now();
    let today = now.date_naive();
    let mut items = Vec::new();
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for worklist", error),
    };

    let approvals =
        match load_approvals(&mut connection, &auth.username, can_approve, can_manage).await {
            Ok(rows) => rows,
            Err(error) => return database_failure("read pending approvals for worklist", error),
        };
    items.extend(approvals.into_iter().map(|row| approval_item(row, now)));

    let payslips = match sqlx::query_as::<_, PayslipRow>(PAYSLIPS_SQL)
        .bind(&auth.username)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read unacknowledged payslips for worklist", error),
    };
    items.extend(payslips.into_iter().map(payslip_item));

    let documents = match sqlx::query_as::<_, DocumentRow>(DOCUMENTS_SQL)
        .bind(&auth.username)
        .bind(DOC_WINDOW_DAYS)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read expiring documents for worklist", error),
    };
    items.extend(documents.into_iter().map(|row| document_item(row, today)));

    let contracts = match sqlx::query_as::<_, ContractRow>(CONTRACTS_SQL)
        .bind(&auth.username)
        .bind(CONTRACT_WINDOW_DAYS)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read expiring contracts for worklist", error),
    };
    items.extend(contracts.into_iter().map(|row| contract_item(row, today)));

    let notice = match sqlx::query_as::<_, NoticeRow>(NOTICE_SQL)
        .fetch_optional(&mut *connection)
        .await
    {
        Ok(row) => row,
        Err(error) => return database_failure("read mandatory announcement for worklist", error),
    };
    if let Some(item) = notice.and_then(notice_item) {
        items.push(item);
    }

    Json(finalize_worklist(items)).into_response()
}

async fn load_approvals(
    connection: &mut PgConnection,
    username: &str,
    can_approve: bool,
    can_manage: bool,
) -> Result<Vec<ApprovalRow>, sqlx::Error> {
    sqlx::query_as::<_, ApprovalRow>(APPROVALS_SQL)
        .bind(username)
        .bind(can_approve)
        .bind(can_manage)
        .bind(FINAL_APPROVAL_QUEUE)
        .bind(LEGACY_FINAL_APPROVAL_QUEUE)
        .fetch_all(connection)
        .await
}

fn approval_item(row: ApprovalRow, now: DateTime<Utc>) -> WorklistItem {
    let request_no = row.request_no.unwrap_or_default();
    let title = row.title.unwrap_or_default();
    let description = if title.trim().is_empty() {
        format!("Đơn {request_no}")
    } else {
        format!("{request_no} · {title}")
    };
    WorklistItem {
        key: format!("approval:{}", row.id),
        kind: "approval".to_owned(),
        title: "Đơn chờ bạn duyệt".to_owned(),
        description,
        priority: due_priority(row.due_at, now).to_owned(),
        due_at: row.due_at,
        route: "/duyet".to_owned(),
    }
}

fn payslip_item(row: PayslipRow) -> WorklistItem {
    WorklistItem {
        key: format!("payslip:{}", row.id),
        kind: "payslip".to_owned(),
        title: "Phiếu lương chưa xác nhận".to_owned(),
        description: format!("Kỳ lương {}", row.period.unwrap_or_default()),
        priority: "medium".to_owned(),
        due_at: None,
        route: "/phieu-luong".to_owned(),
    }
}

fn document_item(row: DocumentRow, today: NaiveDate) -> WorklistItem {
    let title = row.title.unwrap_or_default();
    let label = if title.trim().is_empty() {
        row.doc_type.unwrap_or_default()
    } else {
        title
    };
    WorklistItem {
        key: format!("doc:{}", row.id),
        kind: "document".to_owned(),
        title: "Giấy tờ sắp hết hạn".to_owned(),
        description: format!("{label} · hết hạn {}", format_display_date(row.expires_at)),
        priority: expiry_priority(row.expires_at, today).to_owned(),
        due_at: Some(midnight_utc(row.expires_at)),
        route: "/ho-so".to_owned(),
    }
}

fn contract_item(row: ContractRow, today: NaiveDate) -> WorklistItem {
    let contract_no = row.contract_no.unwrap_or_default();
    let label = if contract_no.trim().is_empty() {
        row.contract_type.unwrap_or_default()
    } else {
        contract_no
    };
    WorklistItem {
        key: format!("contract:{}", row.id),
        kind: "contract".to_owned(),
        title: "Hợp đồng sắp hết hạn".to_owned(),
        description: format!("HĐ {label} · hết hạn {}", format_display_date(row.end_date)),
        priority: expiry_priority(row.end_date, today).to_owned(),
        due_at: Some(midnight_utc(row.end_date)),
        route: "/ho-so".to_owned(),
    }
}

fn notice_item(row: NoticeRow) -> Option<WorklistItem> {
    let message = row.announcement.unwrap_or_default();
    let level = row.announcement_level.unwrap_or_default();
    if message.trim().is_empty() || !matches!(level.as_str(), "warning" | "critical") {
        return None;
    }

    Some(WorklistItem {
        key: "notice".to_owned(),
        kind: "notice".to_owned(),
        title: "Thông báo quan trọng".to_owned(),
        description: truncate_dotnet_utf16(&message, 160),
        priority: if level == "critical" {
            "high"
        } else {
            "medium"
        }
        .to_owned(),
        due_at: None,
        route: "/".to_owned(),
    })
}

fn due_priority(due_utc: Option<DateTime<Utc>>, now: DateTime<Utc>) -> &'static str {
    match due_utc {
        None => "normal",
        Some(due) if due <= now => "high",
        Some(due) if due <= now + Duration::days(1) => "medium",
        Some(_) => "normal",
    }
}

fn expiry_priority(expiry: NaiveDate, today: NaiveDate) -> &'static str {
    let days = expiry.signed_duration_since(today).num_days();
    if days <= 7 {
        "high"
    } else if days <= 30 {
        "medium"
    } else {
        "normal"
    }
}

fn midnight_utc(date: NaiveDate) -> DateTime<Utc> {
    DateTime::from_naive_utc_and_offset(
        date.and_hms_opt(0, 0, 0)
            .expect("a valid date always has a midnight"),
        Utc,
    )
}

fn format_display_date(date: NaiveDate) -> String {
    date.format("%d/%m/%Y").to_string()
}

fn truncate_dotnet_utf16(value: &str, max_code_units: usize) -> String {
    let utf16 = value.encode_utf16().collect::<Vec<_>>();
    if utf16.len() <= max_code_units {
        return value.to_owned();
    }
    let mut truncated = String::from_utf16_lossy(&utf16[..max_code_units]);
    truncated.push('…');
    truncated
}

fn finalize_worklist(mut items: Vec<WorklistItem>) -> WorklistResult {
    let summary = WorklistSummary {
        total: items.len() as i32,
        approvals: count_items(&items, "approval"),
        payslips: count_items(&items, "payslip"),
        documents: count_items(&items, "document"),
        contracts: count_items(&items, "contract"),
        notices: count_items(&items, "notice"),
        overdue: items.iter().filter(|item| item.priority == "high").count() as i32,
    };

    // `sort_by` is stable, matching LINQ OrderBy/ThenBy for equal priority and due date.
    items.sort_by(|left, right| {
        priority_rank(&left.priority)
            .cmp(&priority_rank(&right.priority))
            .then_with(|| compare_due(left.due_at, right.due_at))
    });
    WorklistResult { items, summary }
}

fn count_items(items: &[WorklistItem], kind: &str) -> i32 {
    items.iter().filter(|item| item.kind == kind).count() as i32
}

fn priority_rank(priority: &str) -> u8 {
    match priority {
        "high" => 0,
        "medium" => 1,
        _ => 2,
    }
}

fn compare_due(left: Option<DateTime<Utc>>, right: Option<DateTime<Utc>>) -> Ordering {
    match (left, right) {
        (Some(left), Some(right)) => left.cmp(&right),
        (Some(_), None) => Ordering::Less,
        (None, Some(_)) => Ordering::Greater,
        (None, None) => Ordering::Equal,
    }
}

fn serialize_optional_utc_millis<S>(
    value: &Option<DateTime<Utc>>,
    serializer: S,
) -> Result<S::Ok, S::Error>
where
    S: Serializer,
{
    match value {
        Some(value) => {
            serializer.serialize_some(&value.to_rfc3339_opts(SecondsFormat::Millis, true))
        }
        None => serializer.serialize_none(),
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native worklist database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({
            "message": "Khong ket noi duoc co so du lieu PostgreSQL."
        })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(value: &str) -> DateTime<Utc> {
        DateTime::parse_from_rfc3339(value)
            .unwrap()
            .with_timezone(&Utc)
    }

    fn item(kind: &str, priority: &str, due_at: Option<DateTime<Utc>>) -> WorklistItem {
        WorklistItem {
            key: format!("{kind}:key"),
            kind: kind.to_owned(),
            title: kind.to_owned(),
            description: kind.to_owned(),
            priority: priority.to_owned(),
            due_at,
            route: "/".to_owned(),
        }
    }

    #[test]
    fn aggregation_counts_all_kinds_and_sorts_like_linq() {
        let later = at("2026-08-25T10:00:00Z");
        let earlier = at("2026-08-24T10:00:00Z");
        let result = finalize_worklist(vec![
            item("approval", "normal", None),
            item("payslip", "medium", None),
            item("document", "high", Some(later)),
            item("contract", "high", Some(earlier)),
            item("notice", "high", None),
        ]);

        assert_eq!(
            result.summary,
            WorklistSummary {
                total: 5,
                approvals: 1,
                payslips: 1,
                documents: 1,
                contracts: 1,
                notices: 1,
                overdue: 3,
            }
        );
        assert_eq!(
            result
                .items
                .iter()
                .map(|item| item.kind.as_str())
                .collect::<Vec<_>>(),
            vec!["contract", "document", "notice", "payslip", "approval"]
        );
    }

    #[test]
    fn due_and_expiry_priorities_match_boundary_contracts() {
        let now = at("2026-08-24T12:00:00Z");
        assert_eq!(due_priority(None, now), "normal");
        assert_eq!(due_priority(Some(now), now), "high");
        assert_eq!(due_priority(Some(now + Duration::days(1)), now), "medium");
        assert_eq!(
            due_priority(
                Some(now + Duration::days(1) + Duration::milliseconds(1)),
                now
            ),
            "normal"
        );

        let today = NaiveDate::from_ymd_opt(2026, 8, 24).unwrap();
        assert_eq!(expiry_priority(today + Duration::days(7), today), "high");
        assert_eq!(expiry_priority(today + Duration::days(30), today), "medium");
        assert_eq!(expiry_priority(today + Duration::days(31), today), "normal");
    }

    #[test]
    fn json_is_camel_case_omits_null_and_uses_exact_utc_milliseconds() {
        let without_due = item("payslip", "medium", None);
        let without_due_json = serde_json::to_value(&without_due).unwrap();
        assert!(without_due_json.get("dueAt").is_none());
        assert_eq!(without_due_json["key"], "payslip:key");

        let with_due = item("approval", "high", Some(at("2026-08-24T12:34:56.987654Z")));
        let with_due_json = serde_json::to_value(&with_due).unwrap();
        assert_eq!(with_due_json["dueAt"], "2026-08-24T12:34:56.987Z");

        let result_json = serde_json::to_value(finalize_worklist(vec![with_due])).unwrap();
        assert!(result_json.get("items").is_some());
        assert_eq!(result_json["summary"]["approvals"], 1);
        assert_eq!(result_json["summary"]["overdue"], 1);
    }

    #[test]
    fn approval_payslip_and_notice_text_match_legacy_rules() {
        let approval_id = Uuid::parse_str("00112233-4455-6677-8899-aabbccddeeff").unwrap();
        let approval = approval_item(
            ApprovalRow {
                id: approval_id,
                request_no: Some("REQ-1".to_owned()),
                title: Some("  ".to_owned()),
                req_type: Some("leave".to_owned()),
                due_at: None,
            },
            at("2026-08-24T00:00:00Z"),
        );
        assert_eq!(approval.key, format!("approval:{approval_id}"));
        assert_eq!(approval.description, "Đơn REQ-1");

        let payslip = payslip_item(PayslipRow {
            id: Uuid::nil(),
            period: Some("2026-08".to_owned()),
        });
        assert_eq!(payslip.description, "Kỳ lương 2026-08");

        let long_notice = format!("{}😀", "a".repeat(159));
        let notice = notice_item(NoticeRow {
            announcement: Some(long_notice),
            announcement_level: Some("critical".to_owned()),
        })
        .unwrap();
        assert_eq!(notice.description, format!("{}�…", "a".repeat(159)));
        assert_eq!(notice.priority, "high");
    }
}
