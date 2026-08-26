//! Native company portal endpoints, ported from `PortalEndpoints.cs`.
//!
//! This router must be mounted behind `auth::require_auth`. The original group
//! policy (`portal.read`) is still checked in every handler; administration
//! routes additionally require `portal.manage`.

use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    extract::{
        DefaultBodyLimit, Path, Query, State, rejection::JsonRejection, rejection::QueryRejection,
    },
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
};
use chrono::{DateTime, NaiveDate, NaiveDateTime, SecondsFormat, TimeZone, Utc};
use serde::{Deserialize, Deserializer, Serialize, Serializer};
use serde_json::json;
use sqlx::{AssertSqlSafe, FromRow, PgConnection, PgPool};
use std::{collections::BTreeSet, sync::Arc};

const FEED_LIMIT: i64 = 60;
const ADMIN_LIST_LIMIT: i64 = 500;
const MAX_JSON_BODY_BYTES: usize = 16 * 1024 * 1024;

const FEED_PATH: &str = "/api/portal/feed";
const POSTS_PATH: &str = "/api/portal/posts";
const POST_BY_ID_PATH: &str = "/api/portal/posts/{id}";
const ABOUT_PATH: &str = "/api/portal/about";

const ABOUT_SQL: &str = r#"
    SELECT title, content, cover_image, address, hotline, email, website, updated_at
    FROM app_portal_about
    WHERE id = 1
"#;

const INSERT_POST_SQL: &str = r#"
    INSERT INTO app_portal_posts
        (kind, title, summary, body, cover_image, location, event_at, pinned, published,
         author_username, created_at, updated_at)
    VALUES
        ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
    RETURNING id
"#;

const UPDATE_POST_SQL: &str = r#"
    UPDATE app_portal_posts
    SET kind = $2,
        title = $3,
        summary = $4,
        body = $5,
        cover_image = $6,
        location = $7,
        event_at = $8,
        pinned = $9,
        published = $10,
        updated_at = CURRENT_TIMESTAMP
    WHERE id = $1
"#;

const DELETE_POST_SQL: &str = "DELETE FROM app_portal_posts WHERE id = $1";

const UPSERT_ABOUT_SQL: &str = r#"
    INSERT INTO app_portal_about
        (id, title, content, cover_image, address, hotline, email, website, updated_at)
    VALUES (1, $1, $2, $3, $4, $5, $6, $7, CURRENT_TIMESTAMP)
    ON CONFLICT (id) DO UPDATE SET
        title = EXCLUDED.title,
        content = EXCLUDED.content,
        cover_image = EXCLUDED.cover_image,
        address = EXCLUDED.address,
        hotline = EXCLUDED.hotline,
        email = EXCLUDED.email,
        website = EXCLUDED.website,
        updated_at = CURRENT_TIMESTAMP
"#;

const AUDIT_SQL: &str = r#"
    INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
    VALUES (CURRENT_TIMESTAMP, $1, $2, $3, $4, $5)
"#;

const AUDIT_CREATE_POST: &str = "Đăng bài cổng thông tin";
const AUDIT_UPDATE_POST: &str = "Sửa bài cổng thông tin";
const AUDIT_DELETE_POST: &str = "Xóa bài cổng thông tin";
const AUDIT_UPDATE_ABOUT: &str = "Sửa giới thiệu công ty";
const POST_ENTITY: &str = "PortalPost";
const ABOUT_ENTITY: &str = "PortalAbout";
const POST_NOT_FOUND: &str = "Bài viết không còn tồn tại.";
const TITLE_REQUIRED: &str = "Vui lòng nhập tiêu đề.";
const EVENT_TIME_REQUIRED: &str = "Sự kiện cần có thời gian diễn ra.";
const PAYLOAD_TOO_LARGE_MESSAGE: &str = "Payload vượt giới hạn 16777216 byte.";
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";

