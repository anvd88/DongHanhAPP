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
use chrono::{DateTime, SecondsFormat, Utc};
use serde::{Serialize, Serializer};
use serde_json::json;
use sqlx::{FromRow, PgConnection};
use std::{collections::HashSet, sync::Arc};
use uuid::Uuid;

/// Native authenticated profile routes.
///
/// The caller must place this router behind `auth::require_auth`. Requiring an
/// `Extension<AuthContext>` in both handlers also prevents either handler from
/// accepting an identity supplied by request data.
pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route("/api/auth/me", get(me))
        .route("/api/auth/access-profile", get(access_profile))
}

#[derive(FromRow)]
struct UserRow {
    id: Uuid,
    username: Option<String>,
    full_name: Option<String>,
    email: Option<String>,
    role: Option<String>,
    is_active: Option<bool>,
    approval_status: Option<String>,
    created_at: Option<DateTime<Utc>>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct UserResponse {
    id: Uuid,
    username: String,
    full_name: String,
    email: String,
    role: String,
    is_active: bool,
    approval_status: String,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_utc_millis"
    )]
    created_at: Option<DateTime<Utc>>,
    is_admin: bool,
    is_pending: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    avatar_url: Option<String>,
    verified: bool,
    is_diamond: bool,
    face_registered: bool,
    face_enrollment_pending: bool,
    roles: Vec<String>,
    permissions: Vec<String>,
    can_assign_tasks: bool,
}

async fn me(
    Extension(auth): Extension<AuthContext>,
    State(state): State<Arc<AppState>>,
) -> Response {
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for auth/me", error),
    };

    let user = sqlx::query_as::<_, UserRow>(
        r#"
        SELECT id, username, full_name, email, role, is_active, approval_status, created_at
        FROM app_users
        WHERE username = $1 AND is_deleted = FALSE
        "#,
    )
    .bind(&auth.username)
    .fetch_optional(&mut *connection)
    .await;

    let row = match user {
        Ok(Some(row)) => row,
        Ok(None) => return StatusCode::UNAUTHORIZED.into_response(),
        Err(error) => return database_failure("read current user", error),
    };

    // `Database.Str/Bool` in the .NET API maps unexpected NULL values to the
    // type default. Preserve that behavior even though the compatible schema
    // declares these columns NOT NULL.
    let username = row.username.unwrap_or_default();
    let full_name = row.full_name.unwrap_or_default();
    let email = row.email.unwrap_or_default();
    let role = row.role.unwrap_or_default();
    let is_active = row.is_active.unwrap_or(false);
    let approval_status = row.approval_status.unwrap_or_default();

    let extras = load_profile_extras(&mut connection, &username, row.id, &role).await;
    let ProfileExtras {
        verified,
        is_diamond,
        face_registered,
        face_enrollment_pending,
        avatar_url,
        roles: effective_roles,
    } = extras;

    let permission_set = if effective_roles.is_empty() {
        permissions::for_roles([role.as_str()])
    } else {
        permissions::for_roles(effective_roles.iter().map(String::as_str))
    };
    let can_assign_tasks = permission_set.contains(permissions::TASKS_ASSIGN);
    let mut effective_permissions = permission_set.into_iter().collect::<Vec<_>>();
    effective_permissions.sort_unstable();

    Json(UserResponse {
        id: row.id,
        username,
        full_name,
        email,
        is_active,
        is_admin: role.eq_ignore_ascii_case(roles::ADMIN),
        is_pending: approval_status.eq_ignore_ascii_case("Pending"),
        role,
        approval_status,
        created_at: row.created_at,
        avatar_url,
        verified,
        is_diamond,
        face_registered,
        face_enrollment_pending,
        roles: effective_roles,
        permissions: effective_permissions
            .into_iter()
            .map(str::to_owned)
            .collect(),
        can_assign_tasks,
    })
    .into_response()
}

struct ProfileExtras {
    verified: bool,
    is_diamond: bool,
    face_registered: bool,
    face_enrollment_pending: bool,
    avatar_url: Option<String>,
    roles: Vec<String>,
}

