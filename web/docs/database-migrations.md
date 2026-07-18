# Database migration policy

`PostgresSchema.EnsureAsync` applies the consolidated idempotent baseline and records `001_baseline` in `schema_migrations`. It is safe to execute repeatedly and is tested twice in the same database by CI.

For every later schema change:

1. Assign the next immutable version (`002_...`, `003_...`).
2. Make the SQL transactional and forward-compatible with the currently deployed API.
3. Insert its version only after the SQL succeeds.
4. Add an integration test that upgrades a baseline database twice and verifies both data preservation and idempotency.
5. Back up and run a restore drill before production deployment.
6. Prefer forward fixes. For a binary rollback, restore the verified pre-deployment dump into a new database and switch the connection secret.

Never edit an already-applied migration version. Never put credentials or sensitive row contents in migration output.
