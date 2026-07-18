$ErrorActionPreference = "Stop"
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Fail($message) {
    Write-Host ""
    Write-Host "LỖI: $message" -ForegroundColor Red
    exit 1
}

function Step($message) {
    Write-Host ""
    Write-Host "== $message" -ForegroundColor Cyan
}

function Read-ChoiceValue($prompt, $defaultValue) {
    if ([string]::IsNullOrWhiteSpace($defaultValue)) {
        $value = Read-Host $prompt
    } else {
        $value = Read-Host "$prompt [$defaultValue]"
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $defaultValue
        }
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        Fail "Bạn chưa nhập giá trị cho: $prompt"
    }

    return $value.Trim()
}

function Read-FileText($path) {
    if (-not (Test-Path -LiteralPath $path)) {
        Fail "Không tìm thấy file: $path"
    }
    return [IO.File]::ReadAllText($path)
}

function Write-Utf8NoBom($path, $text) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($path, $text, $encoding)
}

function Regex-Value($text, $pattern, $fallback = "") {
    $match = [regex]::Match($text, $pattern)
    if ($match.Success) {
        return $match.Groups[1].Value
    }
    return $fallback
}

function Next-PatchVersion($version) {
    if ($version -match '^(\d+)\.(\d+)\.(\d+)$') {
        return "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)"
    }
    return ""
}

function Replace-First($text, $pattern, [System.Text.RegularExpressions.MatchEvaluator] $evaluator, $name) {
    $regex = [regex]$pattern
    if (-not $regex.IsMatch($text)) {
        Fail "Không tìm thấy $name trong file."
    }
    return $regex.Replace($text, $evaluator, 1)
}

function Replace-JsonVersion($path, $version, $count) {
    if (-not (Test-Path -LiteralPath $path)) {
        return
    }

    $text = Read-FileText $path
    $regex = [regex]'("version"\s*:\s*")[^"]+(")'
    $updated = $regex.Replace(
        $text,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($m)
            return $m.Groups[1].Value + $version + $m.Groups[2].Value
        },
        $count
    )
    Write-Utf8NoBom $path $updated
}

function Set-EnvLine($text, $key, $value) {
    $pattern = '(?m)^' + [regex]::Escape($key) + '=.*$'
    $regex = [regex]$pattern
    if ($regex.IsMatch($text)) {
        return $regex.Replace(
            $text,
            [System.Text.RegularExpressions.MatchEvaluator]{
                param($m)
                return "$key=$value"
            },
            1
        )
    }

    if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) {
        $text += "`r`n"
    }
    return $text + "$key=$value`r`n"
}


# local.properties là định dạng Java Properties: dấu \ dùng để THOÁT ký tự đứng ngay sau nó, nên
# Android Studio ghi ổ đĩa thành "C\:/Users/..." (hoặc "C\:\\Users\\..."). Bê nguyên chuỗi đó vào
# Join-Path là PowerShell đọc "C\:" thành tên ổ đĩa "C\" rồi ném DriveNotFound.
function Expand-JavaPropsValue([string]$value) {
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $value.Length; $i++) {
        # Gặp \ thì bỏ chính nó đi và lấy nguyên ký tự đứng sau.
        if ($value[$i] -eq '\' -and $i + 1 -lt $value.Length) { $i++ }
        [void]$sb.Append($value[$i])
    }
    return $sb.ToString()
}