#[cfg(test)]
const ROUTE_CONTRACTS: &[(&str, &str)] = &[
    ("GET", FEED_PATH),
    ("GET", POSTS_PATH),
    ("POST", POSTS_PATH),
    ("PUT", POST_BY_ID_PATH),
    ("DELETE", POST_BY_ID_PATH),
    ("GET", ABOUT_PATH),
    ("PUT", ABOUT_PATH),
];

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route(FEED_PATH, get(feed))
        .route(POSTS_PATH, get(list_posts).post(create_post))
        .route(
            POST_BY_ID_PATH,
            axum::routing::put(update_post).delete(delete_post),
        )
        .route(ABOUT_PATH, get(get_about).put(put_about))
        // ASP.NET accepts JSON bodies up to 16 MiB. Axum otherwise defaults
        // to 2 MiB, smaller than a valid 2 MB image after base64 expansion.
        .layer(DefaultBodyLimit::max(MAX_JSON_BODY_BYTES))
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum PortalKind {
    News,
    Event,
}

impl PortalKind {
    const fn as_str(self) -> &'static str {
        match self {
            Self::News => "news",
            Self::Event => "event",
        }
    }
}

fn normalize_kind(kind: Option<&str>) -> PortalKind {
    if kind.is_some_and(|kind| kind.trim().eq_ignore_ascii_case("event")) {
        PortalKind::Event
    } else {
        PortalKind::News
    }
}

#[derive(Debug, Default, Deserialize)]
struct PostsQuery {
    #[serde(alias = "Kind")]
    kind: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct PortalPostRequest {
    #[serde(alias = "Kind")]
    kind: Option<String>,
    #[serde(alias = "Title")]
    title: Option<String>,
    #[serde(alias = "Summary")]
    summary: Option<String>,
    #[serde(alias = "Body")]
    body: Option<String>,
    #[serde(alias = "CoverImage")]
    cover_image: Option<String>,
    #[serde(alias = "Location")]
    location: Option<String>,
    #[serde(
        default,
        alias = "EventAt",
        deserialize_with = "deserialize_optional_dotnet_utc"
    )]
    event_at: Option<DateTime<Utc>>,
    #[serde(default, alias = "Pinned")]
    pinned: bool,
    #[serde(default = "default_published", alias = "Published")]
    published: bool,
}

const fn default_published() -> bool {
    true
}

#[derive(Debug, Eq, PartialEq)]
struct ValidatedPost {
    kind: PortalKind,
    title: String,
    summary: String,
    body: String,
    cover_image: Option<String>,
    location: String,
    event_at: Option<DateTime<Utc>>,
    pinned: bool,
    published: bool,
}

fn validate_post(request: PortalPostRequest) -> Result<ValidatedPost, &'static str> {
    let kind = normalize_kind(request.kind.as_deref());
    let title = trim_to_utf16(request.title.as_deref(), 300);
    if title.trim().is_empty() {
        return Err(TITLE_REQUIRED);
    }

    let event_at = request.event_at;
    if kind == PortalKind::Event && event_at.is_none() {
        return Err(EVENT_TIME_REQUIRED);
    }

    Ok(ValidatedPost {
        kind,
        title,
        summary: trim_to_utf16(request.summary.as_deref(), 600),
        body: trim_to_utf16(request.body.as_deref(), 20_000),
        cover_image: trim_optional(request.cover_image.as_deref()),
        location: trim_to_utf16(request.location.as_deref(), 300),
        event_at,
        pinned: request.pinned,
        published: request.published,
    })
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct PortalAboutRequest {
    #[serde(alias = "Title")]
    title: Option<String>,
    #[serde(alias = "Content")]
    content: Option<String>,
    #[serde(alias = "CoverImage")]
    cover_image: Option<String>,
    #[serde(alias = "Address")]
    address: Option<String>,
    #[serde(alias = "Hotline")]
    hotline: Option<String>,
    #[serde(alias = "Email")]
    email: Option<String>,
    #[serde(alias = "Website")]
    website: Option<String>,
}

#[derive(Debug, Eq, PartialEq)]
struct ValidatedAbout {
    title: String,
    content: String,
    cover_image: Option<String>,
    address: String,
    hotline: String,
    email: String,
    website: String,
}

