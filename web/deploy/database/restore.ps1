param(
    [Parameter(Mandatory=$true)][string]$HostName,
    [int]$Port = 5432,
    [Parameter(Mandatory=$true)][string]$Database,
    [Parameter(Mandatory=$true)][string]$Username,
    [Parameter(Mandatory=$true)][string]$Password,
    [Parameter(Mandatory=$true)][string]$BackupFile,
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($BackupFile)
if (-not [IO.File]::Exists($source)) { throw "Backup not found: $source" }
$env:PGPASSWORD = $Password
try {
    $args = @('--host', $HostName, '--port', $Port, '--username', $Username,
        '--dbname', $Database, '--no-owner', '--no-privileges', '--exit-on-error')
    if ($Clean) { $args += @('--clean', '--if-exists') }
    $args += $source
    & pg_restore @args
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE" }
} finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
