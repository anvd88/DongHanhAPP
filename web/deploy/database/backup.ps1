param(
    [Parameter(Mandatory=$true)][string]$HostName,
    [int]$Port = 5432,
    [Parameter(Mandatory=$true)][string]$Database,
    [Parameter(Mandatory=$true)][string]$Username,
    [Parameter(Mandatory=$true)][string]$Password,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$RetentionDays = 30
)
$ErrorActionPreference = 'Stop'
$resolved = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($resolved) | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $resolved "ketoanmini-$stamp.dump"
$env:PGPASSWORD = $Password
try {
    & pg_dump --host $HostName --port $Port --username $Username --dbname $Database --format custom --no-owner --no-privileges --file $target
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE" }
    Get-ChildItem -LiteralPath $resolved -Filter 'ketoanmini-*.dump' -File |
        Where-Object LastWriteTimeUtc -lt ([DateTime]::UtcNow.AddDays(-$RetentionDays)) |
        Remove-Item -Force
    Write-Output $target
} finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
