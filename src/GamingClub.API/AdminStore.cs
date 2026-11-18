using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

public sealed record AdminBookingDto(int BookingId, int UserId, string Login,
    int SeatId, int SeatNumber, int TariffId, DateTime StartUtc, DateTime EndUtc, string Status);

public sealed record CreateTariffRequest(string Name, string TariffType, decimal PriceRub,
    int? PackageMinutes, string? StartLocalTime, string? EndLocalTime);

public sealed record AdminWriteResult(bool Success, string Message, int? Id = null);

/// <summary>Administrator-only SQL operations. Call only from ADMIN-authorized endpoints.</summary>
public sealed class AdminStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");

    public async Task<IReadOnlyList<AdminBookingDto>> RecentBookingsAsync(CancellationToken ct)
    {
        var bookings = new List<AdminBookingDto>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT TOP (100) b.BookingId, b.UserId, u.Login, b.SeatId, s.SeatNumber,
                b.TariffId, b.StartUtc, b.EndUtc, b.Status
            FROM dbo.Bookings AS b
            JOIN dbo.Users AS u ON u.UserId = b.UserId
            JOIN dbo.Seats AS s ON s.SeatId = b.SeatId
            ORDER BY b.CreatedUtc DESC, b.BookingId DESC
            """;
        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            bookings.Add(new AdminBookingDto(reader.GetInt32(0), reader.GetInt32(1),
                reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetInt32(5),
                DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc),
                reader.GetString(8)));
        return bookings;
    }

    /// <summary>
    /// Creates a NEW tariff instead of mutating historical terms referenced by bookings.
    /// Window times currently use UTC pending an explicit club timezone policy.
    /// </summary>
    public async Task<AdminWriteResult> CreateTariffAsync(CreateTariffRequest data, CancellationToken ct)
    {
        var name = data.Name?.Trim() ?? "";
        var kind = data.TariffType?.Trim().ToUpperInvariant() ?? "";
        if (name.Length is < 2 or > 100 || data.PriceRub < 0m || data.PriceRub > 100_000m ||
            decimal.Round(data.PriceRub, 2) != data.PriceRub)
            return new(false, "Name must be 2–100 characters; price must be 0–100000 with at most 2 decimals.");

        if (kind is not ("HOURLY" or "PACKAGE" or "NIGHT" or "HAPPY_HOURS"))
            return new(false, "Invalid tariff type.");

        int? minutes = data.PackageMinutes;
        TimeSpan? start = null, end = null;
        if (kind == "PACKAGE")
        {
            if (minutes is null or < 1 or > 10080 ||
                !string.IsNullOrEmpty(data.StartLocalTime) || !string.IsNullOrEmpty(data.EndLocalTime))
                return new(false, "Package requires 1–10080 minutes and no time window.");
        }
        else
        {
            if (minutes.HasValue) return new(false, "Only packages may specify minutes.");
            if (kind is "NIGHT" or "HAPPY_HOURS")
            {
                if (!TimeSpan.TryParseExact(data.StartLocalTime, @"hh\:mm",
                        CultureInfo.InvariantCulture, out var first) ||
                    !TimeSpan.TryParseExact(data.EndLocalTime, @"hh\:mm",
                        CultureInfo.InvariantCulture, out var last) ||
                    first == last || first >= TimeSpan.FromDays(1) || last >= TimeSpan.FromDays(1))
                    return new(false, "Time window must use HH:mm (24-hour time), start != end.");
                start = first;
                end = last;
            }
            else if (!string.IsNullOrEmpty(data.StartLocalTime) || !string.IsNullOrEmpty(data.EndLocalTime))
                return new(false, "Hourly tariff cannot have a time window.");
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            INSERT dbo.Tariffs
                (Name, TariffType, PriceRub, PackageMinutes, StartLocalTime, EndLocalTime)
            OUTPUT INSERTED.TariffId
            VALUES (@name, @type, @price, @minutes, @start, @end)
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = name;
        cmd.Parameters.Add("@type", SqlDbType.VarChar, 20).Value = kind;
        var price = cmd.Parameters.Add("@price", SqlDbType.Decimal);
        price.Precision = 10;
        price.Scale = 2;
        price.Value = data.PriceRub;
        cmd.Parameters.Add("@minutes", SqlDbType.Int).Value = (object?)minutes ?? DBNull.Value;
        cmd.Parameters.Add("@start", SqlDbType.Time).Value = (object?)start ?? DBNull.Value;
        cmd.Parameters.Add("@end", SqlDbType.Time).Value = (object?)end ?? DBNull.Value;
        try
        {
            var id = (int)(await cmd.ExecuteScalarAsync(ct) ?? throw new DataException("Tariff insert failed."));
            return new(true, "Tariff created.", id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return new(false, "A tariff with this name already exists.");
        }
    }

    /// <summary>Soft-deactivates a tariff; historical bookings keep their foreign key.</summary>
    public async Task<bool> DeactivateTariffAsync(int tariffId, CancellationToken ct)
    {
        if (tariffId <= 0) return false;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "UPDATE dbo.Tariffs SET IsActive=0 WHERE TariffId=@id AND IsActive=1", connection);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = tariffId;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }
}
