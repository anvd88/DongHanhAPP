# PostgreSQL backup, restore and rollback

Prerequisite: PostgreSQL client tools (`pg_dump`, `pg_restore`). Passwords are accepted as secure deployment inputs and are only exposed through the temporary `PGPASSWORD` process environment.

Create a compressed backup without putting credentials in logs:

```powershell
./backup.ps1 -HostName localhost -Database ketoanmini -Username postgres -Password $env:PGPASSWORD -OutputDirectory D:\Backups\KetoanMini
```

Restore into a newly-created validation database first:

```powershell
./restore.ps1 -HostName localhost -Database ketoanmini_restore_test -Username postgres -Password $env:PGPASSWORD -BackupFile D:\Backups\KetoanMini\ketoanmini-YYYYMMDD-HHMMSS.dump -Clean
```

Validate `/api/health`, the `schema_migrations` table, row counts and a representative login/read flow before switching traffic. Rollback means stopping writes, restoring the last verified dump into a new database, changing only the connection secret, then restarting the API. Never overwrite the only production database during a restore drill.
