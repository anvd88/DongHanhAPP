//! Small read-only catalogs whose source of truth already lives in Rust.
//!
//! Mount this router behind `auth::require_auth`. Route-local middleware keeps
//! the same permission boundaries as the ASP.NET endpoints.

use crate::{
    auth::{AuthContext, permissions, roles},
    state::AppState,
};
use axum::{
    Json, Router,
    body::Body,
    http::{Request, StatusCode},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::get,
};
use serde::Serialize;
use std::sync::Arc;

const ROLE_CATALOG_PATH: &str = "/api/roles/catalog";
const PENALTY_TYPES_PATH: &str = "/api/penalties/types";

const PENALTY_TYPES: &[(&str, &str)] = &[
    ("reminder", "Nhắc nhở"),
    ("warning", "Cảnh cáo"),
    ("fine", "Phạt tiền"),
    ("suspension", "Đình chỉ"),
    ("other", "Khác"),
];

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(
            ROLE_CATALOG_PATH,
            get(role_catalog).route_layer(middleware::from_fn(require_users_manage)),
        )
        .route(
            PENALTY_TYPES_PATH,
            get(penalty_types).route_layer(middleware::from_fn(require_penalty_read)),
        )
}

async fn require_users_manage(request: Request<Body>, next: Next) -> Response {
    require_permission(request, next, permissions::USERS_MANAGE).await
}

async fn require_penalty_read(request: Request<Body>, next: Next) -> Response {
    require_permission(request, next, permissions::PENALTY_READ).await
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

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct PermissionCatalogItem {
    key: &'static str,
    label: &'static str,
}

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct RoleCatalogItem {
    role: &'static str,
    label: &'static str,
    assignable: bool,
    technical: bool,
    permissions: Vec<PermissionCatalogItem>,
}

fn build_role_catalog() -> Vec<RoleCatalogItem> {
    roles::ALL
        .iter()
        .map(|role| RoleCatalogItem {
            role,
            label: roles::label(role),
            assignable: roles::ASSIGNABLE.contains(role),
            technical: *role == roles::KIOSK,
            permissions: permissions::role_permissions(role)
                .unwrap_or_default()
                .iter()
                .map(|key| PermissionCatalogItem {
                    key,
                    label: permissions::label(key),
                })
                .collect(),
        })
        .collect()
}

async fn role_catalog() -> Json<Vec<RoleCatalogItem>> {
    Json(build_role_catalog())
}

#[derive(Debug, Eq, PartialEq, Serialize)]
struct PenaltyTypeItem {
    #[serde(rename = "type")]
    kind: &'static str,
    label: &'static str,
}

fn build_penalty_types() -> Vec<PenaltyTypeItem> {
    PENALTY_TYPES
        .iter()
        .map(|(kind, label)| PenaltyTypeItem { kind, label })
        .collect()
}

async fn penalty_types() -> Json<Vec<PenaltyTypeItem>> {
    Json(build_penalty_types())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn role_catalog_reuses_the_authorization_source_of_truth() {
        let catalog = build_role_catalog();
        assert_eq!(catalog.len(), 12);
        assert_eq!(catalog[0].role, "Admin");
        assert_eq!(catalog[0].label, "Quản trị hệ thống");
        assert!(catalog[0].assignable);
        assert!(!catalog[0].technical);
        assert_eq!(
            catalog[0]
                .permissions
                .iter()
                .map(|item| item.key)
                .collect::<Vec<_>>(),
            permissions::role_permissions(roles::ADMIN).unwrap()
        );

        let kiosk = catalog
            .iter()
            .find(|item| item.role == roles::KIOSK)
            .unwrap();
        assert!(!kiosk.assignable);
        assert!(kiosk.technical);
        assert_eq!(kiosk.permissions.len(), 1);
        assert_eq!(kiosk.permissions[0].key, permissions::ATTENDANCE_KIOSK);
    }

    #[test]
    fn role_catalog_json_matches_the_dotnet_property_contract() {
        let value = serde_json::to_value(build_role_catalog()).unwrap();
        assert_eq!(
            value[0],
            json!({
                "role": "Admin",
                "label": "Quản trị hệ thống",
                "assignable": true,
                "technical": false,
                "permissions": permissions::role_permissions(roles::ADMIN)
                    .unwrap()
                    .iter()
                    .map(|key| json!({"key": key, "label": permissions::label(key)}))
                    .collect::<Vec<_>>()
            })
        );
    }

    #[test]
    fn penalty_type_catalog_is_byte_for_byte_business_compatible() {
        assert_eq!(
            serde_json::to_value(build_penalty_types()).unwrap(),
            json!([
                {"type": "reminder", "label": "Nhắc nhở"},
                {"type": "warning", "label": "Cảnh cáo"},
                {"type": "fine", "label": "Phạt tiền"},
                {"type": "suspension", "label": "Đình chỉ"},
                {"type": "other", "label": "Khác"}
            ])
        );
    }
}
