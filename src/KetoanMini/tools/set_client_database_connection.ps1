param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [int]$Port = 1433,
    [string]$Database = "KetoanMini",
    [string]$Login = "ketoan_app",

    [Parameter(Mandatory = $true)]
    [string]$Password,

    [string]$AppDirectory = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AppDirectory)) {
    $AppDirectory = Split-Path -Parent $PSScriptRoot
}

$configPath = Join-Path $AppDirectory "config\database.json"
$connectionString = "Server=$Server,$Port;Database=$Database;User Id=$Login;Password=$Password;Encrypt=False;TrustServerCertificate=True;"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $configPath) | Out-Null
@{
    ConnectionString = $connectionString
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $configPath -Encoding UTF8

Write-Host "DONE"
Write-Host "Wrote config: $configPath"
Write-Host $connectionString
