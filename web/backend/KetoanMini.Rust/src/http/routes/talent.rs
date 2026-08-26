//! Native employee Talent endpoints, ported from `TalentEndpoints.cs`.
//!
//! Mount this router behind `auth::require_auth`. The C# route group requires
//! authentication but no named permission; every query and mutation is scoped
//! to the employee profile associated with the authenticated username.

use crate::{auth::AuthContext, state::AppState};
use axum::{
    Extension, Json, Router,
    extract::{DefaultBodyLimit, Path, State, rejection::JsonRejection},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::{get, post, put},
};
use chrono::{DateTime, NaiveDate, SecondsFormat, Utc};
use serde::{Deserialize, Serialize, Serializer};
use serde_json::{Value, json};
use sqlx::{FromRow, PgConnection, PgPool, Postgres, pool::PoolConnection};
use std::sync::Arc;
use uuid::Uuid;

const MAX_JSON_BODY_BYTES: usize = 16 * 1024 * 1024;
const PAYLOAD_TOO_LARGE_MESSAGE: &str = "Payload vượt giới hạn 16777216 byte.";
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";
const REVIEW_CLOSED_MESSAGE: &str = "Kỳ đánh giá đã đóng.";
const EMPLOYEE_TIME_ZONE: &str = "Asia/Ho_Chi_Minh";

const ONBOARDING_PATH: &str = "/api/talent/onboarding";
const ONBOARDING_COMPLETE_PATH: &str = "/api/talent/onboarding/{id}/complete";
const PERFORMANCE_PATH: &str = "/api/talent/performance";
const PERFORMANCE_GOAL_PATH: &str = "/api/talent/performance/goals/{id}";
const PERFORMANCE_REVIEW_SELF_PATH: &str = "/api/talent/performance/reviews/{id}/self";
const TRAINING_PATH: &str = "/api/talent/training";
const TRAINING_PROGRESS_PATH: &str = "/api/talent/training/{id}/progress";
const TRAINING_QUIZ_PATH: &str = "/api/talent/training/{id}/quiz";
const BENEFITS_PATH: &str = "/api/talent/benefits";

const ONBOARDING_ITEMS_SQL: &str = r#"
    SELECT id, title, action_key, due_at, policy_text, completed_at, acknowledged_at
    FROM hr_onboarding_tasks
    WHERE employee_id = $1
    ORDER BY completed_at NULLS FIRST, due_at NULLS LAST
"#;

const COMPLETE_ONBOARDING_SQL: &str = r#"
    UPDATE hr_onboarding_tasks
    SET completed_at = CURRENT_TIMESTAMP,
        acknowledged_at = CASE
            WHEN policy_text <> '' THEN CURRENT_TIMESTAMP
            ELSE acknowledged_at
        END
    WHERE id = $1 AND employee_id = $2
"#;

const PERFORMANCE_GOALS_SQL: &str = r#"
    SELECT id, title, description,
           target::double precision AS target,
           progress::double precision AS progress,
           unit, due_at
    FROM hr_performance_goals
    WHERE employee_id = $1
    ORDER BY due_at NULLS LAST
"#;

const PERFORMANCE_REVIEWS_SQL: &str = r#"
    SELECT id, period, closes_at, self_assessment, manager_comment,
           score::double precision AS score, status
    FROM hr_performance_reviews
    WHERE employee_id = $1
    ORDER BY period DESC
"#;

const UPDATE_GOAL_SQL: &str = r#"
    UPDATE hr_performance_goals
    SET progress = LEAST(target, GREATEST(0, $1::text::numeric)),
        updated_at = CURRENT_TIMESTAMP
    WHERE id = $2 AND employee_id = $3
"#;

const UPDATE_SELF_REVIEW_SQL: &str = r#"
    UPDATE hr_performance_reviews
    SET self_assessment = $1, updated_at = CURRENT_TIMESTAMP
    WHERE id = $2 AND employee_id = $3 AND status = 'open'
"#;

const TRAINING_SQL: &str = r#"
    SELECT c.id, c.title, c.description, c.material_url, c.video_url, c.quiz::text AS quiz,
           COALESCE(e.progress, 0) AS progress,
           COALESCE(e.resume_seconds, 0) AS resume_seconds,
           e.score::double precision AS score,
           e.completed_at, e.certificate_expires_at
    FROM hr_training_courses c
    LEFT JOIN hr_training_enrollments e
      ON e.course_id = c.id AND e.employee_id = $1
    WHERE c.active = TRUE
    ORDER BY c.created_at DESC
"#;

