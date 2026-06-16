param(
    [string]$InstanceName = "SQLEXPRESS01",
    [string]$Database = "KetoanMini",
    [int]$Port = 1433,
    [string]$Login = "ketoan_app",
    [string]$Password = "",
    [string]$AppConfigPath = ""
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-SqlIdentifier([string]$Name) {
    return "[" + $Name.Replace("]", "]]") + "]"
}

function ConvertTo-SqlLiteral([string]$Value) {
    return "N'" + $Value.Replace("'", "''") + "'"
}

function ConvertTo-SqlPasswordLiteral([string]$Value) {
    return "'" + $Value.Replace("'", "''") + "'"
}

function Get-PreferredIpv4Address {
    $addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object {
            $_.IPAddress -notlike "127.*" -and
            $_.IPAddress -notlike "169.254.*" -and
            $_.PrefixOrigin -ne "WellKnown"
        } |
        Sort-Object InterfaceMetric, InterfaceIndex

    $first = $addresses | Select-Object -First 1
    if ($null -eq $first) {
        return "127.0.0.1"
    }

    return $first.IPAddress
}

function Get-SqlWmiNamespace {
    $namespaces = Get-WmiObject -Namespace "root\Microsoft\SqlServer" -Class "__Namespace" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "ComputerManagement*" } |
        Sort-Object @{ Expression = { [int]($_.Name -replace "\D", "") } } -Descending

    foreach ($namespace in $namespaces) {
        $path = "root\Microsoft\SqlServer\$($namespace.Name)"
        $protocol = Get-WmiObject -Namespace $path -Class ServerNetworkProtocol -Filter "InstanceName='$InstanceName' AND ProtocolName='Tcp'" -ErrorAction SilentlyContinue
        if ($null -ne $protocol) {
            return $path
        }
    }

    return $null
}

function Enable-SqlTcpIp {
    $namespace = Get-SqlWmiNamespace
    if ([string]::IsNullOrWhiteSpace($namespace)) {
        Write-Warning "Khong tim thay SQL Server WMI namespace de bat TCP/IP. Hay bat TCP/IP bang SQL Server Configuration Manager neu can."
        return
    }

    $tcp = Get-WmiObject -Namespace $namespace -Class ServerNetworkProtocol -Filter "InstanceName='$InstanceName' AND ProtocolName='Tcp'"
    $tcp.SetEnable() | Out-Null

    $properties = Get-WmiObject -Namespace $namespace -Class ServerNetworkProtocolProperty -Filter "InstanceName='$InstanceName' AND ProtocolName='Tcp' AND IPAddressName='IPAll'"
    foreach ($property in $properties) {
        if ($property.PropertyName -eq "TcpPort") {
            $property.SetStringValue([string]$Port) | Out-Null
        }

        if ($property.PropertyName -eq "TcpDynamicPorts") {
            $property.SetStringValue("") | Out-Null
        }
    }
}

function Enable-MixedModeAuthentication {
    $instanceNamesPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"
    if (!(Test-Path $instanceNamesPath)) {
        Write-Warning "Khong tim thay registry SQL Server Instance Names. Bo qua cau hinh Mixed Mode."
        return
    }

    $instanceId = (Get-ItemProperty -Path $instanceNamesPath).$InstanceName
    if ([string]::IsNullOrWhiteSpace($instanceId)) {
        Write-Warning "Khong tim thay instance id cho $InstanceName. Bo qua cau hinh Mixed Mode."
        return
    }

    $loginModePath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer"
    if (Test-Path $loginModePath) {
        Set-ItemProperty -Path $loginModePath -Name "LoginMode" -Value 2
    }
}

function Restart-SqlService {
    $serviceName = if ($InstanceName -eq "MSSQLSERVER") { "MSSQLSERVER" } else { "MSSQL`$$InstanceName" }
    Restart-Service -Name $serviceName -Force
}

