using KetoanMini.Api.Endpoints;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class AnniversaryGreetingTests
{
    private static readonly HrEndpoints.AnniversaryLetterRow Template = new(
        Enabled: true,
        Title: "Tri ân {so_nam} năm gắn bó",
        Body: "Thân gửi {ten}, vào làm từ {ngay_vao_lam}.",
        Signature: "Cảm ơn {ten}!");

    [Fact]
    public void ExactAnniversary_ShowsFilledLetter()
    {
        var result = HrEndpoints.BuildAnniversaryGreeting(
            Template, "Nguyễn Văn An", new DateOnly(2021, 7, 21), new DateOnly(2026, 7, 21));

        Assert.True(result.Show);
        Assert.Equal(5, result.Years);
        Assert.Equal("2026-07-21", result.AnniversaryDate);
        Assert.Equal("anniv-2026-5", result.Key);
        Assert.Equal("Tri ân 5 năm gắn bó", result.Title);
        Assert.Equal("Thân gửi Nguyễn Văn An, vào làm từ 21/07/2021.", result.Body);
        Assert.Equal("Cảm ơn Nguyễn Văn An!", result.Signature);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(-1, false)]
    public void GreetingWindow_IsAnniversaryDayPlusSixDays(int offsetDays, bool expected)
    {
        var anniversary = new DateOnly(2026, 7, 21);
        var result = HrEndpoints.BuildAnniversaryGreeting(
            Template, "An", new DateOnly(2020, 7, 21), anniversary.AddDays(offsetDays));

        Assert.Equal(expected, result.Show);
    }

    [Fact]
    public void WindowCrossingNewYear_StillShowsPreviousYearsAnniversary()
    {
        var result = HrEndpoints.BuildAnniversaryGreeting(
            Template, "An", new DateOnly(2020, 12, 30), new DateOnly(2027, 1, 2));

        Assert.True(result.Show);
        Assert.Equal(6, result.Years);
        Assert.Equal("2026-12-30", result.AnniversaryDate);
    }

    [Fact]
    public void LeapDayAnniversary_UsesFebruary28InNonLeapYear()
    {
        Assert.Equal(
            new DateOnly(2025, 2, 28),
            HrEndpoints.AnniversaryInYear(new DateOnly(2024, 2, 29), 2025));

        var result = HrEndpoints.BuildAnniversaryGreeting(
            Template, "An", new DateOnly(2024, 2, 29), new DateOnly(2025, 2, 28));
        Assert.True(result.Show);
        Assert.Equal(1, result.Years);
    }

    [Fact]
    public void DisabledMissingHireDateOrFirstYear_DoesNotShow()
    {
        var today = new DateOnly(2026, 7, 21);
        Assert.False(HrEndpoints.BuildAnniversaryGreeting(Template with { Enabled = false }, "An", new DateOnly(2020, 7, 21), today).Show);
        Assert.False(HrEndpoints.BuildAnniversaryGreeting(Template, "An", null, today).Show);
        Assert.False(HrEndpoints.BuildAnniversaryGreeting(Template, "An", today, today).Show);
    }

    [Fact]
    public void Preview_AlwaysBuildsFiveYearLetterWithoutChangingRealHireDate()
    {
        var result = HrEndpoints.BuildAnniversaryPreview(
            Template with { Enabled = false }, "Nguyễn Văn An", new DateOnly(2026, 7, 21));

        Assert.True(result.Show);
        Assert.True(result.Preview);
        Assert.Equal(5, result.Years);
        Assert.Equal("preview-anniv-2026-07-21-5", result.Key);
        Assert.Contains("21/07/2021", result.Body);
    }
}
