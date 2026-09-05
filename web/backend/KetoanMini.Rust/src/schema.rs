//! Read-only compatibility check for the schema owned by the existing backend.
//!
//! Rust is not a migration owner during the compatibility phase. This module only reads
//! PostgreSQL catalogs and fails startup when the relations needed by native Rust code are not
//! available yet.

use sqlx::PgPool;
use std::collections::{BTreeMap, BTreeSet};
use thiserror::Error;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct TableRequirement {
    pub table: &'static str,
    pub columns: &'static [&'static str],
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct IndexRequirement {
    pub index: &'static str,
    pub table: &'static str,
    pub columns: &'static [&'static str],
    pub predicate: &'static str,
}

/// Minimum schema used by native authentication, authorization, sessions and access scoping.
///
/// Keep this list limited to columns Rust actually reads or writes. Extra columns in the existing
/// database are intentionally accepted so additive .NET migrations remain forward-compatible.
pub const REQUIRED_SCHEMA: &[TableRequirement] = &[
    TableRequirement {
        table: "app_config",
        columns: &[
            "announcement",
            "announcement_level",
            "face_enroll_banner_enabled",
            "feature_flags",
            "foreground_poll_seconds",
            "id",
            "notices",
            "onboarding",
            "portrait_aspect",
            "portrait_height_factor",
            "portrait_min_width_factor",
            "portrait_vertical_nudge",
            "updated_at",
            "updated_by",
        ],
    },
    TableRequirement {
        table: "app_portal_about",
        columns: &[
            "address",
            "content",
            "cover_image",
            "email",
            "hotline",
            "id",
            "title",
            "updated_at",
            "website",
        ],
    },
    TableRequirement {
        table: "app_portal_posts",
        columns: &[
            "author_username",
            "body",
            "cover_image",
            "created_at",
            "event_at",
            "id",
            "kind",
            "location",
            "pinned",
            "published",
            "summary",
            "title",
            "updated_at",
        ],
    },
    TableRequirement {
        table: "app_users",
        columns: &[
            "approval_status",
            "authorization_version",
            "created_at",
            "email",
            "full_name",
            "id",
            "is_active",
            "is_deleted",
            "password_hash",
            "role",
            "username",
        ],
    },
    TableRequirement {
        table: "audit_logs",
        columns: &[
            "action",
            "details",
            "entity",
            "entity_name",
            "occurred_at",
            "username",
        ],
    },
    TableRequirement {
        table: "gia_cong_hang_hoa",
        columns: &[
            "don_gia_gia_cong",
            "don_vi_tinh",
            "ghi_chu",
            "id",
            "loai_dong",
            "ma_hang",
            "phieu_id",
            "quy_cach",
            "so_luong",
            "ten_hang",
        ],
    },
    TableRequirement {
        table: "gia_cong_phieu",
        columns: &[
            "created_at",
            "doi_tac",
            "ghi_chu",
            "han_hoan_thanh",
            "id",
            "loai_phieu",
            "ma_phieu",
            "ngay_lap",
            "nhan_vien",
            "updated_at",
        ],
    },
    TableRequirement {
        table: "help_faqs",
        columns: &[
            "answer",
            "category",
            "id",
            "is_published",
            "order_no",
            "question",
            "updated_at",
            "updated_by",
        ],
    },
    TableRequirement {
        table: "hr_bank_accounts",
        columns: &[
            "account_holder",
            "account_number",
            "bank",
            "branch",
            "created_at",
            "employee_id",
            "id",
            "is_default",
            "note",
            "updated_at",
        ],
    },
    TableRequirement {
        table: "hr_contracts",
        columns: &[
            "contract_no",
            "contract_type",
            "employee_id",
            "end_date",
            "id",
            "status",
        ],
    },
    TableRequirement {
        table: "hr_departments",
        columns: &["id", "is_accounting", "name"],
    },
    TableRequirement {
        table: "hr_device_tokens",
        columns: &["platform", "token", "updated_at", "username"],
    },
    TableRequirement {
        table: "hr_documents",
        columns: &["doc_type", "employee_id", "expires_at", "id", "title"],
    },
    TableRequirement {
        table: "hr_employee_benefits",
        columns: &[
            "benefit_type",
            "created_at",
            "employee_id",
            "id",
            "title",
            "valid_from",
            "valid_to",
            "value_text",
        ],
    },
    TableRequirement {
        table: "hr_employee_rewards",
        columns: &["awarded_at", "employee_id", "id", "note", "points", "title"],
    },
    TableRequirement {
        table: "hr_employees",
        columns: &[
            "access_role",
            "department_id",
            "dob",
            "email",
            "employee_code",
            "full_name",
            "hire_date",
            "id",
            "location_id",
            "manager_id",
            "phone",
            "position",
            "status",
            "user_id",
            "username",
        ],
    },
    TableRequirement {
        table: "hr_leave_balances",
        columns: &["employee_id", "total_days", "used_days", "year"],
    },
    TableRequirement {
        table: "hr_onboarding_tasks",
        columns: &[
            "acknowledged_at",
            "action_key",
            "completed_at",
            "created_at",
            "due_at",
            "employee_id",
            "id",
            "policy_text",
            "title",
        ],
    },
    TableRequirement {
        table: "hr_payslips",
        columns: &[
            "acknowledged_at",
            "employee_id",
            "id",
            "period",
            "published",
        ],
    },
    TableRequirement {
        table: "hr_penalty_refunds",
        columns: &[
            "amount",
            "appeal_request_no",
            "applied_period",
            "approved_by",
            "created_at",
            "created_by",
            "decided_at",
            "employee_id",
            "id",
            "note",
            "payout_method",
            "penalty_id",
            "penalty_no",
            "reason",
            "refund_no",
            "status",
        ],
    },
    TableRequirement {
        table: "hr_performance_goals",
        columns: &[
            "description",
            "due_at",
            "employee_id",
            "id",
            "progress",
            "target",
            "title",
            "unit",
            "updated_at",
        ],
    },
    TableRequirement {
        table: "hr_performance_reviews",
        columns: &[
            "closes_at",
            "employee_id",
            "id",
            "manager_comment",
            "period",
            "score",
            "self_assessment",
            "status",
            "updated_at",
        ],
    },
    TableRequirement {
        table: "hr_request_approvals",
        columns: &[
            "approver_role",
            "approver_username",
            "request_id",
            "status",
            "step_no",
        ],
    },
    TableRequirement {
        table: "hr_requests",
        columns: &[
            "created_at",
            "current_step",
            "due_at",
            "employee_id",
            "id",
            "payload",
            "req_type",
            "request_no",
            "status",
            "title",
        ],
    },
    TableRequirement {
        table: "hr_shift_assignments",
        columns: &["employee_id", "id", "shift_id", "work_date"],
    },
    TableRequirement {
        table: "hr_shifts",
        columns: &["end_time", "id", "is_overnight", "name", "start_time"],
    },
    TableRequirement {
        table: "hr_training_courses",
        columns: &[
            "active",
            "certificate_valid_months",
            "created_at",
            "description",
            "id",
            "material_url",
            "quiz",
            "title",
            "video_url",
        ],
    },
    TableRequirement {
        table: "hr_training_enrollments",
        columns: &[
            "certificate_expires_at",
            "completed_at",
            "course_id",
            "employee_id",
            "progress",
            "resume_seconds",
            "score",
            "updated_at",
        ],
    },
    TableRequirement {
        table: "survey_answers",
        columns: &[
            "answer",
            "id",
            "option_indices",
            "question_id",
            "response_id",
        ],
    },
    TableRequirement {
        table: "survey_questions",
        columns: &[
            "id",
            "options",
            "order_no",
            "qtype",
            "question",
            "required",
            "survey_id",
        ],
    },
    TableRequirement {
        table: "survey_responses",
        columns: &["id", "respondent_hash", "submitted_at", "survey_id"],
    },
    TableRequirement {
        table: "surveys",
        columns: &[
            "allow_multiple",
            "closes_at",
            "created_at",
            "created_by",
            "description",
            "id",
            "is_active",
            "is_anonymous",
            "title",
        ],
    },
    TableRequirement {
        table: "user_roles",
        columns: &["expires_at", "role", "username"],
    },
    TableRequirement {
        table: "user_sessions",
        columns: &[
            "client_kind",
            "end_reason",
            "ended_at",
            "is_active",
            "last_seen",
            "machine_name",
            "revoked",
            "revoked_at",
            "revoked_by",
            "session_token",
            "started_at",
            "user_agent",
            "username",
        ],
    },
    TableRequirement {
        table: "web_notifications",
        columns: &[
            "actor",
            "app_target",
            "body",
            "category",
            "created_at",
            "id",
            "link",
            "notif_id",
            "read_at",
            "title",
            "username",
        ],
    },
    TableRequirement {
        table: "web_user_preferences",
        columns: &[
            "preference_key",
            "preference_value",
            "updated_at",
            "user_id",
        ],
    },
];