const UPSERT_TRAINING_PROGRESS_SQL: &str = r#"
    INSERT INTO hr_training_enrollments(course_id, employee_id, progress, resume_seconds)
    VALUES ($1, $2, $3, $4)
    ON CONFLICT(course_id, employee_id) DO UPDATE SET
        progress = GREATEST(hr_training_enrollments.progress, $3),
        resume_seconds = $4,
        updated_at = CURRENT_TIMESTAMP
"#;

const UPSERT_QUIZ_RESULT_SQL: &str = r#"
    INSERT INTO hr_training_enrollments(course_id, employee_id, progress, score, completed_at)
    VALUES (
        $1, $2, $3, $4::text::numeric,
        CASE WHEN $5 THEN CURRENT_TIMESTAMP ELSE NULL END
    )
    ON CONFLICT(course_id, employee_id) DO UPDATE SET
        progress = GREATEST(hr_training_enrollments.progress, $3),
        score = $4::text::numeric,
        completed_at = CASE
            WHEN $5 THEN COALESCE(hr_training_enrollments.completed_at, CURRENT_TIMESTAMP)
            ELSE hr_training_enrollments.completed_at
        END,
        updated_at = CURRENT_TIMESTAMP
"#;

const LEAVE_BALANCE_SQL: &str = r#"
    SELECT COALESCE(SUM(total_days), 0)::double precision AS total,
           COALESCE(SUM(used_days), 0)::double precision AS used
    FROM hr_leave_balances
    WHERE employee_id = $1 AND year = EXTRACT(YEAR FROM CURRENT_DATE)
"#;

const LEAVE_HISTORY_SQL: &str = r#"
    SELECT request_no, payload::text AS payload, status, created_at
    FROM hr_requests
    WHERE employee_id = $1 AND req_type IN ('leave', 'sick')
    ORDER BY created_at DESC
    LIMIT 30
"#;

const BENEFITS_SQL: &str = r#"
    SELECT id, benefit_type, title, value_text, valid_from, valid_to
    FROM hr_employee_benefits
    WHERE employee_id = $1
    ORDER BY valid_to NULLS LAST
"#;

const REWARDS_SQL: &str = r#"
    SELECT id, title, points, awarded_at, note
    FROM hr_employee_rewards
    WHERE employee_id = $1
    ORDER BY awarded_at DESC
"#;

#[cfg(test)]
const ROUTE_CONTRACTS: &[(&str, &str)] = &[
    ("GET", ONBOARDING_PATH),
    ("POST", ONBOARDING_COMPLETE_PATH),
    ("GET", PERFORMANCE_PATH),
    ("PUT", PERFORMANCE_GOAL_PATH),
    ("PUT", PERFORMANCE_REVIEW_SELF_PATH),
    ("GET", TRAINING_PATH),
    ("PUT", TRAINING_PROGRESS_PATH),
    ("POST", TRAINING_QUIZ_PATH),
    ("GET", BENEFITS_PATH),
];

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(ONBOARDING_PATH, get(get_onboarding))
        .route(ONBOARDING_COMPLETE_PATH, post(complete_onboarding))
        .route(PERFORMANCE_PATH, get(get_performance))
        .route(PERFORMANCE_GOAL_PATH, put(update_goal))
        .route(PERFORMANCE_REVIEW_SELF_PATH, put(update_self_assessment))
        .route(TRAINING_PATH, get(get_training))
        .route(TRAINING_PROGRESS_PATH, put(update_training_progress))
        .route(TRAINING_QUIZ_PATH, post(submit_quiz))
        .route(BENEFITS_PATH, get(get_benefits))
        .layer(DefaultBodyLimit::max(MAX_JSON_BODY_BYTES))
}

