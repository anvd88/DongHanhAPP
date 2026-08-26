//! Native survey endpoints, ported from `SurveyEndpoints.cs`.
//!
//! This router must be mounted behind `auth::require_auth`. Every route keeps
//! the original `portal.read` group policy; administration routes additionally
//! require `portal.manage`.

use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    extract::{DefaultBodyLimit, FromRequestParts, State, rejection::JsonRejection},
    http::{StatusCode, request::Parts},
    response::{IntoResponse, Response},
    routing::get,
};
use chrono::{DateTime, NaiveDate, NaiveDateTime, SecondsFormat, TimeZone, Utc};
use pbkdf2::{
    hmac::{Hmac, KeyInit, Mac},
    sha2::Sha256,
};
use serde::{Deserialize, Deserializer, Serialize, Serializer};
use serde_json::json;
use sqlx::{FromRow, PgConnection, PgPool};
use std::{collections::HashSet, sync::Arc};
use uuid::Uuid;

const MAX_JSON_BODY_BYTES: usize = 16 * 1024 * 1024;
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";
const TITLE_REQUIRED_MESSAGE: &str = "Thiếu tiêu đề khảo sát.";
const QUESTION_REQUIRED_MESSAGE: &str = "Khảo sát phải có ít nhất một câu hỏi.";
const SURVEY_CLOSED_MESSAGE: &str = "Khảo sát đã đóng.";
const ANSWER_REQUIRED_MESSAGE: &str = "Chưa có câu trả lời.";
const QUESTION_OWNERSHIP_MESSAGE: &str = "Câu hỏi không thuộc khảo sát này.";
const ALREADY_RESPONDED_MESSAGE: &str = "Bạn đã gửi phản hồi cho khảo sát này rồi.";

const SURVEYS_PATH: &str = "/api/surveys";
const ACTIVE_PATH: &str = "/api/surveys/active";
const SURVEY_PATH: &str = "/api/surveys/{id}";
const RESPOND_PATH: &str = "/api/surveys/{id}/respond";
const RESULTS_PATH: &str = "/api/surveys/{id}/results";
const CLOSE_PATH: &str = "/api/surveys/{id}/close";

const INSERT_SURVEY_SQL: &str = r#"
    INSERT INTO surveys
        (id, title, description, is_anonymous, allow_multiple, is_active, created_by, closes_at)
    VALUES ($1, $2, $3, $4, $5, TRUE, $6, $7)
"#;

const INSERT_QUESTION_SQL: &str = r#"
    INSERT INTO survey_questions
        (id, survey_id, question, qtype, options, order_no, required)
    VALUES ($1, $2, $3, $4, $5::jsonb, $6, $7)
"#;

const AUDIT_SQL: &str = r#"
    INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
    VALUES (CURRENT_TIMESTAMP, $1, $2, 'Survey', $3, $4)
"#;

