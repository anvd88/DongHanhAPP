using KetoanMini.Api.Endpoints;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class OvertimeCalculationTests
{
    [Theory]
    [InlineData(7, 46, 0)]
    [InlineData(7, 45, 15)]
    [InlineData(7, 30, 30)]
    [InlineData(8, 0, 0)]
    [InlineData(8, 15, 0)]
    public void CalculateOvertimeMinutes_AppliesMorningMinimum(
        int checkInHour, int checkInMinute, int expected)
    {
        var actual = ShiftEndpoints.CalculateOvertimeMinutes(
            new TimeOnly(checkInHour, checkInMinute), null);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(17, 14, 0)]
    [InlineData(17, 15, 15)]
    [InlineData(17, 30, 30)]
    [InlineData(17, 0, 0)]
    [InlineData(16, 45, 0)]
    public void CalculateOvertimeMinutes_AppliesEveningMinimum(
        int checkOutHour, int checkOutMinute, int expected)
    {
        var actual = ShiftEndpoints.CalculateOvertimeMinutes(
            new TimeOnly(8, 0), new TimeOnly(checkOutHour, checkOutMinute));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CalculateOvertimeMinutes_AddsQualifiedMorningAndEveningPeriods()
    {
        var actual = ShiftEndpoints.CalculateOvertimeMinutes(
            new TimeOnly(7, 40), new TimeOnly(17, 25));

        Assert.Equal(45, actual);
    }

    [Fact]
    public void CalculateOvertimeMinutes_DropsEachShortPeriodSeparately()
    {
        var actual = ShiftEndpoints.CalculateOvertimeMinutes(
            new TimeOnly(7, 50), new TimeOnly(17, 10));

        Assert.Equal(0, actual);
    }
}