function Get-SdkDir($androidDir) {
    $localProps = Join-Path $androidDir "local.properties"
    if (Test-Path -LiteralPath $localProps) {
        $line = [IO.File]::ReadAllLines($localProps) | Where-Object { $_ -match '^\s*sdk\.dir\s*=' } | Select-Object -First 1
        if ($line) {
            $raw = ($line -replace '^\s*sdk\.dir\s*=\s*', '').Trim()
            # Giải thoát TRƯỚC rồi mới nắn dấu gạch: "\\" phải về "\" xong xuôi thì đổi "/" mới đúng.
            return (Expand-JavaPropsValue $raw).Replace('/', '\')
        }
    }
    if ($env:ANDROID_HOME) { return $env:ANDROID_HOME }
    if ($env:ANDROID_SDK_ROOT) { return $env:ANDROID_SDK_ROOT }
    return Join-Path $env:LOCALAPPDATA "Android\Sdk"
}

function Latest-BuildTools($sdkDir) {
    # [IO.Path]::Combine chứ không Join-Path: Join-Path đòi ổ đĩa phải có thật và NÉM lỗi nếu không.
    # Kiểm tra APK chỉ là bước phụ sau khi APK đã ra lò — đường dẫn SDK hỏng thì cảnh báo rồi bỏ qua,
    # đừng để nó giết cả script ($ErrorActionPreference = "Stop") và nuốt mất phần in SHA256 ở cuối.
    if ([string]::IsNullOrWhiteSpace($sdkDir)) { return $null }
    $dir = [IO.Path]::Combine($sdkDir, "build-tools")
    if (-not (Test-Path -LiteralPath $dir)) {
        return $null
    }
    return Get-ChildItem -LiteralPath $dir -Directory |
        Sort-Object { try { [version]$_.Name } catch { [version]"0.0.0" } } -Descending |
        Select-Object -First 1
}

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) {
    $root = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$app = Join-Path $root "Ketoanapk"
$android = Join-Path $app "android"
$gradleFile = Join-Path $android "app\build.gradle"
$envFile = Join-Path $app ".env.android"
$packageFile = Join-Path $app "package.json"
$lockFile = Join-Path $app "package-lock.json"
$artifacts = Join-Path $root "artifacts"

if (-not (Test-Path -LiteralPath $gradleFile)) {
    Fail "Không tìm thấy project Android Ketoanapk: $gradleFile"
}

$gradleText = Read-FileText $gradleFile
$currentCodeText = Regex-Value $gradleText '(?m)^\s*versionCode\s+(\d+)'
$currentName = Regex-Value $gradleText '(?m)^\s*versionName\s+"([^"]+)"'
$currentApi = Regex-Value $gradleText '(?m)^\s*buildConfigField\s+"String",\s+"API_BASE_URL",\s+"\\\"(.+?)\\\""'

if ([string]::IsNullOrWhiteSpace($currentCodeText)) { Fail "Không đọc được versionCode hiện tại từ build.gradle" }
if ([string]::IsNullOrWhiteSpace($currentName)) { Fail "Không đọc được versionName hiện tại từ build.gradle" }

[int]$currentCode = $currentCodeText
$nextCode = $currentCode + 1
$nextName = Next-PatchVersion $currentName
if ([string]::IsNullOrWhiteSpace($nextName)) { $nextName = $currentName }

Step "Công cụ build release KetoanAPK"
Write-Host "Project: $app"
Write-Host "Phiên bản APK hiện tại: $currentName (mã $currentCode)"
if (-not [string]::IsNullOrWhiteSpace($currentApi)) {
    Write-Host "API_BASE_URL hiện tại: $currentApi"
}

$versionName = Read-ChoiceValue "Phiên bản mới (versionName)" $nextName
$versionCodeText = Read-ChoiceValue "Mã phiên bản mới (versionCode), phải lớn hơn $currentCode" ([string]$nextCode)

[int]$versionCode = 0
if (-not [int]::TryParse($versionCodeText, [ref]$versionCode)) {
    Fail "versionCode phải là số nguyên."
}
if ($versionCode -le $currentCode) {
    Fail "versionCode phải lớn hơn mã hiện tại $currentCode."
}

if ([string]::IsNullOrWhiteSpace($currentApi) -and (Test-Path -LiteralPath $envFile)) {
    $envTextForApi = Read-FileText $envFile
    $currentApi = Regex-Value $envTextForApi '(?m)^VITE_API_BASE_URL=(.+)$'
}
$apiBaseUrl = Read-ChoiceValue "API_BASE_URL" $currentApi

$defaultArtifactName = "ketoanapk-hr-$versionName-code$versionCode-release.apk"
$artifactName = Read-ChoiceValue "Tên file APK đầu ra" $defaultArtifactName
$artifactName = [IO.Path]::GetFileName($artifactName)
if (-not $artifactName.EndsWith(".apk", [StringComparison]::OrdinalIgnoreCase)) {
    $artifactName += ".apk"
}
$artifactPath = Join-Path $artifacts $artifactName

Write-Host ""
Write-Host "Sẽ build:"
Write-Host "  Phiên bản      : $versionName"
Write-Host "  Mã phiên bản   : $versionCode"
Write-Host "  API            : $apiBaseUrl"
Write-Host "  File đầu ra    : $artifactPath"
$confirm = Read-Host "Tiếp tục? (Y/N hoặc C/K)"
if ($confirm -notmatch '^(y|yes|c|co|có)$') {
    Write-Host "Đã hủy."
    exit 0
}

Step "Đang cập nhật file phiên bản"
$gradleText = Read-FileText $gradleFile
$gradleText = Replace-First $gradleText '(?m)^(\s*versionCode\s+)\d+' {
    param($m)
    return $m.Groups[1].Value + $versionCode
} "versionCode"
$gradleText = Replace-First $gradleText '(?m)^(\s*versionName\s+)"[^"]+"' {
    param($m)
    return $m.Groups[1].Value + '"' + $versionName + '"'
} "versionName"

$apiRegex = [regex]'(?m)^(\s*buildConfigField\s+"String",\s+"API_BASE_URL",\s+")\\"[^"]+\\"(".*)$'
if ($apiRegex.IsMatch($gradleText)) {
    $gradleText = $apiRegex.Replace(
        $gradleText,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($m)
            return $m.Groups[1].Value + '\"' + $apiBaseUrl + '\"' + $m.Groups[2].Value
        },
        1
    )
} else {
    Write-Host "CẢNH BÁO: Không tìm thấy dòng API_BASE_URL trong build.gradle; giữ nguyên dòng này." -ForegroundColor Yellow
}
Write-Utf8NoBom $gradleFile $gradleText

