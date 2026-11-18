using System.Data;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

/// <summary>SQL Server persistence for the first version of the booking API.</summary>
public sealed class BookingStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub (do not commit credentials).");

    /// <summary>Returns the current seat map from the database.</summary>
    public async Task<IReadOnlyList<SeatDto>> GetSeatsAsync(CancellationToken ct)
    {
        var seats = new List<SeatDto>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT SeatId, HallId, SeatNumber, PosX, PosY, IsActive FROM dbo.Seats ORDER BY HallId, SeatNumber",
            connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            seats.Add(new SeatDto(reader.GetInt32(0), reader.GetInt32(1),
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetBoolean(5)));
        return seats;
    }

    /// <summary>Returns the SQL tariff catalog.</summary>
    public async Task<IReadOnlyList<TariffDto>> GetTariffsAsync(CancellationToken ct)
    {
        var items = new List<TariffDto>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT TariffId, Name, TariffType, PriceRub, PackageMinutes, StartLocalTime, EndLocalTime FROM dbo.Tariffs WHERE IsActive=1 ORDER BY TariffId",
            connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new TariffDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetTimeSpan(5),
                reader.IsDBNull(6) ? null : reader.GetTimeSpan(6)));
        return items;
    }

    /// <summary>Lists bookings for a specific seat.</summary>
    public async Task<IReadOnlyList<BookingDto>> GetBookingsAsync(int seatId, CancellationToken ct)
    {
        var items = new List<BookingDto>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT b.BookingId, b.UserId, b.SeatId, b.TariffId, b.StartUtc, b.EndUtc, b.Status,
                   (SELECT COALESCE(SUM(p.AmountRub), 0) FROM dbo.Payments p WHERE p.BookingId=b.BookingId AND p.PaymentStatus <> 'REFUNDED')
            FROM dbo.Bookings b WHERE b.SeatId=@seatId ORDER BY b.StartUtc
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@seatId", SqlDbType.Int).Value = seatId;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new BookingDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc), reader.GetString(6),
                reader.GetDecimal(7)));
        return items;
    }

    /// <summary>
    /// Creates booking + pending invoice in a SERIALIZABLE transaction.
    /// UPDLOCK/HOLDLOCK on the indexed seat range serializes concurrent seat reservations.
    /// </summary>
    public async Task<BookingResult> CreateAsync(CreateBookingRequest request, CancellationToken ct)
    {
        if (request.UserId <= 0 || request.SeatId <= 0 || request.TariffId <= 0)
            return new(null, BookingError.Invalid, "IDs must be positive.");
        var start = request.StartUtc.UtcDateTime;
        var end = request.EndUtc.UtcDateTime;
        if (end <= start || end - start > TimeSpan.FromDays(7))
            return new(null, BookingError.Invalid, "Invalid interval (max 7 days).");
        if (start.Second != 0 || end.Second != 0 || start.Millisecond != 0 || end.Millisecond != 0)
            return new(null, BookingError.Invalid, "Use full-minute timestamps.");

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Lock seat row first to serialize reservations for this seat even if no booking exists yet.
        const string seatSql = "SELECT IsActive FROM dbo.Seats WITH (UPDLOCK, HOLDLOCK) WHERE SeatId=@id";
        await using (var cmd = new SqlCommand(seatSql, connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = request.SeatId;
            var active = await cmd.ExecuteScalarAsync(ct);
            if (active is null || active is DBNull || !(bool)active)
                return new(null, BookingError.NotFound, "Seat not found or inactive.");
        }

        const string conflictSql = """
            SELECT COUNT_BIG(1) FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK)
            WHERE SeatId=@seat AND Status='ACTIVE' AND StartUtc < @end AND EndUtc > @start
            """;
        await using (var cmd = new SqlCommand(conflictSql, connection, tx))
        {
            cmd.Parameters.Add("@seat", SqlDbType.Int).Value = request.SeatId;
            cmd.Parameters.Add("@start", SqlDbType.DateTime2).Value = start;
            cmd.Parameters.Add("@end", SqlDbType.DateTime2).Value = end;
            if ((long)(await cmd.ExecuteScalarAsync(ct) ?? 0L) > 0)
                return new(null, BookingError.Conflict, "Seat is already reserved for this interval.");
        }

        TariffDto? tariff = null;
        const string tariffSql = """
            SELECT Name, TariffType, PriceRub, PackageMinutes, StartLocalTime, EndLocalTime
            FROM dbo.Tariffs WHERE TariffId=@id AND IsActive=1
            """;
        await using (var cmd = new SqlCommand(tariffSql, connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = request.TariffId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
                tariff = new TariffDto(request.TariffId, r.GetString(0), r.GetString(1),
                    r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetInt32(3),
                    r.IsDBNull(4) ? null : r.GetTimeSpan(4),
                    r.IsDBNull(5) ? null : r.GetTimeSpan(5));
        }
        if (tariff is null) return new(null, BookingError.NotFound, "Tariff not found.");

        decimal price;
        try { price = PricingCalculator.Calculate(tariff, start, end); }
        catch (ArgumentException ex) { return new(null, BookingError.Invalid, ex.Message); }

        // Seed users are initially disabled; ensure user exists before insertion.
        await using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE UserId=@id", connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = request.UserId;
            if ((int)(await cmd.ExecuteScalarAsync(ct) ?? 0) == 0)
                return new(null, BookingError.NotFound, "User not found.");
        }

        int bookingId;
        const string insertSql = """
            INSERT dbo.Bookings (UserId, SeatId, TariffId, StartUtc, EndUtc, Status)
            OUTPUT INSERTED.BookingId
            VALUES (@user, @seat, @tariff, @start, @end, 'ACTIVE')
            """;
        await using (var cmd = new SqlCommand(insertSql, connection, tx))
        {
            cmd.Parameters.Add("@user", SqlDbType.Int).Value = request.UserId;
            cmd.Parameters.Add("@seat", SqlDbType.Int).Value = request.SeatId;
            cmd.Parameters.Add("@tariff", SqlDbType.Int).Value = request.TariffId;
            cmd.Parameters.Add("@start", SqlDbType.DateTime2).Value = start;
            cmd.Parameters.Add("@end", SqlDbType.DateTime2).Value = end;
            bookingId = (int)(await cmd.ExecuteScalarAsync(ct) ?? throw new DataException("Insert failed."));
        }
        await using (var cmd = new SqlCommand(
            "INSERT dbo.Payments (BookingId, AmountRub, PaymentStatus) VALUES (@id, @price, 'PENDING')",
            connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            var amount = cmd.Parameters.Add("@price", SqlDbType.Decimal);
            amount.Precision = 10;
            amount.Scale = 2;
            amount.Value = price;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(new BookingDto(bookingId, request.UserId, request.SeatId, request.TariffId,
            start, end, "ACTIVE", price), BookingError.None);
    }
}