function Invoke-SqlBatch([string]$Server, [string]$Db, [string]$Sql) {
    Add-Type -AssemblyName System.Data
    $connectionString = "Server=$Server;Database=$Db;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;"
    $connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 120
        $command.CommandText = $Sql
        try {
            $command.ExecuteNonQuery() | Out-Null
        }
        catch {
            Write-Host ""
            Write-Host "SQL batch bi loi:" -ForegroundColor Red
            Write-Host $Sql -ForegroundColor Yellow
            throw
        }
    }
    finally {
        $connection.Dispose()
    }
}

if (!(Test-IsAdministrator)) {
    throw "Hay chay PowerShell bang Run as Administrator de cau hinh SQL Server va firewall."
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    $secure = Read-Host "Nhap mat khau SQL cho login $Login" -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

if ([string]::IsNullOrWhiteSpace($Password) -or $Password.Length -lt 8) {
    throw "Mat khau SQL phai co it nhat 8 ky tu."
}

if ([string]::IsNullOrWhiteSpace($AppConfigPath)) {
    $AppConfigPath = Join-Path (Split-Path -Parent $PSScriptRoot) "config\database.json"
}

Write-Host "Buoc 1/7: Bat TCP/IP cho SQL Server..."
Enable-SqlTcpIp

Write-Host "Buoc 2/7: Bat Mixed Mode Authentication..."
Enable-MixedModeAuthentication

Write-Host "Buoc 3/7: Mo firewall TCP $Port..."
$firewallName = "KetoanMini SQL Server TCP $Port"
if ($null -eq (Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $firewallName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain,Private | Out-Null
}

Write-Host "Buoc 4/7: Restart SQL Server service..."
Restart-SqlService
Start-Sleep -Seconds 5

$server = if ($InstanceName -eq "MSSQLSERVER") { "localhost" } else { "localhost\$InstanceName" }
$dbName = ConvertTo-SqlIdentifier $Database
$loginName = ConvertTo-SqlIdentifier $Login
$loginLiteral = ConvertTo-SqlLiteral $Login
$dbLiteral = ConvertTo-SqlLiteral $Database
$passwordLiteral = ConvertTo-SqlPasswordLiteral $Password

Write-Host "Buoc 5/7: Tao database va login SQL..."
Invoke-SqlBatch -Server $server -Db "master" -Sql "IF DB_ID($dbLiteral) IS NULL BEGIN CREATE DATABASE $dbName; END;"
Invoke-SqlBatch -Server $server -Db "master" -Sql "ALTER DATABASE $dbName SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;"

$loginSql = @"
IF SUSER_ID($loginLiteral) IS NULL
BEGIN
    CREATE LOGIN $loginName WITH PASSWORD = $passwordLiteral, CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;
END
ELSE
BEGIN
    ALTER LOGIN $loginName WITH PASSWORD = $passwordLiteral, CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;
END;

ALTER LOGIN $loginName ENABLE;
"@

Invoke-SqlBatch -Server $server -Db "master" -Sql $loginSql

Write-Host "Buoc 6/7: Tao user trong database va cap quyen..."
$dbSql = @"
IF USER_ID($loginLiteral) IS NULL
BEGIN
    CREATE USER $loginName FOR LOGIN $loginName;
END;

IF IS_ROLEMEMBER(N'db_owner', $loginLiteral) <> 1
BEGIN
    ALTER ROLE db_owner ADD MEMBER $loginName;
END;
"@

Invoke-SqlBatch -Server $server -Db $Database -Sql $dbSql

Write-Host "Buoc 7/7: Ghi connection string cho app..."

$ip = Get-PreferredIpv4Address
$connectionString = "Server=$ip,$Port;Database=$Database;User Id=$Login;Password=$Password;Encrypt=False;TrustServerCertificate=True;"
$configDir = Split-Path -Parent $AppConfigPath
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
@{
    ConnectionString = $connectionString
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $AppConfigPath -Encoding UTF8

Write-Host "DONE"
Write-Host "Server IP: $ip"
Write-Host "SQL port: $Port"
Write-Host "App config: $AppConfigPath"
Write-Host "Client connection string:"
Write-Host $connectionString
