//! Canonical permissions and role-to-permission matrix.
//!
//! Values in this module intentionally mirror
//! `KetoanMini.Api.Security.Permissions`. They are authorization protocol data,
//! not presentation-only constants: existing database roles and API policies
//! depend on exact string equality.

use super::roles;
use std::collections::HashSet;

pub const USERS_READ: &str = "users.read";
pub const USERS_MANAGE: &str = "users.manage";
pub const ROLES_MANAGE: &str = "roles.manage";

pub const SYSTEM_SETTINGS_MANAGE: &str = "system.settings.manage";
pub const SYSTEM_RELEASES_MANAGE: &str = "system.releases.manage";
pub const COMPANY_SCOPE_ALL: &str = "scope.company.all";

pub const AUDIT_READ: &str = "audit.read";

pub const ACCOUNTING_ACCESS: &str = "accounting.access";
pub const VOUCHERS_READ: &str = "vouchers.read";
pub const VOUCHERS_CREATE: &str = "vouchers.create";
pub const VOUCHERS_UPDATE: &str = "vouchers.update";
pub const VOUCHERS_APPROVE: &str = "vouchers.approve";
pub const VOUCHERS_CANCEL: &str = "vouchers.cancel";

pub const PAYOUT_READ: &str = "payout.read";
pub const PAYOUT_CREATE: &str = "payout.create";
pub const PAYOUT_APPROVE: &str = "payout.approve";
pub const PAYOUT_PAY: &str = "payout.pay";

pub const COLLECTIONS_SELF: &str = "collections.self";
pub const COLLECTIONS_READ_ALL: &str = "collections.read.all";
pub const COLLECTIONS_CREATE: &str = "collections.create";
pub const COLLECTIONS_RECEIVE: &str = "collections.receive";
pub const COLLECTIONS_RESOLVE: &str = "collections.resolve";

pub const REPORT_READ: &str = "report.read";
pub const REPORT_EXPORT: &str = "report.export";

pub const ATTENDANCE_SELF: &str = "attendance.self";
pub const ATTENDANCE_READ: &str = "attendance.read";
pub const ATTENDANCE_MANAGE: &str = "attendance.manage";
pub const ATTENDANCE_KIOSK: &str = "attendance.kiosk";

pub const PAYROLL_READ: &str = "payroll.read";
pub const PAYROLL_MANAGE: &str = "payroll.manage";

pub const HR_SELF_ACCESS: &str = "hr.self.access";
pub const HR_READ: &str = "hr.read";
pub const HR_MANAGE: &str = "hr.manage";

pub const REQUESTS_SELF: &str = "requests.self";
pub const REQUESTS_APPROVE: &str = "requests.approve";
pub const REQUESTS_MANAGE: &str = "requests.manage";

pub const PENALTY_READ: &str = "penalty.read";
pub const PENALTY_MANAGE: &str = "penalty.manage";

pub const TASKS_SELF: &str = "tasks.self";
pub const TASKS_ASSIGN: &str = "tasks.assign";

pub const PORTAL_READ: &str = "portal.read";
pub const PORTAL_MANAGE: &str = "portal.manage";

/// JWT/identity claim type rebuilt from the current database role set.
pub const CLAIM_TYPE: &str = "perm";

/// Every valid permission, in the same order as the .NET permission catalog.
pub const ALL: &[&str] = &[
    USERS_READ,
    USERS_MANAGE,
    ROLES_MANAGE,
    SYSTEM_SETTINGS_MANAGE,
    SYSTEM_RELEASES_MANAGE,
    COMPANY_SCOPE_ALL,
    AUDIT_READ,
    ACCOUNTING_ACCESS,
    VOUCHERS_READ,
    VOUCHERS_CREATE,
    VOUCHERS_UPDATE,
    VOUCHERS_APPROVE,
    VOUCHERS_CANCEL,
    PAYOUT_READ,
    PAYOUT_CREATE,
    PAYOUT_APPROVE,
    PAYOUT_PAY,
    COLLECTIONS_SELF,
    COLLECTIONS_READ_ALL,
    COLLECTIONS_CREATE,
    COLLECTIONS_RECEIVE,
    COLLECTIONS_RESOLVE,
    REPORT_READ,
    REPORT_EXPORT,
    ATTENDANCE_SELF,
    ATTENDANCE_READ,
    ATTENDANCE_MANAGE,
    ATTENDANCE_KIOSK,
    PAYROLL_READ,
    PAYROLL_MANAGE,
    HR_SELF_ACCESS,
    HR_READ,
    HR_MANAGE,
    REQUESTS_SELF,
    REQUESTS_APPROVE,
    REQUESTS_MANAGE,
    PENALTY_READ,
    PENALTY_MANAGE,
    TASKS_SELF,
    TASKS_ASSIGN,
    PORTAL_READ,
    PORTAL_MANAGE,
];