#[cfg(test)]
const ROUTE_CONTRACTS: &[(&str, &str)] = &[
    ("POST", SURVEYS_PATH),
    ("GET", SURVEYS_PATH),
    ("GET", ACTIVE_PATH),
    ("GET", SURVEY_PATH),
    ("POST", RESPOND_PATH),
    ("GET", RESULTS_PATH),
    ("POST", CLOSE_PATH),
    ("DELETE", SURVEY_PATH),
];

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(SURVEYS_PATH, get(list_surveys).post(create_survey))
        .route(ACTIVE_PATH, get(active_surveys))
        .route(SURVEY_PATH, get(survey_detail).delete(delete_survey))
        .route(RESPOND_PATH, axum::routing::post(respond_to_survey))
        .route(RESULTS_PATH, get(survey_results))
        .route(CLOSE_PATH, axum::routing::post(close_survey))
        .layer(DefaultBodyLimit::max(MAX_JSON_BODY_BYTES))
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct CreateSurveyRequest {
    #[serde(alias = "Title")]
    title: Option<String>,
    #[serde(alias = "Description")]
    description: Option<String>,
    #[serde(alias = "IsAnonymous")]
    is_anonymous: Option<bool>,
    #[serde(alias = "AllowMultiple")]
    allow_multiple: Option<bool>,
    #[serde(
        default,
        alias = "ClosesAt",
        deserialize_with = "deserialize_optional_dotnet_utc"
    )]
    closes_at: Option<DateTime<Utc>>,
    #[serde(alias = "Questions")]
    questions: Option<Vec<QuestionRequest>>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct QuestionRequest {
    #[serde(alias = "Question")]
    question: Option<String>,
    #[serde(alias = "Qtype")]
    qtype: Option<String>,
    #[serde(alias = "Options")]
    options: Option<Vec<Option<String>>>,
    #[serde(alias = "Required")]
    required: Option<bool>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RespondRequest {
    #[serde(alias = "Answers")]
    answers: Option<Vec<AnswerRequest>>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AnswerRequest {
    #[serde(alias = "QuestionId")]
    question_id: Uuid,
    #[serde(alias = "Answer")]
    answer: Option<String>,
    #[serde(alias = "OptionIndices")]
    option_indices: Option<Vec<i32>>,
}

#[derive(Debug, Serialize)]
struct CreatedSurveyResponse {
    id: Uuid,
}

#[derive(Debug, Serialize)]
struct OkResponse {
    ok: bool,
}

#[derive(Debug, FromRow)]
struct SurveyAdminRow {
    id: Uuid,
    title: String,
    description: String,
    is_anonymous: bool,
    allow_multiple: bool,
    is_active: bool,
    created_at: DateTime<Utc>,
    closes_at: Option<DateTime<Utc>>,
    responses: i64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SurveyAdminDto {
    id: Uuid,
    title: String,
    description: String,
    is_anonymous: bool,
    allow_multiple: bool,
    is_active: bool,
    #[serde(serialize_with = "serialize_dotnet_utc")]
    created_at: DateTime<Utc>,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    closes_at: Option<DateTime<Utc>>,
    responses: i64,
}

impl From<SurveyAdminRow> for SurveyAdminDto {
    fn from(row: SurveyAdminRow) -> Self {
        Self {
            id: row.id,
            title: row.title,
            description: row.description,
            is_anonymous: row.is_anonymous,
            allow_multiple: row.allow_multiple,
            is_active: row.is_active,
            created_at: row.created_at,
            closes_at: row.closes_at,
            responses: row.responses,
        }
    }
}

#[derive(Debug, FromRow)]
struct ActiveSurveyRow {
    id: Uuid,
    title: String,
    description: String,
    is_anonymous: bool,
    allow_multiple: bool,
    closes_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ActiveSurveyDto {
    id: Uuid,
    title: String,
    description: String,
    is_anonymous: bool,
    allow_multiple: bool,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    closes_at: Option<DateTime<Utc>>,
    responded: bool,
}

#[derive(Debug, FromRow)]
struct SurveyHeadRow {
    id: Uuid,
    title: String,
    description: String,
    is_anonymous: bool,
    allow_multiple: bool,
    is_active: bool,
    closes_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SurveyHeadDto {
    id: Uuid,
    title: String,
    description: String,
    is_anonymous: bool,
    allow_multiple: bool,
    is_active: bool,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    closes_at: Option<DateTime<Utc>>,
}

impl From<SurveyHeadRow> for SurveyHeadDto {
    fn from(row: SurveyHeadRow) -> Self {
        Self {
            id: row.id,
            title: row.title,
            description: row.description,
            is_anonymous: row.is_anonymous,
            allow_multiple: row.allow_multiple,
            is_active: row.is_active,
            closes_at: row.closes_at,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct QuestionDto {
    id: Uuid,
    question: String,
    qtype: String,
    options: Vec<Option<String>>,
    required: bool,
}

#[derive(Debug, Serialize)]
struct SurveyDetailDto {
    survey: SurveyHeadDto,
    questions: Vec<QuestionDto>,
}

#[derive(Debug, Serialize)]
struct SurveyResultsDto {
    total: i64,
    results: Vec<QuestionResultDto>,
}

#[derive(Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct QuestionResultDto {
    question_id: Uuid,
    question: String,
    qtype: String,
    options: Vec<Option<String>>,
    option_counts: Vec<i32>,
    texts: Vec<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    rating_avg: Option<f64>,
}

#[derive(Debug, FromRow)]
struct SubmissionSurveyRow {
    allow_multiple: bool,
    is_active: bool,
    closes_at: Option<DateTime<Utc>>,
}

#[derive(Debug, FromRow)]
struct AnswerValueRow {
    answer: String,
    opts: String,
}

async fn create_survey(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<CreateSurveyRequest>, JsonRejection>,
) -> Response {
    if !may_manage(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let request = match parse_json(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };

    let title = request.title.unwrap_or_default();
    if title.trim().is_empty() {
        return bad_request(TITLE_REQUIRED_MESSAGE);
    }
    let title = title.trim().to_owned();
    let questions = match request.questions {
        Some(questions) if !questions.is_empty() => questions,
        _ => return bad_request(QUESTION_REQUIRED_MESSAGE),
    };

    let id = Uuid::new_v4();
    let mut transaction = match state.pool.begin().await {
        Ok(transaction) => transaction,
        Err(error) => return database_failure("begin survey creation", error),
    };

    if let Err(error) = sqlx::query(INSERT_SURVEY_SQL)
        .bind(id)
        .bind(&title)
        .bind(request.description.unwrap_or_default())
        .bind(request.is_anonymous.unwrap_or(true))
        .bind(request.allow_multiple.unwrap_or(false))
        .bind(&auth.username)
        .bind(request.closes_at)
        .execute(&mut *transaction)
        .await
    {
        return database_failure("insert survey", error);
    }

    for (order, question) in questions.into_iter().enumerate() {
        let options = strings_json(question.options.as_deref().unwrap_or_default());
        if let Err(error) = sqlx::query(INSERT_QUESTION_SQL)
            .bind(Uuid::new_v4())
            .bind(id)
            .bind(question.question.unwrap_or_default())
            .bind(normalize_type(question.qtype.as_deref()))
            .bind(options)
            .bind(order as i32)
            .bind(question.required.unwrap_or(true))
            .execute(&mut *transaction)
            .await
        {
            return database_failure("insert survey question", error);
        }
    }

    if let Err(error) = transaction.commit().await {
        return database_failure("commit survey creation", error);
    }

    record_audit(&state.pool, &auth.username, "Tạo khảo sát", id, &title).await;
    Json(CreatedSurveyResponse { id }).into_response()
}

async fn list_surveys(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    if !may_manage(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let rows = sqlx::query_as::<_, SurveyAdminRow>(
        r#"
        SELECT s.id, s.title, s.description, s.is_anonymous, s.allow_multiple,
               s.is_active, s.created_at, s.closes_at,
               (SELECT COUNT(*) FROM survey_responses x WHERE x.survey_id = s.id) AS responses
        FROM surveys s
        ORDER BY s.created_at DESC
        "#,
    )
    .fetch_all(&state.pool)
    .await;

    match rows {
        Ok(rows) => Json(
            rows.into_iter()
                .map(SurveyAdminDto::from)
                .collect::<Vec<_>>(),
        )
        .into_response(),
        Err(error) => database_failure("list surveys", error),
    }
}

async fn active_surveys(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    if !may_read(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for active surveys", error),
    };
    let rows = sqlx::query_as::<_, ActiveSurveyRow>(
        r#"
        SELECT id, title, description, is_anonymous, allow_multiple, closes_at
        FROM surveys
        WHERE is_active = TRUE
          AND (closes_at IS NULL OR closes_at > CURRENT_TIMESTAMP)
        ORDER BY created_at DESC
        "#,
    )
    .fetch_all(&mut *connection)
    .await;
    let rows = match rows {
        Ok(rows) => rows,
        Err(error) => return database_failure("read active surveys", error),
    };

    let mut surveys = Vec::with_capacity(rows.len());
    for row in rows {
        let responded = if row.allow_multiple {
            false
        } else {
            let hash = respondent_hash(&state.auth.settings().jwt_key, row.id, &auth.username);
            let count = sqlx::query_scalar::<_, i64>(
                r#"
                SELECT COUNT(*)
                FROM survey_responses
                WHERE survey_id = $1 AND respondent_hash = $2
                "#,
            )
            .bind(row.id)
            .bind(hash)
            .fetch_one(&mut *connection)
            .await;
            match count {
                Ok(count) => count > 0,
                Err(error) => return database_failure("check active survey response", error),
            }
        };

        surveys.push(ActiveSurveyDto {
            id: row.id,
            title: row.title,
            description: row.description,
            is_anonymous: row.is_anonymous,
            allow_multiple: row.allow_multiple,
            closes_at: row.closes_at,
            responded,
        });
    }

    Json(surveys).into_response()
}

async fn survey_detail(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    SurveyId(id): SurveyId,
) -> Response {
    if !may_read(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for survey detail", error),
    };
    let head = sqlx::query_as::<_, SurveyHeadRow>(
        r#"
        SELECT id, title, description, is_anonymous, allow_multiple, is_active, closes_at
        FROM surveys
        WHERE id = $1
        "#,
    )
    .bind(id)
    .fetch_optional(&mut *connection)
    .await;
    let head = match head {
        Ok(Some(head)) => SurveyHeadDto::from(head),
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(error) => return database_failure("read survey detail", error),
    };

    match read_questions(&mut connection, id).await {
        Ok(questions) => Json(SurveyDetailDto {
            survey: head,
            questions,
        })
        .into_response(),
        Err(error) => database_failure("read survey detail questions", error),
    }
}

async fn respond_to_survey(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    SurveyId(id): SurveyId,
    payload: Result<Json<RespondRequest>, JsonRejection>,
) -> Response {
    if !may_read(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let request = match parse_json(payload) {
        Ok(request) => request,
        Err(status) => return status.into_response(),
    };

    let mut transaction = match state.pool.begin().await {
        Ok(transaction) => transaction,
        Err(error) => return database_failure("begin survey response", error),
    };
    let survey = sqlx::query_as::<_, SubmissionSurveyRow>(
        r#"
        SELECT allow_multiple, is_active, closes_at
        FROM surveys
        WHERE id = $1
        FOR SHARE
        "#,
    )
    .bind(id)
    .fetch_optional(&mut *transaction)
    .await;
    let survey = match survey {
        Ok(Some(survey)) => survey,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(error) => return database_failure("read survey for response", error),
    };
    if !survey.is_active
        || survey
            .closes_at
            .is_some_and(|closes_at| closes_at <= Utc::now())
    {
        return bad_request(SURVEY_CLOSED_MESSAGE);
    }

    let answers = match request.answers {
        Some(answers) if !answers.is_empty() => answers,
        _ => return bad_request(ANSWER_REQUIRED_MESSAGE),
    };
    let requested_question_ids = answers
        .iter()
        .map(|answer| answer.question_id)
        .collect::<HashSet<_>>();
    let requested_question_ids_vec = requested_question_ids.iter().copied().collect::<Vec<_>>();
    let owned_question_ids = sqlx::query_scalar::<_, Uuid>(
        r#"
        SELECT id
        FROM survey_questions
        WHERE survey_id = $1 AND id = ANY($2::uuid[])
        "#,
    )
    .bind(id)
    .bind(&requested_question_ids_vec)
    .fetch_all(&mut *transaction)
    .await;
    let owned_question_ids = match owned_question_ids {
        Ok(ids) => ids.into_iter().collect::<HashSet<_>>(),
        Err(error) => return database_failure("validate survey answer questions", error),
    };
    if !answers_reference_only_owned_questions(&requested_question_ids, &owned_question_ids) {
        return bad_request(QUESTION_OWNERSHIP_MESSAGE);
    }

    let response_id = Uuid::new_v4();
    let hash = (!survey.allow_multiple)
        .then(|| respondent_hash(&state.auth.settings().jwt_key, id, &auth.username));
    let insert_response = sqlx::query(
        r#"
        INSERT INTO survey_responses (id, survey_id, respondent_hash)
        VALUES ($1, $2, $3)
        "#,
    )
    .bind(response_id)
    .bind(id)
    .bind(hash.as_deref())
    .execute(&mut *transaction)
    .await;
    if let Err(error) = insert_response {
        if is_unique_violation(&error) {
            return conflict(ALREADY_RESPONDED_MESSAGE);
        }
        return database_failure("insert survey response", error);
    }

    for answer in answers {
        let option_indices = ints_json(answer.option_indices.as_deref().unwrap_or_default());
        if let Err(error) = sqlx::query(
            r#"
            INSERT INTO survey_answers (response_id, question_id, answer, option_indices)
            VALUES ($1, $2, $3, $4::jsonb)
            "#,
        )
        .bind(response_id)
        .bind(answer.question_id)
        .bind(answer.answer.unwrap_or_default())
        .bind(option_indices)
        .execute(&mut *transaction)
        .await
        {
            return database_failure("insert survey answer", error);
        }
    }

    if let Err(error) = transaction.commit().await {
        return database_failure("commit survey response", error);
    }
    Json(OkResponse { ok: true }).into_response()
}

async fn survey_results(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    SurveyId(id): SurveyId,
) -> Response {
    if !may_manage(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for survey results", error),
    };
    let total =
        sqlx::query_scalar::<_, i64>("SELECT COUNT(*) FROM survey_responses WHERE survey_id = $1")
            .bind(id)
            .fetch_one(&mut *connection)
            .await;
    let total = match total {
        Ok(total) => total,
        Err(error) => return database_failure("count survey responses", error),
    };
    let questions = match read_questions(&mut connection, id).await {
        Ok(questions) => questions,
        Err(error) => return database_failure("read survey result questions", error),
    };

    let mut results = Vec::with_capacity(questions.len());
    for question in questions {
        let answers = sqlx::query_as::<_, AnswerValueRow>(
            r#"
            SELECT a.answer, a.option_indices::text AS opts
            FROM survey_answers a
            JOIN survey_responses s ON s.id = a.response_id
            WHERE s.survey_id = $1 AND a.question_id = $2
            "#,
        )
        .bind(id)
        .bind(question.id)
        .fetch_all(&mut *connection)
        .await;
        let answers = match answers {
            Ok(answers) => answers,
            Err(error) => return database_failure("aggregate survey question result", error),
        };
        results.push(aggregate_question(question, &answers));
    }

    Json(SurveyResultsDto { total, results }).into_response()
}

async fn close_survey(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    SurveyId(id): SurveyId,
) -> Response {
    if !may_manage(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let update = sqlx::query("UPDATE surveys SET is_active = FALSE WHERE id = $1")
        .bind(id)
        .execute(&state.pool)
        .await;
    let update = match update {
        Ok(update) => update,
        Err(error) => return database_failure("close survey", error),
    };
    if update.rows_affected() == 0 {
        return StatusCode::NOT_FOUND.into_response();
    }

    record_audit(&state.pool, &auth.username, "Đóng khảo sát", id, "").await;
    StatusCode::NO_CONTENT.into_response()
}

async fn delete_survey(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    SurveyId(id): SurveyId,
) -> Response {
    if !may_manage(&auth) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let deletion = sqlx::query("DELETE FROM surveys WHERE id = $1")
        .bind(id)
        .execute(&state.pool)
        .await;
    let deletion = match deletion {
        Ok(deletion) => deletion,
        Err(error) => return database_failure("delete survey", error),
    };
    if deletion.rows_affected() == 0 {
        return StatusCode::NOT_FOUND.into_response();
    }

    record_audit(&state.pool, &auth.username, "Xóa khảo sát", id, "").await;
    StatusCode::NO_CONTENT.into_response()
}

async fn read_questions(
    connection: &mut PgConnection,
    survey_id: Uuid,
) -> Result<Vec<QuestionDto>, sqlx::Error> {
    #[derive(FromRow)]
    struct Row {
        id: Uuid,
        question: String,
        qtype: String,
        options: String,
        required: bool,
    }

    let rows = sqlx::query_as::<_, Row>(
        r#"
        SELECT id, question, qtype, options::text AS options, required
        FROM survey_questions
        WHERE survey_id = $1
        ORDER BY order_no
        "#,
    )
    .bind(survey_id)
    .fetch_all(connection)
    .await?;
    Ok(rows
        .into_iter()
        .map(|row| QuestionDto {
            id: row.id,
            question: row.question,
            qtype: row.qtype,
            options: parse_strings(&row.options),
            required: row.required,
        })
        .collect())
}

fn aggregate_question(question: QuestionDto, answers: &[AnswerValueRow]) -> QuestionResultDto {
    let mut option_counts = vec![0_i32; question.options.len()];
    let mut texts = Vec::new();
    let mut rating_sum = 0.0_f64;
    let mut rating_count = 0_u64;

    for answer in answers {
        match question.qtype.as_str() {
            "single" | "multi" => {
                for index in parse_ints(&answer.opts) {
                    if index >= 0
                        && let Some(count) = option_counts.get_mut(index as usize)
                    {
                        *count += 1;
                    }
                }
            }
            "rating" => {
                if let Ok(value) = answer.answer.trim().parse::<f64>() {
                    rating_sum += value;
                    rating_count += 1;
                }
            }
            _ => {
                if !answer.answer.trim().is_empty() {
                    texts.push(answer.answer.clone());
                }
            }
        }
    }

    let rating_avg =
        (rating_count > 0).then(|| round_dotnet_two_decimals(rating_sum / rating_count as f64));
    QuestionResultDto {
        question_id: question.id,
        question: question.question,
        qtype: question.qtype,
        options: question.options,
        option_counts,
        texts,
        rating_avg,
    }
}

fn normalize_type(value: Option<&str>) -> &'static str {
    match value.unwrap_or_default().trim().to_lowercase().as_str() {
        "multi" => "multi",
        "text" => "text",
        "rating" => "rating",
        _ => "single",
    }
}

fn strings_json(values: &[Option<String>]) -> String {
    serde_json::to_string(values).unwrap_or_else(|_| "[]".to_owned())
}

fn ints_json(values: &[i32]) -> String {
    serde_json::to_string(values).unwrap_or_else(|_| "[]".to_owned())
}

fn parse_strings(value: &str) -> Vec<Option<String>> {
    serde_json::from_str::<Option<Vec<Option<String>>>>(value)
        .ok()
        .flatten()
        .unwrap_or_default()
}

fn parse_ints(value: &str) -> Vec<i32> {
    serde_json::from_str::<Option<Vec<i32>>>(value)
        .ok()
        .flatten()
        .unwrap_or_default()
}

fn respondent_hash(key: &[u8], survey_id: Uuid, username: &str) -> String {
    let mut hmac =
        Hmac::<Sha256>::new_from_slice(key).expect("HMAC-SHA256 accepts keys of every length");
    hmac.update(format!("{survey_id}|{username}").as_bytes());
    lower_hex(&hmac.finalize().into_bytes())
}

fn lower_hex(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut value = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        value.push(HEX[(byte >> 4) as usize] as char);
        value.push(HEX[(byte & 0x0f) as usize] as char);
    }
    value
}

fn answers_reference_only_owned_questions(
    requested: &HashSet<Uuid>,
    owned: &HashSet<Uuid>,
) -> bool {
    requested.is_subset(owned)
}

fn round_dotnet_two_decimals(value: f64) -> f64 {
    (value * 100.0).round_ties_even() / 100.0
}

fn may_read(auth: &AuthContext) -> bool {
    auth.permissions.contains(permissions::PORTAL_READ)
}

fn may_manage(auth: &AuthContext) -> bool {
    may_read(auth) && auth.permissions.contains(permissions::PORTAL_MANAGE)
}

struct SurveyId(Uuid);

impl<S> FromRequestParts<S> for SurveyId
where
    S: Send + Sync,
{
    type Rejection = StatusCode;

    async fn from_request_parts(parts: &mut Parts, state: &S) -> Result<Self, Self::Rejection> {
        let axum::extract::Path(raw) =
            axum::extract::Path::<String>::from_request_parts(parts, state)
                .await
                .map_err(|_| StatusCode::NOT_FOUND)?;
        Uuid::parse_str(&raw)
            .map(Self)
            .map_err(|_| StatusCode::NOT_FOUND)
    }
}

fn deserialize_optional_dotnet_utc<'de, D>(
    deserializer: D,
) -> Result<Option<DateTime<Utc>>, D::Error>
where
    D: Deserializer<'de>,
{
    let value = Option::<String>::deserialize(deserializer)?;
    value
        .map(|value| {
            parse_dotnet_utc(&value)
                .ok_or_else(|| serde::de::Error::custom("expected an ISO-8601 date and time"))
        })
        .transpose()
}

fn parse_dotnet_utc(value: &str) -> Option<DateTime<Utc>> {
    let value = normalize_dotnet_datetime(value)?;
    let value = value.as_str();
    if let Ok(value) = DateTime::parse_from_rfc3339(value) {
        return Some(value.with_timezone(&Utc));
    }
    for format in [
        "%Y-%m-%dT%H:%M:%S%.f",
        "%Y-%m-%dT%H:%M:%S",
        "%Y-%m-%dT%H:%M",
    ] {
        if let Ok(value) = NaiveDateTime::parse_from_str(value, format) {
            return Some(Utc.from_utc_datetime(&value));
        }
    }
    NaiveDate::parse_from_str(value, "%Y-%m-%d")
        .ok()
        .and_then(|value| value.and_hms_opt(0, 0, 0))
        .map(|value| Utc.from_utc_datetime(&value))
}

fn normalize_dotnet_datetime(value: &str) -> Option<String> {
    let mut value = value.to_owned();
    if !value.is_ascii() {
        return Some(value);
    }
    if value.len() >= 17 {
        let bytes = value.as_bytes();
        let minute_precision = bytes.get(10) == Some(&b'T')
            && bytes.get(13) == Some(&b':')
            && (bytes.get(16) == Some(&b'Z') || matches!(bytes.get(16), Some(b'+') | Some(b'-')));
        if minute_precision {
            value.insert_str(16, ":00");
        }
    }
    if value.as_bytes().get(19) == Some(&b'.') {
        let fraction_digits = value.as_bytes()[20..]
            .iter()
            .take_while(|byte| byte.is_ascii_digit())
            .count();
        if fraction_digits == 0 || fraction_digits > 16 {
            return None;
        }
        if fraction_digits > 7 {
            value.replace_range(27..20 + fraction_digits, "");
        }
    }
    Some(value)
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

fn parse_json<T>(payload: Result<Json<T>, JsonRejection>) -> Result<T, StatusCode> {
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

fn is_unique_violation(error: &sqlx::Error) -> bool {
    error
        .as_database_error()
        .and_then(|error| error.code())
        .is_some_and(|code| code == "23505")
}

fn bad_request(message: &'static str) -> Response {
    (StatusCode::BAD_REQUEST, Json(json!({ "message": message }))).into_response()
}

fn conflict(message: &'static str) -> Response {
    (StatusCode::CONFLICT, Json(json!({ "message": message }))).into_response()
}

async fn record_audit(
    pool: &PgPool,
    username: &str,
    action: &'static str,
    survey_id: Uuid,
    details: &str,
) {
    if let Err(error) = sqlx::query(AUDIT_SQL)
        .bind(username)
        .bind(action)
        .bind(survey_id.to_string())
        .bind(details)
        .execute(pool)
        .await
    {
        tracing::warn!(%error, action, %survey_id, "could not record survey audit event");
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native survey database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::{Value, json};
    use std::collections::BTreeSet;

    fn auth_with(permissions: &[&str]) -> AuthContext {
        AuthContext {
            user_id: Some(Uuid::nil()),
            username: "alice".to_owned(),
            full_name: "Alice".to_owned(),
            sid: Some("web:alice".to_owned()),
            roles: Vec::new(),
            permissions: permissions
                .iter()
                .map(|value| (*value).to_owned())
                .collect(),
            source: crate::auth::TokenSource::Bearer,
            account_state_verified: true,
            session_alive: true,
        }
    }

    #[test]
    fn route_surface_matches_the_eight_dotnet_operations() {
        assert_eq!(ROUTE_CONTRACTS.len(), 8);
        assert_eq!(
            ROUTE_CONTRACTS
                .iter()
                .copied()
                .collect::<HashSet<_>>()
                .len(),
            8
        );
    }

    #[test]
    fn permission_boundary_keeps_group_read_and_manage_policy() {
        let read = auth_with(&[permissions::PORTAL_READ]);
        let manage_only = auth_with(&[permissions::PORTAL_MANAGE]);
        let manage = auth_with(&[permissions::PORTAL_READ, permissions::PORTAL_MANAGE]);
        assert!(may_read(&read));
        assert!(!may_manage(&read));
        assert!(!may_manage(&manage_only));
        assert!(may_manage(&manage));
    }

    #[test]
    fn hmac_matches_dotnet_sha256_and_never_uses_a_fallback_key() {
        let actual = respondent_hash(
            b"test-only-key-with-at-least-thirty-two-bytes",
            Uuid::nil(),
            "alice",
        );
        assert_eq!(
            actual,
            "d29a94858223fd9d8e7810fd814192c512c156cd9bc769f35f7e83fba4e32159"
        );
        assert_eq!(actual.len(), 64);
    }

    #[test]
    fn question_type_and_json_parsers_match_legacy_fallbacks() {
        assert_eq!(normalize_type(Some(" MULTI ")), "multi");
        assert_eq!(normalize_type(Some("Text")), "text");
        assert_eq!(normalize_type(Some("RATING")), "rating");
        assert_eq!(normalize_type(Some("unknown")), "single");
        assert_eq!(normalize_type(None), "single");
        assert_eq!(parse_strings("not-json"), Vec::<Option<String>>::new());
        assert_eq!(parse_strings("null"), Vec::<Option<String>>::new());
        assert_eq!(parse_ints("[0,2,-1]"), vec![0, 2, -1]);
        assert_eq!(parse_ints("[0,null]"), Vec::<i32>::new());
    }

    #[test]
    fn answer_question_ownership_allows_duplicates_but_not_foreign_ids() {
        let own = Uuid::new_v4();
        let foreign = Uuid::new_v4();
        let owned = HashSet::from([own]);
        assert!(answers_reference_only_owned_questions(
            &HashSet::from([own]),
            &owned
        ));
        assert!(!answers_reference_only_owned_questions(
            &HashSet::from([own, foreign]),
            &owned
        ));
    }

    #[test]
    fn response_dates_have_milliseconds_z_and_nulls_are_omitted() {
        let created_at = DateTime::parse_from_rfc3339("2026-08-24T10:11:12.987654Z")
            .unwrap()
            .with_timezone(&Utc);
        let value = serde_json::to_value(SurveyAdminDto {
            id: Uuid::nil(),
            title: "T".to_owned(),
            description: String::new(),
            is_anonymous: true,
            allow_multiple: false,
            is_active: true,
            created_at,
            closes_at: None,
            responses: 0,
        })
        .unwrap();
        assert_eq!(value["createdAt"], "2026-08-24T10:11:12.987Z");
        assert!(value.get("closesAt").is_none());
    }

    #[test]
    fn aggregation_preserves_anonymity_counts_text_and_rating_contracts() {
        let single = aggregate_question(
            QuestionDto {
                id: Uuid::nil(),
                question: "Pick".to_owned(),
                qtype: "single".to_owned(),
                options: vec![Some("A".to_owned()), Some("B".to_owned())],
                required: true,
            },
            &[
                AnswerValueRow {
                    answer: String::new(),
                    opts: "[0,4]".to_owned(),
                },
                AnswerValueRow {
                    answer: String::new(),
                    opts: "[0,1]".to_owned(),
                },
            ],
        );
        assert_eq!(single.option_counts, vec![2, 1]);
        assert!(single.texts.is_empty());
        assert_eq!(single.rating_avg, None);

        let rating = aggregate_question(
            QuestionDto {
                id: Uuid::nil(),
                question: "Rate".to_owned(),
                qtype: "rating".to_owned(),
                options: Vec::new(),
                required: true,
            },
            &[
                AnswerValueRow {
                    answer: " 2.34 ".to_owned(),
                    opts: "[]".to_owned(),
                },
                AnswerValueRow {
                    answer: "2.35".to_owned(),
                    opts: "[]".to_owned(),
                },
                AnswerValueRow {
                    answer: "bad".to_owned(),
                    opts: "[]".to_owned(),
                },
            ],
        );
        // Math.Round(double, 2) uses midpoint-to-even by default in .NET.
        assert_eq!(rating.rating_avg, Some(2.34));

        let serialized = serde_json::to_value(single).unwrap();
        assert!(serialized.get("ratingAvg").is_none());
        let keys = serialized
            .as_object()
            .unwrap()
            .keys()
            .cloned()
            .collect::<BTreeSet<_>>();
        assert_eq!(
            keys,
            BTreeSet::from([
                "optionCounts".to_owned(),
                "options".to_owned(),
                "qtype".to_owned(),
                "question".to_owned(),
                "questionId".to_owned(),
                "texts".to_owned(),
            ])
        );
    }

    #[test]
    fn request_defaults_remain_nullable_and_camel_case() {
        let request: CreateSurveyRequest = serde_json::from_value(json!({
            "title": "Survey",
            "isAnonymous": null,
            "allowMultiple": null,
            "closesAt": null,
            "questions": [{ "qtype": null, "options": null, "required": null }]
        }))
        .unwrap();
        assert_eq!(request.is_anonymous, None);
        assert_eq!(request.allow_multiple, None);
        assert_eq!(request.closes_at, None);
        let question = request.questions.unwrap().pop().unwrap();
        assert!(question.question.is_none());
        assert!(question.options.is_none());
        assert_eq!(question.required, None);

        let _: Value = serde_json::to_value(OkResponse { ok: true }).unwrap();
    }
}
