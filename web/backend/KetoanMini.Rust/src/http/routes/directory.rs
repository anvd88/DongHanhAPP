use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    extract::{Query, State, rejection::QueryRejection},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
};
use serde::{Deserialize, Serialize};
use serde_json::json;
use sqlx::{FromRow, PgConnection};
use std::{
    collections::{BTreeSet, HashMap, HashSet},
    sync::Arc,
};
use uuid::Uuid;

const DIRECTORY_SQL: &str = r#"
    SELECT e.id, e.full_name, e.position, e.phone, e.email, e.username, e.manager_id,
           d.id AS dept_id, d.name AS dept_name, m.full_name AS manager_name,
           COALESCE(pres.is_online, FALSE) AS is_online
    FROM hr_employees e
    LEFT JOIN hr_departments d ON d.id = e.department_id
    LEFT JOIN hr_employees m ON m.id = e.manager_id
    LEFT JOIN LATERAL (
        SELECT BOOL_OR(
            us.is_active = TRUE
            AND us.last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds'
        ) AS is_online
        FROM user_sessions us
        WHERE us.username = e.username
    ) pres ON TRUE
    WHERE e.status = 'Active'
    ORDER BY d.name NULLS LAST, e.full_name
"#;

const DIRECTORY_BY_DEPARTMENT_SQL: &str = r#"
    SELECT e.id, e.full_name, e.position, e.phone, e.email, e.username, e.manager_id,
           d.id AS dept_id, d.name AS dept_name, m.full_name AS manager_name,
           COALESCE(pres.is_online, FALSE) AS is_online
    FROM hr_employees e
    LEFT JOIN hr_departments d ON d.id = e.department_id
    LEFT JOIN hr_employees m ON m.id = e.manager_id
    LEFT JOIN LATERAL (
        SELECT BOOL_OR(
            us.is_active = TRUE
            AND us.last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds'
        ) AS is_online
        FROM user_sessions us
        WHERE us.username = e.username
    ) pres ON TRUE
    WHERE e.status = 'Active' AND e.department_id = $1
    ORDER BY d.name NULLS LAST, e.full_name
"#;

const ORG_CHART_SQL: &str = r#"
    SELECT e.id, e.full_name, e.position, e.manager_id, d.name AS dept_name
    FROM hr_employees e
    LEFT JOIN hr_departments d ON d.id = e.department_id
    WHERE e.status = 'Active'
    ORDER BY e.full_name
"#;

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route("/api/directory", get(list_directory))
        .route("/api/directory/org-chart", get(org_chart))
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DirectoryQuery {
    search: Option<String>,
    department_id: Option<Uuid>,
}

#[derive(FromRow)]
struct EmployeeIdentityRow {
    id: Uuid,
    #[allow(dead_code)]
    manager_id: Option<Uuid>,
}

#[derive(FromRow)]
struct DirectoryRow {
    id: Uuid,
    full_name: Option<String>,
    position: Option<String>,
    phone: Option<String>,
    email: Option<String>,
    #[allow(dead_code)]
    username: Option<String>,
    manager_id: Option<Uuid>,
    dept_id: Option<Uuid>,
    dept_name: Option<String>,
    manager_name: Option<String>,
    is_online: Option<bool>,
}

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct DirectoryItem {
    id: Uuid,
    full_name: String,
    position: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    department_id: Option<Uuid>,
    #[serde(skip_serializing_if = "Option::is_none")]
    department_name: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    manager_id: Option<Uuid>,
    #[serde(skip_serializing_if = "Option::is_none")]
    manager_name: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    phone: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    email: Option<String>,
    can_see_contact: bool,
    online: bool,
}