#[derive(Debug, FromRow)]
struct OnboardingItemRow {
    id: Uuid,
    title: String,
    action_key: String,
    due_at: Option<DateTime<Utc>>,
    policy_text: String,
    completed_at: Option<DateTime<Utc>>,
    acknowledged_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct OnboardingItemDto {
    id: Uuid,
    title: String,
    action_key: String,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    due_at: Option<DateTime<Utc>>,
    policy_text: String,
    completed: bool,
    acknowledged: bool,
}

impl From<OnboardingItemRow> for OnboardingItemDto {
    fn from(row: OnboardingItemRow) -> Self {
        Self {
            id: row.id,
            title: row.title,
            action_key: row.action_key,
            due_at: row.due_at,
            policy_text: row.policy_text,
            completed: row.completed_at.is_some(),
            acknowledged: row.acknowledged_at.is_some(),
        }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct OnboardingDto {
    mentor_name: String,
    items: Vec<OnboardingItemDto>,
}

#[derive(Debug, FromRow)]
struct PerformanceGoalRow {
    id: Uuid,
    title: String,
    description: String,
    target: f64,
    progress: f64,
    unit: String,
    due_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct PerformanceGoalDto {
    id: Uuid,
    title: String,
    description: String,
    target: f64,
    progress: f64,
    unit: String,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    due_at: Option<DateTime<Utc>>,
}

impl From<PerformanceGoalRow> for PerformanceGoalDto {
    fn from(row: PerformanceGoalRow) -> Self {
        Self {
            id: row.id,
            title: row.title,
            description: row.description,
            target: row.target,
            progress: row.progress,
            unit: row.unit,
            due_at: row.due_at,
        }
    }
}

#[derive(Debug, FromRow)]
struct PerformanceReviewRow {
    id: Uuid,
    period: String,
    closes_at: Option<DateTime<Utc>>,
    self_assessment: String,
    manager_comment: String,
    score: Option<f64>,
    status: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct PerformanceReviewDto {
    id: Uuid,
    period: String,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    closes_at: Option<DateTime<Utc>>,
    self_assessment: String,
    manager_comment: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    score: Option<f64>,
    status: String,
}

impl From<PerformanceReviewRow> for PerformanceReviewDto {
    fn from(row: PerformanceReviewRow) -> Self {
        Self {
            id: row.id,
            period: row.period,
            closes_at: row.closes_at,
            self_assessment: row.self_assessment,
            manager_comment: row.manager_comment,
            score: row.score,
            status: row.status,
        }
    }
}

#[derive(Debug, Serialize)]
struct PerformanceDto {
    goals: Vec<PerformanceGoalDto>,
    reviews: Vec<PerformanceReviewDto>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ProgressRequest {
    #[serde(default = "zero_json_number", alias = "Progress")]
    progress: serde_json::Number,
}

fn zero_json_number() -> serde_json::Number {
    0.into()
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct SelfReviewRequest {
    #[serde(default, alias = "Text")]
    text: Option<String>,
}

#[derive(Debug, FromRow)]
struct TrainingRow {
    id: Uuid,
    title: String,
    description: String,
    material_url: String,
    video_url: String,
    quiz: String,
    progress: i32,
    resume_seconds: i32,
    score: Option<f64>,
    completed_at: Option<DateTime<Utc>>,
    certificate_expires_at: Option<NaiveDate>,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
struct PublicQuizQuestion {
    text: String,
    options: Vec<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct TrainingDto {
    id: Uuid,
    title: String,
    description: String,
    material_url: String,
    video_url: String,
    quiz: Vec<PublicQuizQuestion>,
    progress: i32,
    resume_seconds: i32,
    #[serde(skip_serializing_if = "Option::is_none")]
    score: Option<f64>,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    completed_at: Option<DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    certificate_expires_at: Option<NaiveDate>,
}

impl From<TrainingRow> for TrainingDto {
    fn from(row: TrainingRow) -> Self {
        Self {
            id: row.id,
            title: row.title,
            description: row.description,
            material_url: row.material_url,
            video_url: row.video_url,
            quiz: public_quiz(&row.quiz),
            progress: row.progress,
            resume_seconds: row.resume_seconds,
            score: row.score,
            completed_at: row.completed_at,
            certificate_expires_at: row.certificate_expires_at,
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct TrainingProgressRequest {
    #[serde(default, alias = "Progress")]
    progress: i32,
    #[serde(default, alias = "ResumeSeconds")]
    resume_seconds: i32,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct QuizRequest {
    #[serde(alias = "Answers")]
    answers: Vec<String>,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
struct QuizScore {
    cents: u32,
}

impl QuizScore {
    const PERFECT: Self = Self { cents: 10_000 };

    const fn passed(self) -> bool {
        self.cents >= 7_000
    }

    fn sql_decimal(self) -> String {
        format!("{}.{:02}", self.cents / 100, self.cents % 100)
    }
}

impl Serialize for QuizScore {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        if self.cents.is_multiple_of(100) {
            serializer.serialize_u32(self.cents / 100)
        } else {
            serializer.serialize_f64(f64::from(self.cents) / 100.0)
        }
    }
}

#[derive(Debug, Serialize)]
struct QuizResultDto {
    score: QuizScore,
    passed: bool,
}

#[derive(Debug, FromRow)]
struct LeaveBalanceRow {
    total: f64,
    used: f64,
}

#[derive(Clone, Copy, Debug)]
struct DotNetDecimal(f64);

impl Serialize for DotNetDecimal {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        // System.Text.Json writes an integral decimal as `0`, while serializing an f64 directly
        // would emit `0.0`. Leave balances are bounded business values, so this conversion is
        // exact for every value accepted by the existing schema.
        if self.0.fract() == 0.0 {
            serializer.serialize_i64(self.0 as i64)
        } else {
            serializer.serialize_f64(self.0)
        }
    }
}

#[derive(Debug, FromRow)]
struct LeaveHistoryRow {
    request_no: String,
    payload: String,
    status: String,
    created_at: DateTime<Utc>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct LeaveHistoryDto {
    request_no: String,
    payload: Value,
    status: String,
    #[serde(serialize_with = "serialize_dotnet_utc")]
    created_at: DateTime<Utc>,
}

impl From<LeaveHistoryRow> for LeaveHistoryDto {
    fn from(row: LeaveHistoryRow) -> Self {
        Self {
            request_no: row.request_no,
            payload: parse_json_or_empty_array(&row.payload),
            status: row.status,
            created_at: row.created_at,
        }
    }
}

#[derive(Debug, FromRow)]
struct BenefitRow {
    id: Uuid,
    benefit_type: String,
    title: String,
    value_text: String,
    valid_from: Option<NaiveDate>,
    valid_to: Option<NaiveDate>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BenefitDto {
    id: Uuid,
    #[serde(rename = "type")]
    benefit_type: String,
    title: String,
    #[serde(rename = "value")]
    value_text: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    valid_from: Option<NaiveDate>,
    #[serde(skip_serializing_if = "Option::is_none")]
    valid_to: Option<NaiveDate>,
}

impl From<BenefitRow> for BenefitDto {
    fn from(row: BenefitRow) -> Self {
        Self {
            id: row.id,
            benefit_type: row.benefit_type,
            title: row.title,
            value_text: row.value_text,
            valid_from: row.valid_from,
            valid_to: row.valid_to,
        }
    }
}

#[derive(Debug, FromRow, Serialize)]
#[serde(rename_all = "camelCase")]
struct RewardDto {
    id: Uuid,
    title: String,
    points: i32,
    awarded_at: NaiveDate,
    note: String,
}

#[derive(Debug, FromRow, Default)]
struct EmployeeDatesRow {
    dob: Option<NaiveDate>,
    hire_date: Option<NaiveDate>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BenefitsDto {
    leave_total: DotNetDecimal,
    leave_used: DotNetDecimal,
    leave_remaining: DotNetDecimal,
    leave_history: Vec<LeaveHistoryDto>,
    benefits: Vec<BenefitDto>,
    rewards: Vec<RewardDto>,
    #[serde(skip_serializing_if = "Option::is_none")]
    birthday: Option<NaiveDate>,
    #[serde(skip_serializing_if = "Option::is_none")]
    hire_date: Option<NaiveDate>,
}

async fn get_onboarding(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for onboarding", error),
    };

    let mentor_name = match sqlx::query_scalar::<_, String>(
        r#"
        SELECT COALESCE(m.full_name, '')
        FROM hr_employees e
        LEFT JOIN hr_employees m ON m.id = e.manager_id
        WHERE e.id = $1
        "#,
    )
    .bind(employee_id)
    .fetch_optional(&mut *connection)
    .await
    {
        Ok(value) => value.unwrap_or_default(),
        Err(error) => return database_failure("read onboarding mentor", error),
    };

    let rows = match sqlx::query_as::<_, OnboardingItemRow>(ONBOARDING_ITEMS_SQL)
        .bind(employee_id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read onboarding tasks", error),
    };

    Json(OnboardingDto {
        mentor_name,
        items: rows.into_iter().map(OnboardingItemDto::from).collect(),
    })
    .into_response()
}

async fn complete_onboarding(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
) -> Response {
    let Some(id) = parse_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for onboarding completion", error),
    };
    let result = match sqlx::query(COMPLETE_ONBOARDING_SQL)
        .bind(id)
        .bind(employee_id)
        .execute(&mut *connection)
        .await
    {
        Ok(result) => result,
        Err(error) => return database_failure("complete onboarding task", error),
    };
    empty_mutation_response(result.rows_affected(), StatusCode::NOT_FOUND)
}

async fn get_performance(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for performance", error),
    };
    let goals = match sqlx::query_as::<_, PerformanceGoalRow>(PERFORMANCE_GOALS_SQL)
        .bind(employee_id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows.into_iter().map(PerformanceGoalDto::from).collect(),
        Err(error) => return database_failure("read performance goals", error),
    };
    let reviews = match sqlx::query_as::<_, PerformanceReviewRow>(PERFORMANCE_REVIEWS_SQL)
        .bind(employee_id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows.into_iter().map(PerformanceReviewDto::from).collect(),
        Err(error) => return database_failure("read performance reviews", error),
    };
    Json(PerformanceDto { goals, reviews }).into_response()
}

async fn update_goal(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
    payload: Result<Json<ProgressRequest>, JsonRejection>,
) -> Response {
    let Some(id) = parse_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for goal update", error),
    };
    let result = match sqlx::query(UPDATE_GOAL_SQL)
        .bind(request.progress.to_string())
        .bind(id)
        .bind(employee_id)
        .execute(&mut *connection)
        .await
    {
        Ok(result) => result,
        Err(error) => return database_failure("update performance goal", error),
    };
    empty_mutation_response(result.rows_affected(), StatusCode::NOT_FOUND)
}

async fn update_self_assessment(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
    payload: Result<Json<SelfReviewRequest>, JsonRejection>,
) -> Response {
    let Some(id) = parse_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for self review", error),
    };
    let result = match sqlx::query(UPDATE_SELF_REVIEW_SQL)
        .bind(request.text.unwrap_or_default())
        .bind(id)
        .bind(employee_id)
        .execute(&mut *connection)
        .await
    {
        Ok(result) => result,
        Err(error) => return database_failure("update performance self review", error),
    };
    if result.rows_affected() == 0 {
        return (
            StatusCode::BAD_REQUEST,
            Json(json!({ "message": REVIEW_CLOSED_MESSAGE })),
        )
            .into_response();
    }
    StatusCode::NO_CONTENT.into_response()
}

async fn get_training(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for training", error),
    };
    let rows = match sqlx::query_as::<_, TrainingRow>(TRAINING_SQL)
        .bind(employee_id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read training courses", error),
    };
    Json(rows.into_iter().map(TrainingDto::from).collect::<Vec<_>>()).into_response()
}

async fn update_training_progress(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
    payload: Result<Json<TrainingProgressRequest>, JsonRejection>,
) -> Response {
    let Some(id) = parse_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let (progress, resume_seconds) = normalize_training_progress(request);
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for training progress", error),
    };
    if let Err(error) = sqlx::query(UPSERT_TRAINING_PROGRESS_SQL)
        .bind(id)
        .bind(employee_id)
        .bind(progress)
        .bind(resume_seconds)
        .execute(&mut *connection)
        .await
    {
        return database_failure("update training progress", error);
    }
    StatusCode::NO_CONTENT.into_response()
}

async fn submit_quiz(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
    payload: Result<Json<QuizRequest>, JsonRejection>,
) -> Response {
    let Some(id) = parse_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for training quiz", error),
    };
    let raw = match sqlx::query_scalar::<_, String>(
        "SELECT quiz::text FROM hr_training_courses WHERE id = $1 AND active = TRUE",
    )
    .bind(id)
    .fetch_optional(&mut *connection)
    .await
    {
        Ok(Some(raw)) => raw,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(error) => return database_failure("read training quiz", error),
    };

    let expected = match expected_quiz_answers(&raw) {
        Ok(expected) => expected,
        Err(error) => return stored_data_failure("parse training quiz", error),
    };
    let score = grade_quiz(&expected, &request.answers);
    let passed = score.passed();
    let progress = if passed { 100 } else { 0 };
    if let Err(error) = sqlx::query(UPSERT_QUIZ_RESULT_SQL)
        .bind(id)
        .bind(employee_id)
        .bind(progress)
        .bind(score.sql_decimal())
        .bind(passed)
        .execute(&mut *connection)
        .await
    {
        return database_failure("store training quiz result", error);
    }
    Json(QuizResultDto { score, passed }).into_response()
}

async fn get_benefits(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    let (mut connection, employee_id) = match employee_connection(&state.pool, &auth).await {
        Ok(value) => value,
        Err(error) => return database_failure("resolve employee for benefits", error),
    };
    let balance = match sqlx::query_as::<_, LeaveBalanceRow>(LEAVE_BALANCE_SQL)
        .bind(employee_id)
        .fetch_one(&mut *connection)
        .await
    {
        Ok(balance) => balance,
        Err(error) => return database_failure("read leave balance", error),
    };
    let leave_history = match sqlx::query_as::<_, LeaveHistoryRow>(LEAVE_HISTORY_SQL)
        .bind(employee_id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows.into_iter().map(LeaveHistoryDto::from).collect(),
        Err(error) => return database_failure("read leave history", error),
    };
    let benefits = match sqlx::query_as::<_, BenefitRow>(BENEFITS_SQL)
        .bind(employee_id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows.into_iter().map(BenefitDto::from).collect(),
        Err(error) => return database_failure("read employee benefits", error),
    };
    let rewards = match sqlx::query_as::<_, RewardDto>(REWARDS_SQL)
        .bind(employee_id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read employee rewards", error),
    };
    let dates = match sqlx::query_as::<_, EmployeeDatesRow>(
        "SELECT dob, hire_date FROM hr_employees WHERE id = $1",
    )
    .bind(employee_id)
    .fetch_optional(&mut *connection)
    .await
    {
        Ok(dates) => dates.unwrap_or_default(),
        Err(error) => return database_failure("read employee benefit dates", error),
    };

    Json(BenefitsDto {
        leave_total: DotNetDecimal(balance.total),
        leave_used: DotNetDecimal(balance.used),
        leave_remaining: DotNetDecimal(balance.total - balance.used),
        leave_history,
        benefits,
        rewards,
        birthday: dates.dob,
        hire_date: dates.hire_date,
    })
    .into_response()
}

fn normalize_training_progress(request: TrainingProgressRequest) -> (i32, i32) {
    (
        request.progress.clamp(0, 100),
        request.resume_seconds.max(0),
    )
}

fn public_quiz(raw: &str) -> Vec<PublicQuizQuestion> {
    fn convert(value: &Value) -> Option<Vec<PublicQuizQuestion>> {
        value
            .as_array()?
            .iter()
            .map(|question| {
                let question = question.as_object()?;
                let text = match question.get("text") {
                    None | Some(Value::Null) => String::new(),
                    Some(Value::String(value)) => value.clone(),
                    Some(_) => return None,
                };
                let options = match question.get("options") {
                    Some(Value::Array(values)) => values
                        .iter()
                        .map(|value| match value {
                            Value::Null => Some(String::new()),
                            Value::String(value) => Some(value.clone()),
                            _ => None,
                        })
                        .collect::<Option<Vec<_>>>()?,
                    _ => Vec::new(),
                };
                Some(PublicQuizQuestion { text, options })
            })
            .collect()
    }

    serde_json::from_str::<Value>(raw)
        .ok()
        .and_then(|value| convert(&value))
        .unwrap_or_default()
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum StoredQuizError {
    InvalidJson,
    ExpectedArray,
    ExpectedQuestionObject,
    ExpectedCorrectString,
}

fn expected_quiz_answers(raw: &str) -> Result<Vec<String>, StoredQuizError> {
    let value: Value = serde_json::from_str(raw).map_err(|_| StoredQuizError::InvalidJson)?;
    let questions = value.as_array().ok_or(StoredQuizError::ExpectedArray)?;
    questions
        .iter()
        .map(|question| {
            let question = question
                .as_object()
                .ok_or(StoredQuizError::ExpectedQuestionObject)?;
            match question.get("correct") {
                None | Some(Value::Null) => Ok(String::new()),
                Some(Value::String(value)) => Ok(value.clone()),
                Some(_) => Err(StoredQuizError::ExpectedCorrectString),
            }
        })
        .collect()
}

/// Decimal `Math.Round(value, 2)` uses midpoint-to-even by default. Work in
/// hundredths of a point so the result is exact and independent of floats.
fn grade_quiz(expected: &[String], answers: &[String]) -> QuizScore {
    if expected.is_empty() {
        return QuizScore::PERFECT;
    }
    let correct = expected
        .iter()
        .zip(answers)
        .filter(|(expected, answer)| expected == answer)
        .count() as u64;
    let count = expected.len() as u64;
    let numerator = correct * 10_000;
    let mut cents = numerator / count;
    let remainder = numerator % count;
    let twice_remainder = remainder * 2;
    if twice_remainder > count || (twice_remainder == count && cents % 2 == 1) {
        cents += 1;
    }
    QuizScore {
        cents: cents as u32,
    }
}

fn parse_json_or_empty_array(raw: &str) -> Value {
    serde_json::from_str(raw).unwrap_or_else(|_| json!([]))
}

fn parse_id(raw: &str) -> Option<Uuid> {
    Uuid::parse_str(raw).ok()
}

fn empty_mutation_response(rows_affected: u64, when_empty: StatusCode) -> Response {
    if rows_affected == 0 {
        when_empty.into_response()
    } else {
        StatusCode::NO_CONTENT.into_response()
    }
}

#[derive(Debug, FromRow)]
struct EmployeeSeedRow {
    id: Uuid,
    full_name: Option<String>,
    email: Option<String>,
    account_date: Option<NaiveDate>,
}

async fn employee_connection(
    pool: &PgPool,
    auth: &AuthContext,
) -> Result<(PoolConnection<Postgres>, Uuid), sqlx::Error> {
    let mut connection = pool.acquire().await?;
    let employee_id = ensure_employee_for_user(&mut connection, &auth.username).await?;
    Ok((connection, employee_id))
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

fn serialize_dotnet_utc<S>(value: &DateTime<Utc>, serializer: S) -> Result<S::Ok, S::Error>
where
    S: Serializer,
{
    serializer.serialize_str(&value.to_rfc3339_opts(SecondsFormat::Millis, true))
}

fn serialize_optional_dotnet_utc<S>(
    value: &Option<DateTime<Utc>>,
    serializer: S,
) -> Result<S::Ok, S::Error>
where
    S: Serializer,
{
    match value {
        Some(value) => serialize_dotnet_utc(value, serializer),
        None => serializer.serialize_none(),
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum TalentJsonError {
    PayloadTooLarge,
    Status(StatusCode),
}

impl IntoResponse for TalentJsonError {
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

fn json_payload<T>(payload: Result<Json<T>, JsonRejection>) -> Result<T, TalentJsonError> {
    match payload {
        Ok(Json(request)) => Ok(request),
        Err(rejection) => {
            if rejection.status() == StatusCode::PAYLOAD_TOO_LARGE {
                return Err(TalentJsonError::PayloadTooLarge);
            }
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(TalentJsonError::Status(status))
        }
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::error!(%error, operation, "talent database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

fn stored_data_failure(operation: &'static str, error: StoredQuizError) -> Response {
    tracing::error!(?error, operation, "invalid stored talent data");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    #[test]
    fn route_contract_has_all_nine_authenticated_routes() {
        assert_eq!(ROUTE_CONTRACTS.len(), 9);
        assert_eq!(
            ROUTE_CONTRACTS,
            &[
                ("GET", "/api/talent/onboarding"),
                ("POST", "/api/talent/onboarding/{id}/complete"),
                ("GET", "/api/talent/performance"),
                ("PUT", "/api/talent/performance/goals/{id}"),
                ("PUT", "/api/talent/performance/reviews/{id}/self"),
                ("GET", "/api/talent/training"),
                ("PUT", "/api/talent/training/{id}/progress"),
                ("POST", "/api/talent/training/{id}/quiz"),
                ("GET", "/api/talent/benefits"),
            ]
        );
    }

    #[test]
    fn every_employee_record_query_is_ownership_scoped() {
        for sql in [
            ONBOARDING_ITEMS_SQL,
            COMPLETE_ONBOARDING_SQL,
            PERFORMANCE_GOALS_SQL,
            PERFORMANCE_REVIEWS_SQL,
            UPDATE_GOAL_SQL,
            UPDATE_SELF_REVIEW_SQL,
            TRAINING_SQL,
            UPSERT_TRAINING_PROGRESS_SQL,
            UPSERT_QUIZ_RESULT_SQL,
            LEAVE_BALANCE_SQL,
            LEAVE_HISTORY_SQL,
            BENEFITS_SQL,
            REWARDS_SQL,
        ] {
            assert!(sql.contains("employee_id"));
        }
    }

    #[test]
    fn training_progress_matches_dotnet_clamps() {
        assert_eq!(
            normalize_training_progress(TrainingProgressRequest {
                progress: -4,
                resume_seconds: -9,
            }),
            (0, 0)
        );
        assert_eq!(
            normalize_training_progress(TrainingProgressRequest {
                progress: 140,
                resume_seconds: 37,
            }),
            (100, 37)
        );
    }

    #[test]
    fn public_quiz_removes_answers_and_preserves_null_string_behavior() {
        let questions = public_quiz(
            r#"[{"text":"Q1","options":["A",null,"C"],"correct":"A"},{"correct":"B"}]"#,
        );
        assert_eq!(
            questions,
            vec![
                PublicQuizQuestion {
                    text: "Q1".to_owned(),
                    options: vec!["A".to_owned(), String::new(), "C".to_owned()],
                },
                PublicQuizQuestion {
                    text: String::new(),
                    options: Vec::new(),
                },
            ]
        );
        let value = serde_json::to_value(questions).unwrap();
        assert!(value[0].get("correct").is_none());
    }

    #[test]
    fn public_quiz_fails_closed_to_empty_list_for_bad_stored_shapes() {
        assert!(public_quiz("not-json").is_empty());
        assert!(public_quiz(r#"{"text":"not an array"}"#).is_empty());
        assert!(public_quiz(r#"[{"text":42}]"#).is_empty());
        assert!(public_quiz(r#"[{"options":[true]}]"#).is_empty());
    }

    #[test]
    fn quiz_grading_is_ordinal_and_uses_bankers_rounding() {
        let thirty_two = vec!["yes".to_owned(); 32];
        let one_correct = ["yes".to_owned()];
        assert_eq!(grade_quiz(&thirty_two, &one_correct).cents, 312);

        let mut three_correct = vec!["no".to_owned(); 32];
        three_correct[..3].fill("yes".to_owned());
        assert_eq!(grade_quiz(&thirty_two, &three_correct).cents, 938);
        assert_eq!(grade_quiz(&["A".to_owned()], &["a".to_owned()]).cents, 0);
        assert_eq!(grade_quiz(&[], &[]), QuizScore::PERFECT);
    }

    #[test]
    fn quiz_score_json_keeps_number_contract_and_pass_boundary() {
        assert_eq!(
            serde_json::to_value(QuizScore::PERFECT).unwrap(),
            json!(100)
        );
        assert_eq!(
            serde_json::to_value(QuizScore { cents: 1_250 }).unwrap(),
            json!(12.5)
        );
        assert!(QuizScore { cents: 7_000 }.passed());
        assert!(!QuizScore { cents: 6_999 }.passed());
        assert_eq!(QuizScore { cents: 6_667 }.sql_decimal(), "66.67");
    }

    #[test]
    fn leave_decimal_json_matches_system_text_json_number_shape() {
        assert_eq!(serde_json::to_string(&DotNetDecimal(0.0)).unwrap(), "0");
        assert_eq!(serde_json::to_string(&DotNetDecimal(12.5)).unwrap(), "12.5");
    }

    #[test]
    fn quiz_grading_rejects_corrupt_private_answer_shapes() {
        assert_eq!(
            expected_quiz_answers(r#"[{"correct":1}]"#),
            Err(StoredQuizError::ExpectedCorrectString)
        );
        assert_eq!(
            expected_quiz_answers(r#"{"correct":"A"}"#),
            Err(StoredQuizError::ExpectedArray)
        );
    }

    #[test]
    fn nullable_fields_are_omitted_and_dates_match_global_json_contract() {
        let dto = OnboardingItemDto {
            id: Uuid::nil(),
            title: "Task".to_owned(),
            action_key: String::new(),
            due_at: None,
            policy_text: String::new(),
            completed: false,
            acknowledged: false,
        };
        let value = serde_json::to_value(dto).unwrap();
        assert!(value.get("dueAt").is_none());

        let history = LeaveHistoryDto {
            request_no: "R1".to_owned(),
            payload: json!({}),
            status: "approved".to_owned(),
            created_at: Utc.with_ymd_and_hms(2026, 8, 24, 1, 2, 3).unwrap(),
        };
        assert_eq!(
            serde_json::to_value(history).unwrap()["createdAt"],
            "2026-08-24T01:02:03.000Z"
        );
    }

    #[test]
    fn leave_payload_parser_matches_csharp_fallback() {
        assert_eq!(parse_json_or_empty_array("bad-json"), json!([]));
        assert_eq!(
            parse_json_or_empty_array(r#"{"days":2}"#),
            json!({ "days": 2 })
        );
    }

    #[test]
    fn malformed_guid_and_empty_mutations_preserve_status_contract() {
        assert!(parse_id("not-a-guid").is_none());
        assert_eq!(
            empty_mutation_response(0, StatusCode::NOT_FOUND).status(),
            StatusCode::NOT_FOUND
        );
        assert_eq!(
            empty_mutation_response(1, StatusCode::NOT_FOUND).status(),
            StatusCode::NO_CONTENT
        );
    }

    #[test]
    fn request_json_is_camel_case_and_case_insensitive_for_dotnet_names() {
        let lower: TrainingProgressRequest =
            serde_json::from_value(json!({ "progress": 3, "resumeSeconds": 4 })).unwrap();
        let dotnet: TrainingProgressRequest =
            serde_json::from_value(json!({ "Progress": 5, "ResumeSeconds": 6 })).unwrap();
        assert_eq!(normalize_training_progress(lower), (3, 4));
        assert_eq!(normalize_training_progress(dotnet), (5, 6));

        let missing: TrainingProgressRequest = serde_json::from_value(json!({})).unwrap();
        assert_eq!(normalize_training_progress(missing), (0, 0));
        let missing: ProgressRequest = serde_json::from_value(json!({})).unwrap();
        assert_eq!(missing.progress, zero_json_number());
    }
}
