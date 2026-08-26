use crate::{auth::AuthContext, state::AppState};
use axum::{
    Extension, Router,
    body::Body,
    extract::{Query, State},
    http::{StatusCode, header},
    response::{IntoResponse, Response},
    routing::get,
};
use chrono::{DateTime, Days, Duration, NaiveDate, NaiveDateTime, NaiveTime, Utc};
use serde::Deserialize;
use sqlx::FromRow;
use std::sync::Arc;
use uuid::Uuid;

const CALENDAR_CONTENT_TYPE: &str = "text/calendar; charset=utf-8";
const CALENDAR_CONTENT_DISPOSITION: &str =
    "attachment; filename=LichLamViec.ics; filename*=UTF-8''LichLamViec.ics";
const VIETNAM_UTC_OFFSET_HOURS: i64 = 7;

const SHIFT_QUERY: &str = r#"
    SELECT a.id, a.work_date, s.name, s.start_time, s.end_time, s.is_overnight
    FROM hr_shift_assignments a
    JOIN hr_shifts s ON s.id = a.shift_id
    JOIN hr_employees e ON e.id = a.employee_id
    WHERE e.username = $1 AND a.work_date BETWEEN $2 AND $3
    ORDER BY a.work_date
"#;

/// The caller must place this router behind `auth::require_auth`; requiring `AuthContext` keeps
/// ownership tied to the authenticated identity rather than any request parameter.
pub fn router() -> Router<Arc<AppState>> {
    Router::new().route("/api/schedule/ical", get(export_ical))
}

#[derive(Debug, Default, Deserialize)]
struct ScheduleQuery {
    from: Option<String>,
    to: Option<String>,
}

#[derive(Debug, FromRow)]
struct ShiftRow {
    id: Uuid,
    work_date: NaiveDate,
    name: String,
    start_time: NaiveTime,
    end_time: NaiveTime,
    is_overnight: bool,
}

async fn export_ical(
    Extension(auth): Extension<AuthContext>,
    State(state): State<Arc<AppState>>,
    Query(query): Query<ScheduleQuery>,
) -> Response {
    let Some((start, end)) = resolve_range(&query, Utc::now().date_naive()) else {
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    };

    // Match the .NET order: establish the DB connection, capture one shared DTSTAMP, then execute
    // the assignment query. All events in one response therefore have the same stamp.
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for schedule export", error),
    };
    let stamp = Utc::now();
    let rows = match sqlx::query_as::<_, ShiftRow>(SHIFT_QUERY)
        .bind(&auth.username)
        .bind(start)
        .bind(end)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read personal shift assignments", error),
    };

    let Some(calendar) = render_calendar(&rows, stamp) else {
        tracing::error!("could not render an out-of-range schedule timestamp");
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    };
    calendar_response(calendar)
}

fn resolve_range(query: &ScheduleQuery, today_utc: NaiveDate) -> Option<(NaiveDate, NaiveDate)> {
    let start = query
        .from
        .as_deref()
        .and_then(parse_date)
        .unwrap_or(today_utc);
    let end = match query.to.as_deref().and_then(parse_date) {
        Some(end) => end,
        None => start.checked_add_days(Days::new(60))?,
    };
    Some((start, end))
}

fn parse_date(raw: &str) -> Option<NaiveDate> {
    NaiveDate::parse_from_str(raw.trim(), "%Y-%m-%d").ok()
}

fn render_calendar(rows: &[ShiftRow], stamp: DateTime<Utc>) -> Option<String> {
    let mut calendar = String::from(
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//KetoanMini//Lich lam viec//VI\r\nCALSCALE:GREGORIAN\r\n",
    );
    let stamp = stamp.format("%Y%m%dT%H%M%SZ").to_string();

    for row in rows {
        let start_utc = local_to_utc(row.work_date.and_time(row.start_time))?;
        let end_day = if row.is_overnight {
            row.work_date.checked_add_days(Days::new(1))?
        } else {
            row.work_date
        };
        let end_utc = local_to_utc(end_day.and_time(row.end_time))?;
        let name = if row.name.trim().is_empty() {
            "Ca làm việc"
        } else {
            &row.name
        };

        calendar.push_str("BEGIN:VEVENT\r\n");
        calendar.push_str("UID:shift-");
        calendar.push_str(&row.id.to_string());
        calendar.push_str("@ketoanmini\r\nDTSTAMP:");
        calendar.push_str(&stamp);
        calendar.push_str("\r\nDTSTART:");
        calendar.push_str(&format_ical_utc(start_utc));
        calendar.push_str("\r\nDTEND:");
        calendar.push_str(&format_ical_utc(end_utc));
        calendar.push_str("\r\nSUMMARY:");
        calendar.push_str(&escape_ical_text(name));
        calendar.push_str("\r\nEND:VEVENT\r\n");
    }

    calendar.push_str("END:VCALENDAR\r\n");
    Some(calendar)
}