impl From<PortalAboutRequest> for ValidatedAbout {
    fn from(request: PortalAboutRequest) -> Self {
        Self {
            title: trim_to_utf16(request.title.as_deref(), 300),
            content: trim_to_utf16(request.content.as_deref(), 20_000),
            cover_image: trim_optional(request.cover_image.as_deref()),
            address: trim_to_utf16(request.address.as_deref(), 400),
            hotline: trim_to_utf16(request.hotline.as_deref(), 100),
            email: trim_to_utf16(request.email.as_deref(), 200),
            website: trim_to_utf16(request.website.as_deref(), 200),
        }
    }
}

#[derive(Debug, FromRow)]
struct PortalPostRow {
    id: i64,
    kind: String,
    title: String,
    summary: String,
    body: String,
    cover_image: Option<String>,
    location: String,
    event_at: Option<DateTime<Utc>>,
    pinned: bool,
    published: bool,
    author_username: String,
    author_name: String,
    created_at: DateTime<Utc>,
    updated_at: DateTime<Utc>,
}

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct PortalPostDto {
    id: i64,
    kind: String,
    title: String,
    summary: String,
    body: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    cover_image: Option<String>,
    location: String,
    #[serde(
        skip_serializing_if = "Option::is_none",
        serialize_with = "serialize_optional_dotnet_utc"
    )]
    event_at: Option<DateTime<Utc>>,
    pinned: bool,
    published: bool,
    author_username: String,
    author_name: String,
    #[serde(serialize_with = "serialize_dotnet_utc")]
    created_at: DateTime<Utc>,
    #[serde(serialize_with = "serialize_dotnet_utc")]
    updated_at: DateTime<Utc>,
}

impl From<PortalPostRow> for PortalPostDto {
    fn from(row: PortalPostRow) -> Self {
        Self {
            id: row.id,
            kind: row.kind,
            title: row.title,
            summary: row.summary,
            body: row.body,
            cover_image: row.cover_image,
            location: row.location,
            event_at: row.event_at,
            pinned: row.pinned,
            published: row.published,
            author_username: row.author_username,
            author_name: row.author_name,
            created_at: row.created_at,
            updated_at: row.updated_at,
        }
    }
}

#[derive(Debug, FromRow)]
struct PortalAboutRow {
    title: String,
    content: String,
    cover_image: Option<String>,
    address: String,
    hotline: String,
    email: String,
    website: String,
    updated_at: DateTime<Utc>,
}

#[derive(Clone, Debug, Eq, PartialEq)]
enum DotNetDateTime {
    Default,
    Utc(DateTime<Utc>),
}

impl Serialize for DotNetDateTime {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        match self {
            Self::Default => serializer.serialize_str("0001-01-01T00:00:00.000Z"),
            Self::Utc(value) => serialize_dotnet_utc(value, serializer),
        }
    }
}

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct PortalAboutDto {
    title: String,
    content: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    cover_image: Option<String>,
    address: String,
    hotline: String,
    email: String,
    website: String,
    updated_at: DotNetDateTime,
}

impl From<PortalAboutRow> for PortalAboutDto {
    fn from(row: PortalAboutRow) -> Self {
        Self {
            title: row.title,
            content: row.content,
            cover_image: row.cover_image,
            address: row.address,
            hotline: row.hotline,
            email: row.email,
            website: row.website,
            updated_at: DotNetDateTime::Utc(row.updated_at),
        }
    }
}

impl PortalAboutDto {
    fn empty() -> Self {
        Self {
            title: String::new(),
            content: String::new(),
            cover_image: None,
            address: String::new(),
            hotline: String::new(),
            email: String::new(),
            website: String::new(),
            updated_at: DotNetDateTime::Default,
        }
    }
}

#[derive(Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct PortalFeedDto {
    about: PortalAboutDto,
    news: Vec<PortalPostDto>,
    events: Vec<PortalPostDto>,
}