#[derive(FromRow)]
struct ProfileExtrasRow {
    verified: bool,
    is_diamond: bool,
    face_registered: bool,
    face_enrollment_pending: bool,
    avatar_url: Option<String>,
    secondary_roles: String,
}

/// Every optional profile flag in one round trip.
///
/// These auxiliary tables are deliberately absent from the startup schema guard, so an older
/// deployment can genuinely be missing one. That is why the .NET original — and the isolated
/// helpers below — read each table separately and swallow failures. Attempting the combined
/// statement first keeps that resilience (any error falls back to the per-table path) while
/// turning the normal `/api/auth/me` from seven round trips into two.
async fn load_profile_extras(
    connection: &mut PgConnection,
    username: &str,
    user_id: Uuid,
    role: &str,
) -> ProfileExtras {
    let is_admin = role.eq_ignore_ascii_case(roles::ADMIN);
    let combined = sqlx::query_as::<_, ProfileExtrasRow>(
        r#"
        SELECT EXISTS(SELECT 1 FROM web_verified_users WHERE username = $1) AS verified,
               EXISTS(SELECT 1 FROM web_diamond_members WHERE username = $1) AS is_diamond,
               EXISTS(SELECT 1 FROM cham_cong_face WHERE username = $1) AS face_registered,
               EXISTS(SELECT 1 FROM cham_cong_face_enrollments
                      WHERE lower(username) = lower($1)
                        AND status = 'pending'
                        AND expires_at > CURRENT_TIMESTAMP) AS face_enrollment_pending,
               -- Ảnh đại diện: nguồn DUY NHẤT là hr_employees.avatar (giống .NET sau đợt gộp); bảng
               -- web_user_avatars chỉ còn là bản lưu. Ghép user_id trước, username sau.
               (SELECT e.avatar FROM hr_employees e
                 WHERE e.user_id = $2 OR lower(e.username) = lower($1)
                 ORDER BY (e.user_id = $2) DESC NULLS LAST
                 LIMIT 1) AS avatar_url,
               COALESCE((SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                         FROM user_roles ur
                         WHERE ur.username = $1
                           AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)), '')
                   AS secondary_roles
        "#,
    )
    .bind(username)
    .bind(user_id)
    .fetch_one(&mut *connection)
    .await;

    match combined {
        Ok(row) => ProfileExtras {
            // Admin is verified/diamond by role, exactly as the isolated helpers short-circuit.
            verified: is_admin || row.verified,
            is_diamond: is_admin || row.is_diamond,
            face_registered: row.face_registered,
            face_enrollment_pending: row.face_enrollment_pending,
            avatar_url: row.avatar_url.filter(|value| !value.trim().is_empty()),
            roles: combine_effective_roles(role, &row.secondary_roles),
        },
        Err(error) => {
            tracing::debug!(%error, "combined profile lookup unavailable; using isolated reads");
            ProfileExtras {
                verified: load_verified(&mut *connection, username, role).await,
                is_diamond: load_diamond(&mut *connection, username, role).await,
                face_registered: load_face_registered(&mut *connection, username).await,
                face_enrollment_pending: load_face_enrollment_pending(&mut *connection, username)
                    .await,
                avatar_url: load_avatar_url(&mut *connection, user_id, username).await,
                roles: load_all_roles(&mut *connection, username, role).await,
            }
        }
    }
}

/// Same rule as `load_all_roles`: an unrecognised primary role contributes NOTHING.
///
/// Deliberately not `roles::combine`, which falls back to `Employee` for an unknown primary and
/// would hand a corrupt role string a real permission set instead of failing closed.
fn combine_effective_roles(primary_role: &str, secondary_csv: &str) -> Vec<String> {
    let mut effective_roles = Vec::new();
    if let Some(primary) = roles::normalize(primary_role) {
        effective_roles.push(primary.to_owned());
    }
    for secondary in secondary_csv.split(',') {
        let Some(normalized) = roles::normalize(secondary) else {
            continue;
        };
        if !effective_roles.iter().any(|role| role == normalized) {
            effective_roles.push(normalized.to_owned());
        }
    }
    effective_roles
}