/// Sequences consumed by native write paths. They are only located in the catalog here; startup
/// never calls `nextval` and therefore remains read-only.
pub const REQUIRED_SEQUENCES: &[&str] = &[
    "gia_cong_hang_hoa_id_seq",
    "gia_cong_phieu_id_seq",
    "hr_employee_code_seq",
];

/// Unique partial indexes that native security or idempotency guarantees depend on.
pub const REQUIRED_INDEXES: &[IndexRequirement] = &[IndexRequirement {
    index: "ux_survey_once",
    table: "survey_responses",
    columns: &["survey_id", "respondent_hash"],
    predicate: "respondent_hash IS NOT NULL",
}];

#[derive(Debug, Error, Eq, PartialEq)]
pub enum SchemaCompatibilityError {
    #[error(
        "cannot inspect PostgreSQL schema while {operation}: {category}; no migration was attempted"
    )]
    Inspection {
        operation: &'static str,
        category: String,
    },

    #[error(
        "PostgreSQL schema is incompatible with KetoanMini Rust: {details}. \
         This check is read-only; run the existing .NET schema migrations before starting Rust"
    )]
    Incompatible { details: String },
}

/// Verify that the existing `public` schema can support routes implemented natively in Rust.
///
/// The function deliberately starts a database-enforced read-only transaction. It never creates,
/// alters or drops an object and it does not write a migration marker.
pub async fn check_compatibility(pool: &PgPool) -> Result<(), SchemaCompatibilityError> {
    let mut transaction = pool
        .begin()
        .await
        .map_err(|error| inspection_error("opening a read-only transaction", error))?;

    sqlx::query("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, READ ONLY")
        .execute(&mut *transaction)
        .await
        .map_err(|error| inspection_error("enforcing read-only catalog access", error))?;

    let mut observed = SchemaSnapshot::new();
    for requirement in REQUIRED_SCHEMA {
        let qualified_name = format!("public.{}", requirement.table);
        let is_table = sqlx::query_scalar::<_, bool>(
            r#"
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_class AS relation
                WHERE relation.oid = pg_catalog.to_regclass($1)
                  AND relation.relkind IN ('r', 'p')
            )
            "#,
        )
        .bind(&qualified_name)
        .fetch_one(&mut *transaction)
        .await
        .map_err(|error| inspection_error("locating a required table", error))?;

        if !is_table {
            continue;
        }

        let columns = sqlx::query_scalar::<_, String>(
            r#"
            SELECT attribute.attname
            FROM pg_catalog.pg_attribute AS attribute
            WHERE attribute.attrelid = pg_catalog.to_regclass($1)
              AND attribute.attnum > 0
              AND NOT attribute.attisdropped
            ORDER BY attribute.attname
            "#,
        )
        .bind(&qualified_name)
        .fetch_all(&mut *transaction)
        .await
        .map_err(|error| inspection_error("reading required table columns", error))?;

        observed.insert(requirement.table, columns.into_iter().collect());
    }

    let mut observed_sequences = BTreeSet::new();
    for &sequence in REQUIRED_SEQUENCES {
        let qualified_name = format!("public.{sequence}");
        let is_sequence = sqlx::query_scalar::<_, bool>(
            r#"
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_class AS relation
                WHERE relation.oid = pg_catalog.to_regclass($1)
                  AND relation.relkind = 'S'
            )
            "#,
        )
        .bind(&qualified_name)
        .fetch_one(&mut *transaction)
        .await
        .map_err(|error| inspection_error("locating a required sequence", error))?;
        if is_sequence {
            observed_sequences.insert(sequence);
        }
    }

    let mut observed_indexes = IndexSnapshot::new();
    for requirement in REQUIRED_INDEXES {
        let qualified_index = format!("public.{}", requirement.index);
        let qualified_table = format!("public.{}", requirement.table);
        let definition = sqlx::query_as::<_, (bool, Vec<String>, Option<String>)>(
            r#"
            SELECT index_info.indisunique,
                   ARRAY(
                       SELECT attribute.attname::text
                       FROM unnest(index_info.indkey) WITH ORDINALITY
                           AS key(attribute_number, position)
                       JOIN pg_catalog.pg_attribute AS attribute
                         ON attribute.attrelid = index_info.indrelid
                        AND attribute.attnum = key.attribute_number
                       WHERE key.attribute_number > 0
                       ORDER BY key.position
                   )::text[] AS columns,
                   pg_catalog.pg_get_expr(index_info.indpred, index_info.indrelid) AS predicate
            FROM pg_catalog.pg_index AS index_info
            WHERE index_info.indexrelid = pg_catalog.to_regclass($1)
              AND index_info.indrelid = pg_catalog.to_regclass($2)
            "#,
        )
        .bind(&qualified_index)
        .bind(&qualified_table)
        .fetch_optional(&mut *transaction)
        .await
        .map_err(|error| inspection_error("reading a required index", error))?;

        if let Some((unique, columns, predicate)) = definition {
            observed_indexes.insert(
                requirement.index,
                ObservedIndex {
                    unique,
                    columns,
                    predicate,
                },
            );
        }
    }

    // A rollback makes the no-write startup contract explicit, even though PostgreSQL already
    // enforces READ ONLY and the transaction contains catalog SELECTs only.
    transaction
        .rollback()
        .await
        .map_err(|error| inspection_error("closing the read-only transaction", error))?;

    let mut issues = validate_snapshot(REQUIRED_SCHEMA, &observed);
    issues.extend(validate_sequences(REQUIRED_SEQUENCES, &observed_sequences));
    issues.extend(validate_indexes(REQUIRED_INDEXES, &observed_indexes));
    if issues.is_empty() {
        Ok(())
    } else {
        Err(SchemaCompatibilityError::Incompatible {
            details: issues
                .iter()
                .map(SchemaIssue::describe)
                .collect::<Vec<_>>()
                .join("; "),
        })
    }
}

