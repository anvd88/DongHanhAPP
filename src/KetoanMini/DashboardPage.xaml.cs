using WpfControls = System.Windows.Controls;

namespace KetoanMini;

public partial class DashboardPage : WpfControls.UserControl
{
    public DashboardPage(AccountingStore store, AppUser user)
    {
        InitializeComponent();
        LoadDashboard(store, user);
    }

    private void LoadDashboard(AccountingStore store, AppUser user)
    {
        var now = DateOnly.FromDateTime(DateTime.Today);
        var activeCustomers = store.Data.Customers.Count(c => c.IsActive);
        var documents = store.Data.Documents.Count;
        var totalPayments = store.Data.Payments.Sum(p => p.Amount);
        var monthRevenue = store.Data.Payments
            .Where(p => p.Date.Year == now.Year && p.Date.Month == now.Month)
            .Sum(p => p.Amount);

        WelcomeText.Text = $"Chào mừng trở lại, {user.DisplayName}";
        CustomersValue.Text = activeCustomers.ToString();
        DocumentsValue.Text = documents.ToString();
        PaymentsValue.Text = TextUtil.FormatMoney(totalPayments);
        MonthlyRevenueValue.Text = TextUtil.FormatMoney(monthRevenue);
        MonthlyRevenueSub.Text = $"Tháng {now.Month}/{now.Year}";

        ActivityGrid.ItemsSource = BuildRecentRows(store);
    }

    private static List<DashboardActivityRow> BuildRecentRows(AccountingStore store)
    {
        var rows = store.Data.Documents
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.VoucherNo)
            .Take(12)
            .Select(d => new DashboardActivityRow(
                d.VoucherNo,
                d.Date.ToString("dd/MM/yyyy"),
                d.CustomerName,
                d.Content,
                TextUtil.FormatMoney(d.Total)))
            .ToList();

        if (rows.Count > 0)
        {
            return rows;
        }

        return
        [
            new("001", DateTime.Today.ToString("dd/MM/yyyy"), "Khách lẻ", "Chứng từ mẫu", "0"),
            new("BH26-0006", DateTime.Today.AddDays(-1).ToString("dd/MM/yyyy"), "Công ty mẫu", "Bán hàng", "0"),
            new("BH26-0005", DateTime.Today.AddDays(-2).ToString("dd/MM/yyyy"), "Khách hàng", "Bán hàng", "0"),
            new("BH26-0007", DateTime.Today.AddDays(-3).ToString("dd/MM/yyyy"), "Đối tác", "Dịch vụ", "0"),
            new("BH26-0008", DateTime.Today.AddDays(-4).ToString("dd/MM/yyyy"), "Khách hàng", "Gia công", "0"),
            new("7777", DateTime.Today.AddDays(-5).ToString("dd/MM/yyyy"), "Khách hàng", "Thu chi", "0"),
            new("7778", DateTime.Today.AddDays(-6).ToString("dd/MM/yyyy"), "Khách hàng", "Thanh toán", "0"),
            new("BH26-6003", DateTime.Today.AddDays(-7).ToString("dd/MM/yyyy"), "Khách hàng", "Bán hàng", "0"),
            new("BH26-0004", DateTime.Today.AddDays(-8).ToString("dd/MM/yyyy"), "Khách hàng", "Bán hàng", "0")
        ];
    }

    private sealed record DashboardActivityRow(string VoucherNo, string Date, string Customer, string Content, string Total);
}
