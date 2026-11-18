namespace GamingClub.API;

/// <summary>A reservation interval associated with one physical gaming seat.</summary>
public sealed record OccupancyInterval(int SeatId, DateTime StartUtc, DateTime EndUtc);

/// <summary>Time-weighted hall utilization for a requested UTC interval.</summary>
public sealed record OccupancyReport(DateTime FromUtc, DateTime ToUtc, int SeatCount,
    decimal ReservedMinutes, decimal CapacityMinutes, decimal OccupancyPercent);

/// <summary>
/// Calculates booked computer-minutes / available computer-minutes.
/// Overlapping intervals on the same seat are merged to avoid counting twice.
/// </summary>
public static class OccupancyCalculator
{
    /// <summary>Calculate occupancy only for active seat IDs.</summary>
    public static OccupancyReport Calculate(DateTime fromUtc, DateTime toUtc,
        IEnumerable<int> activeSeatIds, IEnumerable<OccupancyInterval> bookings)
    {
        if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Report interval must use UTC.");
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(31))
            throw new ArgumentOutOfRangeException(nameof(toUtc), "Choose a period from 1 tick to 31 days.");

        var active = activeSeatIds.ToHashSet();
        var capacity = (decimal)(toUtc - fromUtc).TotalMinutes * active.Count;
        decimal occupied = 0m;
        foreach (var group in bookings.Where(b => active.Contains(b.SeatId)
                        && b.EndUtc > fromUtc && b.StartUtc < toUtc)
                     .GroupBy(b => b.SeatId))
        {
            DateTime? currentStart = null;
            DateTime currentEnd = default;
            foreach (var b in group.OrderBy(b => b.StartUtc))
            {
                if (b.EndUtc <= b.StartUtc) continue;
                var start = b.StartUtc < fromUtc ? fromUtc : b.StartUtc;
                var end = b.EndUtc > toUtc ? toUtc : b.EndUtc;
                if (!currentStart.HasValue) { currentStart = start; currentEnd = end; }
                else if (start <= currentEnd)
                {
                    if (end > currentEnd) currentEnd = end;
                }
                else
                {
                    occupied += (decimal)(currentEnd - currentStart.Value).TotalMinutes;
                    currentStart = start;
                    currentEnd = end;
                }
            }
            if (currentStart.HasValue)
                occupied += (decimal)(currentEnd - currentStart.Value).TotalMinutes;
        }

        var percent = capacity == 0 ? 0 : Math.Round(occupied * 100m / capacity, 2);
        return new OccupancyReport(fromUtc, toUtc, active.Count,
            Math.Round(occupied, 2), Math.Round(capacity, 2), percent);
    }
}