async fn feed(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    if !may_read(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for portal feed", error),
    };
    let about = match read_about(&mut connection).await {
        Ok(about) => about,
        Err(error) => return database_failure("read portal about for feed", error),
    };
    let news = match read_posts(&mut connection, PortalKind::News, true, false, FEED_LIMIT).await {
        Ok(posts) => posts,
        Err(error) => return database_failure("read portal news feed", error),
    };
    let events = match read_posts(&mut connection, PortalKind::Event, true, true, FEED_LIMIT).await
    {
        Ok(posts) => posts,
        Err(error) => return database_failure("read portal event feed", error),
    };

    Json(PortalFeedDto {
        about,
        news,
        events,
    })
    .into_response()
}

async fn list_posts(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    query: Result<Query<PostsQuery>, QueryRejection>,
) -> Response {
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let Query(query) = match query {
        Ok(query) => query,
        Err(_) => return StatusCode::BAD_REQUEST.into_response(),
    };
    let kind = normalize_kind(query.kind.as_deref());

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for portal posts", error),
    };
    match read_posts(&mut connection, kind, false, false, ADMIN_LIST_LIMIT).await {
        Ok(posts) => Json(posts).into_response(),
        Err(error) => database_failure("read portal administration posts", error),
    }
}

async fn create_post(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<PortalPostRequest>, JsonRejection>,
) -> Response {
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let post = match validate_post(request) {
        Ok(post) => post,
        Err(message) => return bad_request(message),
    };

    let id = match sqlx::query_scalar::<_, i64>(INSERT_POST_SQL)
        .bind(post.kind.as_str())
        .bind(&post.title)
        .bind(&post.summary)
        .bind(&post.body)
        .bind(&post.cover_image)
        .bind(&post.location)
        .bind(post.event_at)
        .bind(post.pinned)
        .bind(post.published)
        .bind(&auth.username)
        .fetch_one(&state.pool)
        .await
    {
        Ok(id) => id,
        Err(error) => return database_failure("create portal post", error),
    };

    let entity_name = id.to_string();
    let details = format!("[{}] {}", post.kind.as_str(), post.title);
    record_audit(
        &state.pool,
        &auth.username,
        AUDIT_CREATE_POST,
        POST_ENTITY,
        &entity_name,
        &details,
    )
    .await;
    (StatusCode::OK, Json(json!({ "id": id }))).into_response()
}

async fn update_post(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
    payload: Result<Json<PortalPostRequest>, JsonRejection>,
) -> Response {
    let Some(id) = parse_post_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let post = match validate_post(request) {
        Ok(post) => post,
        Err(message) => return bad_request(message),
    };

    let result = match sqlx::query(UPDATE_POST_SQL)
        .bind(id)
        .bind(post.kind.as_str())
        .bind(&post.title)
        .bind(&post.summary)
        .bind(&post.body)
        .bind(&post.cover_image)
        .bind(&post.location)
        .bind(post.event_at)
        .bind(post.pinned)
        .bind(post.published)
        .execute(&state.pool)
        .await
    {
        Ok(result) => result,
        Err(error) => return database_failure("update portal post", error),
    };
    if result.rows_affected() == 0 {
        return not_found();
    }

    let entity_name = id.to_string();
    let details = format!("[{}] {}", post.kind.as_str(), post.title);
    record_audit(
        &state.pool,
        &auth.username,
        AUDIT_UPDATE_POST,
        POST_ENTITY,
        &entity_name,
        &details,
    )
    .await;
    StatusCode::NO_CONTENT.into_response()
}

async fn delete_post(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(id): Path<String>,
) -> Response {
    let Some(id) = parse_post_id(&id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }

    let result = match sqlx::query(DELETE_POST_SQL)
        .bind(id)
        .execute(&state.pool)
        .await
    {
        Ok(result) => result,
        Err(error) => return database_failure("delete portal post", error),
    };
    if result.rows_affected() == 0 {
        return not_found();
    }

    let entity_name = id.to_string();
    record_audit(
        &state.pool,
        &auth.username,
        AUDIT_DELETE_POST,
        POST_ENTITY,
        &entity_name,
        "",
    )
    .await;
    StatusCode::NO_CONTENT.into_response()
}