const BASE_EMPLOYEE: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
];

const ACCOUNTING_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    ACCOUNTING_ACCESS,
    VOUCHERS_READ,
    VOUCHERS_CREATE,
    VOUCHERS_UPDATE,
    VOUCHERS_CANCEL,
    PAYOUT_READ,
    PAYOUT_CREATE,
    COLLECTIONS_SELF,
    COLLECTIONS_READ_ALL,
    COLLECTIONS_CREATE,
    REPORT_READ,
    REPORT_EXPORT,
    AUDIT_READ,
];

const ADMIN_PERMISSIONS: &[&str] = &[
    USERS_READ,
    USERS_MANAGE,
    ROLES_MANAGE,
    SYSTEM_SETTINGS_MANAGE,
    SYSTEM_RELEASES_MANAGE,
    COMPANY_SCOPE_ALL,
    AUDIT_READ,
    ACCOUNTING_ACCESS,
    VOUCHERS_READ,
    VOUCHERS_CREATE,
    VOUCHERS_UPDATE,
    VOUCHERS_APPROVE,
    VOUCHERS_CANCEL,
    PAYOUT_READ,
    PAYOUT_CREATE,
    PAYOUT_APPROVE,
    PAYOUT_PAY,
    REPORT_READ,
    REPORT_EXPORT,
    ATTENDANCE_SELF,
    ATTENDANCE_READ,
    ATTENDANCE_MANAGE,
    ATTENDANCE_KIOSK,
    PAYROLL_READ,
    PAYROLL_MANAGE,
    HR_SELF_ACCESS,
    HR_READ,
    HR_MANAGE,
    REQUESTS_SELF,
    REQUESTS_APPROVE,
    REQUESTS_MANAGE,
    PENALTY_READ,
    PENALTY_MANAGE,
    TASKS_SELF,
    TASKS_ASSIGN,
    PORTAL_READ,
    PORTAL_MANAGE,
];

const EXECUTIVE_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    COMPANY_SCOPE_ALL,
    ACCOUNTING_ACCESS,
    VOUCHERS_READ,
    PAYOUT_READ,
    REPORT_READ,
    REPORT_EXPORT,
    PAYROLL_READ,
    HR_READ,
    ATTENDANCE_READ,
    AUDIT_READ,
];

const PAYROLL_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    PAYROLL_READ,
    PAYROLL_MANAGE,
    REPORT_READ,
];

const CHIEF_ACCOUNTANT_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    ACCOUNTING_ACCESS,
    VOUCHERS_READ,
    VOUCHERS_CREATE,
    VOUCHERS_UPDATE,
    VOUCHERS_CANCEL,
    PAYOUT_READ,
    PAYOUT_CREATE,
    COLLECTIONS_SELF,
    COLLECTIONS_READ_ALL,
    COLLECTIONS_CREATE,
    REPORT_READ,
    REPORT_EXPORT,
    AUDIT_READ,
    VOUCHERS_APPROVE,
    PAYOUT_APPROVE,
    COLLECTIONS_RESOLVE,
    PAYROLL_READ,
];

const CASHIER_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    ACCOUNTING_ACCESS,
    PAYOUT_READ,
    PAYOUT_PAY,
    COLLECTIONS_SELF,
    COLLECTIONS_READ_ALL,
    COLLECTIONS_RECEIVE,
    REPORT_READ,
    AUDIT_READ,
];

const HR_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    HR_READ,
    HR_MANAGE,
    ATTENDANCE_READ,
    ATTENDANCE_MANAGE,
    REQUESTS_APPROVE,
    REQUESTS_MANAGE,
    PAYROLL_READ,
    PENALTY_MANAGE,
    REPORT_READ,
    PORTAL_MANAGE,
];

