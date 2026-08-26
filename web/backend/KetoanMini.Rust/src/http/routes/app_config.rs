//! Native vertical slice for `AppConfigEndpoints.cs`.
//!
//! The .NET application remains the sole schema owner. This module deliberately
//! contains runtime DML only: no table creation, migration, or trigger setup.
//! Mount [`router`] behind `auth::require_auth`. Every authenticated account may
//! read the configuration; the write handler additionally checks
//! `system.settings.manage` before touching the database.

use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    extract::{State, rejection::JsonRejection},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
};
use serde::{Deserialize, Serialize};
use serde_json::json;
use sqlx::{FromRow, PgConnection, PgPool};
use std::{
    collections::{BTreeSet, HashSet},
    fmt::Display,
    sync::Arc,
};

const APP_CONFIG_PATH: &str = "/api/app-config";

const READ_SQL: &str = r#"
    SELECT announcement, announcement_level, face_enroll_banner_enabled, foreground_poll_seconds,
           portrait_height_factor, portrait_vertical_nudge, portrait_aspect, portrait_min_width_factor,
           call_config::text AS call_config, feature_flags::text AS feature_flags,
           onboarding::text AS onboarding, notices::text AS notices
    FROM app_config
    WHERE id = 1
"#;

const UPDATE_SQL: &str = r#"
    UPDATE app_config SET
        announcement = COALESCE($1, announcement),
        announcement_level = COALESCE($2, announcement_level),
        face_enroll_banner_enabled = COALESCE($3, face_enroll_banner_enabled),
        foreground_poll_seconds = COALESCE($4, foreground_poll_seconds),
        portrait_height_factor = COALESCE($5, portrait_height_factor),
        portrait_vertical_nudge = COALESCE($6, portrait_vertical_nudge),
        portrait_aspect = COALESCE($7, portrait_aspect),
        portrait_min_width_factor = COALESCE($8, portrait_min_width_factor),
        call_config = COALESCE($9::jsonb, call_config),
        feature_flags = COALESCE($10::jsonb, feature_flags),
        onboarding = COALESCE($11::jsonb, onboarding),
        notices = COALESCE($12::jsonb, notices),
        updated_at = CURRENT_TIMESTAMP,
        updated_by = $13
    WHERE id = 1
"#;

const AUDIT_SQL: &str = r#"
    INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
    VALUES (CURRENT_TIMESTAMP, $1, $2, $3, $4, $5)
"#;

const AUDIT_ACTION: &str = "Sửa cấu hình ứng dụng";
const AUDIT_ENTITY: &str = "AppConfig";
const AUDIT_ENTITY_NAME: &str = "app";
const AUDIT_DETAILS: &str = "Cập nhật remote config.";
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";

const DEFAULT_STUN: [&str; 2] = [
    "stun:stun.l.google.com:19302",
    "stun:stun1.l.google.com:19302",
];

#[cfg(test)]
const ROUTE_CONTRACTS: &[(&str, &str, Option<&str>)] = &[
    ("GET", APP_CONFIG_PATH, None),
    (
        "PUT",
        APP_CONFIG_PATH,
        Some(permissions::SYSTEM_SETTINGS_MANAGE),
    ),
];

/// Both methods belong behind `auth::require_auth`; PUT performs a second,
/// endpoint-local permission check to preserve the .NET policy boundary.
pub fn router() -> Router<Arc<AppState>> {
    Router::new().route(APP_CONFIG_PATH, get(get_app_config).put(put_app_config))
}

#[derive(Debug, FromRow)]
struct AppConfigRow {
    announcement: Option<String>,
    announcement_level: Option<String>,
    face_enroll_banner_enabled: Option<bool>,
    foreground_poll_seconds: Option<i32>,
    portrait_height_factor: f64,
    portrait_vertical_nudge: f64,
    portrait_aspect: f64,
    portrait_min_width_factor: f64,
    call_config: Option<String>,
    feature_flags: Option<String>,
    onboarding: Option<String>,
    notices: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct AppConfigDto {
    announcement: String,
    announcement_level: String,
    face_enroll_banner_enabled: bool,
    foreground_poll_seconds: i32,
    portrait_height_factor: f64,
    portrait_vertical_nudge: f64,
    portrait_aspect: f64,
    portrait_min_width_factor: f64,
    call: CallConfigDto,
    features: FeatureFlagsDto,
    onboarding: OnboardingDto,
    notices: Vec<String>,
}

impl Default for AppConfigDto {
    fn default() -> Self {
        Self {
            announcement: String::new(),
            announcement_level: "info".to_owned(),
            face_enroll_banner_enabled: true,
            foreground_poll_seconds: 20,
            portrait_height_factor: 1.85,
            portrait_vertical_nudge: 0.15,
            portrait_aspect: 0.75,
            portrait_min_width_factor: 1.35,
            call: with_default_stun(CallConfigDto::default()),
            features: FeatureFlagsDto::default(),
            onboarding: OnboardingDto::default(),
            notices: Vec::new(),
        }
    }
}

impl TryFrom<AppConfigRow> for AppConfigDto {
    type Error = ConfigReadError;