async fn list_directory(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    query: Result<Query<DirectoryQuery>, QueryRejection>,
) -> Response {
    let Query(query) = match query {
        Ok(query) => query,
        // ASP.NET query binding rejects an invalid Guid with 400 before entering
        // the handler. Do not leak Axum/serde parser details in a text response.
        Err(_) => return StatusCode::BAD_REQUEST.into_response(),
    };

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for directory", error),
    };
    let viewer = match load_employee_identity(&mut connection, &auth.username).await {
        Ok(viewer) => viewer,
        Err(error) => return database_failure("read directory viewer profile", error),
    };

    let rows = match query.department_id {
        Some(department_id) => {
            sqlx::query_as::<_, DirectoryRow>(DIRECTORY_BY_DEPARTMENT_SQL)
                .bind(department_id)
                .fetch_all(&mut *connection)
                .await
        }
        None => {
            sqlx::query_as::<_, DirectoryRow>(DIRECTORY_SQL)
                .fetch_all(&mut *connection)
                .await
        }
    };
    let rows = match rows {
        Ok(rows) => rows,
        Err(error) => return database_failure("read employee directory", error),
    };

    let normalized_search = no_accent(query.search.as_deref());
    let can_see_all_contacts = has_companywide_contact_scope(&auth.permissions);
    let viewer_id = viewer.map(|employee| employee.id);
    let mut items = Vec::with_capacity(rows.len());
    for row in rows {
        let full_name = row.full_name.unwrap_or_default();
        let position = row.position.unwrap_or_default();
        if !matches_search(&normalized_search, &full_name, &position) {
            continue;
        }

        let can_see_contact =
            may_see_contact(can_see_all_contacts, viewer_id, row.id, row.manager_id);
        items.push(DirectoryItem {
            id: row.id,
            full_name,
            position,
            department_id: row.dept_id,
            department_name: null_if_empty(row.dept_name),
            manager_id: row.manager_id,
            manager_name: null_if_empty(row.manager_name),
            phone: can_see_contact.then(|| null_if_empty(row.phone)).flatten(),
            email: can_see_contact.then(|| null_if_empty(row.email)).flatten(),
            can_see_contact,
            online: row.is_online.unwrap_or(false),
        });
    }

    Json(items).into_response()
}

async fn load_employee_identity(
    connection: &mut PgConnection,
    username: &str,
) -> Result<Option<EmployeeIdentityRow>, sqlx::Error> {
    sqlx::query_as::<_, EmployeeIdentityRow>(
        "SELECT id, manager_id FROM hr_employees WHERE username = $1 LIMIT 1",
    )
    .bind(username)
    .fetch_optional(&mut *connection)
    .await
}

fn may_see_contact(
    can_see_all_contacts: bool,
    viewer_id: Option<Uuid>,
    employee_id: Uuid,
    manager_id: Option<Uuid>,
) -> bool {
    can_see_all_contacts
        || viewer_id == Some(employee_id)
        || viewer_id.is_some_and(|viewer_id| manager_id == Some(viewer_id))
}

fn has_companywide_contact_scope(effective_permissions: &BTreeSet<String>) -> bool {
    // `ClaimsPrincipal.IsHrManager()` in the current API is exactly an
    // `hr.manage` permission check. `users.manage` alone is not widened here.
    effective_permissions.contains(permissions::HR_MANAGE)
}

fn matches_search(normalized_search: &str, full_name: &str, position: &str) -> bool {
    normalized_search.is_empty()
        || no_accent(Some(full_name)).contains(normalized_search)
        || no_accent(Some(position)).contains(normalized_search)
}

fn null_if_empty(value: Option<String>) -> Option<String> {
    value.filter(|value| !value.trim().is_empty())
}