fn local_to_utc(local_naive: NaiveDateTime) -> Option<NaiveDateTime> {
    // Vietnam/Asia-Bangkok has no daylight-saving transition in the supported application era;
    // this is the same UTC+07 conversion used by AttendancePolicy.LocalToUtc.
    local_naive.checked_sub_signed(Duration::hours(VIETNAM_UTC_OFFSET_HOURS))
}

fn format_ical_utc(value: NaiveDateTime) -> String {
    value.format("%Y%m%dT%H%M%SZ").to_string()
}

/// Escape RFC 5545 text in the exact order used by the existing C# endpoint.
fn escape_ical_text(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace(';', "\\;")
        .replace(',', "\\,")
        .replace("\r\n", "\\n")
        .replace('\n', "\\n")
}

fn calendar_response(calendar: String) -> Response {
    let bytes = calendar.into_bytes();
    let content_length = bytes.len().to_string();
    Response::builder()
        .status(StatusCode::OK)
        .header(header::CONTENT_TYPE, CALENDAR_CONTENT_TYPE)
        .header(header::CONTENT_DISPOSITION, CALENDAR_CONTENT_DISPOSITION)
        .header(header::CONTENT_LENGTH, content_length)
        .body(Body::from(bytes))
        .expect("static iCalendar response headers must be valid")
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::error!(%error, operation, "schedule database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        axum::Json(serde_json::json!({
            "message": "Khong ket noi duoc co so du lieu PostgreSQL."
        })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn date(year: i32, month: u32, day: u32) -> NaiveDate {
        NaiveDate::from_ymd_opt(year, month, day).unwrap()
    }

    fn time(hour: u32, minute: u32) -> NaiveTime {
        NaiveTime::from_hms_opt(hour, minute, 0).unwrap()
    }

    #[test]
    fn escaping_matches_the_legacy_rfc5545_order() {
        assert_eq!(
            escape_ical_text("A\\B;C,D\r\nE\nF\rG"),
            "A\\\\B\\;C\\,D\\nE\\nF\rG"
        );
    }

    #[test]
    fn local_shift_times_are_rendered_as_vietnam_to_utc() {
        let work_date = date(2026, 8, 15);
        assert_eq!(
            format_ical_utc(local_to_utc(work_date.and_time(time(8, 0))).unwrap()),
            "20260815T010000Z"
        );

        let next_day = work_date.checked_add_days(Days::new(1)).unwrap();
        assert_eq!(
            format_ical_utc(local_to_utc(next_day.and_time(time(6, 0))).unwrap()),
            "20260815T230000Z"
        );
    }

    #[test]
    fn calendar_body_is_utf8_crlf_and_byte_compatible() {
        let row = ShiftRow {
            id: Uuid::parse_str("00112233-4455-6677-8899-aabbccddeeff").unwrap(),
            work_date: date(2026, 8, 15),
            name: "Ca Sáng; A,B".to_owned(),
            start_time: time(8, 0),
            end_time: time(17, 0),
            is_overnight: false,
        };
        let stamp = DateTime::parse_from_rfc3339("2026-08-01T02:03:04Z")
            .unwrap()
            .with_timezone(&Utc);

        let rendered = render_calendar(&[row], stamp).unwrap();
        assert_eq!(
            rendered.as_bytes(),
            concat!(
                "BEGIN:VCALENDAR\r\n",
                "VERSION:2.0\r\n",
                "PRODID:-//KetoanMini//Lich lam viec//VI\r\n",
                "CALSCALE:GREGORIAN\r\n",
                "BEGIN:VEVENT\r\n",
                "UID:shift-00112233-4455-6677-8899-aabbccddeeff@ketoanmini\r\n",
                "DTSTAMP:20260801T020304Z\r\n",
                "DTSTART:20260815T010000Z\r\n",
                "DTEND:20260815T100000Z\r\n",
                "SUMMARY:Ca Sáng\\; A\\,B\r\n",
                "END:VEVENT\r\n",
                "END:VCALENDAR\r\n"
            )
            .as_bytes()
        );
        assert!(!rendered.as_bytes().starts_with(&[0xef, 0xbb, 0xbf]));
        assert!(!rendered.replace("\r\n", "").contains('\n'));
    }

    #[test]
    fn query_defaults_match_the_sixty_day_utc_contract() {
        let today = date(2026, 8, 24);
        let query = ScheduleQuery::default();
        assert_eq!(
            resolve_range(&query, today),
            Some((today, date(2026, 10, 23)))
        );

        let query = ScheduleQuery {
            from: Some("2026-08-01".to_owned()),
            to: Some("invalid".to_owned()),
        };
        assert_eq!(
            resolve_range(&query, today),
            Some((date(2026, 8, 1), date(2026, 9, 30)))
        );
    }

    #[test]
    fn response_headers_match_results_file_contract() {
        let response = calendar_response("x".to_owned());
        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(
            response.headers()[header::CONTENT_TYPE],
            CALENDAR_CONTENT_TYPE
        );
        assert_eq!(
            response.headers()[header::CONTENT_DISPOSITION],
            CALENDAR_CONTENT_DISPOSITION
        );
        assert_eq!(response.headers()[header::CONTENT_LENGTH], "1");
    }
}
