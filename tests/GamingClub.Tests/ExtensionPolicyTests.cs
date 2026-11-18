using GamingClub.API;
using Xunit;

namespace GamingClub.Tests;

public class ExtensionPolicyTests
{
    private static readonly DateTime Start = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TariffDto Hourly = new(1, "Hourly", "HOURLY", 120m, null, null, null);
    private static readonly TariffDto Package = new(2, "3 hours", "PACKAGE", 300m, 180, null, null);
    private static DateTime At(int minutes) => Start.AddMinutes(minutes);

    [Fact]
    public void ExtraOneHour_Is120()
    {
        Assert.Equal(120m, ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(120), At(30)));
    }

    [Fact]
    public void ExtraHalfHour_Is60()
    {
        Assert.Equal(60m, ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(90), At(30)));
    }

    [Fact]
    public void PackageWithinSameBlock_HasNoExtraCharge()
    {
        Assert.Equal(0m, ExtensionPolicy.CalculateExtra(Package, Start, At(60), At(120), At(30)));
    }

    [Fact]
    public void PackageCrossesBoundary_ChargesNewBlock()
    {
        Assert.Equal(300m, ExtensionPolicy.CalculateExtra(Package, Start, At(170), At(181), At(30)));
    }

    [Fact]
    public void SameEndTime_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(60), At(30)));

    [Fact]
    public void EarlierEndTime_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(50), At(30)));

    [Fact]
    public void SessionNotStarted_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(90), At(-1)));

    [Fact]
    public void ExpiredSession_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(90), At(60)));

    [Fact]
    public void TotalOverSevenDays_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), Start.AddDays(8), At(30)));

    [Fact]
    public void EndAtPartialMinute_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(90).AddSeconds(1), At(30)));

    [Fact]
    public void SubMillisecondExtensionEnd_IsRejected()
    {
        // DATETIME2(0) would round this away; validation must reject it.
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60), At(90).AddTicks(1), At(30)));
    }

    [Fact]
    public void NonUtcEnd_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            ExtensionPolicy.CalculateExtra(Hourly, Start, At(60),
                DateTime.SpecifyKind(At(90), DateTimeKind.Unspecified), At(30)));
}