#[derive(Clone, Debug, FromRow)]
struct OrgRow {
    id: Uuid,
    full_name: Option<String>,
    position: Option<String>,
    manager_id: Option<Uuid>,
    dept_name: Option<String>,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct OrgNode {
    id: Uuid,
    full_name: String,
    position: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    department_name: Option<String>,
    reports: Vec<OrgNode>,
}

async fn org_chart(
    State(state): State<Arc<AppState>>,
    Extension(_auth): Extension<AuthContext>,
) -> Response {
    let rows = sqlx::query_as::<_, OrgRow>(ORG_CHART_SQL)
        .fetch_all(&state.pool)
        .await;
    match rows {
        Ok(rows) => Json(build_org_tree(&rows)).into_response(),
        Err(error) => database_failure("read organization chart", error),
    }
}

/// Reproduce the insertion-ordered .NET dictionary behavior without depending
/// on a hash-map iteration order: SQL returns rows by full name, and both roots
/// and each manager's reports retain that order.
fn build_org_tree(rows: &[OrgRow]) -> Vec<OrgNode> {
    let active_ids = rows.iter().map(|row| row.id).collect::<HashSet<_>>();
    let mut reports_by_manager = HashMap::<Uuid, Vec<usize>>::new();
    let mut root_indices = Vec::new();

    for (index, row) in rows.iter().enumerate() {
        match row
            .manager_id
            .filter(|manager_id| active_ids.contains(manager_id))
        {
            Some(manager_id) => reports_by_manager
                .entry(manager_id)
                .or_default()
                .push(index),
            None => root_indices.push(index),
        }
    }

    root_indices
        .into_iter()
        .map(|index| build_org_node(index, rows, &reports_by_manager))
        .collect()
}

fn build_org_node(
    index: usize,
    rows: &[OrgRow],
    reports_by_manager: &HashMap<Uuid, Vec<usize>>,
) -> OrgNode {
    let row = &rows[index];
    let reports = reports_by_manager
        .get(&row.id)
        .into_iter()
        .flatten()
        .copied()
        .map(|child_index| build_org_node(child_index, rows, reports_by_manager))
        .collect();

    OrgNode {
        id: row.id,
        full_name: row.full_name.clone().unwrap_or_default(),
        position: row.position.clone().unwrap_or_default(),
        department_name: null_if_empty(row.dept_name.clone()),
        reports,
    }
}

/// Match `DirectoryEndpoints.NoAccent`: trim, remove Vietnamese diacritics and
/// combining marks, lowercase invariantly, and fold đ/Đ to d.
fn no_accent(value: Option<&str>) -> String {
    let Some(value) = value.filter(|value| !value.trim().is_empty()) else {
        return String::new();
    };

    let mut normalized = String::with_capacity(value.len());
    for character in value.trim().chars() {
        if is_combining_mark(character) {
            continue;
        }
        if let Some(base) = vietnamese_base_character(character) {
            normalized.push(base);
            continue;
        }
        for lowercase in character.to_lowercase() {
            if !is_combining_mark(lowercase) {
                normalized.push(lowercase);
            }
        }
    }
    normalized
}

fn is_combining_mark(character: char) -> bool {
    matches!(
        character as u32,
        0x0300..=0x036f | 0x1ab0..=0x1aff | 0x1dc0..=0x1dff | 0x20d0..=0x20ff | 0xfe20..=0xfe2f
    )
}

fn vietnamese_base_character(character: char) -> Option<char> {
    match character {
        'a' | 'A' | 'à' | 'á' | 'â' | 'ã' | 'ä' | 'å' | 'ă' | 'ạ' | 'ả' | 'ấ' | 'ầ' | 'ẩ' | 'ẫ'
        | 'ậ' | 'ắ' | 'ằ' | 'ẳ' | 'ẵ' | 'ặ' | 'À' | 'Á' | 'Â' | 'Ã' | 'Ä' | 'Å' | 'Ă' | 'Ạ'
        | 'Ả' | 'Ấ' | 'Ầ' | 'Ẩ' | 'Ẫ' | 'Ậ' | 'Ắ' | 'Ằ' | 'Ẳ' | 'Ẵ' | 'Ặ' => {
            Some('a')
        }
        'e' | 'E' | 'è' | 'é' | 'ê' | 'ë' | 'ẹ' | 'ẻ' | 'ẽ' | 'ế' | 'ề' | 'ể' | 'ễ' | 'ệ' | 'È'
        | 'É' | 'Ê' | 'Ë' | 'Ẹ' | 'Ẻ' | 'Ẽ' | 'Ế' | 'Ề' | 'Ể' | 'Ễ' | 'Ệ' => {
            Some('e')
        }
        'i' | 'I' | 'ì' | 'í' | 'î' | 'ï' | 'ĩ' | 'ỉ' | 'ị' | 'Ì' | 'Í' | 'Î' | 'Ï' | 'Ĩ' | 'Ỉ'
        | 'Ị' => Some('i'),
        'o' | 'O' | 'ò' | 'ó' | 'ô' | 'õ' | 'ö' | 'ọ' | 'ỏ' | 'ố' | 'ồ' | 'ổ' | 'ỗ' | 'ộ' | 'ơ'
        | 'ớ' | 'ờ' | 'ở' | 'ỡ' | 'ợ' | 'Ò' | 'Ó' | 'Ô' | 'Õ' | 'Ö' | 'Ọ' | 'Ỏ' | 'Ố' | 'Ồ'
        | 'Ổ' | 'Ỗ' | 'Ộ' | 'Ơ' | 'Ớ' | 'Ờ' | 'Ở' | 'Ỡ' | 'Ợ' => Some('o'),
        'u' | 'U' | 'ù' | 'ú' | 'û' | 'ü' | 'ũ' | 'ủ' | 'ụ' | 'ư' | 'ứ' | 'ừ' | 'ử' | 'ữ' | 'ự'
        | 'Ù' | 'Ú' | 'Û' | 'Ü' | 'Ũ' | 'Ủ' | 'Ụ' | 'Ư' | 'Ứ' | 'Ừ' | 'Ử' | 'Ữ' | 'Ự' => {
            Some('u')
        }
        'y' | 'Y' | 'ý' | 'ỳ' | 'ŷ' | 'ÿ' | 'ỷ' | 'ỹ' | 'ỵ' | 'Ý' | 'Ỳ' | 'Ŷ' | 'Ÿ' | 'Ỷ' | 'Ỹ'
        | 'Ỵ' => Some('y'),
        'd' | 'D' | 'đ' | 'Đ' => Some('d'),
        _ => None,
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native directory database operation failed");
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

    fn org_row(id: Uuid, name: &str, manager_id: Option<Uuid>, department: Option<&str>) -> OrgRow {
        OrgRow {
            id,
            full_name: Some(name.to_owned()),
            position: Some("Nhân viên".to_owned()),
            manager_id,
            dept_name: department.map(str::to_owned),
        }
    }

    #[test]
    fn vietnamese_search_is_case_and_accent_insensitive() {
        assert_eq!(no_accent(Some("  NGUYỄN ĐẶNG  ")), "nguyen dang");
        assert_eq!(no_accent(Some("Kế toán tiền lương")), "ke toan tien luong");
        assert_eq!(
            no_accent(Some("Ke\u{0302}\u{0301} toa\u{0301}n")),
            "ke toan"
        );
        assert!(matches_search("nguyen", "Nguyễn Văn An", "Kế toán"));
        assert!(matches_search("ke toan", "Nguyễn Văn An", "Kế toán"));
        assert!(!matches_search("thu kho", "Nguyễn Văn An", "Kế toán"));
    }

    #[test]
    fn contact_privacy_uses_permission_self_and_direct_ownership_only() {
        let viewer = Uuid::new_v4();
        let direct_report = Uuid::new_v4();
        let unrelated = Uuid::new_v4();

        assert!(may_see_contact(true, None, unrelated, None));
        assert!(may_see_contact(false, Some(viewer), viewer, None));
        assert!(may_see_contact(
            false,
            Some(viewer),
            direct_report,
            Some(viewer)
        ));
        assert!(!may_see_contact(false, Some(viewer), unrelated, None));
        assert!(!may_see_contact(false, None, unrelated, None));
    }

    #[test]
    fn companywide_contact_scope_requires_hr_manage() {
        let users_only = BTreeSet::from([permissions::USERS_MANAGE.to_owned()]);
        let hr_manager = BTreeSet::from([permissions::HR_MANAGE.to_owned()]);

        assert!(!has_companywide_contact_scope(&users_only));
        assert!(has_companywide_contact_scope(&hr_manager));
    }

    #[test]
    fn organization_tree_nests_reports_and_promotes_missing_managers_to_roots() {
        let manager = Uuid::new_v4();
        let report = Uuid::new_v4();
        let orphan = Uuid::new_v4();
        let missing_manager = Uuid::new_v4();
        let rows = vec![
            org_row(manager, "A Manager", None, Some("Điều hành")),
            org_row(report, "B Report", Some(manager), Some("Kế toán")),
            org_row(orphan, "C Orphan", Some(missing_manager), None),
        ];

        let tree = build_org_tree(&rows);
        assert_eq!(tree.len(), 2);
        assert_eq!(tree[0].id, manager);
        assert_eq!(tree[0].reports.len(), 1);
        assert_eq!(tree[0].reports[0].id, report);
        assert_eq!(tree[1].id, orphan);
        assert!(tree[1].reports.is_empty());
        assert_eq!(tree[0].department_name.as_deref(), Some("Điều hành"));
    }

    #[test]
    fn active_manager_cycles_are_not_exposed_as_roots_like_dotnet() {
        let first = Uuid::new_v4();
        let second = Uuid::new_v4();
        let rows = vec![
            org_row(first, "A", Some(second), None),
            org_row(second, "B", Some(first), None),
        ];

        assert!(build_org_tree(&rows).is_empty());
    }

    #[test]
    fn hidden_contact_and_blank_optional_fields_are_omitted_from_json() {
        let value = serde_json::to_value(DirectoryItem {
            id: Uuid::nil(),
            full_name: "Nhân viên".to_owned(),
            position: String::new(),
            department_id: None,
            department_name: null_if_empty(Some("  ".to_owned())),
            manager_id: None,
            manager_name: None,
            phone: None,
            email: None,
            can_see_contact: false,
            online: false,
        })
        .unwrap();

        assert_eq!(value["fullName"], "Nhân viên");
        assert_eq!(value["canSeeContact"], false);
        assert_eq!(value["online"], false);
        for omitted in [
            "departmentId",
            "departmentName",
            "managerId",
            "managerName",
            "phone",
            "email",
        ] {
            assert!(value.get(omitted).is_none(), "{omitted} must be omitted");
        }
    }
}
