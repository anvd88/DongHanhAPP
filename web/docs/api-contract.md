# KetoanMini API contract

The machine-readable contract is served at `/swagger/v1/swagger.json`. Swagger UI is available at `/swagger` in Development only.

## Authentication and authorization

Send `Authorization: Bearer <JWT>` for protected APIs. Every `/api/*` endpoint must carry authorization metadata or explicitly declare `AllowAnonymous`; `InfrastructureTests.EveryNonPublicApiEndpoint_DeclaresAuthorization` enforces this rule in CI. Roles are `Admin`, `HR`, `Accounting`, `Employee`, and `Kiosk`. Resource endpoints additionally enforce employee ownership, conversation membership, approval assignment, or management scope in their handlers.

## Common status codes

| Code | Meaning |
|---|---|
| 200/204 | Request completed |
| 400 | Invalid state or input; response contains a safe `message` |
| 401 | Missing, invalid, expired, idle-expired, or revoked session |
| 403 | Authenticated but outside the role/resource scope |
| 404 | Resource not found or deliberately hidden from the caller |
| 409 | Concurrent update or duplicate business operation |
| 413 | Payload exceeds the configured limit |
| 429 | Rate limit exceeded |
| 503 | Database temporarily unavailable; internal details are not returned |

## Compatibility policy

- Existing fields and enum values are not removed within API v1.
- New response fields are additive; clients must ignore unknown fields.
- Database changes are idempotent and recorded in `schema_migrations`.
- APK and web contract changes must pass the backend integration suite and the Android staging smoke checklist before release.
- Sensitive values (passwords, JWTs, recovery codes, face embeddings, encryption keys, raw diagnostics) must never appear in audit details or application logs.

## Server-driven QR protocol

Authenticated APK clients send every scanned value to `POST /api/qr/resolve` with protocol version `1`
and the capabilities `server_decision`, `open_https_url`, and `dismiss`. The APK does not parse a
business prefix. The server returns a presentation plus up to three generic actions:

- `server_decision`: send the opaque `decisionToken` and selected `actionId` to `POST /api/qr/decision`;
- `open_https_url`: open only after an explicit tap and only after both server and APK HTTPS validation;
- `dismiss`: close the presentation locally.

Decision tokens are encrypted, never outlive the source QR session, and are bound to the authenticated
immutable user ID plus app session ID. Bodies are limited before JSON binding and the server must not log
raw QR values or tokens. A `server_decision` handler must be idempotent because a response lost in transit
can make the APK safely retry the same signed decision. The legacy `/api/auth/qr/scan`, `confirm`, and
`reject` routes remain available for already-installed APK versions.

Simple QR features can be added without rebuilding the APK or backend by adding `message` or
`open_https_url` entries under `QrScanner:Actions` in `appsettings.Local.json`. HTTPS links require an
exact or explicit wildcard host in `QrScanner:AllowedHttpsHosts`; scanned content is never treated as a
URL automatically. Native Android capabilities outside these three primitives still require an APK update.

## Domain routes

The OpenAPI document is authoritative. Main route groups are: `auth`, `qr`, `audit`, `app-config`, `worklist`, `chat`, `directory`, `schedule`, `shifts`, `timesheet`, `chamcong`, `requests`, `hr`, `talent`, `payroll`, `surveys`, `feedback`, `help`, `users`, and `portal`.
