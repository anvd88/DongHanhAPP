using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace KetoanMini;

public sealed record ExcelExportProgress(int Percent, string Status);

public static class DirectExcelExporter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static Task ExportCustomerWorkbookAsync(ExportPayload payload, string outputPath, IProgress<ExcelExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            progress?.Report(new ExcelExportProgress(35, "Đang chuẩn hóa dữ liệu khách hàng..."));
            var sheets = BuildSheets(payload);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ExcelExportProgress(45, "Đang tạo workbook OpenXML trực tiếp..."));
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            WritePackageFiles(archive, sheets, payload.GeneratedAt);

            var sheetCount = Math.Max(1, sheets.Count);
            for (var i = 0; i < sheets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var percent = 45 + (int)Math.Round((i + 1) * 45.0 / sheetCount);
                progress?.Report(new ExcelExportProgress(percent, $"Đang ghi sheet {i + 1}/{sheets.Count}: {sheets[i].Name}"));
                WriteWorksheet(archive, sheets[i], i + 1);
            }

            progress?.Report(new ExcelExportProgress(95, "Đang hoàn tất file Excel..."));
        }, cancellationToken);
    }

    private static List<SheetData> BuildSheets(ExportPayload payload)
    {
        var customers = payload.Customers
            .Where(customer => !string.IsNullOrWhiteSpace(customer.Name))
            .GroupBy(customer => customer.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .OrderBy(customer => customer.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var name in payload.Documents.Select(doc => doc.Customer).Concat(payload.Payments.Select(pay => pay.Customer)))
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                customers.All(customer => !string.Equals(customer.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            {
                customers.Add(new ExportCustomer { Name = name.Trim() });
            }
        }

        customers = customers.OrderBy(customer => customer.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sheets = new List<SheetData> { BuildSummarySheet(payload, customers) };
        sheetNames.Add("Tổng hợp");

        foreach (var customer in customers)
        {
            var sheetName = UniqueSheetName(customer.Name, sheetNames);
            sheets.Add(BuildCustomerSheet(payload, customer, sheetName));
        }

        return sheets;
    }

    private static SheetData BuildSummarySheet(ExportPayload payload, IReadOnlyList<ExportCustomer> customers)
    {
        var rows = new List<RowData>
        {
            Row(["Công ty TNHH Inox Cường Phát"], CellStyle.Title),
            Row(["Báo cáo công nợ theo khách hàng"], CellStyle.Subtitle),
            Row([$"Thời điểm xuất: {DateTime.Now:dd/MM/yyyy HH:mm}"], CellStyle.Normal),
            Row([], CellStyle.Normal),
            Row(["Tên KH", "MST", "Điện thoại", "Địa chỉ", "Tổng bán hàng", "Tổng mua/chi", "Đã thanh toán", "Còn lại", "Số giao dịch"], CellStyle.Header)
        };

        foreach (var customer in customers)
        {
            var transactions = BuildCustomerTransactions(payload, customer.Name);
            var totalSales = transactions.Where(item => item.Type == "Bán hàng").Sum(item => item.Debit);
            var totalPurchases = transactions.Where(item => item.Type == "Mua hàng" || item.Type == "Chi tiền").Sum(item => item.Debit);
            var totalPayments = transactions.Sum(item => item.Payment);
            var balance = totalSales + totalPurchases - totalPayments;

            rows.Add(Row([
                Cell.Text(customer.Name),
                Cell.Text(customer.TaxCode),
                Cell.Text(customer.Phone),
                Cell.Text(customer.Address),
                Cell.Number(totalSales),
                Cell.Number(totalPurchases),
                Cell.Number(totalPayments),
                Cell.Number(balance),
                Cell.Number(transactions.Count)
            ], CellStyle.Normal));
        }

        rows.Add(Row([], CellStyle.Normal));
        rows.Add(Row([
            Cell.Text("Tổng cộng"),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Number(rows.Skip(5).Select(row => row.Cells.ElementAtOrDefault(4)?.NumberValue ?? 0m).Sum()),
            Cell.Number(rows.Skip(5).Select(row => row.Cells.ElementAtOrDefault(5)?.NumberValue ?? 0m).Sum()),
            Cell.Number(rows.Skip(5).Select(row => row.Cells.ElementAtOrDefault(6)?.NumberValue ?? 0m).Sum()),
            Cell.Number(rows.Skip(5).Select(row => row.Cells.ElementAtOrDefault(7)?.NumberValue ?? 0m).Sum()),
            Cell.Text("")
        ], CellStyle.Total));

        return new SheetData("Tổng hợp", rows, [28, 16, 18, 38, 16, 16, 16, 16, 14], 5);
    }

    private static SheetData BuildCustomerSheet(ExportPayload payload, ExportCustomer customer, string sheetName)
    {
        var transactions = BuildCustomerTransactions(payload, customer.Name);
        var totalSales = transactions.Where(item => item.Type == "Bán hàng").Sum(item => item.Debit);
        var totalPurchases = transactions.Where(item => item.Type == "Mua hàng" || item.Type == "Chi tiền").Sum(item => item.Debit);
        var totalPayments = transactions.Sum(item => item.Payment);
        var balance = totalSales + totalPurchases - totalPayments;

        var rows = new List<RowData>
        {
            Row([$"Chi tiết công nợ - {customer.Name}"], CellStyle.Title),
            Row([$"Thời điểm xuất: {DateTime.Now:dd/MM/yyyy HH:mm}"], CellStyle.Normal),
            Row(["Tên KH", customer.Name, "Mã số thuế", customer.TaxCode], CellStyle.Normal),
            Row(["Điện thoại", customer.Phone, "Địa chỉ", customer.Address], CellStyle.Normal),
            Row(["Tổng bán hàng", totalSales, "Tổng mua/chi", totalPurchases, "Đã thanh toán", totalPayments, "Còn lại", balance], CellStyle.Total),
            Row([], CellStyle.Normal),
            Row(["Ngày", "Loại", "Số phiếu", "Tên đã nhập", "Diễn giải", "Hàng hóa / Nội dung", "TK / Phương thức", "Số lượng", "Đơn giá", "Phát sinh", "Thanh toán", "Còn lại", "Ghi chú"], CellStyle.Header)
        };

        var runningBalance = 0m;
        foreach (var item in transactions)
        {
            runningBalance += item.Debit - item.Payment;
            rows.Add(Row([
                Cell.Text(item.Date),
                Cell.Text(item.Type),
                Cell.Text(item.VoucherNo),
                Cell.Text(item.CustomerInputName),
                Cell.Text(item.Description),
                Cell.Text(item.Detail),
                Cell.Text(item.AccountOrMethod),
                item.Quantity.HasValue ? Cell.Number(item.Quantity.Value) : Cell.Text(""),
                item.UnitPrice.HasValue ? Cell.Number(item.UnitPrice.Value) : Cell.Text(""),
                item.Debit == 0 ? Cell.Text("") : Cell.Number(item.Debit),
                item.Payment == 0 ? Cell.Text("") : Cell.Number(item.Payment),
                Cell.Number(runningBalance),
                Cell.Text(item.Note)
            ], CellStyle.Normal));
        }

        if (transactions.Count == 0)
        {
            rows.Add(Row(["", "Chưa có giao dịch", "", "", "", "", "", "", "", "", "", "", ""], CellStyle.Normal));
        }

        rows.Add(Row([], CellStyle.Normal));
        rows.Add(Row([
            Cell.Text("Tổng cộng"),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Text(""),
            Cell.Number(transactions.Sum(item => item.Debit)),
            Cell.Number(transactions.Sum(item => item.Payment)),
            Cell.Number(balance),
            Cell.Text("")
        ], CellStyle.Total));

        return new SheetData(sheetName, rows, [13, 14, 16, 22, 28, 34, 26, 12, 15, 16, 16, 16, 32], 7);
    }

    private static List<CustomerExportTransaction> BuildCustomerTransactions(ExportPayload payload, string customerName)
    {
        var normalizedCustomerName = Normalize(customerName);
        var sequence = 0;
        var rows = new List<CustomerExportTransaction>();

        foreach (var document in payload.Documents.Where(doc => Normalize(doc.Customer) == normalizedCustomerName))
        {
            var type = DocumentTransactionType(document.Content);
            IReadOnlyList<ExportDocumentLine> lines = document.Lines.Count == 0
                ? [new ExportDocumentLine { LineContent = document.Content, Quantity = 1, UnitPrice = 0, Note = document.Note }]
                : document.Lines;

            foreach (var line in lines)
            {
                rows.Add(new CustomerExportTransaction(
                    document.Date,
                    sequence++,
                    type,
                    document.VoucherNo,
                    document.CustomerInputName,
                    document.Content,
                    line.LineContent,
                    JoinText(line.Category, line.Spec),
                    line.Quantity,
                    line.UnitPrice,
                    Math.Abs(line.Quantity * line.UnitPrice),
                    0m,
                    JoinText(document.Note, line.Note)));
            }
        }

        foreach (var payment in payload.Payments.Where(pay => Normalize(pay.Customer) == normalizedCustomerName))
        {
            var signedAmount = IsExpensePayment(payment.Content) ? -Math.Abs(payment.Amount) : Math.Abs(payment.Amount);
            rows.Add(new CustomerExportTransaction(
                payment.Date,
                sequence++,
                signedAmount >= 0 ? "Thu tiền" : "Chi tiền",
                "",
                payment.CustomerInputName,
                payment.Content,
                payment.Content,
                JoinText(payment.Method, payment.Account),
                null,
                null,
                signedAmount < 0 ? Math.Abs(signedAmount) : 0m,
                signedAmount >= 0 ? Math.Abs(signedAmount) : 0m,
                payment.Note));
        }

        return rows
            .OrderBy(row => ParseDate(row.Date))
            .ThenBy(row => row.Sequence)
            .ToList();
    }

    private static void WritePackageFiles(ZipArchive archive, IReadOnlyList<SheetData> sheets, string generatedAt)
    {
        WriteEntry(archive, "[Content_Types].xml", ContentTypesXml(sheets.Count));
        WriteEntry(archive, "_rels/.rels", PackageRelsXml());
        WriteEntry(archive, "docProps/app.xml", AppXml(sheets));
        WriteEntry(archive, "docProps/core.xml", CoreXml(generatedAt));
        WriteEntry(archive, "xl/workbook.xml", WorkbookXml(sheets));
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml(sheets.Count));
        WriteEntry(archive, "xl/styles.xml", StylesXml());
    }

    private static void WriteWorksheet(ZipArchive archive, SheetData sheet, int index)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append($"""<worksheet xmlns="{SpreadsheetNamespace}" xmlns:r="{RelationshipNamespace}">""");
        if (sheet.FreezeTopRows > 0)
        {
            var topLeft = $"A{sheet.FreezeTopRows + 1}";
            sb.Append($"""<sheetViews><sheetView workbookViewId="0"><pane ySplit="{sheet.FreezeTopRows}" topLeftCell="{topLeft}" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        }

        sb.Append("<cols>");
        for (var i = 0; i < sheet.ColumnWidths.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<col min="{i + 1}" max="{i + 1}" width="{sheet.ColumnWidths[i]}" customWidth="1"/>""");
        }
        sb.Append("</cols><sheetData>");

        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            var row = sheet.Rows[rowIndex];
            sb.Append(CultureInfo.InvariantCulture, $"""<row r="{rowIndex + 1}">""");
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                AppendCell(sb, row.Cells[columnIndex], row.Style, rowIndex + 1, columnIndex + 1);
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        WriteEntry(archive, $"xl/worksheets/sheet{index}.xml", sb.ToString());
    }

    private static void AppendCell(StringBuilder sb, Cell cell, CellStyle rowStyle, int row, int column)
    {
        var reference = $"{ColumnName(column)}{row}";
        var style = CellStyleIndex(cell.StyleOverride ?? rowStyle, cell.IsNumber);
        if (cell.IsNumber)
        {
            var value = cell.NumberValue.ToString(CultureInfo.InvariantCulture);
            sb.Append(CultureInfo.InvariantCulture, $"""<c r="{reference}" s="{style}"><v>{value}</v></c>""");
            return;
        }

        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{reference}" t="inlineStr" s="{style}"><is><t xml:space="preserve">{Escape(cell.TextValue)}</t></is></c>""");
    }

    private static string ContentTypesXml(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        sb.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""");
        sb.Append("""<Default Extension="xml" ContentType="application/xml"/>""");
        sb.Append("""<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""");
        sb.Append("""<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (var i = 1; i <= sheetCount; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        }
        sb.Append("""<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>""");
        sb.Append("""<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>""");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string PackageRelsXml()
    {
        return $$"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{{PackageRelationshipNamespace}}"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>""";
    }

    private static string WorkbookXml(IReadOnlyList<SheetData> sheets)
    {
        var sb = new StringBuilder();
        sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{SpreadsheetNamespace}" xmlns:r="{RelationshipNamespace}"><sheets>""");
        for (var i = 0; i < sheets.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<sheet name="{EscapeAttribute(sheets[i].Name)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
        }
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string WorkbookRelsXml(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{PackageRelationshipNamespace}">""");
        for (var i = 1; i <= sheetCount; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        }
        sb.Append(CultureInfo.InvariantCulture, $"""<Relationship Id="rId{sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string AppXml(IReadOnlyList<SheetData> sheets)
    {
        var sheetNames = string.Concat(sheets.Select(sheet => $"<vt:lpstr>{Escape(sheet.Name)}</vt:lpstr>"));
        return $$"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>KetoanMini</Application><DocSecurity>0</DocSecurity><ScaleCrop>false</ScaleCrop><HeadingPairs><vt:vector size="2" baseType="variant"><vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant><vt:variant><vt:i4>{{sheets.Count}}</vt:i4></vt:variant></vt:vector></HeadingPairs><TitlesOfParts><vt:vector size="{{sheets.Count}}" baseType="lpstr">{{sheetNames}}</vt:vector></TitlesOfParts></Properties>""";
    }

    private static string CoreXml(string generatedAt)
    {
        var created = DateTime.TryParse(generatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var generated)
            ? generated.ToUniversalTime()
            : DateTime.UtcNow;
        var timestamp = created.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return $$"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:creator>KetoanMini</dc:creator><cp:lastModifiedBy>KetoanMini</cp:lastModifiedBy><dcterms:created xsi:type="dcterms:W3CDTF">{{timestamp}}</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}</dcterms:modified></cp:coreProperties>""";
    }

    private static string StylesXml()
    {
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0"/></numFmts><fonts count="4"><font><sz val="10"/><name val="Segoe UI"/></font><font><b/><sz val="12"/><name val="Segoe UI"/></font><font><b/><sz val="16"/><name val="Segoe UI"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="10"/><name val="Segoe UI"/></font></fonts><fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF105E8F"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEAF4FB"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD9E2EA"/></left><right style="thin"><color rgb="FFD9E2EA"/></right><top style="thin"><color rgb="FFD9E2EA"/></top><bottom style="thin"><color rgb="FFD9E2EA"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="8"><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/><xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="0" fontId="3" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/><xf numFmtId="0" fontId="1" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/><xf numFmtId="164" fontId="1" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1"/><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";
    }

    private static RowData Row(object[] values, CellStyle style)
    {
        return new RowData(values.Select(value => value switch
        {
            Cell cell => cell,
            decimal number => Cell.Number(number),
            int number => Cell.Number(number),
            long number => Cell.Number(number),
            double number => Cell.Number((decimal)number),
            _ => Cell.Text(Convert.ToString(value, CultureInfo.CurrentCulture) ?? "")
        }).ToList(), style);
    }

    private static RowData Row(Cell[] values, CellStyle style)
    {
        return new RowData(values.ToList(), style);
    }

    private static int CellStyleIndex(CellStyle style, bool isNumber)
    {
        return (style, isNumber) switch
        {
            (CellStyle.Title, _) => 1,
            (CellStyle.Subtitle, _) => 2,
            (CellStyle.Header, _) => 3,
            (CellStyle.Total, true) => 6,
            (CellStyle.Total, false) => 4,
            (CellStyle.Blank, _) => 7,
            (_, true) => 5,
            _ => 0
        };
    }

    private static string UniqueSheetName(string name, HashSet<string> existingNames)
    {
        var baseName = SafeSheetName(name);
        var candidate = baseName;
        for (var i = 2; existingNames.Contains(candidate); i++)
        {
            var suffix = $"_{i}";
            candidate = baseName.Length + suffix.Length > 31
                ? baseName[..(31 - suffix.Length)] + suffix
                : baseName + suffix;
        }

        existingNames.Add(candidate);
        return candidate;
    }

    private static string SafeSheetName(string name)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "Khach hang" : name.Trim();
        foreach (var invalid in new[] { '\\', '/', '?', '*', '[', ']', ':' })
        {
            safe = safe.Replace(invalid, ' ');
        }

        safe = string.Join(" ", safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return safe.Length > 31 ? safe[..31] : safe;
    }

    private static string ColumnName(int column)
    {
        var name = "";
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }
        return name;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? "";
    }

    private static string EscapeAttribute(string value)
    {
        return Escape(value).Replace("\"", "&quot;");
    }

    private static string Normalize(string value)
    {
        return TextUtil.RemoveDiacritics(value).Trim().ToLowerInvariant();
    }

    private static DateOnly ParseDate(string value)
    {
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateOnly.FromDateTime(DateTime.Today);
    }

    private static string DocumentTransactionType(string content)
    {
        var normalized = Normalize(content);
        if (normalized.Contains("ban hang", StringComparison.OrdinalIgnoreCase))
        {
            return "Bán hàng";
        }

        if (normalized.Contains("mua hang", StringComparison.OrdinalIgnoreCase))
        {
            return "Mua hàng";
        }

        return content;
    }

    private static bool IsExpensePayment(string content)
    {
        var normalized = Normalize(content);
        return normalized is "chi tra" or "tra tien";
    }

    private static string JoinText(params string[] parts)
    {
        return string.Join(" - ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));
    }

    private sealed record SheetData(string Name, List<RowData> Rows, List<double> ColumnWidths, int FreezeTopRows);
    private sealed record RowData(List<Cell> Cells, CellStyle Style);
    private sealed record CustomerExportTransaction(string Date, int Sequence, string Type, string VoucherNo, string CustomerInputName, string Description, string Detail, string AccountOrMethod, decimal? Quantity, decimal? UnitPrice, decimal Debit, decimal Payment, string Note);

    private enum CellStyle
    {
        Normal,
        Title,
        Subtitle,
        Header,
        Total,
        Blank
    }

    private sealed record Cell(string TextValue, decimal NumberValue, bool IsNumber, CellStyle? StyleOverride = null)
    {
        public static Cell Text(string value, CellStyle? style = null)
        {
            return new Cell(value, 0m, false, style);
        }

        public static Cell Number(decimal value, CellStyle? style = null)
        {
            return new Cell("", value, true, style);
        }
    }
}
