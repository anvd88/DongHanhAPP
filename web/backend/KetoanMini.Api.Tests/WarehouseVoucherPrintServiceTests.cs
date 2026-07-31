using KetoanMini.Api.Services;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class WarehouseVoucherPrintServiceTests
{
    [Theory]
    [InlineData("Cuộn 430/BA", "0.6x1200xC", "Cuộn 430/BA 0.6x1200xC")]
    [InlineData("  Cuộn 410  ", "  1.0x1250xC  ", "Cuộn 410 1.0x1250xC")]
    [InlineData("Cuộn 430/BA", "", "Cuộn 430/BA")]
    [InlineData("", "0.6x1200xC", "0.6x1200xC")]
    public void ItemDescription_CombinesProductTypeAndSpecification(
        string lineContent,
        string specification,
        string expected)
    {
        Assert.Equal(
            expected,
            WarehouseVoucherPrintService.FormatWarehouseItemDescription(lineContent, specification));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(3, 19)]
    [InlineData(13, 29)]
    public void FirstUnusedRow_FollowsTheNumberOfEnteredLines(int usedLineCount, int expectedRow)
    {
        Assert.Equal(expectedRow, WarehouseVoucherPrintService.GetFirstUnusedWorksheetRow(usedLineCount));
    }

    [Fact]
    public void FullFourteenLineVoucher_DoesNotNeedASlash()
    {
        Assert.Null(WarehouseVoucherPrintService.GetFirstUnusedWorksheetRow(14));
    }

    [Fact]
    public void SlashEndpoints_AreShortenedEquallyAndKeepTheSameCenter()
    {
        const double left = 10;
        const double top = 20;
        const double width = 550;
        const double height = 221;
        var endpoints = WarehouseVoucherPrintService.CalculateUnusedRowsSlashEndpoints(left, top, width, height);

        var startInset = Math.Sqrt(
            Math.Pow((left + width) - endpoints.StartX, 2) +
            Math.Pow(top - endpoints.StartY, 2));
        var endInset = Math.Sqrt(
            Math.Pow(left - endpoints.EndX, 2) +
            Math.Pow((top + height) - endpoints.EndY, 2));

        Assert.Equal(46.5, startInset, precision: 6);
        Assert.Equal(46.5, endInset, precision: 6);
        Assert.Equal(left + (width / 2), (endpoints.StartX + endpoints.EndX) / 2, precision: 6);
        Assert.Equal(top + (height / 2), (endpoints.StartY + endpoints.EndY) / 2, precision: 6);
    }
}
