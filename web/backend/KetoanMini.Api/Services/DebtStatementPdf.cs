using System.Globalization;
using System.Text;
using KetoanMini.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KetoanMini.Api.Services;

/// <summary>
/// Sổ chi tiết công nợ phải thu của một khách hàng, kết xuất PDF để in hoặc gửi cho khách đối chiếu.
/// </summary>
/// <remarks>
/// Phần đầu thư lặp lại đúng nội dung đang in trên mẫu Templates/PhieuXuatKho.xlsx. Hai tờ giấy
/// khách nhận được phải cùng một danh tính công ty, nên đổi địa chỉ hay số tài khoản thì phải sửa
/// cả hai nơi.
/// </remarks>
public static class DebtStatementPdf
{
    private const string CompanyName = "CÔNG TY TNHH INOX CƯỜNG PHÁT";
    private const string CompanyAddress =
        "Đ/C: Số 12, Đường Gamuda Garden 3-7/1B, Khu đô thị Gamuda Gardens, Phường Hoàng Mai, Thành phố Hà Nội";
    private const string CompanyWarehouse =
        "ĐC/VPGD - Kho: Xóm 3 - Thôn Văn Giáp - Xã Thường Tín - Thành phố Hà Nội";
    private const string CompanyTax = "MST: 0105844593 - ĐĐ: 0919.304.316 / 0834.304.316";
    private const string CompanyBank1 =
        "TK1: 020017028686 tại Ngân hàng Sacombank - PGD Tân Mai - CN Thanh Trì - Hà Nội";
    private const string CompanyBank2 = "TK2: 1037526789 tại Ngân hàng Vietcombank - CN Tây Hồ - Hà Nội";

    // Phông lấy từ hệ điều hành của máy chủ. Times New Roman là phông quen mắt của chứng từ kế toán
    // Việt Nam và có sẵn dấu tiếng Việt. Nếu có ngày chuyển máy chủ sang Linux thì phải nhúng tệp
    // phông rồi đăng ký qua FontManager, vì QuestPDF chỉ tìm phông đã cài trong hệ điều hành.
    private const string Font = "Times New Roman";

    private static readonly NumberFormatInfo Money = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    static DebtStatementPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Tên tệp gợi ý cho trình duyệt: có tên khách và kỳ để tải nhiều khách không đè lên nhau.</summary>
    public static string FileName(DebtDetailDto detail)
    {
        var period = detail.From is null && detail.To is null
            ? "toan-bo"
            : $"{Stamp(detail.From)}-{Stamp(detail.To)}";
        return $"Cong-no-{Slug(detail.Customer.Name)}-{period}.pdf";
    }

    public static byte[] Render(DebtDetailDto detail, List<DebtVoucherDto> vouchers)
        => Build(detail, vouchers).GeneratePdf();

    /// <summary>Bản dựng tài liệu, tách riêng để test còn kết xuất được ra ảnh mà soi bố cục.</summary>
    internal static IDocument Build(DebtDetailDto detail, List<DebtVoucherDto> vouchers)
    {
        // Sổ trả về từ API xếp mới nhất trước cho bảng trên web dễ nhìn; bản in thì phải xuôi theo
        // thời gian, nếu không cột còn nợ đọc ngược.
        var rows = Enumerable.Reverse(detail.Transactions).ToList();
        var summary = detail.Summary;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(10));

