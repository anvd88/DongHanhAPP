<#
.SYNOPSIS
    Xác minh .NET sidecar và Rust gateway trước khi đổi Cloudflare origin sang :5240.

.DESCRIPTION
    Kiểm tra health của hai tiến trình, hợp đồng /api/info native và một route public chưa port phải
    được Rust stream nguyên trạng từ .NET. Script chỉ đọc; không sửa Cloudflare hay cấu hình máy.
#>
[CmdletBinding()]
param(
    [string] $DotNetBase = "http://127.0.0.1:5239",
    [string] $RustBase = "http://127.0.0.1:5240"
)

$ErrorActionPreference = "Stop"

function Invoke-Probe {
    param(
        [Parameter(Mandatory = $true)] [System.Net.Http.HttpClient] $Client,
        [Parameter(Mandatory = $true)] [string] $Base,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Get,
        "$($Base.TrimEnd('/'))$Path"
    )
    $request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https") | Out-Null
    $request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "app.ketoancp.click") | Out-Null
    $response = $null
    try {
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        [pscustomobject]@{
            Status = [int] $response.StatusCode
            ContentType = $response.Content.Headers.ContentType.MediaType
            Body = $body
        }
    } finally {
        $request.Dispose()
        if ($response) { $response.Dispose() }
    }
}

function Assert-Status {
    param([object] $Probe, [int] $Expected, [string] $Name)
    if ($Probe.Status -ne $Expected) {
        throw "$Name tra HTTP $($Probe.Status), can $Expected."
    }
}

function Get-BodyHash {
    param([byte[]] $Body)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        ([BitConverter]::ToString($sha.ComputeHash($Body))).Replace("-", "").ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)

try {
    Assert-Status (Invoke-Probe $client $DotNetBase "/api/health") 200 ".NET health"
    Assert-Status (Invoke-Probe $client $RustBase "/api/health") 200 "Rust health"

    $info = Invoke-Probe $client $RustBase "/api/info"
    Assert-Status $info 200 "Rust native /api/info"
    $infoJson = [System.Text.Encoding]::UTF8.GetString($info.Body) | ConvertFrom-Json
    if ($infoJson.app -ne "KetoanMini Web API" -or $infoJson.status -ne "ok") {
        throw "Rust /api/info sai hop dong JSON."
    }

    # Route này có trong OpenAPI nhưng chưa native Rust: kết quả qua gateway phải trùng .NET.
    $compatPath = "/api/releases/public/latest"
    $dotnetCompat = Invoke-Probe $client $DotNetBase $compatPath
    $rustCompat = Invoke-Probe $client $RustBase $compatPath
    if ($rustCompat.Status -in 501, 502) {
        throw "Compatibility route $compatPath tra HTTP $($rustCompat.Status)."
    }
    if ($rustCompat.Status -ne $dotnetCompat.Status) {
        throw "Compatibility status lech: .NET=$($dotnetCompat.Status), Rust=$($rustCompat.Status)."
    }
    if ((Get-BodyHash $rustCompat.Body) -ne (Get-BodyHash $dotnetCompat.Body)) {
        throw "Compatibility body lech giua Rust va .NET tai $compatPath."
    }

    Write-Host "CUTOVER CHECK OK: .NET health + Rust native + compatibility stream deu dat."
} finally {
    $client.Dispose()
    $handler.Dispose()
}