fn inspection_error(operation: &'static str, error: sqlx::Error) -> SchemaCompatibilityError {
    // Do not retain or format the original SQLx error: connection/configuration errors can contain
    // credentials. A PostgreSQL SQLSTATE is useful for operations while containing no connection
    // string, username or password.
    let category = error
        .as_database_error()
        .and_then(|database_error| database_error.code())
        .map(|code| format!("catalog query rejected with SQLSTATE {code}"))
        .unwrap_or_else(|| "database connection or pool operation failed".to_owned());

    SchemaCompatibilityError::Inspection {
        operation,
        category,
    }
}

type SchemaSnapshot = BTreeMap<&'static str, BTreeSet<String>>;
type IndexSnapshot = BTreeMap<&'static str, ObservedIndex>;

#[derive(Clone, Debug, Eq, PartialEq)]
struct ObservedIndex {
    unique: bool,
    columns: Vec<String>,
    predicate: Option<String>,
}

#[derive(Clone, Debug, Eq, PartialEq)]
enum SchemaIssue {
    Table(&'static str),
    Sequence(&'static str),
    Index(&'static str),
    IndexDefinition(&'static str),
    Columns {
        table: &'static str,
        columns: Vec<&'static str>,
    },
}

impl SchemaIssue {
    fn describe(&self) -> String {
        match self {
            Self::Table(table) => format!("missing table public.{table}"),
            Self::Sequence(sequence) => format!("missing sequence public.{sequence}"),
            Self::Index(index) => format!("missing index public.{index}"),
            Self::IndexDefinition(index) => {
                format!(
                    "index public.{index} does not match its required unique partial definition"
                )
            }
            Self::Columns { table, columns } => format!(
                "table public.{table} is missing columns [{}]",
                columns.join(", ")
            ),
        }
    }
}

fn validate_indexes(required: &[IndexRequirement], observed: &IndexSnapshot) -> Vec<SchemaIssue> {
    let mut issues = Vec::new();
    for requirement in required {
        let Some(index) = observed.get(requirement.index) else {
            issues.push(SchemaIssue::Index(requirement.index));
            continue;
        };

        let expected_columns = requirement
            .columns
            .iter()
            .map(|column| (*column).to_owned())
            .collect::<Vec<_>>();
        let predicate_matches = index.predicate.as_deref().is_some_and(|predicate| {
            normalize_predicate(predicate) == normalize_predicate(requirement.predicate)
        });
        if !index.unique || index.columns != expected_columns || !predicate_matches {
            issues.push(SchemaIssue::IndexDefinition(requirement.index));
        }
    }
    issues
}

fn normalize_predicate(value: &str) -> String {
    value
        .chars()
        .filter(|character| {
            !character.is_ascii_whitespace() && !matches!(character, '(' | ')' | '"')
        })
        .flat_map(char::to_lowercase)
        .collect()
}

fn validate_sequences(
    required: &[&'static str],
    observed: &BTreeSet<&'static str>,
) -> Vec<SchemaIssue> {
    required
        .iter()
        .copied()
        .filter(|sequence| !observed.contains(sequence))
        .map(SchemaIssue::Sequence)
        .collect()
}

fn validate_snapshot(manifest: &[TableRequirement], snapshot: &SchemaSnapshot) -> Vec<SchemaIssue> {
    let mut issues = Vec::new();
    for requirement in manifest {
        let Some(observed_columns) = snapshot.get(requirement.table) else {
            issues.push(SchemaIssue::Table(requirement.table));
            continue;
        };

        let missing = requirement
            .columns
            .iter()
            .copied()
            .filter(|column| !observed_columns.contains(*column))
            .collect::<Vec<_>>();
        if !missing.is_empty() {
            issues.push(SchemaIssue::Columns {
                table: requirement.table,
                columns: missing,
            });
        }
    }
    issues
}

#[cfg(test)]
mod tests {
    use super::*;

    fn complete_snapshot() -> SchemaSnapshot {
        REQUIRED_SCHEMA
            .iter()
            .map(|requirement| {
                (
                    requirement.table,
                    requirement
                        .columns
                        .iter()
                        .map(|column| (*column).to_owned())
                        .collect(),
                )
            })
            .collect()
    }

    fn complete_index_snapshot() -> IndexSnapshot {
        REQUIRED_INDEXES
            .iter()
            .map(|requirement| {
                (
                    requirement.index,
                    ObservedIndex {
                        unique: true,
                        columns: requirement
                            .columns
                            .iter()
                            .map(|column| (*column).to_owned())
                            .collect(),
                        predicate: Some(requirement.predicate.to_owned()),
                    },
                )
            })
            .collect()
    }

    #[test]
    fn manifest_has_unique_sorted_tables_and_columns() {
        let tables = REQUIRED_SCHEMA
            .iter()
            .map(|requirement| requirement.table)
            .collect::<Vec<_>>();
        let mut expected_tables = tables.clone();
        expected_tables.sort_unstable();
        expected_tables.dedup();
        assert_eq!(tables, expected_tables);

        for requirement in REQUIRED_SCHEMA {
            assert!(!requirement.columns.is_empty());
            let mut expected_columns = requirement.columns.to_vec();
            expected_columns.sort_unstable();
            expected_columns.dedup();
            assert_eq!(requirement.columns, expected_columns);
        }

        let mut expected_sequences = REQUIRED_SEQUENCES.to_vec();
        expected_sequences.sort_unstable();
        expected_sequences.dedup();
        assert_eq!(REQUIRED_SEQUENCES, expected_sequences);

        let indexes = REQUIRED_INDEXES
            .iter()
            .map(|requirement| requirement.index)
            .collect::<Vec<_>>();
        let mut expected_indexes = indexes.clone();
        expected_indexes.sort_unstable();
        expected_indexes.dedup();
        assert_eq!(indexes, expected_indexes);
        for requirement in REQUIRED_INDEXES {
            assert!(!requirement.columns.is_empty());
            assert!(!requirement.predicate.trim().is_empty());
        }
    }

    #[test]
    fn validation_accepts_required_and_additive_columns() {
        let mut snapshot = complete_snapshot();
        snapshot
            .get_mut("app_users")
            .unwrap()
            .insert("future_additive_column".to_owned());

        assert_eq!(validate_snapshot(REQUIRED_SCHEMA, &snapshot), Vec::new());
    }

    #[test]
    fn validation_reports_missing_tables_and_columns_deterministically() {
        let mut snapshot = complete_snapshot();
        snapshot.remove("hr_employees");
        snapshot
            .get_mut("app_users")
            .unwrap()
            .remove("authorization_version");
        snapshot.get_mut("user_sessions").unwrap().remove("revoked");

        assert_eq!(
            validate_snapshot(REQUIRED_SCHEMA, &snapshot),
            vec![
                SchemaIssue::Columns {
                    table: "app_users",
                    columns: vec!["authorization_version"],
                },
                SchemaIssue::Table("hr_employees"),
                SchemaIssue::Columns {
                    table: "user_sessions",
                    columns: vec!["revoked"],
                },
            ]
        );
    }

    #[test]
    fn validation_reports_a_missing_sequence_without_attempting_to_create_it() {
        assert_eq!(
            validate_sequences(
                REQUIRED_SEQUENCES,
                &BTreeSet::from(["gia_cong_hang_hoa_id_seq", "gia_cong_phieu_id_seq"])
            ),
            vec![SchemaIssue::Sequence("hr_employee_code_seq")]
        );
        assert!(
            validate_sequences(
                REQUIRED_SEQUENCES,
                &BTreeSet::from([
                    "gia_cong_hang_hoa_id_seq",
                    "gia_cong_phieu_id_seq",
                    "hr_employee_code_seq"
                ])
            )
            .is_empty()
        );
    }

    #[test]
    fn validation_requires_the_atomic_survey_dedup_index() {
        assert_eq!(
            validate_indexes(REQUIRED_INDEXES, &IndexSnapshot::new()),
            vec![SchemaIssue::Index("ux_survey_once")]
        );

        let mut indexes = complete_index_snapshot();
        indexes.get_mut("ux_survey_once").unwrap().unique = false;
        assert_eq!(
            validate_indexes(REQUIRED_INDEXES, &indexes),
            vec![SchemaIssue::IndexDefinition("ux_survey_once")]
        );

        let mut indexes = complete_index_snapshot();
        indexes.get_mut("ux_survey_once").unwrap().predicate =
            Some("((respondent_hash IS NOT NULL))".to_owned());
        assert!(validate_indexes(REQUIRED_INDEXES, &indexes).is_empty());
    }

    #[test]
    fn incompatibility_message_is_actionable_and_contains_no_connection_data() {
        let error = SchemaCompatibilityError::Incompatible {
            details: SchemaIssue::Table("app_users").describe(),
        };
        let message = error.to_string();

        assert!(message.contains("missing table public.app_users"));
        assert!(message.contains("read-only"));
        assert!(!message.contains("postgres://"));
        assert!(!message.contains("Password="));
    }
}