                // Trang đầu đã có đầu thư đầy đủ, nhắc lại tên công ty ngay dưới là thừa; từ trang
                // hai trở đi thì cần, vì tờ rời dễ lạc khỏi tập.
                page.Header().SkipOnce().PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem().Text(CompanyName).FontSize(9).SemiBold();
                    row.RelativeItem().AlignRight().Text($"Công nợ: {detail.Customer.Name}").FontSize(9);
                });

                page.Content().PaddingVertical(6).Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Element(Letterhead);
                    column.Item().Element(c => Title(c, detail));
                    column.Item().Element(c => CustomerBlock(c, detail));
                    column.Item().Element(c => SummaryBlock(c, summary));
                    column.Item().Element(c => Ledger(c, rows, summary));
                    if (vouchers.Count > 0) column.Item().Element(c => VoucherDetails(c, vouchers));
                    column.Item().Element(Signatures);
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).Italic());
                        t.Span("In lúc ");
                        t.Span(DateTime.Now.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture));
                    });
                    row.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8));
                        t.Span("Trang ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        });
    }

    private static void Letterhead(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text(CompanyName).FontSize(12).Bold();
            column.Item().Text(CompanyAddress).FontSize(8.5f);
            column.Item().Text(CompanyWarehouse).FontSize(8.5f);
            column.Item().Text(CompanyTax).FontSize(8.5f);
            column.Item().Text(CompanyBank1).FontSize(8.5f);
            column.Item().Text(CompanyBank2).FontSize(8.5f);
        });
    }

    private static void Title(IContainer container, DebtDetailDto detail)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text("SỔ CHI TIẾT CÔNG NỢ PHẢI THU").FontSize(15).Bold();
            column.Item().AlignCenter().Text(PeriodLabel(detail.From, detail.To)).FontSize(10).Italic();
        });
    }

    private static void CustomerBlock(IContainer container, DebtDetailDto detail)
    {
        var customer = detail.Customer;
        container.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(6).Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(t =>
            {
                t.Span("Khách hàng: ");
                t.Span(customer.Name).Bold();
            });
            if (!string.IsNullOrWhiteSpace(customer.Address))
                column.Item().Text($"Địa chỉ: {customer.Address}");

            var line = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(customer.TaxCode)) line.Append($"MST: {customer.TaxCode}");
            if (!string.IsNullOrWhiteSpace(customer.Phone))
            {
                if (line.Length > 0) line.Append("   -   ");
                line.Append($"Điện thoại: {customer.Phone}");
            }
            if (line.Length > 0) column.Item().Text(line.ToString());
        });
    }

    private static void SummaryBlock(IContainer container, DebtSummaryDto summary)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.RelativeColumn();
                c.RelativeColumn();
                c.RelativeColumn();
                c.RelativeColumn();
            });

            table.Header(header =>
            {
                Box(header.Cell()).AlignCenter().Text("Dư đầu kỳ").FontSize(9).SemiBold();
                Box(header.Cell()).AlignCenter().Text("Bán trong kỳ").FontSize(9).SemiBold();
                Box(header.Cell()).AlignCenter().Text("Hàng trả lại").FontSize(9).SemiBold();
                Box(header.Cell()).AlignCenter().Text("Đã thu").FontSize(9).SemiBold();
                Box(header.Cell()).AlignCenter().Text("Dư cuối kỳ").FontSize(9).SemiBold();
            });

            Box(table.Cell()).AlignRight().Text(Amount(summary.CarriedBalance));
            Box(table.Cell()).AlignRight().Text(Amount(summary.SalesTotal));
            Box(table.Cell()).AlignRight().Text(Amount(summary.ReturnsTotal));
            Box(table.Cell()).AlignRight().Text(Amount(summary.CollectedTotal));
            Box(table.Cell()).AlignRight().Text(Amount(summary.Balance)).Bold();
        });
    }

    private static void Ledger(IContainer container, List<DebtTransactionDto> rows, DebtSummaryDto summary)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Diễn biến công nợ trong kỳ").FontSize(11).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(56);
                    c.ConstantColumn(70);
                    c.RelativeColumn();
                    c.ConstantColumn(74);
                    c.ConstantColumn(74);
                    c.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    Head(header.Cell()).Text("Ngày").FontSize(9).SemiBold();
                    Head(header.Cell()).Text("Chứng từ").FontSize(9).SemiBold();
                    Head(header.Cell()).Text("Diễn giải").FontSize(9).SemiBold();
                    Head(header.Cell()).AlignRight().Text("Tăng nợ").FontSize(9).SemiBold();
                    Head(header.Cell()).AlignRight().Text("Giảm nợ").FontSize(9).SemiBold();
                    Head(header.Cell()).AlignRight().Text("Còn nợ").FontSize(9).SemiBold();
                });

                foreach (var row in rows)
                {
                    var off = row.Cancelled;
                    Body(table.Cell()).Text(row.Date.ToString("dd/MM/yy", CultureInfo.InvariantCulture)).Italic(off);
                    Body(table.Cell()).Text(row.Reference).Italic(off);
                    Body(table.Cell()).Text(Describe(row)).Italic(off);
                    Body(table.Cell()).AlignRight().Text(Amount(row.Debit, blankZero: true)).Italic(off);
                    Body(table.Cell()).AlignRight().Text(Amount(row.Credit, blankZero: true)).Italic(off);
                    Body(table.Cell()).AlignRight().Text(Amount(row.RunningBalance)).SemiBold();
                }

                table.Footer(footer =>
                {
                    Foot(footer.Cell().ColumnSpan(3)).AlignRight().Text("Cộng phát sinh trong kỳ").SemiBold();
                    Foot(footer.Cell()).AlignRight().Text(Amount(summary.SalesTotal)).SemiBold();
                    Foot(footer.Cell()).AlignRight()
                        .Text(Amount(summary.ReturnsTotal + summary.CollectedTotal)).SemiBold();
                    Foot(footer.Cell()).AlignRight().Text(Amount(summary.Balance)).Bold();
                });
            });

            column.Item().PaddingTop(2).Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8.5f).Italic());
                t.Span("Dòng in nghiêng là chứng từ đã huỷ, giữ lại để đối chiếu và không tính vào số dư. ");
                t.Span("Hàng trả lại làm giảm số đã bán, không phải khách trả tiền.");
            });
        });
    }

    private static void VoucherDetails(IContainer container, List<DebtVoucherDto> vouchers)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Chi tiết hàng hoá theo từng phiếu").FontSize(11).SemiBold();

            foreach (var voucher in vouchers)
            {
                column.Item().PaddingTop(4).Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(9.5f));
                    t.Span(voucher.Kind == "return" ? "Phiếu trả hàng " : "Phiếu bán hàng ").SemiBold();
                    t.Span(voucher.VoucherNo).SemiBold();
                    t.Span($"   ngày {Day(voucher.Date)}");
                    if (!string.IsNullOrWhiteSpace(voucher.Content)) t.Span($"   -   {voucher.Content}");
                    t.Span($"   -   {Amount(voucher.Total)} đ").SemiBold();
                });

                if (voucher.Lines.Count == 0) continue;

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(96);
                        c.ConstantColumn(62);
                        c.ConstantColumn(74);
                        c.ConstantColumn(80);
                    });

                    table.Header(header =>
                    {
                        Head(header.Cell()).Text("Chủng loại hàng hoá").FontSize(9).SemiBold();
                        Head(header.Cell()).Text("Quy cách").FontSize(9).SemiBold();
                        Head(header.Cell()).AlignRight().Text("Khối lượng").FontSize(9).SemiBold();
                        Head(header.Cell()).AlignRight().Text("Đơn giá").FontSize(9).SemiBold();
                        Head(header.Cell()).AlignRight().Text("Thành tiền").FontSize(9).SemiBold();
                    });

                    foreach (var line in voucher.Lines)
                    {
                        Body(table.Cell()).Text(line.Content).FontSize(9);
                        Body(table.Cell()).Text(line.Spec).FontSize(9);
                        Body(table.Cell()).AlignRight().Text(Quantity(line.Quantity)).FontSize(9);
                        Body(table.Cell()).AlignRight().Text(Amount(line.UnitPrice)).FontSize(9);
                        Body(table.Cell()).AlignRight().Text(Amount(line.Amount)).FontSize(9);
                    }
                });
            }
        });
    }

    private static void Signatures(IContainer container)
    {
        container.PaddingTop(18).Row(row =>
        {
            Sign(row.RelativeItem(), "NGƯỜI LẬP BIỂU");
            Sign(row.RelativeItem(), "KẾ TOÁN TRƯỞNG");
            Sign(row.RelativeItem(), "KHÁCH HÀNG XÁC NHẬN");
        });

        static void Sign(IContainer container, string title) => container.Column(column =>
        {
            column.Item().AlignCenter().Text(title).FontSize(9.5f).SemiBold();
            column.Item().AlignCenter().Text("(Ký, ghi rõ họ tên)").FontSize(8.5f).Italic();
            column.Item().Height(52);
        });
    }

    private static IContainer Box(IContainer cell)
        => cell.Border(0.5f).BorderColor(Colors.Grey.Medium).PaddingVertical(4).PaddingHorizontal(4);

    private static IContainer Head(IContainer cell)
        => cell.Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(3).PaddingHorizontal(4);

    private static IContainer Body(IContainer cell)
        => cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3).PaddingHorizontal(4);

    private static IContainer Foot(IContainer cell)
        => cell.BorderTop(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(4).PaddingHorizontal(4);

    private static string Describe(DebtTransactionDto row)
    {
        // Hai dòng số dư tự mô tả đủ rồi; gắn thêm nhãn chỉ tạo ra "Dư nợ đầu kỳ: Dư nợ đầu kỳ".
        if (row.Kind is "carried" or "opening" && !string.IsNullOrWhiteSpace(row.Description))
            return row.Description;

        var label = row.Kind switch
        {
            "carried" => "Dư nợ đầu kỳ",
            "opening" => "Số dư đầu kỳ",
            "sale" => "Bán hàng",
            "return" => "Khách trả hàng",
            "receipt" => "Phiếu thu",
            _ => "Khách thanh toán",
        };
        return string.IsNullOrWhiteSpace(row.Description) ? label : $"{label}: {row.Description}";
    }

    private static string PeriodLabel(DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return "Toàn bộ phát sinh từ trước tới nay";
        if (from is null) return $"Đến hết ngày {Day(to!.Value)}";
        if (to is null) return $"Từ ngày {Day(from.Value)}";

        if (from.Value.Day == 1 && to.Value == LastDayOfMonth(to.Value) && from.Value.Year == to.Value.Year)
        {
            if (from.Value.Month == 1 && to.Value.Month == 12) return $"Năm {from.Value.Year}";
            if (from.Value.Month == to.Value.Month) return $"Tháng {from.Value.Month:00} năm {from.Value.Year}";
        }
        return $"Từ ngày {Day(from.Value)} đến ngày {Day(to.Value)}";
    }

    private static DateOnly LastDayOfMonth(DateOnly value)
        => new(value.Year, value.Month, DateTime.DaysInMonth(value.Year, value.Month));

    private static string Day(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Stamp(DateOnly? value) => value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "x";

    private static string Amount(decimal value, bool blankZero = false)
        => blankZero && value == 0 ? "" : value.ToString("#,##0", Money);

    private static string Quantity(decimal value)
        => value == Math.Floor(value) ? value.ToString("#,##0", Money) : value.ToString("#,##0.00", Money);

    /// <summary>Bỏ dấu tiếng Việt để tên tệp tải về không bị mã hoá thành chuỗi khó đọc.</summary>
    private static string Slug(string value)
    {
        var normalized = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