$envText = ""
if (Test-Path -LiteralPath $envFile) {
    $envText = Read-FileText $envFile
}
$envText = Set-EnvLine $envText "VITE_APP_TARGET" "hr-apk"
$envText = Set-EnvLine $envText "VITE_API_BASE_URL" $apiBaseUrl
$envText = Set-EnvLine $envText "VITE_ANDROID_VERSION_CODE" $versionCode
$envText = Set-EnvLine $envText "VITE_ANDROID_VERSION_NAME" $versionName
Write-Utf8NoBom $envFile $envText

Replace-JsonVersion $packageFile $versionName 1
Replace-JsonVersion $lockFile $versionName 2

Step "Đang build APK release"
$defaultJdk = "C:\Program Files\Android\Android Studio\jbr"
if (Test-Path -LiteralPath (Join-Path $defaultJdk "bin\java.exe")) {
    $env:JAVA_HOME = $defaultJdk
} elseif (-not $env:JAVA_HOME -or -not (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME "bin\java.exe"))) {
    Fail "Không tìm thấy JDK 21. Hãy cài Android Studio JBR hoặc đặt JAVA_HOME trước khi chạy script."
}
$env:PATH = (Join-Path $env:JAVA_HOME "bin") + ";" + $env:PATH
Write-Host "JAVA_HOME=$env:JAVA_HOME"

$gradlew = Join-Path $android "gradlew.bat"
if (-not (Test-Path -LiteralPath $gradlew)) {
    Fail "Không tìm thấy Gradle wrapper: $gradlew"
}

