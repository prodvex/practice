namespace GamingClub.API;

/// <summary>Pure pricing algorithm used for reservations and unit tests.</summary>
public static class PricingCalculator
{
    /// <summary>
    /// Calculates an invoice for a selected tariff. Demo time-window tariffs use UTC clock time.
    /// A future release must use the actual club timezone and explicit policy for transitions.
    /// Package tariff charges for each started full package; hourly prices are per minute.
    /// </summary>
    public static decimal Calculate(TariffDto tariff, DateTime startUtc, DateTime endUtc)
    {
        if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamps must be UTC.");
        if (endUtc <= startUtc || endUtc - startUtc > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(endUtc), "Session must last >0 and <=7 days.");
        if (tariff.PriceRub < 0) throw new ArgumentOutOfRangeException(nameof(tariff.PriceRub));

        int minutes = checked((int)Math.Ceiling((endUtc - startUtc).TotalMinutes));
        if (tariff.TariffType == "PACKAGE")
        {
            if (!tariff.PackageMinutes.HasValue || tariff.PackageMinutes.Value <= 0)
                throw new ArgumentException("Package must specify positive minutes.");
            int packages = (int)Math.Ceiling((decimal)minutes / tariff.PackageMinutes.Value);
            return packages * tariff.PriceRub;
        }
        if (tariff.TariffType is not ("HOURLY" or "NIGHT" or "HAPPY_HOURS"))
            throw new ArgumentException("Unsupported tariff type.");

        if (tariff.TariffType is "NIGHT" or "HAPPY_HOURS")
        {
            if (!tariff.StartLocalTime.HasValue || !tariff.EndLocalTime.HasValue ||
                tariff.StartLocalTime == tariff.EndLocalTime)
                throw new ArgumentException("Time window is missing.");

            // Every charged minute must belong to this tariff's allowed window.
            for (int i = 0; i < minutes; i++)
            {
                var at = startUtc.AddMinutes(i).TimeOfDay;
                var from = tariff.StartLocalTime.Value;
                var until = tariff.EndLocalTime.Value;
                bool inside = from < until ? at >= from && at < until : at >= from || at < until;
                if (!inside) throw new ArgumentException("Selected interval is outside tariff hours.");
            }
        }
        return Math.Round(minutes * tariff.PriceRub / 60m, 2, MidpointRounding.AwayFromZero);
    }
}
