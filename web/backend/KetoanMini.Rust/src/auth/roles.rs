//! Canonical application roles.
//!
//! This module intentionally mirrors `KetoanMini.Api.Security.AppRoles`. Role
//! strings are persisted in PostgreSQL and embedded in existing JWTs, so their
//! spelling, aliases, ordering, labels, and priority are compatibility data.

pub const ADMIN: &str = "Admin";
pub const EXECUTIVE: &str = "Executive";
pub const ACCOUNTING: &str = "Accounting";
pub const PAYROLL: &str = "Payroll";
pub const CASHIER: &str = "Cashier";
pub const HR: &str = "HR";
pub const DRIVER: &str = "Driver";
pub const EMPLOYEE: &str = "Employee";
pub const KIOSK: &str = "Kiosk";
pub const WAREHOUSE: &str = "Warehouse";
pub const CHIEF_ACCOUNTANT: &str = "ChiefAccountant";
pub const MANAGER: &str = "Manager";

/// Every canonical role, in the same stable order exposed by the .NET API.
pub const ALL: &[&str] = &[
    ADMIN,
    EXECUTIVE,
    CHIEF_ACCOUNTANT,
    ACCOUNTING,
    PAYROLL,
    CASHIER,
    WAREHOUSE,
    HR,
    MANAGER,
    DRIVER,
    EMPLOYEE,
    KIOSK,
];

/// Roles that may be assigned to an employee profile. Kiosk is a technical
/// identity rather than a job role.
pub const ASSIGNABLE: &[&str] = &[
    ADMIN,
    EXECUTIVE,
    CHIEF_ACCOUNTANT,
    ACCOUNTING,
    PAYROLL,
    CASHIER,
    WAREHOUSE,
    HR,
    MANAGER,
    DRIVER,
    EMPLOYEE,
];

/// Roles that may be granted in addition to a user's primary role.
/// Admin and Kiosk are deliberately excluded.
pub const SECONDARY: &[&str] = &[
    WAREHOUSE,
    MANAGER,
    CHIEF_ACCOUNTANT,
    ACCOUNTING,
    PAYROLL,
    CASHIER,
    HR,
    DRIVER,
];

/// Normalize persisted, legacy, and Vietnamese role aliases to their canonical
/// value. Unknown and blank values fail closed by returning `None`.
pub fn normalize(role: &str) -> Option<&'static str> {
    let role = role.trim();
    if role.is_empty() {
        return None;
    }

    match role.to_lowercase().as_str() {
        "admin" => Some(ADMIN),
        "executive" | "director" | "giamdoc" | "giam doc" | "giám đốc" | "ban giám đốc" => {
            Some(EXECUTIVE)
        }
        "accounting" | "ketoan" | "ke toan" => Some(ACCOUNTING),
        "payroll"
        | "payrollaccountant"
        | "ketoantienluong"
        | "ke toan tien luong"
        | "kế toán tiền lương" => Some(PAYROLL),
        "chiefaccountant" | "ketoantruong" | "ke toan truong" | "kế toán trưởng" => {
            Some(CHIEF_ACCOUNTANT)
        }
        "cashier" | "thuquy" | "thu quy" | "thủ quỹ" => Some(CASHIER),
        "manager" | "truongphong" | "truong phong" | "trưởng phòng" => Some(MANAGER),
        "hr" | "humanresources" => Some(HR),
        "driver" | "laixe" | "lai xe" | "lái xe" => Some(DRIVER),
        "employee" | "user" => Some(EMPLOYEE),
        "kiosk" => Some(KIOSK),
        "warehouse" | "thukho" | "thu kho" | "thủ kho" | "storekeeper" => Some(WAREHOUSE),
        _ => None,
    }
}

/// Build the effective role list exactly like the current .NET
/// `AccessProfileService.Combine`: the normalized primary role comes first,
/// a missing/invalid primary falls back to Employee, and valid CSV extras are
/// appended once in input order.
pub fn combine(primary: Option<&str>, extra_csv: &str) -> Vec<String> {
    let primary = primary.and_then(normalize).unwrap_or(EMPLOYEE);
    let mut combined = vec![primary.to_owned()];

    for extra in extra_csv.split(',') {
        if let Some(normalized) = normalize(extra)
            && !combined.iter().any(|role| role.as_str() == normalized)
        {
            combined.push(normalized.to_owned());
        }
    }

    combined
}

/// Priority used to choose a stable primary role for multi-role employees.
/// Effective authorization is still the union of every active role.
pub fn primary_priority(role: Option<&str>) -> i32 {
    match role.and_then(normalize) {
        Some(ADMIN) => 1000,
        Some(CHIEF_ACCOUNTANT) => 900,
        Some(ACCOUNTING) => 800,
        Some(PAYROLL) => 750,
        Some(CASHIER) => 700,
        Some(HR) => 600,
        Some(MANAGER) => 500,
        Some(WAREHOUSE) => 400,
        Some(EXECUTIVE) => 300,
        Some(DRIVER) => 200,
        Some(EMPLOYEE) => 100,
        Some(KIOSK) => 0,
        _ => -1,
    }
}

