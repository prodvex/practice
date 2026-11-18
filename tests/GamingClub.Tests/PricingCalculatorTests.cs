using GamingClub.API;
using Xunit;

namespace GamingClub.Tests;

public class PricingCalculatorTests
{
    private static readonly DateTime Base = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private static TariffDto Hourly(decimal price = 120m) =>
        new(1, "Hourly", "HOURLY", price, null, null, null);

    private static TariffDto Package(int minutes = 180) =>
        new(2, "3 hours", "PACKAGE", 300m, minutes, null, null);

    private static TariffDto Night() =>
        new(3, "Night", "NIGHT", 80m, null, new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0));

    private static TariffDto Happy() =>
        new(4, "Happy", "HAPPY_HOURS", 90m, null, new TimeSpan(12, 0, 0), new TimeSpan(16, 0, 0));

    [Fact] public void Hourly_OneHour_Is120() =>
        Assert.Equal(120m, PricingCalculator.Calculate(Hourly(), Base, Base.AddHours(1)));

    [Fact] public void Hourly_HalfHour_Is60() =>
        Assert.Equal(60m, PricingCalculator.Calculate(Hourly(), Base, Base.AddMinutes(30)));

    [Fact] public void Hourly_RoundsToStartedMinute() =>
        Assert.Equal(2m, PricingCalculator.Calculate(Hourly(), Base, Base.AddSeconds(30)));

    [Fact] public void Hourly_ZeroPrice_IsZero() =>
        Assert.Equal(0m, PricingCalculator.Calculate(Hourly(0m), Base, Base.AddHours(1)));

    [Fact] public void Hourly_NegativePrice_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PricingCalculator.Calculate(Hourly(-1m), Base, Base.AddHours(1)));

    [Fact] public void ZeroDuration_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PricingCalculator.Calculate(Hourly(), Base, Base));

    [Fact] public void NegativeDuration_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PricingCalculator.Calculate(Hourly(), Base, Base.AddMinutes(-1)));

    [Fact] public void MoreThanSevenDays_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PricingCalculator.Calculate(Hourly(), Base, Base.AddDays(8)));

    [Fact] public void NonUtc_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            PricingCalculator.Calculate(Hourly(), DateTime.SpecifyKind(Base, DateTimeKind.Unspecified), Base.AddHours(1)));

    [Fact] public void Package_ExactBoundary_OnePackage() =>
        Assert.Equal(300m, PricingCalculator.Calculate(Package(), Base, Base.AddHours(3)));

    [Fact] public void Package_OneMinuteOver_TwoPackages() =>
        Assert.Equal(600m, PricingCalculator.Calculate(Package(), Base, Base.AddMinutes(181)));

    [Fact] public void Package_MissingMinutes_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            PricingCalculator.Calculate(Package(0), Base, Base.AddHours(1)));

    [Fact] public void Night_CrossesMidnight_Succeeds()
    {
        var start = new DateTime(2026, 11, 18, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal(160m, PricingCalculator.Calculate(Night(), start, start.AddHours(2)));
    }

    [Fact] public void Night_OutsideWindow_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            PricingCalculator.Calculate(Night(), Base, Base.AddHours(1)));

    [Fact] public void Happy_InsideWindow_Succeeds()
    {
        var start = Base.AddHours(2);
        Assert.Equal(90m, PricingCalculator.Calculate(Happy(), start, start.AddHours(1)));
    }

    [Fact] public void Happy_EndsAfterWindow_Throws()
    {
        var start = Base.AddHours(5).AddMinutes(30);
        Assert.Throws<ArgumentException>(() =>
            PricingCalculator.Calculate(Happy(), start, start.AddHours(1)));
    }

    [Fact] public void UnknownTariff_Throws()
    {
        var invalid = new TariffDto(10, "Unknown", "SPECIAL", 90m, null, null, null);
        Assert.Throws<ArgumentException>(() =>
            PricingCalculator.Calculate(invalid, Base, Base.AddHours(1)));
    }
}