const MANAGER_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    HR_READ,
    REQUESTS_APPROVE,
    TASKS_ASSIGN,
    ATTENDANCE_READ,
    REPORT_READ,
];

const WAREHOUSE_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    TASKS_ASSIGN,
];

const DRIVER_PERMISSIONS: &[&str] = &[
    HR_SELF_ACCESS,
    ATTENDANCE_SELF,
    REQUESTS_SELF,
    TASKS_SELF,
    PORTAL_READ,
    PENALTY_READ,
    COLLECTIONS_SELF,
];

const KIOSK_PERMISSIONS: &[&str] = &[ATTENDANCE_KIOSK];

/// Canonical role-to-permission mapping in the same order as the .NET map.
pub const ROLE_PERMISSIONS: &[(&str, &[&str])] = &[
    (roles::ADMIN, ADMIN_PERMISSIONS),
    (roles::EMPLOYEE, BASE_EMPLOYEE),
    (roles::EXECUTIVE, EXECUTIVE_PERMISSIONS),
    (roles::ACCOUNTING, ACCOUNTING_PERMISSIONS),
    (roles::PAYROLL, PAYROLL_PERMISSIONS),
    (roles::CHIEF_ACCOUNTANT, CHIEF_ACCOUNTANT_PERMISSIONS),
    (roles::CASHIER, CASHIER_PERMISSIONS),
    (roles::HR, HR_PERMISSIONS),
    (roles::MANAGER, MANAGER_PERMISSIONS),
    (roles::WAREHOUSE, WAREHOUSE_PERMISSIONS),
    (roles::DRIVER, DRIVER_PERMISSIONS),
    (roles::KIOSK, KIOSK_PERMISSIONS),
];

/// Resolve a canonical or aliased role to its permission slice.
pub fn role_permissions(role: &str) -> Option<&'static [&'static str]> {
    let canonical = roles::normalize(role)?;
    ROLE_PERMISSIONS
        .iter()
        .find_map(|(candidate, permissions)| (*candidate == canonical).then_some(*permissions))
}

/// Union permissions from every active primary/secondary role. Unknown roles
/// are ignored, so an entirely unknown role set has no permissions.
pub fn for_roles<I, R>(role_values: I) -> HashSet<&'static str>
where
    I: IntoIterator<Item = R>,
    R: AsRef<str>,
{
    let mut result = HashSet::new();
    for role in role_values {
        if let Some(permissions) = role_permissions(role.as_ref()) {
            result.extend(permissions.iter().copied());
        }
    }
    result
}

pub fn is_known(permission: &str) -> bool {
    ALL.contains(&permission)
}

pub fn policy(permission: &str) -> String {
    format!("perm:{permission}")
}