async fn load_verified(connection: &mut PgConnection, username: &str, role: &str) -> bool {
    if role.eq_ignore_ascii_case(roles::ADMIN) {
        return true;
    }
    sqlx::query_scalar::<_, i32>("SELECT 1 FROM web_verified_users WHERE username = $1 LIMIT 1")
        .bind(username)
        .fetch_optional(&mut *connection)
        .await
        .ok()
        .flatten()
        .is_some()
}

async fn load_diamond(connection: &mut PgConnection, username: &str, role: &str) -> bool {
    if role.eq_ignore_ascii_case(roles::ADMIN) {
        return true;
    }
    sqlx::query_scalar::<_, i32>("SELECT 1 FROM web_diamond_members WHERE username = $1 LIMIT 1")
        .bind(username)
        .fetch_optional(&mut *connection)
        .await
        .ok()
        .flatten()
        .is_some()
}

async fn load_face_registered(connection: &mut PgConnection, username: &str) -> bool {
    sqlx::query_scalar::<_, i32>("SELECT 1 FROM cham_cong_face WHERE username = $1 LIMIT 1")
        .bind(username)
        .fetch_optional(&mut *connection)
        .await
        .ok()
        .flatten()
        .is_some()
}

async fn load_face_enrollment_pending(connection: &mut PgConnection, username: &str) -> bool {
    sqlx::query_scalar::<_, i32>(
        r#"
        SELECT 1
        FROM cham_cong_face_enrollments
        WHERE lower(username) = lower($1)
          AND status = 'pending'
          AND expires_at > CURRENT_TIMESTAMP
        LIMIT 1
        "#,
    )
    .bind(username)
    .fetch_optional(&mut *connection)
    .await
    .ok()
    .flatten()
    .is_some()
}

async fn load_avatar_url(
    connection: &mut PgConnection,
    user_id: Uuid,
    username: &str,
) -> Option<String> {
    sqlx::query_scalar::<_, Option<String>>(
        "SELECT e.avatar FROM hr_employees e \
         WHERE e.user_id = $1 OR lower(e.username) = lower($2) \
         ORDER BY (e.user_id = $1) DESC NULLS LAST \
         LIMIT 1",
    )
    .bind(user_id)
    .bind(username)
    .fetch_optional(&mut *connection)
    .await
    .ok()
    .flatten()
    .flatten()
    .filter(|value| !value.trim().is_empty())
}

async fn load_all_roles(
    connection: &mut PgConnection,
    username: &str,
    primary_role: &str,
) -> Vec<String> {
    let mut effective_roles = Vec::new();
    if let Some(primary) = roles::normalize(primary_role) {
        effective_roles.push(primary.to_owned());
    }
    if username.trim().is_empty() {
        return effective_roles;
    }

    let secondary_roles = sqlx::query_scalar::<_, Option<String>>(
        r#"
        SELECT role
        FROM user_roles
        WHERE username = $1
          AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP)
        "#,
    )
    .bind(username)
    .fetch_all(&mut *connection)
    .await;

    // The .NET helper treats a missing `user_roles` table during bootstrap as
    // "no secondary roles" and retains the normalized primary role.
    let Ok(secondary_roles) = secondary_roles else {
        return effective_roles;
    };
    for secondary in secondary_roles {
        let Some(normalized) = secondary.as_deref().and_then(roles::normalize) else {
            continue;
        };
        if !effective_roles.iter().any(|role| role == normalized) {
            effective_roles.push(normalized.to_owned());
        }
    }
    effective_roles
}

#[derive(FromRow)]
struct AccessProfileRow {
    primary_role: Option<String>,
    full_name: Option<String>,
    authorization_version: i32,
    secondary_roles: String,
}

