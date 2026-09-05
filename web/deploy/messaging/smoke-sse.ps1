param(
    [Parameter(Mandatory=$true)][string]$BearerToken,
    [string]$DirectBaseUrl = "http://127.0.0.1:5239",
    [Parameter(Mandatory=$true)][string]$GatewayBaseUrl
)

$ErrorActionPreference = "Stop"

function Test-SseStream([string]$Name, [string]$BaseUrl) {
    $target = $BaseUrl.TrimEnd('/') + "/api/realtime/stream?after=0"
    $output = & curl.exe --silent --show-error --no-buffer --max-time 25 `
        -H "Authorization: Bearer $BearerToken" `
        -H "Accept: text/event-stream" `
        -H "Last-Event-ID: 0" $target 2>&1
    if ($LASTEXITCODE -notin @(0, 28)) { throw "$Name SSE request failed: $output" }
    if ($output -notmatch '(?m)^(id:|: heartbeat)') { throw "$Name buffered or returned no SSE frame." }
    Write-Host "$Name SSE streaming OK"
}

Test-SseStream "direct" $DirectBaseUrl
Test-SseStream "gateway/public" $GatewayBaseUrl
