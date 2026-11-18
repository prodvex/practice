using GamingClub.API;
using Xunit;

namespace GamingClub.Tests;

public class OccupancyCalculatorTests
{
    private static readonly DateTime Start = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime At(int minutes) => Start.AddMinutes(minutes);

    [Fact]
    public void EmptyHall_HasZeroCapacityAndOccupancy()
    {
        var result = OccupancyCalculator.Calculate(Start, At(60), [], []);
        Assert.Equal(0, result.SeatCount);
        Assert.Equal(0m, result.OccupancyPercent);
    }

    [Fact]
    public void EmptyBookings_TwoSeats_ZeroPercent()
    {
        var result = OccupancyCalculator.Calculate(Start, At(60), [1, 2], []);
        Assert.Equal(120m, result.CapacityMinutes);
        Assert.Equal(0m, result.ReservedMinutes);
    }

    [Fact]
    public void FullHourOnOneOfTwoSeats_IsHalfUtilization()
    {
        var bookings = new[] { new OccupancyInterval(1, Start, At(60)) };
        var result = OccupancyCalculator.Calculate(Start, At(60), [1, 2], bookings);
        Assert.Equal(60m, result.ReservedMinutes);
        Assert.Equal(50m, result.OccupancyPercent);
    }

    [Fact]
    public void BookingIsClippedToReportPeriod()
    {
        var bookings = new[] { new OccupancyInterval(1, At(-30), At(30)) };
        var result = OccupancyCalculator.Calculate(Start, At(60), [1], bookings);
        Assert.Equal(30m, result.ReservedMinutes);
        Assert.Equal(50m, result.OccupancyPercent);
    }

    [Fact]
    public void OverlappingBookingsOnSameSeat_AreNotDoubleCounted()
    {
        var bookings = new[]
        {
            new OccupancyInterval(1, At(0), At(40)),
            new OccupancyInterval(1, At(20), At(60))
        };
        var result = OccupancyCalculator.Calculate(Start, At(60), [1], bookings);
        Assert.Equal(60m, result.ReservedMinutes);
        Assert.Equal(100m, result.OccupancyPercent);
    }

    [Fact]
    public void BookingOnDisabledSeat_DoesNotCount()
    {
        var bookings = new[] { new OccupancyInterval(3, Start, At(60)) };
        var result = OccupancyCalculator.Calculate(Start, At(60), [1, 2], bookings);
        Assert.Equal(0m, result.ReservedMinutes);
    }

    [Fact]
    public void AdjacentBookings_MergeWithoutExtraMinutes()
    {
        var bookings = new[]
        {
            new OccupancyInterval(1, Start, At(30)),
            new OccupancyInterval(1, At(30), At(60))
        };
        var result = OccupancyCalculator.Calculate(Start, At(60), [1], bookings);
        Assert.Equal(60m, result.ReservedMinutes);
    }

    [Fact]
    public void NegativePeriod_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OccupancyCalculator.Calculate(At(60), Start, [1], []));

    [Fact]
    public void PeriodOver31Days_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OccupancyCalculator.Calculate(Start, Start.AddDays(32), [1], []));

    [Fact]
    public void Csv_UsesSeedAndInvariantNumbers()
    {
        var report = OccupancyCalculator.Calculate(Start, At(60), [1, 2],
            [new OccupancyInterval(1, Start, At(30))]);
        var csv = System.Text.Encoding.UTF8.GetString(CsvReport.Export(report));
        Assert.Contains("241810", csv);
        Assert.Contains("25.00", csv);
        Assert.Contains("occupancy_percent", csv);
    }
}
