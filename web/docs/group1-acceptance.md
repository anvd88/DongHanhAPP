# Group 1 acceptance record

Last automated verification: 2026-07-15.

## Automated evidence

- Release build uses warnings as errors.
- Integration tests run against a dedicated PostgreSQL database.
- OpenAPI generation and the presence of key routes are tested.
- Every API route must explicitly select authenticated or anonymous access.
- Approval authorization, rejection reason, and concurrent decisions are tested.
- Audit authorization/filtering/redaction, app config, worklist, directory, schedule, survey, help, and account security have integration coverage.

## Source-complete domains

All 23 requested domains have corresponding API/schema or operational implementation. The principal endpoint files are under `backend/KetoanMini.Api/Endpoints`; infrastructure evidence is in `backend/KetoanMini.Api.Tests`, `deploy/database`, and this documentation directory.

## External acceptance still required before production sign-off

The following cannot be truthfully certified from source code or a local TestServer and remain release gates:

- Verify FCM delivery using production-like Firebase credentials on foreground, background, and terminated APK states.
- Verify TURN relay candidates and voice/video calls across Wi-Fi/Wi-Fi, Wi-Fi/mobile, and mobile/mobile using at least two physical devices.
- Run Android connected UI tests, permission flows, offline attendance synchronization, and file sharing on supported Android versions.
- Run staging contract smoke tests jointly with the Android/web owners.
- Execute representative chat/notification/dashboard load tests against staging-sized data.
- Perform and record a production-like backup restore drill, including recovery time and recovery point evidence.

These gates must not be marked passed without logs, screenshots, test reports, or an operator signature. Source completion is not the same as production acceptance.