/// Vietnamese permission label, falling back to the unchanged key.
pub fn label(permission: &str) -> &str {
    match permission {
        USERS_READ => "Xem danh sách người dùng",
        USERS_MANAGE => "Quản lý tài khoản & phân quyền",
        ROLES_MANAGE => "Quản lý vai trò",
        SYSTEM_SETTINGS_MANAGE => "Cấu hình hệ thống",
        SYSTEM_RELEASES_MANAGE => "Quản lý bản cập nhật APK",
        COMPANY_SCOPE_ALL => "Xem dữ liệu toàn công ty",
        AUDIT_READ => "Xem nhật ký hoạt động",
        ACCOUNTING_ACCESS => "Vào khu kế toán",
        VOUCHERS_READ => "Xem chứng từ",
        VOUCHERS_CREATE => "Lập chứng từ",
        VOUCHERS_UPDATE => "Sửa chứng từ",
        VOUCHERS_APPROVE => "Duyệt chứng từ",
        VOUCHERS_CANCEL => "Hủy chứng từ",
        PAYOUT_READ => "Xem sổ phiếu chi tiền mặt",
        PAYOUT_CREATE => "Lập phiếu chi tiền mặt",
        PAYOUT_APPROVE => "Duyệt phiếu chi tiền mặt",
        PAYOUT_PAY => "Thực hiện chi tiền mặt",
        COLLECTIONS_SELF => "Xử lý lệnh thu tiền được giao cho mình",
        COLLECTIONS_READ_ALL => "Xem toàn bộ lệnh thu tiền khách hàng",
        COLLECTIONS_CREATE => "Tạo lệnh thu tiền khách hàng",
        COLLECTIONS_RECEIVE => "Kiểm đếm và nhận tiền từ tài xế",
        COLLECTIONS_RESOLVE => "Xử lý sai lệch lệnh thu tiền",
        REPORT_READ => "Xem báo cáo",
        REPORT_EXPORT => "Xuất báo cáo",
        ATTENDANCE_SELF => "Tự chấm công & xem bảng công của mình",
        ATTENDANCE_READ => "Xem chấm công nhân viên",
        ATTENDANCE_MANAGE => "Quản lý chấm công",
        ATTENDANCE_KIOSK => "Máy kiosk chấm công ẩn danh",
        PAYROLL_READ => "Xem bảng lương",
        PAYROLL_MANAGE => "Quản lý bảng lương",
        HR_SELF_ACCESS => "Xem hồ sơ/đơn từ của mình",
        HR_READ => "Xem dữ liệu nhân sự",
        HR_MANAGE => "Quản lý nhân sự",
        REQUESTS_SELF => "Gửi đơn từ",
        REQUESTS_APPROVE => "Duyệt đơn từ",
        REQUESTS_MANAGE => "Quản lý đơn từ",
        PENALTY_READ => "Xem phạt/kỷ luật của mình",
        PENALTY_MANAGE => "Quản lý phạt/kỷ luật",
        TASKS_SELF => "Nhận & báo cáo việc được giao",
        TASKS_ASSIGN => "Giao việc & nghiệm thu",
        PORTAL_READ => "Xem cổng thông tin",
        PORTAL_MANAGE => "Quản trị cổng thông tin",
        _ => permission,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn set(role: &str) -> HashSet<&'static str> {
        for_roles([role])
    }

    #[test]
    fn catalog_and_role_matrix_are_complete_and_unique() {
        assert_eq!(ALL.len(), ALL.iter().copied().collect::<HashSet<_>>().len());
        assert_eq!(ALL.len(), 42);
        assert_eq!(ROLE_PERMISSIONS.len(), roles::ALL.len());

        for role in roles::ALL {
            let permissions = role_permissions(role).expect("every canonical role must be mapped");
            assert_eq!(
                permissions.len(),
                permissions.iter().copied().collect::<HashSet<_>>().len(),
                "duplicate permission for {role}"
            );
            assert!(
                permissions.iter().all(|permission| is_known(permission)),
                "unknown permission for {role}"
            );
        }
    }

    #[test]
    fn admin_has_every_technical_permission_but_no_cash_collection_role_rights() {
        let admin = set(roles::ADMIN);
        let collection_rights = [
            COLLECTIONS_SELF,
            COLLECTIONS_READ_ALL,
            COLLECTIONS_CREATE,
            COLLECTIONS_RECEIVE,
            COLLECTIONS_RESOLVE,
        ];

        assert!(
            collection_rights
                .iter()
                .all(|permission| !admin.contains(permission))
        );
        assert!(
            ALL.iter()
                .filter(|permission| !collection_rights.contains(permission))
                .all(|permission| admin.contains(permission))
        );
    }

    #[test]
    fn only_admin_can_manage_accounts_and_system_settings() {
        let admin = set(roles::ADMIN);
        assert!(admin.contains(USERS_MANAGE));
        assert!(admin.contains(SYSTEM_SETTINGS_MANAGE));

        for role in roles::ALL
            .iter()
            .copied()
            .filter(|role| *role != roles::ADMIN)
        {
            let permissions = set(role);
            assert!(!permissions.contains(USERS_MANAGE), "role {role}");
            assert!(!permissions.contains(SYSTEM_SETTINGS_MANAGE), "role {role}");
        }
    }

    #[test]
    fn kiosk_and_employee_stay_least_privileged() {
        assert_eq!(set(roles::KIOSK), HashSet::from([ATTENDANCE_KIOSK]));
        assert_eq!(
            set(roles::EMPLOYEE),
            BASE_EMPLOYEE.iter().copied().collect()
        );
        assert!(!set(roles::EMPLOYEE).contains(&USERS_MANAGE));
    }

    #[test]
    fn chief_accountant_inherits_accounting_and_only_gets_expected_extensions() {
        let accounting = set(roles::ACCOUNTING);
        let chief = set(roles::CHIEF_ACCOUNTANT);
        assert!(accounting.is_subset(&chief));

        let extensions: HashSet<_> = chief.difference(&accounting).copied().collect();
        assert_eq!(
            extensions,
            HashSet::from([
                VOUCHERS_APPROVE,
                PAYOUT_APPROVE,
                COLLECTIONS_RESOLVE,
                PAYROLL_READ,
            ])
        );
    }

    #[test]
    fn cashier_and_driver_collection_boundaries_match_business_roles() {
        let accounting = set(roles::ACCOUNTING);
        assert!(accounting.contains(COLLECTIONS_SELF));
        assert!(accounting.contains(COLLECTIONS_READ_ALL));
        assert!(accounting.contains(COLLECTIONS_CREATE));
        assert!(!accounting.contains(COLLECTIONS_RECEIVE));
        assert!(!accounting.contains(COLLECTIONS_RESOLVE));

        let chief = set(roles::CHIEF_ACCOUNTANT);
        assert!(chief.contains(COLLECTIONS_SELF));
        assert!(chief.contains(COLLECTIONS_READ_ALL));
        assert!(chief.contains(COLLECTIONS_CREATE));
        assert!(!chief.contains(COLLECTIONS_RECEIVE));
        assert!(chief.contains(COLLECTIONS_RESOLVE));

        let cashier = set(roles::CASHIER);
        assert!(cashier.contains(PAYOUT_PAY));
        assert!(!cashier.contains(PAYOUT_CREATE));
        assert!(!cashier.contains(PAYOUT_APPROVE));
        assert!(cashier.contains(COLLECTIONS_RECEIVE));
        assert!(!cashier.contains(COLLECTIONS_CREATE));
        assert!(!cashier.contains(COLLECTIONS_RESOLVE));

        let driver = set(roles::DRIVER);
        assert!(driver.contains(COLLECTIONS_SELF));
        assert!(!driver.contains(COLLECTIONS_READ_ALL));
        assert!(!driver.contains(COLLECTIONS_CREATE));
        assert!(!driver.contains(COLLECTIONS_RECEIVE));
        assert!(!driver.contains(COLLECTIONS_RESOLVE));

        for role in [
            roles::EMPLOYEE,
            roles::EXECUTIVE,
            roles::PAYROLL,
            roles::HR,
            roles::MANAGER,
            roles::WAREHOUSE,
            roles::KIOSK,
        ] {
            let permissions = set(role);
            for collection_permission in [
                COLLECTIONS_SELF,
                COLLECTIONS_READ_ALL,
                COLLECTIONS_CREATE,
                COLLECTIONS_RECEIVE,
                COLLECTIONS_RESOLVE,
            ] {
                assert!(
                    !permissions.contains(collection_permission),
                    "role {role} unexpectedly has {collection_permission}"
                );
            }
        }
    }

    #[test]
    fn executive_is_company_wide_read_only() {
        let executive = set(roles::EXECUTIVE);
        assert!(executive.contains(COMPANY_SCOPE_ALL));
        assert!(executive.contains(VOUCHERS_READ));
        assert!(executive.contains(PAYROLL_READ));
        assert!(!executive.contains(USERS_MANAGE));
        assert!(!executive.contains(VOUCHERS_CREATE));
        assert!(!executive.contains(VOUCHERS_APPROVE));
        assert!(!executive.contains(PAYOUT_PAY));
    }

    #[test]
    fn multiple_roles_union_permissions_and_unknown_roles_fail_closed() {
        let combined = for_roles([roles::EMPLOYEE, roles::WAREHOUSE]);
        assert!(combined.contains(TASKS_SELF));
        assert!(combined.contains(TASKS_ASSIGN));
        assert!(for_roles(["not-a-role"]).is_empty());
        assert_eq!(role_permissions("Thủ kho"), Some(WAREHOUSE_PERMISSIONS));
    }

    #[test]
    fn policy_and_labels_are_wire_compatible() {
        assert_eq!(CLAIM_TYPE, "perm");
        assert_eq!(policy(PAYROLL_MANAGE), "perm:payroll.manage");
        assert_eq!(
            label(COLLECTIONS_RECEIVE),
            "Kiểm đếm và nhận tiền từ tài xế"
        );
        assert_eq!(label("future.permission"), "future.permission");
    }
}
