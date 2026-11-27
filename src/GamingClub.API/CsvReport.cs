using System.Globalization;
using System.Text;

namespace GamingClub.API;

/// <summary>Exports occupancy statistics to a single-record UTF-8 CSV.</summary>
public static class CsvReport
{
    public static byte[] Export(RevenueReport report)
    {
        var csv = "from_utc;to_utc;paid_rub;pending_rub;seed\r\n" +
            string.Create(CultureInfo.InvariantCulture,
                $"{report.FromUtc:O};{report.ToUtc:O};{report.PaidRub:F2};{report.PendingRub:F2};241810\r\n");
        return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
    }

    /// <summary>Produces an Excel-compatible CSV using semicolon separators.</summary>
    public static byte[] Export(OccupancyReport report)
    {
        var csv = new StringBuilder();
        csv.AppendLine("from_utc;to_utc;seat_count;reserved_minutes;capacity_minutes;occupancy_percent;seed");
        csv.Append(report.FromUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
            .Append(report.ToUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
            .Append(report.SeatCount).Append(';')
            .Append(report.ReservedMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';')
            .Append(report.CapacityMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';')
            .Append(report.OccupancyPercent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
            .Append("241810").AppendLine();
        return new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(csv.ToString())).ToArray();
    }
}
