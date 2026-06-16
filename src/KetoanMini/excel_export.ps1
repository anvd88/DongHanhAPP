param(
    [Parameter(Mandatory = $true)][string]$TemplatePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$DataPath,
    [int]$CleanTemplate = 1
)

$ErrorActionPreference = "Stop"

function As-Array($Value) {
    if ($null -eq $Value) { return @() }
    if ($Value -is [System.Array]) { return $Value }
    return @($Value)
}

function To-Double($Value) {
    if ($null -eq $Value) { return 0.0 }
    try {
        return [double]::Parse([string]$Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        return 0.0
    }
}

function To-ExcelSerial($DateText) {
    $d = [datetime]::ParseExact([string]$DateText, "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
    return [int](($d - [datetime]"1899-12-30").TotalDays)
}

function Normalize-Name([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return "" }
    $s = ($Text -replace "-", "")
    $s = (($s -replace "\s+", " ").Trim())
    $s = $s.Replace("đ", "d").Replace("Đ", "D")
    $normalized = $s.Normalize([System.Text.NormalizationForm]::FormD)
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $normalized.ToCharArray()) {
        $cat = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch)
        if ($cat -ne [System.Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$sb.Append($ch)
        }
    }
    $plain = $sb.ToString().Normalize([System.Text.NormalizationForm]::FormC)
    return ($plain -replace "\s+", "_")
}

function Get-SafeSheetName([string]$Name) {
    $s = ($Name -replace '[\\/\?\*\[\]:]', "_").Trim()
    if ([string]::IsNullOrWhiteSpace($s)) { $s = "Khach_hang" }
    if ($s.Length -gt 31) { $s = $s.Substring(0, 31) }
    return $s
}

function Get-SafeTableName([string]$Name) {
    $s = Normalize-Name $Name
    $s = ($s -replace '[ \-\.,/\\:;''"\(\)\[\]\{\}\+=\*\?!@#\$%\^&]', "_")
    while ($s.Contains("__")) { $s = $s.Replace("__", "_") }
    $s = $s.Trim("_")
    if ([string]::IsNullOrWhiteSpace($s)) { $s = "BangMoi" }
    if ($s.Substring(0, 1) -notmatch "[A-Za-z_]") { $s = "T_$s" }
    if ($s.Length -gt 255) { $s = $s.Substring(0, 255) }
    return $s
}

function Get-Worksheet([string]$Name) {
    try { return $script:Workbook.Worksheets.Item($Name) }
    catch { return $null }
}

function Find-ListObject([string]$Name) {
    foreach ($ws in $script:Workbook.Worksheets) {
        foreach ($lo in $ws.ListObjects) {
            if ([string]::Equals($lo.Name, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $lo
            }
        }
    }
    return $null
}

function Clear-Table($ListObject) {
    if ($null -eq $ListObject) { return }
    try {
        if ($null -ne $ListObject.DataBodyRange) {
            $ListObject.DataBodyRange.Rows.Delete() | Out-Null
        }
    }
    catch {
        # Fall through to row-by-row deletion for tables that reject range deletion.
    }

    try {
        for ($i = $ListObject.ListRows.Count; $i -ge 1; $i--) {
            $ListObject.ListRows.Item($i).Delete() | Out-Null
        }
    }
    catch {
        try {
            if ($null -ne $ListObject.DataBodyRange) {
                $ListObject.DataBodyRange.ClearContents() | Out-Null
            }
        }
        catch {
            # Empty Excel tables can throw on DataBodyRange. They are already clear.
        }
    }
}

function Get-FirstTable($Worksheet) {
    if ($null -eq $Worksheet) { return $null }
    if ($Worksheet.ListObjects.Count -lt 1) { return $null }
    return $Worksheet.ListObjects.Item(1)
}

function Table-HasFirstColumnValue($ListObject, [string]$Value) {
    if ($null -eq $ListObject -or $null -eq $ListObject.DataBodyRange) { return $false }
    foreach ($cell in $ListObject.ListColumns.Item(1).DataBodyRange.Cells) {
        if ([string]::Equals([string]$cell.Value2, $Value, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function TableName-Exists([string]$Name) {
    return ($null -ne (Find-ListObject $Name))
}

function Get-UniqueTableName([string]$BaseName) {
    $name = Get-SafeTableName $BaseName
    if (-not (TableName-Exists $name)) { return $name }
    for ($i = 2; $i -lt 10000; $i++) {
        $candidate = "$name`_$i"
        if (-not (TableName-Exists $candidate)) { return $candidate }
    }
    return "BangMoi_$([guid]::NewGuid().ToString('N').Substring(0, 8))"
}

function Add-CustomerToIndexes([string]$CustomerName, [string]$TableName) {
    $loInfo = Find-ListObject "Thong_tin_thanh_toan"
    if ($null -ne $loInfo -and -not (Table-HasFirstColumnValue $loInfo $CustomerName)) {
        $row = $loInfo.ListRows.Add()
        $row.Range.Cells.Item(1, 1).Value2 = $CustomerName
    }

    $loMap = Find-ListObject "Bangdich"
    if ($null -ne $loMap -and -not (Table-HasFirstColumnValue $loMap $CustomerName)) {
        $row = $loMap.ListRows.Add()
        $row.Range.Cells.Item(1, 1).Value2 = $CustomerName
        if ($loMap.ListColumns.Count -ge 2) {
            $row.Range.Cells.Item(1, 2).Value2 = $TableName
        }
    }
}

function Ensure-CustomerSheet([string]$CustomerName) {
    $sheetName = Get-SafeSheetName $CustomerName
    $ws = Get-Worksheet $sheetName
    if ($null -ne $ws) {
        Add-CustomerToIndexes $CustomerName (Get-SafeTableName $CustomerName)
        return $ws
    }

    $wsMau = Get-Worksheet "Mau"
    if ($null -eq $wsMau) {
        throw "Khong tim thay sheet Mau trong template."
    }

    $insertBefore = $script:Workbook.Worksheets.Count - 9 + 1
    $wsMau.Copy($script:Workbook.Worksheets.Item($insertBefore))
    $ws = $script:Excel.ActiveSheet
    $ws.Name = $sheetName

    $lo = Get-FirstTable $ws
    if ($null -ne $lo) {
        $lo.Name = Get-UniqueTableName $CustomerName
        Clear-Table $lo
    }

    try { $ws.Range("B6").MergeArea.Cells.Item(1, 1).Value2 = "Ten KH: $CustomerName" } catch {}
    try { $ws.Range("D6").MergeArea.Cells.Item(1, 1).Value2 = "Ten KH: $CustomerName" } catch {}
    try { $ws.Range("F5").Value2 = 0; $ws.Range("F5").NumberFormat = "#,##0" } catch {}

    Add-CustomerToIndexes $CustomerName (Get-SafeTableName $CustomerName)
    return $ws
}

function Is-NegativeContent([string]$Content) {
    $s = (($Content -replace [char]160, " ") -replace "\s+", " ").Trim().ToLowerInvariant()
    return ($s -eq "mua hàng" -or $s -eq "mua hang" -or $s -eq "gc")
}

function Get-SignedPaymentAmount([string]$Content, $Amount) {
    $value = [math]::Abs((To-Double $Amount))
    $s = (($Content -replace [char]160, " ") -replace "\s+", " ").Trim().ToLowerInvariant()
    if ($s -eq "chi trả" -or $s -eq "chi tra" -or $s -eq "trả tiền" -or $s -eq "tra tien") {
        return -1 * $value
    }
    return $value
}

function Add-CustomerLedgerRow(
    [string]$CustomerName,
    [string]$VoucherNo,
    [string]$DateText,
    [string]$Content,
    [string]$Category,
    [string]$Spec,
    $Quantity,
    $UnitPrice,
    $Payment,
    [string]$Note
) {
    $ws = Ensure-CustomerSheet $CustomerName
    $lo = Get-FirstTable $ws
    if ($null -eq $lo) {
        throw "Sheet [$CustomerName] khong co table cong no."
    }

    $row = $lo.ListRows.Add()
    $range = $row.Range
    $range.Cells.Item(1, 1).Value2 = $VoucherNo
    $range.Cells.Item(1, 2).Value2 = To-ExcelSerial $DateText
    $range.Cells.Item(1, 2).NumberFormat = "dd/mm/yyyy"
    $range.Cells.Item(1, 3).Value2 = $Content
    $range.Cells.Item(1, 4).Value2 = $Category
    $range.Cells.Item(1, 5).Value2 = $Spec
    $qtyValue = [double](To-Double $Quantity)
    $range.Cells.Item(1, 6).Value = $qtyValue
    $range.Cells.Item(1, 6).NumberFormat = "#,##0.###"

    $unit = [double](To-Double $UnitPrice)
    if (Is-NegativeContent $Content) { $unit = -1 * [math]::Abs($unit) }
    $range.Cells.Item(1, 7).Value = $unit
    $range.Cells.Item(1, 7).NumberFormat = "#,##0"

    $range.Cells.Item(1, 8).FormulaR1C1 = "=RC[-1]*RC[-2]"
    $range.Cells.Item(1, 8).NumberFormat = "#,##0"
    if ($null -ne $Payment) {
        $range.Cells.Item(1, 9).Value = [double](To-Double $Payment)
    }
    $range.Cells.Item(1, 9).NumberFormat = "#,##0"
    $range.Cells.Item(1, 10).FormulaR1C1 = "=N(R[-1]C)+RC[-2]-RC[-1]"
    $range.Cells.Item(1, 10).NumberFormat = "#,##0"
    $range.Cells.Item(1, 11).Value2 = $Note

    if (($Content -eq "Bán hàng" -or $Content -eq "Ban hang") -and ([math]::Abs($unit) -lt 0.000001)) {
        $range.Interior.Pattern = 1
        $range.Interior.Color = 65535
    }
    else {
        $range.Interior.Pattern = -4142
    }
}

function Get-NextDataRow([string]$SheetName) {
    $ws = Get-Worksheet $SheetName
    if ($null -eq $ws) { return 2 }
    $last = $ws.Cells.Item($ws.Rows.Count, 1).End(-4162).Row
    if ($last -lt 1) { return 2 }
    return [math]::Max(2, $last + 1)
}

function Initialize-OutputRows([bool]$Clean) {
    if ($Clean) {
        $script:Query1NextRow = 2
        $script:Query2NextRow = 2
        $script:PaymentNextRow = 2
    }
    else {
        $script:Query1NextRow = Get-NextDataRow "Query1"
        $script:Query2NextRow = Get-NextDataRow "Query2"
        $script:PaymentNextRow = Get-NextDataRow "Thanh toán KH"
    }
}

function Set-CellValue($Cell, $Value) {
    if ($null -eq $Value) {
        $Cell.ClearContents()
        return
    }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) {
        $Cell.Value = [double]$Value
    }
    else {
        $Cell.Value = [string]$Value
    }
}

function Resize-TableRange([string]$TableName, [string]$SheetName, [int]$LastRow, [int]$LastColumn) {
    $lo = Find-ListObject $TableName
    $ws = $null
    if (-not [string]::IsNullOrWhiteSpace($SheetName)) {
        $ws = Get-Worksheet $SheetName
    }
    if ($null -eq $ws -and $null -ne $lo) {
        $ws = $lo.Parent
    }
    if ($null -eq $lo -or $null -eq $ws -or $LastRow -lt 1) { return }
    try {
        $range = $ws.Range($ws.Cells.Item(1, 1), $ws.Cells.Item([math]::Max(1, $LastRow), $LastColumn))
        $lo.Resize($range)
    }
    catch {
        # Query-backed tables can refuse resize. The written sheet cells are still kept.
    }
}

function Add-Query1Row([object[]]$Values) {
    $ws = Get-Worksheet "Query1"
    if ($null -eq $ws) { return }
    $rowIndex = $script:Query1NextRow
    for ($i = 0; $i -lt [math]::Min($Values.Count, 11); $i++) {
        $cell = $ws.Cells.Item($rowIndex, $i + 1)
        if ($null -eq $Values[$i]) {
            $cell.ClearContents()
        }
        else {
            Set-CellValue $cell $Values[$i]
        }
        if ($i -eq 2) { $cell.NumberFormat = "dd/mm/yyyy" }
        if ($i -ge 6 -and $i -le 9) { $cell.NumberFormat = "#,##0" }
    }
    $script:Query1NextRow++
}

function Add-Query2Row([object[]]$Values) {
    $ws = Get-Worksheet "Query2"
    if ($null -eq $ws) { return }
    $rowIndex = $script:Query2NextRow
    for ($i = 0; $i -lt [math]::Min($Values.Count, 10); $i++) {
        $cell = $ws.Cells.Item($rowIndex, $i + 1)
        if ($null -eq $Values[$i]) {
            $cell.ClearContents()
        }
        else {
            Set-CellValue $cell $Values[$i]
        }
        if ($i -eq 2) { $cell.NumberFormat = "dd/mm/yyyy" }
        if ($i -ge 6 -and $i -le 8) { $cell.NumberFormat = "#,##0" }
    }
    $script:Query2NextRow++
}

function Add-PaymentSummaryRow($Payment) {
    $lo = Find-ListObject "Thanh_Toan_KH"
    if ($null -eq $lo) { return }
    $ws = $lo.Parent
    $amount = Get-SignedPaymentAmount $Payment.content $Payment.amount
    $rowIndex = $script:PaymentNextRow
    Set-CellValue ($ws.Cells.Item($rowIndex, 1)) ([string]$Payment.customer)
    Set-CellValue ($ws.Cells.Item($rowIndex, 2)) (To-ExcelSerial $Payment.date)
    $ws.Cells.Item($rowIndex, 2).NumberFormat = "dd/mm/yyyy"
    Set-CellValue ($ws.Cells.Item($rowIndex, 3)) ([string]$Payment.content)
    Set-CellValue ($ws.Cells.Item($rowIndex, 4)) ([string]$Payment.method)
    Set-CellValue ($ws.Cells.Item($rowIndex, 5)) ([string]$Payment.account)
    Set-CellValue ($ws.Cells.Item($rowIndex, 6)) $amount
    $ws.Cells.Item($rowIndex, 6).NumberFormat = "#,##0"
    $script:PaymentNextRow++
}

function Update-CustomerBalances() {
    $lastCustomerIndex = $script:Workbook.Worksheets.Count - 9
    for ($i = 2; $i -le $lastCustomerIndex; $i++) {
        $ws = $script:Workbook.Worksheets.Item($i)
        $lo = Get-FirstTable $ws
        if ($null -eq $lo) { continue }
        $balance = 0
        if ($null -ne $lo.DataBodyRange -and $lo.ListRows.Count -gt 0 -and $lo.ListColumns.Count -ge 10) {
            $balance = $lo.ListColumns.Item(10).DataBodyRange.Cells.Item($lo.ListRows.Count, 1).Value2
            if ($null -eq $balance -or [string]$balance -eq "") { $balance = 0 }
        }
        try {
            $ws.Range("F5").Value = [double](To-Double $balance)
            $ws.Range("F5").NumberFormat = "#,##0"
        }
        catch {}
    }
}

function Rebuild-DebtSummary() {
    $wsSummary = Get-Worksheet "Tong hop cong no"
    $lo = Find-ListObject "Tong_hop_cong_no"
    if ($null -eq $wsSummary -or $null -eq $lo) { return }
    Clear-Table $lo

    $lastCustomerIndex = $script:Workbook.Worksheets.Count - 9
    for ($i = 2; $i -le $lastCustomerIndex; $i++) {
        $ws = $script:Workbook.Worksheets.Item($i)
        $table = Get-FirstTable $ws
        if ($null -eq $table -or $null -eq $table.DataBodyRange -or $table.ListRows.Count -eq 0) { continue }
        if ($table.ListColumns.Count -lt 10) { continue }

        $lastBalance = $table.ListColumns.Item(10).DataBodyRange.Cells.Item($table.ListRows.Count, 1).Value2
        if ($null -eq $lastBalance -or [string]$lastBalance -eq "") { continue }

        $row = $lo.ListRows.Add()
        $anchor = $row.Range.Cells.Item(1, 1)
        $wsSummary.Hyperlinks.Add($anchor, "", "'" + $ws.Name.Replace("'", "''") + "'!A1", "", $ws.Name) | Out-Null
        $row.Range.Cells.Item(1, 2).Value = [double](To-Double $lastBalance)
        $row.Range.Cells.Item(1, 2).NumberFormat = "#,##0"
    }
}

function Clear-TemplateData() {
    $lastCustomerIndex = $script:Workbook.Worksheets.Count - 9
    for ($i = 2; $i -le $lastCustomerIndex; $i++) {
        $ws = $script:Workbook.Worksheets.Item($i)
        $lo = Get-FirstTable $ws
        Clear-Table $lo
        try { $ws.Range("F5").Value2 = 0 } catch {}
    }

    foreach ($name in @("Tong_hop_cong_no", "Bang_Tong_Hop", "Check", "Thong_tin_thanh_toan", "Thanh_Toan_KH", "Nhap_Thanh_Toan", "Nhap", "Giay_Nhap", "Bangdich")) {
        Clear-Table (Find-ListObject $name)
    }
}

$payload = Get-Content -Raw -Encoding UTF8 -LiteralPath $DataPath | ConvertFrom-Json

if (-not (Test-Path -LiteralPath $TemplatePath)) {
    throw "Template khong ton tai: $TemplatePath"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
Copy-Item -LiteralPath $TemplatePath -Destination $OutputPath -Force

$script:Excel = New-Object -ComObject Excel.Application
$script:Excel.Visible = $false
$script:Excel.DisplayAlerts = $false
$script:Excel.EnableEvents = $false
$script:Excel.ScreenUpdating = $false
$script:Excel.AutomationSecurity = 3

try {
    $script:Workbook = $script:Excel.Workbooks.Open($OutputPath)

    if ($CleanTemplate -eq 1) {
        Clear-TemplateData
    }
    Initialize-OutputRows ($CleanTemplate -eq 1)

    $customers = As-Array $payload.customers
    foreach ($customer in $customers) {
        if ([string]::IsNullOrWhiteSpace([string]$customer.name)) { continue }
        $ws = Ensure-CustomerSheet ([string]$customer.name)
        $lo = Get-FirstTable $ws
        $tableName = if ($null -ne $lo) { [string]$lo.Name } else { Get-SafeTableName ([string]$customer.name) }
        Add-CustomerToIndexes ([string]$customer.name) $tableName
    }

    foreach ($doc in (As-Array $payload.documents)) {
        $docDate = [string]$doc.date
        $customerName = [string]$doc.customer
        $voucherNo = [string]$doc.voucher_no
        foreach ($line in (As-Array $doc.lines)) {
            $lineContent = [string]$line.line_content
            if ([string]::IsNullOrWhiteSpace($lineContent)) { $lineContent = [string]$doc.content }
            $qty = To-Double $line.quantity
            $unit = To-Double $line.unit_price
            if (Is-NegativeContent $lineContent) { $unit = -1 * [math]::Abs($unit) }
            $amount = $qty * $unit

            Add-CustomerLedgerRow `
                $customerName `
                $voucherNo `
                $docDate `
                $lineContent `
                ([string]$line.category) `
                ([string]$line.spec) `
                $qty `
                $line.unit_price `
                $null `
                ([string]$line.note)

            $serial = To-ExcelSerial $docDate
            Add-Query1Row -Values @($customerName, $voucherNo, $serial, $lineContent, [string]$line.category, [string]$line.spec, $qty, $unit, $amount, $null, [string]$line.note)
            Add-Query2Row -Values @($customerName, $voucherNo, $serial, $lineContent, [string]$line.category, [string]$line.spec, $qty, $unit, $amount, [string]$line.note)
        }
    }

    foreach ($payment in (As-Array $payload.payments)) {
        $amount = Get-SignedPaymentAmount $payment.content $payment.amount
        Add-CustomerLedgerRow `
            ([string]$payment.customer) `
            "" `
            ([string]$payment.date) `
            ([string]$payment.content) `
            ([string]$payment.method) `
            ([string]$payment.account) `
            0 `
            0 `
            $amount `
            ([string]$payment.note)

        Add-PaymentSummaryRow $payment
        $serial = To-ExcelSerial $payment.date
        Add-Query1Row -Values @([string]$payment.customer, "", $serial, [string]$payment.content, [string]$payment.method, [string]$payment.account, 0, 0, 0, $amount, [string]$payment.note)
    }

    Resize-TableRange "Bang_Tong_Hop" "Query1" ($script:Query1NextRow - 1) 11
    Resize-TableRange "Check" "Query2" ($script:Query2NextRow - 1) 10
    Resize-TableRange "Thanh_Toan_KH" "" ($script:PaymentNextRow - 1) 6

    $script:Excel.CalculateFullRebuild()
    Update-CustomerBalances
    Rebuild-DebtSummary

    $script:Workbook.Save()
    $script:Workbook.Close($true)
    Write-Output "EXPORTED=$OutputPath"
}
catch {
    Write-Output ("EXPORT_ERROR: " + $_.Exception.Message)
    if ($_.InvocationInfo) {
        Write-Output ("AT: " + $_.InvocationInfo.PositionMessage)
    }
    if ($_.ScriptStackTrace) {
        Write-Output ("STACK: " + $_.ScriptStackTrace)
    }
    throw
}
finally {
    if ($null -ne $script:Workbook) {
        try { $script:Workbook.Close($false) } catch {}
    }
    if ($null -ne $script:Excel) {
        $script:Excel.DisplayAlerts = $true
        $script:Excel.EnableEvents = $true
        $script:Excel.ScreenUpdating = $true
        $script:Excel.Quit()
        [Runtime.InteropServices.Marshal]::ReleaseComObject($script:Excel) | Out-Null
    }
}