    fn try_from(row: AppConfigRow) -> Result<Self, Self::Error> {
        Ok(Self {
            announcement: row.announcement.unwrap_or_default(),
            announcement_level: row.announcement_level.unwrap_or_default(),
            face_enroll_banner_enabled: row.face_enroll_banner_enabled.unwrap_or(false),
            foreground_poll_seconds: row.foreground_poll_seconds.unwrap_or_default(),
            portrait_height_factor: row.portrait_height_factor,
            portrait_vertical_nudge: row.portrait_vertical_nudge,
            portrait_aspect: row.portrait_aspect,
            portrait_min_width_factor: row.portrait_min_width_factor,
            call: parse_call(row.call_config.as_deref()).map_err(|source| {
                ConfigReadError::Json {
                    section: "call_config",
                    source,
                }
            })?,
            features: parse_features(row.feature_flags.as_deref()).map_err(|source| {
                ConfigReadError::Json {
                    section: "feature_flags",
                    source,
                }
            })?,
            onboarding: parse_onboarding(row.onboarding.as_deref()).map_err(|source| {
                ConfigReadError::Json {
                    section: "onboarding",
                    source,
                }
            })?,
            notices: parse_notices(row.notices.as_deref()).map_err(|source| {
                ConfigReadError::Json {
                    section: "notices",
                    source,
                }
            })?,
        })
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AppConfigPatch {
    #[serde(alias = "Announcement")]
    announcement: Option<String>,
    #[serde(alias = "AnnouncementLevel")]
    announcement_level: Option<String>,
    #[serde(alias = "FaceEnrollBannerEnabled")]
    face_enroll_banner_enabled: Option<bool>,
    #[serde(alias = "ForegroundPollSeconds")]
    foreground_poll_seconds: Option<i32>,
    #[serde(alias = "PortraitHeightFactor")]
    portrait_height_factor: Option<f64>,
    #[serde(alias = "PortraitVerticalNudge")]
    portrait_vertical_nudge: Option<f64>,
    #[serde(alias = "PortraitAspect")]
    portrait_aspect: Option<f64>,
    #[serde(alias = "PortraitMinWidthFactor")]
    portrait_min_width_factor: Option<f64>,
    #[serde(alias = "Call")]
    call: Option<CallConfigDto>,
    #[serde(alias = "Features")]
    features: Option<FeatureFlagsDto>,
    #[serde(alias = "Onboarding")]
    onboarding: Option<OnboardingDto>,
    #[serde(alias = "Notices")]
    notices: Option<Vec<Option<String>>>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct CallConfigDto {
    #[serde(alias = "CallsEnabled")]
    calls_enabled: bool,
    #[serde(alias = "VideoCallEnabled")]
    video_call_enabled: bool,
    #[serde(alias = "StunServers", skip_serializing_if = "Option::is_none")]
    stun_servers: Option<Vec<Option<String>>>,
    #[serde(alias = "ForceRelay")]
    force_relay: bool,
    #[serde(alias = "OutgoingTimeoutSeconds")]
    outgoing_timeout_seconds: i32,
    #[serde(alias = "IncomingTimeoutSeconds")]
    incoming_timeout_seconds: i32,
    #[serde(alias = "VideoWidth")]
    video_width: i32,
    #[serde(alias = "VideoHeight")]
    video_height: i32,
    #[serde(alias = "VideoFps")]
    video_fps: i32,
    #[serde(alias = "VideoMaxBitrateKbps")]
    video_max_bitrate_kbps: i32,
    #[serde(alias = "AudioMaxBitrateKbps")]
    audio_max_bitrate_kbps: i32,
}

impl Default for CallConfigDto {
    fn default() -> Self {
        Self {
            calls_enabled: true,
            video_call_enabled: true,
            stun_servers: None,
            force_relay: false,
            outgoing_timeout_seconds: 30,
            incoming_timeout_seconds: 45,
            video_width: 1280,
            video_height: 720,
            video_fps: 30,
            video_max_bitrate_kbps: 0,
            audio_max_bitrate_kbps: 0,
        }
    }
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct FeatureFlagsDto {
    #[serde(alias = "LocationEnabled")]
    location_enabled: bool,
    #[serde(alias = "OfflineAttendanceEnabled")]
    offline_attendance_enabled: bool,
    #[serde(alias = "BiometricAttendanceEnabled")]
    biometric_attendance_enabled: bool,
    #[serde(alias = "ChatFileTransferEnabled")]
    chat_file_transfer_enabled: bool,
    #[serde(alias = "CompanyPortalEnabled")]
    company_portal_enabled: bool,
}

impl Default for FeatureFlagsDto {
    fn default() -> Self {
        Self {
            location_enabled: true,
            offline_attendance_enabled: true,
            biometric_attendance_enabled: true,
            chat_file_transfer_enabled: true,
            company_portal_enabled: true,
        }
    }
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct OnboardingDto {
    #[serde(alias = "CameraReason", skip_serializing_if = "Option::is_none")]
    camera_reason: Option<String>,
    #[serde(alias = "LocationReason", skip_serializing_if = "Option::is_none")]
    location_reason: Option<String>,
    #[serde(alias = "NotificationReason", skip_serializing_if = "Option::is_none")]
    notification_reason: Option<String>,
    #[serde(alias = "MicrophoneReason", skip_serializing_if = "Option::is_none")]
    microphone_reason: Option<String>,
    #[serde(alias = "IntroText", skip_serializing_if = "Option::is_none")]
    intro_text: Option<String>,
}

impl Default for OnboardingDto {
    fn default() -> Self {
        Self {
            camera_reason: Some(String::new()),
            location_reason: Some(String::new()),
            notification_reason: Some(String::new()),
            microphone_reason: Some(String::new()),
            intro_text: Some(String::new()),
        }
    }
}

/// Internal database representation. Plain JsonSerializer.Serialize in .NET
/// writes record property names in PascalCase, even though HTTP JSON is camelCase.
#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct DbCallConfig<'a> {
    calls_enabled: bool,
    video_call_enabled: bool,
    stun_servers: &'a Option<Vec<Option<String>>>,
    force_relay: bool,
    outgoing_timeout_seconds: i32,
    incoming_timeout_seconds: i32,
    video_width: i32,
    video_height: i32,
    video_fps: i32,
    video_max_bitrate_kbps: i32,
    audio_max_bitrate_kbps: i32,
}

impl<'a> From<&'a CallConfigDto> for DbCallConfig<'a> {
    fn from(value: &'a CallConfigDto) -> Self {
        Self {
            calls_enabled: value.calls_enabled,
            video_call_enabled: value.video_call_enabled,
            stun_servers: &value.stun_servers,
            force_relay: value.force_relay,
            outgoing_timeout_seconds: value.outgoing_timeout_seconds,
            incoming_timeout_seconds: value.incoming_timeout_seconds,
            video_width: value.video_width,
            video_height: value.video_height,
            video_fps: value.video_fps,
            video_max_bitrate_kbps: value.video_max_bitrate_kbps,
            audio_max_bitrate_kbps: value.audio_max_bitrate_kbps,
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct DbFeatureFlags {
    location_enabled: bool,
    offline_attendance_enabled: bool,
    biometric_attendance_enabled: bool,
    chat_file_transfer_enabled: bool,
    company_portal_enabled: bool,
}

impl From<&FeatureFlagsDto> for DbFeatureFlags {
    fn from(value: &FeatureFlagsDto) -> Self {
        Self {
            location_enabled: value.location_enabled,
            offline_attendance_enabled: value.offline_attendance_enabled,
            biometric_attendance_enabled: value.biometric_attendance_enabled,
            chat_file_transfer_enabled: value.chat_file_transfer_enabled,
            company_portal_enabled: value.company_portal_enabled,
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct DbOnboarding<'a> {
    camera_reason: &'a Option<String>,
    location_reason: &'a Option<String>,
    notification_reason: &'a Option<String>,
    microphone_reason: &'a Option<String>,
    intro_text: &'a Option<String>,
}

impl<'a> From<&'a OnboardingDto> for DbOnboarding<'a> {
    fn from(value: &'a OnboardingDto) -> Self {
        Self {
            camera_reason: &value.camera_reason,
            location_reason: &value.location_reason,
            notification_reason: &value.notification_reason,
            microphone_reason: &value.microphone_reason,
            intro_text: &value.intro_text,
        }
    }
}

#[derive(Debug)]
struct NormalizedPatch {
    announcement: Option<String>,
    announcement_level: Option<String>,
    face_enroll_banner_enabled: Option<bool>,
    foreground_poll_seconds: Option<i32>,
    portrait_height_factor: Option<f64>,
    portrait_vertical_nudge: Option<f64>,
    portrait_aspect: Option<f64>,
    portrait_min_width_factor: Option<f64>,
    call_json: Option<String>,
    features_json: Option<String>,
    onboarding_json: Option<String>,
    notices_json: Option<String>,
}

impl TryFrom<AppConfigPatch> for NormalizedPatch {
    type Error = serde_json::Error;

    fn try_from(value: AppConfigPatch) -> Result<Self, Self::Error> {
        let call_json = value
            .call
            .map(clamp_call)
            .map(|call| serde_json::to_string(&DbCallConfig::from(&call)))
            .transpose()?;
        let features_json = value
            .features
            .map(|features| serde_json::to_string(&DbFeatureFlags::from(&features)))
            .transpose()?;
        let onboarding_json = value
            .onboarding
            .map(clamp_onboarding)
            .map(|onboarding| serde_json::to_string(&DbOnboarding::from(&onboarding)))
            .transpose()?;
        let notices_json = value
            .notices
            .map(clean_notices)
            .map(|notices| serde_json::to_string(&notices))
            .transpose()?;

        Ok(Self {
            announcement: value.announcement,
            announcement_level: normalize_level(value.announcement_level.as_deref()),
            face_enroll_banner_enabled: value.face_enroll_banner_enabled,
            foreground_poll_seconds: value
                .foreground_poll_seconds
                .map(|value| value.clamp(5, 3600)),
            portrait_height_factor: value
                .portrait_height_factor
                .map(|value| value.clamp(1.0, 4.0)),
            portrait_vertical_nudge: value
                .portrait_vertical_nudge
                .map(|value| value.clamp(-1.0, 1.0)),
            portrait_aspect: value.portrait_aspect.map(|value| value.clamp(0.4, 1.0)),
            portrait_min_width_factor: value
                .portrait_min_width_factor
                .map(|value| value.clamp(0.5, 3.0)),
            call_json,
            features_json,
            onboarding_json,
            notices_json,
        })
    }
}

#[derive(Debug, thiserror::Error)]
enum ConfigReadError {
    #[error("database query failed: {0}")]
    Database(#[from] sqlx::Error),
    #[error("stored {section} JSON is incompatible: {source}")]
    Json {
        section: &'static str,
        #[source]
        source: serde_json::Error,
    },
}

async fn get_app_config(
    State(state): State<Arc<AppState>>,
    Extension(_auth): Extension<AuthContext>,
) -> Response {
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for app config", error),
    };

    match read_config(&mut connection).await {
        Ok(config) => Json(config).into_response(),
        Err(error) => database_failure("read app config", error),
    }
}

async fn put_app_config(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<AppConfigPatch>, JsonRejection>,
) -> Response {
    if !may_manage(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    let patch = match json_payload(payload) {
        Ok(patch) => patch,
        Err(status) => return status.into_response(),
    };

    // Match .NET's ordering: model binding first, then open one connection that
    // remains in use for both UPDATE and the response read-back.
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection to update app config", error),
    };
    let patch = match NormalizedPatch::try_from(patch) {
        Ok(patch) => patch,
        Err(error) => {
            tracing::error!(%error, "could not serialize normalized app config patch");
            return StatusCode::INTERNAL_SERVER_ERROR.into_response();
        }
    };

    if let Err(error) = sqlx::query(UPDATE_SQL)
        .bind(patch.announcement.as_deref())
        .bind(patch.announcement_level.as_deref())
        .bind(patch.face_enroll_banner_enabled)
        .bind(patch.foreground_poll_seconds)
        .bind(patch.portrait_height_factor)
        .bind(patch.portrait_vertical_nudge)
        .bind(patch.portrait_aspect)
        .bind(patch.portrait_min_width_factor)
        .bind(patch.call_json.as_deref())
        .bind(patch.features_json.as_deref())
        .bind(patch.onboarding_json.as_deref())
        .bind(patch.notices_json.as_deref())
        .bind(&auth.username)
        .execute(&mut *connection)
        .await
    {
        return database_failure("update app config", error);
    }

    // The existing statement trigger on app_config publishes realtime scope
    // `config`; emitting another notification here would duplicate .NET behavior.
    // RecordAudit opens a separate pooled connection and is deliberately outside
    // a transaction/best-effort, exactly like ApiHelpers.RecordAudit.
    record_audit(&state.pool, &auth.username).await;

    match read_config(&mut connection).await {
        Ok(config) => Json(config).into_response(),
        Err(error) => database_failure("reload app config after update", error),
    }
}

async fn read_config(connection: &mut PgConnection) -> Result<AppConfigDto, ConfigReadError> {
    let row = sqlx::query_as::<_, AppConfigRow>(READ_SQL)
        .fetch_optional(connection)
        .await?;
    row.map(AppConfigDto::try_from)
        .transpose()
        .map(|config| config.unwrap_or_default())
}

async fn record_audit(pool: &PgPool, username: &str) {
    // The existing audit_logs trigger publishes realtime scope `audit`.
    if let Err(error) = sqlx::query(AUDIT_SQL)
        .bind(username)
        .bind(AUDIT_ACTION)
        .bind(AUDIT_ENTITY)
        .bind(AUDIT_ENTITY_NAME)
        .bind(AUDIT_DETAILS)
        .execute(pool)
        .await
    {
        tracing::warn!(%error, "could not record app config audit event");
    }
}

fn may_manage(permission_set: &BTreeSet<String>) -> bool {
    permission_set.contains(permissions::SYSTEM_SETTINGS_MANAGE)
}

fn json_payload(
    payload: Result<Json<AppConfigPatch>, JsonRejection>,
) -> Result<AppConfigPatch, StatusCode> {
    match payload {
        Ok(Json(patch)) => Ok(patch),
        Err(rejection) => {
            // ASP.NET model binding reports valid JSON with an incompatible DTO
            // field as 400; Axum's JsonDataError normally reports 422.
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(status)
        }
    }
}

fn database_failure(operation: &'static str, error: impl Display) -> Response {
    tracing::warn!(%error, operation, "native app config operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

fn normalize_level(level: Option<&str>) -> Option<String> {
    match level?.trim().to_lowercase().as_str() {
        "info" => Some("info".to_owned()),
        "warning" => Some("warning".to_owned()),
        "critical" => Some("critical".to_owned()),
        _ => None,
    }
}

fn default_stun_servers() -> Vec<Option<String>> {
    DEFAULT_STUN
        .into_iter()
        .map(|value| Some(value.to_owned()))
        .collect()
}

fn with_default_stun(mut call: CallConfigDto) -> CallConfigDto {
    if call.stun_servers.as_ref().is_none_or(Vec::is_empty) {
        call.stun_servers = Some(default_stun_servers());
    }
    call
}

fn clamp_call(mut call: CallConfigDto) -> CallConfigDto {
    call.outgoing_timeout_seconds = call.outgoing_timeout_seconds.clamp(10, 120);
    call.incoming_timeout_seconds = call.incoming_timeout_seconds.clamp(10, 120);
    call.video_width = call.video_width.clamp(160, 1920);
    call.video_height = call.video_height.clamp(120, 1080);
    call.video_fps = call.video_fps.clamp(1, 30);
    call.video_max_bitrate_kbps = call.video_max_bitrate_kbps.clamp(0, 8000);
    call.audio_max_bitrate_kbps = call.audio_max_bitrate_kbps.clamp(0, 512);
    with_default_stun(call)
}

fn cap_onboarding(value: Option<&str>) -> String {
    match value {
        None | Some("") => String::new(),
        Some(value) => truncate_utf16(value, 2_000),
    }
}

fn clamp_onboarding(onboarding: OnboardingDto) -> OnboardingDto {
    OnboardingDto {
        camera_reason: Some(cap_onboarding(onboarding.camera_reason.as_deref())),
        location_reason: Some(cap_onboarding(onboarding.location_reason.as_deref())),
        notification_reason: Some(cap_onboarding(onboarding.notification_reason.as_deref())),
        microphone_reason: Some(cap_onboarding(onboarding.microphone_reason.as_deref())),
        intro_text: Some(cap_onboarding(onboarding.intro_text.as_deref())),
    }
}

fn clean_notices(notices: Vec<Option<String>>) -> Vec<String> {
    let mut seen = HashSet::new();
    let mut cleaned = Vec::with_capacity(notices.len().min(20));

    for notice in notices {
        let notice = notice.unwrap_or_default();
        let notice = notice.trim();
        if notice.is_empty() {
            continue;
        }
        let notice = truncate_utf16(notice, 300);
        if seen.insert(notice.clone()) {
            cleaned.push(notice);
            if cleaned.len() == 20 {
                break;
            }
        }
    }
    cleaned
}

/// C# String.Length/range limits count UTF-16 code units rather than Unicode
/// scalar values. Preserve that boundary, including replacement of a cut
/// surrogate, instead of using Rust's byte or char count.
fn truncate_utf16(value: &str, maximum_units: usize) -> String {
    let mut units = value.encode_utf16();
    let prefix: Vec<u16> = units.by_ref().take(maximum_units).collect();
    if units.next().is_none() {
        value.to_owned()
    } else {
        String::from_utf16_lossy(&prefix)
    }
}

fn parse_call(json: Option<&str>) -> Result<CallConfigDto, serde_json::Error> {
    let Some(json) = meaningful_json(json, "{}") else {
        return Ok(with_default_stun(CallConfigDto::default()));
    };
    let call = serde_json::from_str::<Option<CallConfigDto>>(json)?.unwrap_or_default();
    Ok(with_default_stun(call))
}

fn parse_features(json: Option<&str>) -> Result<FeatureFlagsDto, serde_json::Error> {
    let Some(json) = meaningful_json(json, "{}") else {
        return Ok(FeatureFlagsDto::default());
    };
    Ok(serde_json::from_str::<Option<FeatureFlagsDto>>(json)?.unwrap_or_default())
}

fn parse_onboarding(json: Option<&str>) -> Result<OnboardingDto, serde_json::Error> {
    let Some(json) = meaningful_json(json, "{}") else {
        return Ok(OnboardingDto::default());
    };
    Ok(serde_json::from_str::<Option<OnboardingDto>>(json)?.unwrap_or_default())
}

fn parse_notices(json: Option<&str>) -> Result<Vec<String>, serde_json::Error> {
    let Some(json) = meaningful_json(json, "[]") else {
        return Ok(Vec::new());
    };
    let notices = serde_json::from_str::<Option<Vec<Option<String>>>>(json)?.unwrap_or_default();
    Ok(clean_notices(notices))
}

fn meaningful_json<'a>(json: Option<&'a str>, empty_value: &str) -> Option<&'a str> {
    let json = json?.trim();
    (!json.is_empty() && json != empty_value).then_some(json)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::{Value, json};

    #[test]
    fn route_and_permission_boundaries_are_explicit() {
        assert_eq!(
            ROUTE_CONTRACTS,
            &[
                ("GET", "/api/app-config", None),
                ("PUT", "/api/app-config", Some("system.settings.manage"))
            ]
        );

        assert!(!may_manage(&BTreeSet::new()));
        assert!(may_manage(&BTreeSet::from([
            permissions::SYSTEM_SETTINGS_MANAGE.to_owned()
        ])));
        assert!(!may_manage(&BTreeSet::from([
            permissions::PORTAL_MANAGE.to_owned()
        ])));
    }

    #[test]
    fn default_response_matches_the_camel_case_dotnet_contract() {
        let value = serde_json::to_value(AppConfigDto::default()).unwrap();
        assert_eq!(
            value,
            json!({
                "announcement": "",
                "announcementLevel": "info",
                "faceEnrollBannerEnabled": true,
                "foregroundPollSeconds": 20,
                "portraitHeightFactor": 1.85,
                "portraitVerticalNudge": 0.15,
                "portraitAspect": 0.75,
                "portraitMinWidthFactor": 1.35,
                "call": {
                    "callsEnabled": true,
                    "videoCallEnabled": true,
                    "stunServers": DEFAULT_STUN,
                    "forceRelay": false,
                    "outgoingTimeoutSeconds": 30,
                    "incomingTimeoutSeconds": 45,
                    "videoWidth": 1280,
                    "videoHeight": 720,
                    "videoFps": 30,
                    "videoMaxBitrateKbps": 0,
                    "audioMaxBitrateKbps": 0
                },
                "features": {
                    "locationEnabled": true,
                    "offlineAttendanceEnabled": true,
                    "biometricAttendanceEnabled": true,
                    "chatFileTransferEnabled": true,
                    "companyPortalEnabled": true
                },
                "onboarding": {
                    "cameraReason": "",
                    "locationReason": "",
                    "notificationReason": "",
                    "microphoneReason": "",
                    "introText": ""
                },
                "notices": []
            })
        );
    }

    #[test]
    fn partial_nested_patch_uses_defaults_clamps_and_pascal_case_db_json() {
        let patch: AppConfigPatch = serde_json::from_value(json!({
            "announcementLevel": " WARNING ",
            "foregroundPollSeconds": 1,
            "portraitVerticalNudge": 8.0,
            "call": {
                "outgoingTimeoutSeconds": 999,
                "videoWidth": 10,
                "stunServers": []
            },
            "features": { "locationEnabled": false },
            "onboarding": { "cameraReason": null }
        }))
        .unwrap();
        let patch = NormalizedPatch::try_from(patch).unwrap();

        assert_eq!(patch.announcement_level.as_deref(), Some("warning"));
        assert_eq!(patch.foreground_poll_seconds, Some(5));
        assert_eq!(patch.portrait_vertical_nudge, Some(1.0));

        let call: Value = serde_json::from_str(patch.call_json.as_deref().unwrap()).unwrap();
        assert_eq!(call["OutgoingTimeoutSeconds"], 120);
        assert_eq!(call["VideoWidth"], 160);
        assert_eq!(call["StunServers"], json!(DEFAULT_STUN));
        assert!(call.get("outgoingTimeoutSeconds").is_none());

        let features: Value =
            serde_json::from_str(patch.features_json.as_deref().unwrap()).unwrap();
        assert_eq!(features["LocationEnabled"], false);
        assert_eq!(features["BiometricAttendanceEnabled"], true);

        let onboarding: Value =
            serde_json::from_str(patch.onboarding_json.as_deref().unwrap()).unwrap();
        assert_eq!(onboarding["CameraReason"], "");
        assert_eq!(onboarding["LocationReason"], "");
    }

    #[test]
    fn notices_follow_trim_utf16_deduplication_and_count_limits() {
        let long = format!("{}tail", "a".repeat(300));
        let mut input = vec![
            Some("  Nhớ nộp báo cáo tuần  ".to_owned()),
            None,
            Some("   ".to_owned()),
            Some("Nhớ nộp báo cáo tuần".to_owned()),
            Some(long),
            Some("a".repeat(300)),
        ];
        input.extend((0..30).map(|index| Some(format!("notice-{index}"))));

        let result = clean_notices(input);
        assert_eq!(result.len(), 20);
        assert_eq!(result[0], "Nhớ nộp báo cáo tuần");
        assert_eq!(result[1], "a".repeat(300));
        assert_eq!(result[2], "notice-0");
    }

    #[test]
    fn stored_pascal_case_json_and_empty_blocks_keep_legacy_defaults() {
        let call = parse_call(Some(r#"{"CallsEnabled":false,"VideoFps":9}"#)).unwrap();
        assert!(!call.calls_enabled);
        assert_eq!(call.video_fps, 9);
        assert_eq!(call.stun_servers, Some(default_stun_servers()));

        let features = parse_features(Some(r#"{"locationEnabled":false}"#)).unwrap();
        assert!(!features.location_enabled);
        assert!(features.offline_attendance_enabled);

        assert_eq!(
            parse_onboarding(Some(" {} ")).unwrap(),
            OnboardingDto::default()
        );
        assert!(parse_notices(Some(" null ")).unwrap().is_empty());
    }

    #[test]
    fn runtime_sql_is_dml_only_and_keeps_all_thirteen_binds() {
        let update = UPDATE_SQL.to_ascii_uppercase();
        assert!(update.contains("UPDATE APP_CONFIG"));
        assert!(update.contains("$13"));
        assert!(!update.contains("CREATE "));
        assert!(!update.contains("ALTER "));
        assert!(!update.contains("DROP "));
    }
}