/// Match the current .NET definition: every recognized role except Employee is
/// privileged, including the technical Kiosk role.
pub fn is_privileged(role: Option<&str>) -> bool {
    matches!(role.and_then(normalize), Some(normalized) if normalized != EMPLOYEE)
}

/// Vietnamese display label. An unknown value is returned unchanged, matching
/// the .NET fallback behavior.
pub fn label(role: &str) -> &str {
    match normalize(role) {
        Some(ADMIN) => "Quản trị hệ thống",
        Some(EXECUTIVE) => "Ban giám đốc",
        Some(ACCOUNTING) => "Kế toán",
        Some(PAYROLL) => "Kế toán tiền lương",
        Some(CHIEF_ACCOUNTANT) => "Kế toán trưởng",
        Some(CASHIER) => "Thủ quỹ",
        Some(HR) => "Quản lý nhân sự",
        Some(MANAGER) => "Quản lý",
        Some(DRIVER) => "Lái xe",
        Some(EMPLOYEE) => "Nhân viên",
        Some(KIOSK) => "Kiosk",
        Some(WAREHOUSE) => "Thủ kho",
        _ => role,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashSet;

    #[test]
    fn catalogs_are_unique_and_keep_security_boundaries() {
        assert_eq!(ALL.len(), ALL.iter().copied().collect::<HashSet<_>>().len());
        assert_eq!(
            ASSIGNABLE.len(),
            ASSIGNABLE.iter().copied().collect::<HashSet<_>>().len()
        );
        assert_eq!(
            SECONDARY.len(),
            SECONDARY.iter().copied().collect::<HashSet<_>>().len()
        );

        assert_eq!(ALL.len(), 12);
        assert_eq!(ASSIGNABLE.len(), 11);
        assert_eq!(SECONDARY.len(), 8);

        assert!(ALL.contains(&KIOSK));
        assert!(!ASSIGNABLE.contains(&KIOSK));
        assert!(!SECONDARY.contains(&ADMIN));
        assert!(!SECONDARY.contains(&KIOSK));
        assert!(!SECONDARY.contains(&EMPLOYEE));
    }

    #[test]
    fn legacy_and_vietnamese_aliases_normalize_exactly() {
        let cases = [
            (" ADMIN ", ADMIN),
            ("director", EXECUTIVE),
            ("Ban Giám Đốc", EXECUTIVE),
            ("ke toan", ACCOUNTING),
            ("KẾ TOÁN TIỀN LƯƠNG", PAYROLL),
            ("ketoantruong", CHIEF_ACCOUNTANT),
            ("Thủ Quỹ", CASHIER),
            ("trưởng phòng", MANAGER),
            ("HumanResources", HR),
            ("LÁI XE", DRIVER),
            ("user", EMPLOYEE),
            ("storekeeper", WAREHOUSE),
        ];

        for (input, expected) in cases {
            assert_eq!(normalize(input), Some(expected), "alias {input}");
        }
        assert_eq!(normalize("   "), None);
        assert_eq!(normalize("super-admin"), None);
    }

    #[test]
    fn combine_matches_access_profile_role_union() {
        assert_eq!(combine(None, ""), vec![EMPLOYEE]);
        assert_eq!(combine(Some("not-a-role"), ""), vec![EMPLOYEE]);
        assert_eq!(
            combine(
                Some(" ke toan "),
                "Thủ kho, ACCOUNTING,unknown,thủ quỹ, Thủ kho"
            ),
            vec![ACCOUNTING, WAREHOUSE, CASHIER]
        );
        assert_eq!(
            combine(Some("Employee"), ", driver, user, lái xe,"),
            vec![EMPLOYEE, DRIVER]
        );
    }

    #[test]
    fn primary_priority_matches_the_dotnet_order() {
        let ordered = [
            ADMIN,
            CHIEF_ACCOUNTANT,
            ACCOUNTING,
            PAYROLL,
            CASHIER,
            HR,
            MANAGER,
            WAREHOUSE,
            EXECUTIVE,
            DRIVER,
            EMPLOYEE,
            KIOSK,
        ];
        assert!(
            ordered
                .windows(2)
                .all(|pair| { primary_priority(Some(pair[0])) > primary_priority(Some(pair[1])) })
        );
        assert_eq!(primary_priority(Some("unknown")), -1);
    }

    #[test]
    fn privilege_and_labels_follow_normalization() {
        assert!(!is_privileged(Some("employee")));
        assert!(is_privileged(Some("kiosk")));
        assert!(!is_privileged(Some("unknown")));
        assert_eq!(label("kế toán trưởng"), "Kế toán trưởng");
        assert_eq!(label("FutureRole"), "FutureRole");
        assert_eq!(label(""), "");
    }
}
