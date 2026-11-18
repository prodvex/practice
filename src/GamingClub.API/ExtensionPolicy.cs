namespace GamingClub.API;

/// <summary>Checks permitted extension and computes its incremental charge.</summary>
public static class ExtensionPolicy
{
    /// <summary>
    /// All timestamps must be UTC full minutes. Session must still be live.
    /// Returns difference between prices for the old and extended interval;
    /// package extensions within a paid block may cost zero.
    /// </summary>
    public static decimal CalculateExtra(TariffDto tariff, DateTime bookedStartUtc,
        DateTime bookedEndUtc, DateTime requestedEndUtc, DateTime nowUtc)
    {
        if (new[] { bookedStartUtc, bookedEndUtc, requestedEndUtc, nowUtc }
            .Any(t => t.Kind != DateTimeKind.Utc))
            throw new ArgumentException("All timestamps must be UTC.");
        if (bookedStartUtc >= bookedEndUtc || requestedEndUtc <= bookedEndUtc)
            throw new ArgumentException("Extension end must be later than booked end.");
        if (nowUtc < bookedStartUtc || nowUtc >= bookedEndUtc)
            throw new ArgumentException("Only currently active sessions can be extended.");
        if (requestedEndUtc - bookedStartUtc > TimeSpan.FromDays(7))
            throw new ArgumentException("Total booking cannot exceed seven days.");
        if (requestedEndUtc.Second != 0 || requestedEndUtc.Millisecond != 0)
            throw new ArgumentException("End time must be a full minute.");

        decimal oldPrice = PricingCalculator.Calculate(tariff, bookedStartUtc, bookedEndUtc);
        decimal newPrice = PricingCalculator.Calculate(tariff, bookedStartUtc, requestedEndUtc);
        if (newPrice < oldPrice)
            throw new ArgumentException("Extension cannot reduce booking price.");
        return newPrice - oldPrice;
    }
}
