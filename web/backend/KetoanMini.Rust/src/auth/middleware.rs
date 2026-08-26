use super::{
    AUTH_COOKIE, AuthService, CSRF_COOKIE, CSRF_HEADER, TokenIdentity, TokenSource, permissions,
    roles,
};
use crate::{network, state::AppState};
use axum::{
    Json,
    body::Body,
    extract::{ConnectInfo, State},
    http::{HeaderMap, HeaderValue, Method, Request, StatusCode, header},
    middleware::Next,
    response::{IntoResponse, Response},
};
use cookie::{Cookie, SameSite, time::OffsetDateTime};
use serde::Serialize;
use serde_json::json;
use sqlx::FromRow;
use std::{collections::BTreeSet, net::SocketAddr, sync::Arc};
use subtle::ConstantTimeEq;
use uuid::Uuid;

#[derive(Clone, Debug)]
pub struct AuthContext {
    pub user_id: Option<Uuid>,
    pub username: String,
    pub full_name: String,
    pub sid: Option<String>,
    pub roles: Vec<String>,
    pub permissions: BTreeSet<String>,
    pub source: TokenSource,
    pub account_state_verified: bool,
    pub session_alive: bool,
}

#[derive(FromRow)]
struct SessionStateRow {
    is_active: bool,
    session_exists: bool,
    session_active: bool,
    revoked: bool,
    idle_expired: bool,
    primary_role: String,
    secondary_roles: String,
}

enum SessionDecision {
    Accepted(AuthContext),
    Rejected(&'static str),
}

pub async fn require_auth(
    State(state): State<Arc<AppState>>,
    mut request: Request<Body>,
    next: Next,
) -> Response {
    let source_and_token = extract_token(request.headers());
    let Some((source, token)) = source_and_token else {
        return StatusCode::UNAUTHORIZED.into_response();
    };

    if source == TokenSource::Cookie
        && state.auth.settings().cookie_auth
        && !csrf_is_valid(request.method(), request.headers())
    {
        return (
            StatusCode::FORBIDDEN,
            Json(json!({
                "message": "Yêu cầu không hợp lệ (thiếu mã chống giả mạo). Hãy tải lại trang và thử lại.",
                "code": "csrf_required"
            })),
        )
            .into_response();
    }

    let token_identity = match state.auth.decode(&token) {
        Ok(identity) => identity,
        Err(_) => return StatusCode::UNAUTHORIZED.into_response(),
    };

    let decision = refresh_identity(
        &state,
        token_identity.clone(),
        source,
        request.headers().contains_key("x-background-poll"),
    )
    .await;

    let context = match decision {
        SessionDecision::Accepted(context) => context,
        SessionDecision::Rejected(message) => {
            let secure = is_effectively_https(&request);
            let mut response = (
                StatusCode::UNAUTHORIZED,
                Json(json!({ "message": message })),
            )
                .into_response();
            clear_auth_cookies(response.headers_mut(), secure);
            return response;
        }
    };

    request.extensions_mut().insert(context);
    let should_renew = source == TokenSource::Cookie
        && state.auth.settings().cookie_auth
        && !request.headers().contains_key("x-background-poll")
        && should_renew_cookie(&state.auth, token_identity.expires_at_unix);
    let csrf = should_renew
        .then(|| cookie_value(request.headers(), CSRF_COOKIE))
        .flatten();
    let secure = should_renew.then(|| is_effectively_https(&request));
    let mut response = next.run(request).await;

    if let Some(secure) = secure {
        let now = jsonwebtoken::get_current_timestamp() as i64;
        if let Ok((token, expires_at)) = state.auth.renew_web_token(&token_identity, now) {
            refresh_auth_cookies(
                response.headers_mut(),
                &token,
                csrf.as_deref(),
                expires_at,
                secure,
            );
        }
    }
    response
}

async fn refresh_identity(
    state: &AppState,
    token: TokenIdentity,
    source: TokenSource,
    background_poll: bool,
) -> SessionDecision {
    let query = sqlx::query_as::<_, SessionStateRow>(
        r#"
        SELECT u.is_active,
               s.session_token IS NOT NULL AS session_exists,
               COALESCE(s.is_active, FALSE) AS session_active,
               COALESCE(s.revoked, FALSE) AS revoked,
               (s.session_token IS NOT NULL AND s.last_seen IS NOT NULL
                AND $3 > 0
                AND s.last_seen < CURRENT_TIMESTAMP - make_interval(days => $3)) AS idle_expired,
               u.role AS primary_role,
               COALESCE((SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                         FROM user_roles ur
                         WHERE ur.username = u.username
                           AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)), '') AS secondary_roles
        FROM app_users u
        LEFT JOIN user_sessions s
          ON s.session_token = $2 AND s.username = u.username
        WHERE u.username = $1 AND u.is_deleted = FALSE
        LIMIT 1
        "#,
    )
    .bind(&token.username)
    .bind(token.sid.as_deref())
    .bind(state.auth.settings().session_idle_days)
    .fetch_optional(&state.pool)
    .await;