async fn get_about(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
) -> Response {
    if !may_read(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for portal about", error),
    };
    match read_about(&mut connection).await {
        Ok(about) => Json(about).into_response(),
        Err(error) => database_failure("read portal about", error),
    }
}

async fn put_about(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<PortalAboutRequest>, JsonRejection>,
) -> Response {
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    let about = ValidatedAbout::from(request);

    if let Err(error) = sqlx::query(UPSERT_ABOUT_SQL)
        .bind(&about.title)
        .bind(&about.content)
        .bind(&about.cover_image)
        .bind(&about.address)
        .bind(&about.hotline)
        .bind(&about.email)
        .bind(&about.website)
        .execute(&state.pool)
        .await
    {
        return database_failure("update portal about", error);
    }

    record_audit(
        &state.pool,
        &auth.username,
        AUDIT_UPDATE_ABOUT,
        ABOUT_ENTITY,
        "1",
        &about.title,
    )
    .await;
    StatusCode::NO_CONTENT.into_response()
}

async fn read_posts(
    connection: &mut PgConnection,
    kind: PortalKind,
    published_only: bool,
    upcoming_only: bool,
    limit: i64,
) -> Result<Vec<PortalPostDto>, sqlx::Error> {
    let sql = build_posts_sql(kind, published_only, upcoming_only);
    // Every fragment is selected from closed enums/booleans below; user data
    // remains bound in $1/$2 and is never interpolated into the SQL string.
    let rows = sqlx::query_as::<_, PortalPostRow>(AssertSqlSafe(sql))
        .bind(kind.as_str())
        .bind(limit)
        .fetch_all(&mut *connection)
        .await?;
    Ok(rows.into_iter().map(PortalPostDto::from).collect())
}

fn build_posts_sql(kind: PortalKind, published_only: bool, upcoming_only: bool) -> String {
    let mut sql = String::from(
        r#"
        SELECT p.id, p.kind, p.title, p.summary, p.body, p.cover_image, p.location, p.event_at,
               p.pinned, p.published, p.author_username,
               COALESCE(NULLIF(u.full_name, ''), p.author_username) AS author_name,
               p.created_at, p.updated_at
        FROM app_portal_posts p
        LEFT JOIN app_users u ON u.username = p.author_username
        WHERE p.kind = $1
        "#,
    );
    if published_only {
        sql.push_str(" AND p.published = TRUE");
    }
    if upcoming_only {
        sql.push_str(
            " AND (p.event_at IS NULL OR p.event_at >= date_trunc('day', CURRENT_TIMESTAMP))",
        );
    }
    match kind {
        PortalKind::Event => {
            sql.push_str(" ORDER BY p.event_at ASC NULLS LAST, p.created_at DESC");
        }
        PortalKind::News => {
            sql.push_str(" ORDER BY p.pinned DESC, p.created_at DESC");
        }
    }
    sql.push_str(" LIMIT $2");
    sql
}

async fn read_about(connection: &mut PgConnection) -> Result<PortalAboutDto, sqlx::Error> {
    let row = sqlx::query_as::<_, PortalAboutRow>(ABOUT_SQL)
        .fetch_optional(&mut *connection)
        .await?;
    Ok(row
        .map(PortalAboutDto::from)
        .unwrap_or_else(PortalAboutDto::empty))
}

fn may_read(permission_set: &BTreeSet<String>) -> bool {
    permission_set.contains(permissions::PORTAL_READ)
}

/// The C# contract has no author-ownership or role-name exception: management
/// is governed only by the group `portal.read` plus `portal.manage`.
fn may_manage(permission_set: &BTreeSet<String>) -> bool {
    may_read(permission_set) && permission_set.contains(permissions::PORTAL_MANAGE)
}