#[derive(FromRow)]
struct EmployeeScopeRow {
    access_role: Option<String>,
    department_id: Option<Uuid>,
    location_id: Option<Uuid>,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ScopeKind {
    SelfOnly,
    Department,
    Branch,
    All,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
struct AccessScope {
    kind: ScopeKind,
    department_id: Option<Uuid>,
    location_id: Option<Uuid>,
}

impl AccessScope {
    const SELF_ONLY: Self = Self {
        kind: ScopeKind::SelfOnly,
        department_id: None,
        location_id: None,
    };

    const ALL: Self = Self {
        kind: ScopeKind::All,
        department_id: None,
        location_id: None,
    };

    const fn name(self) -> &'static str {
        match self.kind {
            ScopeKind::All => "all",
            ScopeKind::Department => "department",
            ScopeKind::Branch => "branch",
            ScopeKind::SelfOnly => "self",
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct AccessProfileResponse {
    username: String,
    full_name: String,
    primary_role: String,
    roles: Vec<String>,
    role_labels: Vec<String>,
    permissions: Vec<String>,
    scope: &'static str,
    #[serde(skip_serializing_if = "Option::is_none")]
    department_id: Option<Uuid>,
    #[serde(skip_serializing_if = "Option::is_none")]
    location_id: Option<Uuid>,
    ui_profile: &'static str,
    landing_path: &'static str,
    authorization_version: i32,
}

async fn access_profile(
    Extension(auth): Extension<AuthContext>,
    State(state): State<Arc<AppState>>,
) -> Response {
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for access profile", error),
    };

    let profile = sqlx::query_as::<_, AccessProfileRow>(
        r#"
        SELECT u.role AS primary_role,
               u.full_name,
               COALESCE(u.authorization_version, 1) AS authorization_version,
               COALESCE((
                   SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                   FROM user_roles ur
                   WHERE ur.username = u.username
                     AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
               ), '') AS secondary_roles
        FROM app_users u
        WHERE u.username = $1 AND u.is_deleted = FALSE
        LIMIT 1
        "#,
    )
    .bind(&auth.username)
    .fetch_optional(&mut *connection)
    .await;

    let row = match profile {
        Ok(Some(row)) => row,
        Ok(None) => return access_profile_unavailable(),
        Err(error) => return database_failure("read access profile", error),
    };

    let raw_primary_role = row.primary_role.unwrap_or_default();
    let effective_roles = roles::combine(Some(&raw_primary_role), &row.secondary_roles);
    let permission_set = permissions::for_roles(effective_roles.iter().map(String::as_str));
    let scope = resolve_scope(&mut connection, &auth.username, &permission_set).await;
    let ui_profile = ui_profile_for(&permission_set);
    let landing_path = landing_path_for(ui_profile, &permission_set);
    let mut effective_permissions = permission_set.into_iter().collect::<Vec<_>>();
    effective_permissions.sort_unstable();

    Json(AccessProfileResponse {
        username: auth.username,
        full_name: row.full_name.unwrap_or_default(),
        primary_role: roles::normalize(&raw_primary_role)
            .unwrap_or(roles::EMPLOYEE)
            .to_owned(),
        role_labels: effective_roles
            .iter()
            .map(|role| roles::label(role).to_owned())
            .collect(),
        roles: effective_roles,
        permissions: effective_permissions
            .into_iter()
            .map(str::to_owned)
            .collect(),
        scope: scope.name(),
        department_id: scope.department_id,
        location_id: scope.location_id,
        ui_profile,
        landing_path,
        authorization_version: row.authorization_version,
    })
    .into_response()
}

async fn resolve_scope(
    connection: &mut PgConnection,
    username: &str,
    effective_permissions: &HashSet<&'static str>,
) -> AccessScope {
    if effective_permissions.contains(permissions::USERS_MANAGE)
        || effective_permissions.contains(permissions::HR_MANAGE)
    {
        return AccessScope::ALL;
    }

    let company_wide = effective_permissions.contains(permissions::COMPANY_SCOPE_ALL);
    let employee = sqlx::query_as::<_, EmployeeScopeRow>(
        r#"
        SELECT e.access_role, e.department_id, e.location_id
        FROM app_users u
        JOIN hr_employees e
          ON e.user_id = u.id
          OR (e.user_id IS NULL AND lower(e.username) = lower(u.username))
        WHERE lower(u.username) = lower($1) AND u.is_deleted = FALSE
        ORDER BY (e.user_id = u.id) DESC
        LIMIT 1
        "#,
    )
    .bind(username)
    .fetch_optional(&mut *connection)
    .await;

    match employee {
        // `AccessProfileService` catches an Npgsql failure here and narrows to
        // self-only, even for a company-wide role. This is intentionally fail closed.
        Err(_) => AccessScope::SELF_ONLY,
        Ok(employee) => scope_from_employee(company_wide, employee),
    }
}

fn scope_from_employee(company_wide: bool, employee: Option<EmployeeScopeRow>) -> AccessScope {
    let Some(employee) = employee else {
        return if company_wide {
            AccessScope::ALL
        } else {
            AccessScope::SELF_ONLY
        };
    };

    match employee.access_role.as_deref().unwrap_or_default() {
        "dept_manager" if employee.department_id.is_some() => AccessScope {
            kind: ScopeKind::Department,
            department_id: employee.department_id,
            location_id: employee.location_id,
        },
        "location_manager" if employee.location_id.is_some() => AccessScope {
            kind: ScopeKind::Branch,
            department_id: employee.department_id,
            location_id: employee.location_id,
        },
        _ if company_wide => AccessScope::ALL,
        _ => AccessScope {
            kind: ScopeKind::SelfOnly,
            department_id: employee.department_id,
            location_id: employee.location_id,
        },
    }
}

fn ui_profile_for(effective_permissions: &HashSet<&'static str>) -> &'static str {
    if effective_permissions.contains(permissions::USERS_MANAGE) {
        "admin"
    } else if effective_permissions.contains(permissions::COMPANY_SCOPE_ALL) {
        "executive"
    } else if effective_permissions.contains(permissions::ATTENDANCE_KIOSK)
        && effective_permissions.len() == 1
    {
        "kiosk"
    } else if effective_permissions.contains(permissions::HR_MANAGE) {
        "hr"
    } else if effective_permissions.contains(permissions::ACCOUNTING_ACCESS) {
        "accounting"
    } else {
        "workspace"
    }
}

fn landing_path_for(
    ui_profile: &str,
    effective_permissions: &HashSet<&'static str>,
) -> &'static str {
    match ui_profile {
        "admin" | "executive" => "/dashboard",
        "kiosk" => "/kiosk",
        "hr" => "/quanly-nhansu",
        "accounting" => "/ketoan",
        _ if effective_permissions.contains(permissions::HR_SELF_ACCESS) => "/nhan-su",
        _ => "/chats",
    }
}

fn access_profile_unavailable() -> Response {
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({
            "message": "Không xác định được quyền hiện hành. Vui lòng thử lại."
        })),
    )
        .into_response()
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    // SQLx never interpolates bind values into the query text. Avoid returning
    // database details to callers while retaining an actionable server-side log.
    tracing::warn!(%error, operation, "native authentication database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({
            "message": "Khong ket noi duoc co so du lieu PostgreSQL."
        })),
    )
        .into_response()
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn scope_resolution_matches_manager_and_company_precedence() {
        let department_id = Uuid::new_v4();
        let location_id = Uuid::new_v4();

        assert_eq!(scope_from_employee(false, None), AccessScope::SELF_ONLY);
        assert_eq!(scope_from_employee(true, None), AccessScope::ALL);
        assert_eq!(
            scope_from_employee(
                false,
                Some(EmployeeScopeRow {
                    access_role: Some("dept_manager".to_owned()),
                    department_id: Some(department_id),
                    location_id: Some(location_id),
                }),
            ),
            AccessScope {
                kind: ScopeKind::Department,
                department_id: Some(department_id),
                location_id: Some(location_id),
            }
        );
        assert_eq!(
            scope_from_employee(
                true,
                Some(EmployeeScopeRow {
                    access_role: Some("staff".to_owned()),
                    department_id: Some(department_id),
                    location_id: Some(location_id),
                }),
            ),
            AccessScope::ALL
        );
    }

    #[test]
    fn ui_profile_and_landing_path_keep_the_dotnet_priority() {
        let admin = HashSet::from([permissions::USERS_MANAGE, permissions::COMPANY_SCOPE_ALL]);
        assert_eq!(ui_profile_for(&admin), "admin");
        assert_eq!(landing_path_for("admin", &admin), "/dashboard");

        let kiosk = HashSet::from([permissions::ATTENDANCE_KIOSK]);
        assert_eq!(ui_profile_for(&kiosk), "kiosk");
        assert_eq!(landing_path_for("kiosk", &kiosk), "/kiosk");

        let workspace = HashSet::from([permissions::ATTENDANCE_KIOSK, permissions::HR_SELF_ACCESS]);
        assert_eq!(ui_profile_for(&workspace), "workspace");
        assert_eq!(landing_path_for("workspace", &workspace), "/nhan-su");
    }

    #[test]
    fn combined_role_fast_path_matches_the_isolated_reader_and_fails_closed() {
        // Vai trò chính + phụ, khử trùng lặp, giữ đúng thứ tự "chính trước".
        assert_eq!(
            combine_effective_roles("Admin", "Warehouse,Driver"),
            ["Admin", "Warehouse", "Driver"]
        );
        assert_eq!(
            combine_effective_roles("Employee", "Employee,Warehouse"),
            ["Employee", "Warehouse"]
        );
        assert_eq!(combine_effective_roles("Employee", ""), ["Employee"]);

        // Vai trò chính không nhận diện được ⇒ KHÔNG có vai trò nào, để `me` rơi về
        // `for_roles([role])` và ra tập quyền rỗng. Khác hẳn `roles::combine`, vốn hạ về
        // Employee và sẽ cấp quyền thật cho một chuỗi vai trò hỏng.
        assert!(combine_effective_roles("khong-phai-vai-tro", "").is_empty());
        assert_ne!(
            combine_effective_roles("khong-phai-vai-tro", ""),
            roles::combine(Some("khong-phai-vai-tro"), "")
        );

        // Vai trò phụ lạ bị bỏ qua chứ không làm hỏng cả danh sách.
        assert_eq!(
            combine_effective_roles("Employee", "khong-ton-tai,Driver"),
            ["Employee", "Driver"]
        );
    }

    #[test]
    fn user_json_is_camel_case_and_omits_null_values() {
        let created_at = DateTime::parse_from_rfc3339("2026-08-24T01:02:03Z")
            .unwrap()
            .with_timezone(&Utc);
        let value = serde_json::to_value(UserResponse {
            id: Uuid::nil(),
            username: "employee".to_owned(),
            full_name: "Nhân viên".to_owned(),
            email: String::new(),
            role: roles::EMPLOYEE.to_owned(),
            is_active: true,
            approval_status: "Approved".to_owned(),
            created_at: Some(created_at),
            is_admin: false,
            is_pending: false,
            avatar_url: None,
            verified: false,
            is_diamond: false,
            face_registered: false,
            face_enrollment_pending: false,
            roles: vec![roles::EMPLOYEE.to_owned()],
            permissions: vec![permissions::CHAT_ACCESS.to_owned()],
            can_assign_tasks: false,
        })
        .unwrap();

        assert_eq!(value["createdAt"], "2026-08-24T01:02:03.000Z");
        assert_eq!(value["fullName"], "Nhân viên");
        assert_eq!(value["canAssignTasks"], false);
        assert!(value.get("avatarUrl").is_none());
        assert!(value.get("created_at").is_none());
    }

    #[test]
    fn access_profile_omits_null_scope_ids() {
        let value = serde_json::to_value(AccessProfileResponse {
            username: "employee".to_owned(),
            full_name: "Nhân viên".to_owned(),
            primary_role: roles::EMPLOYEE.to_owned(),
            roles: vec![roles::EMPLOYEE.to_owned()],
            role_labels: vec![roles::label(roles::EMPLOYEE).to_owned()],
            permissions: vec![permissions::HR_SELF_ACCESS.to_owned()],
            scope: "self",
            department_id: None,
            location_id: None,
            ui_profile: "workspace",
            landing_path: "/nhan-su",
            authorization_version: 1,
        })
        .unwrap();

        assert!(value.get("departmentId").is_none());
        assert!(value.get("locationId").is_none());
        assert_eq!(value["authorizationVersion"], 1);
    }
}