    let Ok(row) = query else {
        // Compatibility with the existing runtime: a transient DB error keeps basic identity but
        // deliberately grants no permission. Permission-protected operations therefore fail closed.
        return SessionDecision::Accepted(AuthContext {
            user_id: token.user_id,
            username: token.username,
            full_name: token.full_name,
            sid: token.sid,
            roles: token.roles,
            permissions: BTreeSet::new(),
            source,
            account_state_verified: false,
            session_alive: false,
        });
    };

    let Some(row) = row else {
        return SessionDecision::Rejected("Tài khoản đã bị khóa.");
    };
    if !row.is_active {
        return SessionDecision::Rejected("Tài khoản đã bị khóa.");
    }
    if row.revoked {
        return SessionDecision::Rejected("Thiết bị này đã bị thu hồi. Vui lòng đăng nhập lại.");
    }
    if row.idle_expired {
        return SessionDecision::Rejected(
            "Phiên đăng nhập đã hết hạn do lâu không hoạt động. Vui lòng đăng nhập lại.",
        );
    }
    if row.session_exists && !row.session_active {
        return SessionDecision::Rejected("Phiên đăng nhập đã kết thúc. Vui lòng đăng nhập lại.");
    }

    let session_alive = row.session_exists && row.session_active;
    if session_alive
        && !background_poll
        && let Some(sid) = token.sid.as_deref()
        && let Err(error) = sqlx::query(
            r#"
            UPDATE user_sessions SET last_seen = CURRENT_TIMESTAMP
            WHERE session_token = $1 AND username = $2
              AND is_active = TRUE AND revoked = FALSE
              AND last_seen < CURRENT_TIMESTAMP - INTERVAL '2 minutes'
            "#,
        )
        .bind(sid)
        .bind(&token.username)
        .execute(&state.pool)
        .await
    {
        // The account/session check already succeeded. Matching .NET semantics, a best-effort
        // last_seen write must not turn an otherwise valid request into a 500/401.
        tracing::warn!(%error, "could not refresh session last_seen");
    }

    let fresh_roles = roles::combine(Some(&row.primary_role), &row.secondary_roles);
    let fresh_permissions = permissions::for_roles(fresh_roles.iter().map(String::as_str))
        .into_iter()
        .map(str::to_owned)
        .collect();
    SessionDecision::Accepted(AuthContext {
        user_id: token.user_id,
        username: token.username,
        full_name: token.full_name,
        sid: token.sid,
        roles: fresh_roles,
        permissions: fresh_permissions,
        source,
        account_state_verified: true,
        session_alive,
    })
}

fn extract_token(headers: &HeaderMap) -> Option<(TokenSource, String)> {
    if let Some(raw) = headers.get(header::AUTHORIZATION)
        && !raw.as_bytes().is_empty()
    {
        // Any non-empty Authorization value wins over the ambient browser cookie, including an
        // invalid/non-ASCII one. Falling back after a malformed header would violate the current
        // Bearer -> cookie precedence and could authenticate a request the caller meant to reject.
        let value = raw.to_str().ok()?;
        let (scheme, token) = value.split_once(' ')?;
        if scheme.eq_ignore_ascii_case("Bearer") && !token.trim().is_empty() {
            return Some((TokenSource::Bearer, token.trim().to_owned()));
        }
        return None;
    }

    cookie_value(headers, AUTH_COOKIE).map(|token| (TokenSource::Cookie, token))
}

fn cookie_value(headers: &HeaderMap, name: &str) -> Option<String> {
    headers
        .get_all(header::COOKIE)
        .iter()
        .filter_map(|value| value.to_str().ok())
        .flat_map(|value| value.split(';'))
        .filter_map(|value| Cookie::parse(value.trim()).ok())
        .find(|cookie| cookie.name() == name)
        .map(|cookie| cookie.value().to_owned())
        .filter(|value| !value.is_empty())
}