fn parse_post_id(raw: &str) -> Option<i64> {
    raw.parse().ok()
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

/// Mirrors `UtcDateTimeConverter.Read`: an explicit offset represents an
/// instant, while an unspecified value is treated as UTC rather than local
/// server time.
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

/// `Utf8JsonReader.GetDateTime` accepts minute precision and up to sixteen
/// fractional-second digits. `DateTime` itself keeps the first seven digits.
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

fn trim_optional(value: Option<&str>) -> Option<String> {
    let value = value.unwrap_or_default().trim();
    (!value.is_empty()).then(|| value.to_owned())
}

/// C# limits strings by UTF-16 code units (`string.Length`). Keep the same
/// boundary without ever creating an invalid unpaired surrogate in Rust.
fn trim_to_utf16(value: Option<&str>, max_units: usize) -> String {
    let value = value.unwrap_or_default().trim();
    let mut units = 0_usize;
    let mut end = 0_usize;
    for (index, character) in value.char_indices() {
        let next_units = units + character.len_utf16();
        if next_units > max_units {
            break;
        }
        units = next_units;
        end = index + character.len_utf8();
    }
    value[..end].to_owned()
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum PortalJsonError {
    PayloadTooLarge,
    Status(StatusCode),
}

impl IntoResponse for PortalJsonError {
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

fn json_payload<T>(payload: Result<Json<T>, JsonRejection>) -> Result<T, PortalJsonError> {
    match payload {
        Ok(Json(request)) => Ok(request),
        Err(rejection) => {
            if rejection.status() == StatusCode::PAYLOAD_TOO_LARGE {
                return Err(PortalJsonError::PayloadTooLarge);
            }
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(PortalJsonError::Status(status))
        }
    }
}

fn bad_request(message: &'static str) -> Response {
    (StatusCode::BAD_REQUEST, Json(json!({ "message": message }))).into_response()
}

fn not_found() -> Response {
    (
        StatusCode::NOT_FOUND,
        Json(json!({ "message": POST_NOT_FOUND })),
    )
        .into_response()
}

async fn record_audit(
    pool: &PgPool,
    username: &str,
    action: &'static str,
    entity: &'static str,
    entity_name: &str,
    details: &str,
) {
    // PostgreSQL triggers publish `portal` for app_portal_* writes and `audit`
    // for this best-effort audit row; no in-process duplicate notification.
    if let Err(error) = sqlx::query(AUDIT_SQL)
        .bind(username)
        .bind(action)
        .bind(entity)
        .bind(entity_name)
        .bind(details)
        .execute(pool)
        .await
    {
        tracing::warn!(%error, action, entity, "could not record portal audit event");
    }
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native portal database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;
    use http_body_util::BodyExt;

    fn post_request(value: serde_json::Value) -> PortalPostRequest {
        serde_json::from_value(value).unwrap()
    }

    #[test]
    fn route_contract_contains_exactly_seven_dotnet_endpoints() {
        let expected: &[(&str, &str)] = &[
            ("GET", "/api/portal/feed"),
            ("GET", "/api/portal/posts"),
            ("POST", "/api/portal/posts"),
            ("PUT", "/api/portal/posts/{id}"),
            ("DELETE", "/api/portal/posts/{id}"),
            ("GET", "/api/portal/about"),
            ("PUT", "/api/portal/about"),
        ];
        assert_eq!(ROUTE_CONTRACTS, expected);
    }

    #[tokio::test]
    async fn payload_limit_and_413_json_match_dotnet() {
        assert_eq!(MAX_JSON_BODY_BYTES, 16_777_216);
        assert_eq!(
            PAYLOAD_TOO_LARGE_MESSAGE,
            "Payload vượt giới hạn 16777216 byte."
        );

        let response = PortalJsonError::PayloadTooLarge.into_response();
        assert_eq!(response.status(), StatusCode::PAYLOAD_TOO_LARGE);
        let body = response.into_body().collect().await.unwrap().to_bytes();
        assert_eq!(
            serde_json::from_slice::<serde_json::Value>(&body).unwrap(),
            json!({ "message": "Payload vượt giới hạn 16777216 byte." })
        );
    }

    #[test]
    fn group_and_management_permissions_match_original_policy() {
        let none = BTreeSet::new();
        assert!(!may_read(&none));
        assert!(!may_manage(&none));

        let read = BTreeSet::from([permissions::PORTAL_READ.to_owned()]);
        assert!(may_read(&read));
        assert!(!may_manage(&read));

        let manage_only = BTreeSet::from([permissions::PORTAL_MANAGE.to_owned()]);
        assert!(!may_read(&manage_only));
        assert!(!may_manage(&manage_only));

        let both = BTreeSet::from([
            permissions::PORTAL_READ.to_owned(),
            permissions::PORTAL_MANAGE.to_owned(),
        ]);
        assert!(may_read(&both));
        assert!(may_manage(&both));
    }

    #[test]
    fn kind_and_post_defaults_are_wire_compatible() {
        assert_eq!(normalize_kind(None), PortalKind::News);
        assert_eq!(normalize_kind(Some(" EVENT ")), PortalKind::Event);
        assert_eq!(normalize_kind(Some("announcement")), PortalKind::News);

        let post = validate_post(post_request(json!({ "title": " Thông báo " }))).unwrap();
        assert_eq!(post.kind, PortalKind::News);
        assert_eq!(post.title, "Thông báo");
        assert!(!post.pinned);
        assert!(post.published);
        assert_eq!(post.cover_image, None);
        assert_eq!(post.event_at, None);
    }

    #[test]
    fn post_validation_locks_required_fields_and_trim_boundaries() {
        assert_eq!(
            validate_post(post_request(json!({ "title": "   " }))),
            Err(TITLE_REQUIRED)
        );
        assert_eq!(
            validate_post(post_request(json!({
                "kind": "event",
                "title": "Họp công ty"
            }))),
            Err(EVENT_TIME_REQUIRED)
        );

        let title = format!("  {}Z  ", "a".repeat(300));
        let post = validate_post(post_request(json!({
            "title": title,
            "summary": "  tóm tắt  ",
            "coverImage": "   ",
            "location": "  Hà Nội  "
        })))
        .unwrap();
        assert_eq!(post.title, "a".repeat(300));
        assert_eq!(post.summary, "tóm tắt");
        assert_eq!(post.cover_image, None);
        assert_eq!(post.location, "Hà Nội");

        assert_eq!(trim_to_utf16(Some("😀😀a"), 4), "😀😀");
        assert_eq!(trim_to_utf16(Some("😀a"), 1), "");
    }

    #[test]
    fn post_queries_preserve_visibility_upcoming_filter_and_ordering() {
        let news = build_posts_sql(PortalKind::News, true, false);
        assert!(news.contains("p.published = TRUE"));
        assert!(!news.contains("date_trunc"));
        assert!(news.contains("ORDER BY p.pinned DESC, p.created_at DESC"));

        let events = build_posts_sql(PortalKind::Event, true, true);
        assert!(events.contains("p.published = TRUE"));
        assert!(events.contains("p.event_at IS NULL"));
        assert!(events.contains("date_trunc('day', CURRENT_TIMESTAMP)"));
        assert!(events.contains("ORDER BY p.event_at ASC NULLS LAST, p.created_at DESC"));

        let admin = build_posts_sql(PortalKind::Event, false, false);
        assert!(!admin.contains("p.published = TRUE"));
        assert!(!admin.contains("date_trunc"));
        assert!(admin.ends_with(" LIMIT $2"));
        assert_eq!((FEED_LIMIT, ADMIN_LIST_LIMIT), (60, 500));
    }

    #[test]
    fn dto_omits_nulls_and_uses_the_global_dotnet_utc_format() {
        assert_eq!(
            serde_json::to_value(PortalAboutDto::empty()).unwrap(),
            json!({
                "title": "",
                "content": "",
                "address": "",
                "hotline": "",
                "email": "",
                "website": "",
                "updatedAt": "0001-01-01T00:00:00.000Z"
            })
        );

        let at = DateTime::parse_from_rfc3339("2026-08-24T03:04:05.123456Z")
            .unwrap()
            .with_timezone(&Utc);
        let dto = PortalPostDto {
            id: 7,
            kind: "news".to_owned(),
            title: "Tin".to_owned(),
            summary: String::new(),
            body: String::new(),
            cover_image: None,
            location: String::new(),
            event_at: None,
            pinned: false,
            published: true,
            author_username: "admin".to_owned(),
            author_name: "Quản trị".to_owned(),
            created_at: at,
            updated_at: at,
        };
        let value = serde_json::to_value(dto).unwrap();
        assert!(value.get("coverImage").is_none());
        assert!(value.get("eventAt").is_none());
        assert_eq!(value["createdAt"], "2026-08-24T03:04:05.123Z");
        assert_eq!(value["updatedAt"], "2026-08-24T03:04:05.123Z");
    }

    #[test]
    fn request_time_converter_matches_dotnet_utc_normalization() {
        let with_offset = validate_post(post_request(json!({
            "Kind": "event",
            "Title": "Họp",
            "EventAt": "2026-08-24T10:04:05.987654+07:00"
        })))
        .unwrap();
        assert_eq!(
            with_offset.event_at.unwrap(),
            DateTime::parse_from_rfc3339("2026-08-24T03:04:05.987654Z")
                .unwrap()
                .with_timezone(&Utc)
        );

        let unspecified = validate_post(post_request(json!({
            "kind": "event",
            "title": "Họp",
            "eventAt": "2026-08-24T03:04"
        })))
        .unwrap();
        assert_eq!(
            unspecified.event_at.unwrap(),
            DateTime::parse_from_rfc3339("2026-08-24T03:04:00Z")
                .unwrap()
                .with_timezone(&Utc)
        );

        assert_eq!(
            parse_dotnet_utc("2026-08-24T10:04+07:00").unwrap(),
            DateTime::parse_from_rfc3339("2026-08-24T03:04:00Z")
                .unwrap()
                .with_timezone(&Utc)
        );
        assert_eq!(
            parse_dotnet_utc("2026-08-24T03:04:05.1234567890123456Z").unwrap(),
            DateTime::parse_from_rfc3339("2026-08-24T03:04:05.1234567Z")
                .unwrap()
                .with_timezone(&Utc)
        );
        assert!(parse_dotnet_utc("2026-08-24T03:04:05.12345678901234567Z").is_none());
    }

    #[test]
    fn about_normalization_and_audit_literals_match_dotnet() {
        let request: PortalAboutRequest = serde_json::from_value(json!({
            "title": "  Công ty  ",
            "content": null,
            "coverImage": "  data:image/png;base64,AA==  ",
            "address": "  Hà Nội  "
        }))
        .unwrap();
        assert_eq!(
            ValidatedAbout::from(request),
            ValidatedAbout {
                title: "Công ty".to_owned(),
                content: String::new(),
                cover_image: Some("data:image/png;base64,AA==".to_owned()),
                address: "Hà Nội".to_owned(),
                hotline: String::new(),
                email: String::new(),
                website: String::new(),
            }
        );
        assert_eq!(
            (
                AUDIT_CREATE_POST,
                AUDIT_UPDATE_POST,
                AUDIT_DELETE_POST,
                AUDIT_UPDATE_ABOUT,
                POST_ENTITY,
                ABOUT_ENTITY,
            ),
            (
                "Đăng bài cổng thông tin",
                "Sửa bài cổng thông tin",
                "Xóa bài cổng thông tin",
                "Sửa giới thiệu công ty",
                "PortalPost",
                "PortalAbout",
            )
        );
    }

    #[test]
    fn long_route_constraint_matches_signed_i64_boundary() {
        assert_eq!(parse_post_id("9223372036854775807"), Some(i64::MAX));
        assert_eq!(parse_post_id("-1"), Some(-1));
        assert_eq!(parse_post_id("9223372036854775808"), None);
        assert_eq!(parse_post_id("not-a-long"), None);
    }
}