$buildExit = 0
Push-Location $android
try {
    # --no-problems-report: Gradle kết thúc build bằng việc ghi đè build\reports\problems\problems-report.html.
    # Trên máy này file đó không ghi/xoá được (kể cả sau "gradlew --stop", Get-Acl cũng bị từ chối — nghi
    # Controlled Folder Access chặn ghi vào Desktop), nên Gradle ném FileAlreadyExistsException và trả mã
    # thoát ≠ 0 => script này Fail dù APK ĐÃ build xong. Cờ này bỏ hẳn bước sinh report (Gradle 8.6+;
    # wrapper của dự án đang là 8.14.3). Báo cáo đó chỉ để chẩn đoán, không ảnh hưởng APK.
    & $gradlew --no-daemon assembleRelease --no-problems-report
    $buildExit = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($buildExit -ne 0) {
    Fail "Gradle assembleRelease thất bại với mã thoát $buildExit."
}

$releaseApk = Join-Path $android "app\build\outputs\apk\release\app-release.apk"
if (-not (Test-Path -LiteralPath $releaseApk)) {
    Fail "APK release chưa được tạo: $releaseApk"
}

Step "Đang copy APK sang artifacts"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Copy-Item -LiteralPath $releaseApk -Destination $artifactPath -Force
Write-Host "Đã copy APK tới: $artifactPath"

Step "Đang kiểm tra APK"
$sdkDir = Get-SdkDir $android
$buildTools = Latest-BuildTools $sdkDir
if ($buildTools) {
    $aapt = Join-Path $buildTools.FullName "aapt.exe"
    $apksigner = Join-Path $buildTools.FullName "apksigner.bat"

    if (Test-Path -LiteralPath $aapt) {
        $badging = & $aapt dump badging $artifactPath 2>&1
        $badging | Select-String -Pattern "^package:" | ForEach-Object { Write-Host $_.Line }
        $badging | Select-String -Pattern "^application-label:'" | Select-Object -First 1 | ForEach-Object { Write-Host $_.Line }

        # Bản release chỉ được mang CPU của điện thoại thật (xem ndk.abiFilters trong app/build.gradle).
        # Lỡ ai gỡ abiFilters là APK phình thêm ~42 MB x86/x86_64 mà nhân viên phải tải — chỉ nhìn dung
        # lượng thì khó nhận ra, nên soi thẳng danh sách ABI ở đây.
        $nativeLine = $badging | Select-String -Pattern "^native-code:" | Select-Object -First 1
        if ($nativeLine) {
            Write-Host $nativeLine.Line
            if ($nativeLine.Line -match "x86") {
                Write-Host "CẢNH BÁO: APK release có kèm x86/x86_64 (chỉ máy ảo mới dùng) — kiểm tra lại ndk.abiFilters trong app/build.gradle." -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "CẢNH BÁO: Không tìm thấy aapt.exe; bỏ qua bước kiểm tra phiên bản." -ForegroundColor Yellow
    }

    if (Test-Path -LiteralPath $apksigner) {
        & $apksigner verify --print-certs $artifactPath
        if ($LASTEXITCODE -ne 0) {
            Write-Host "CẢNH BÁO: Kiểm tra chữ ký bằng apksigner thất bại." -ForegroundColor Yellow
        }
    } else {
        Write-Host "CẢNH BÁO: Không tìm thấy apksigner.bat; bỏ qua bước kiểm tra chữ ký." -ForegroundColor Yellow
    }
} else {
    Write-Host "CẢNH BÁO: Không tìm thấy Android SDK build-tools; bỏ qua bước kiểm tra APK." -ForegroundColor Yellow
}

$hash = Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256
$sizeMb = [math]::Round((Get-Item -LiteralPath $artifactPath).Length / 1MB, 1)

Step "Hoàn tất"
Write-Host "APK: $artifactPath"
# Nhân viên tải đúng file này qua tunnel mỗi lần cập nhật nên dung lượng là con số đáng nhìn.
Write-Host "Dung lượng: $sizeMb MB"
Write-Host "SHA256: $($hash.Hash)"
Write-Host ""
Write-Host "Hãy upload APK này trong màn hình quản trị cập nhật với:"
Write-Host "  Phiên bản    : $versionName"
Write-Host "  Mã phiên bản : $versionCode"
Write-Host ""
Write-Host "Lưu ý: các bản cập nhật sau phải dùng versionCode lớn hơn $versionCode."
exit 0