fn csrf_is_valid(method: &Method, headers: &HeaderMap) -> bool {
    if matches!(
        *method,
        Method::GET | Method::HEAD | Method::OPTIONS | Method::TRACE
    ) {
        return true;
    }
    let Some(cookie) = cookie_value(headers, CSRF_COOKIE) else {
        return false;
    };
    let Some(header) = headers
        .get(CSRF_HEADER)
        .and_then(|value| value.to_str().ok())
        .filter(|value| !value.is_empty())
    else {
        return false;
    };
    bool::from(cookie.as_bytes().ct_eq(header.as_bytes()))
}

fn should_renew_cookie(auth: &AuthService, expires_at: i64) -> bool {
    let now = jsonwebtoken::get_current_timestamp() as i64;
    let half_life = auth
        .settings()
        .web_expire_hours
        .saturating_mul(3_600)
        .saturating_div(2);
    expires_at.saturating_sub(now) < half_life
}

fn is_effectively_https(request: &Request<Body>) -> bool {
    let peer = request
        .extensions()
        .get::<ConnectInfo<SocketAddr>>()
        .map(|value| value.0.ip());
    peer.is_some_and(|peer| network::forwarded_proto(peer, request.headers()) == "https")
}

fn refresh_auth_cookies(
    headers: &mut HeaderMap,
    token: &str,
    csrf: Option<&str>,
    expires_at: i64,
    secure: bool,
) {
    append_cookie(
        headers,
        Cookie::build((AUTH_COOKIE, token.to_owned()))
            .path("/")
            .http_only(true)
            .secure(secure)
            .same_site(SameSite::Lax)
            .expires(OffsetDateTime::from_unix_timestamp(expires_at).ok()),
    );
    if let Some(csrf) = csrf {
        append_cookie(
            headers,
            Cookie::build((CSRF_COOKIE, csrf.to_owned()))
                .path("/")
                .http_only(false)
                .secure(secure)
                .same_site(SameSite::Lax)
                .expires(OffsetDateTime::from_unix_timestamp(expires_at).ok()),
        );
    }
}

fn clear_auth_cookies(headers: &mut HeaderMap, secure: bool) {
    for (name, http_only) in [(AUTH_COOKIE, true), (CSRF_COOKIE, false)] {
        append_cookie(
            headers,
            Cookie::build((name, ""))
                .path("/")
                .http_only(http_only)
                .secure(secure)
                .same_site(SameSite::Lax)
                .expires(OffsetDateTime::UNIX_EPOCH)
                .max_age(cookie::time::Duration::ZERO),
        );
    }
}

fn append_cookie(headers: &mut HeaderMap, cookie: cookie::CookieBuilder<'_>) {
    if let Ok(value) = HeaderValue::from_str(&cookie.build().to_string()) {
        headers.append(header::SET_COOKIE, value);
    }
}

#[derive(Serialize)]
#[allow(dead_code)]
struct _NeverExposeSecretsInErrors {
    message: &'static str,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bearer_has_precedence_over_cookie() {
        let mut headers = HeaderMap::new();
        headers.insert(header::AUTHORIZATION, "Bearer android".parse().unwrap());
        headers.insert(header::COOKIE, "km_auth=browser".parse().unwrap());
        assert_eq!(
            extract_token(&headers),
            Some((TokenSource::Bearer, "android".to_owned()))
        );
    }

    #[test]
    fn malformed_authorization_does_not_fall_back_to_cookie() {
        let mut headers = HeaderMap::new();
        headers.insert(header::AUTHORIZATION, "Basic abc".parse().unwrap());
        headers.insert(header::COOKIE, "km_auth=browser".parse().unwrap());
        assert_eq!(extract_token(&headers), None);

        headers.insert(
            header::AUTHORIZATION,
            HeaderValue::from_bytes(b"Bearer \xff").unwrap(),
        );
        assert_eq!(extract_token(&headers), None);
    }

    #[test]
    fn csrf_is_constant_time_compatible_and_only_required_for_unsafe_methods() {
        let mut headers = HeaderMap::new();
        headers.insert(header::COOKIE, "km_csrf=AABB; km_auth=x".parse().unwrap());
        headers.insert(CSRF_HEADER, "AABB".parse().unwrap());
        assert!(csrf_is_valid(&Method::POST, &headers));
        headers.insert(CSRF_HEADER, "AABC".parse().unwrap());
        assert!(!csrf_is_valid(&Method::POST, &headers));
        assert!(csrf_is_valid(&Method::GET, &HeaderMap::new()));
    }
}
