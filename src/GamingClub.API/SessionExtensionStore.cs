using System.Data;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

/// <summary>Result of a server-validated session extension.</summary>
public sealed record ExtensionResult(bool Success, string Message, DateTime? NewEndUtc = null,
    decimal ExtraChargeRub = 0m, bool Conflict = false);

/// <summary>Extends a booked session using SQL Server transactions and seat-level locking.</summary>
public sealed class SessionExtensionStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");

    /// <summary>
    /// Extends the booking and active session end atomically and adds a pending incremental
    /// payment when the tariff requires it. Serializes with concurrent new reservations.
    /// </summary>
    public async Task<ExtensionResult> ExtendAsync(int bookingId, DateTimeOffset newEnd, CancellationToken ct)
    {
        if (bookingId <= 0) return new(false, "Booking ID must be positive.");
        var requestedEnd = newEnd.UtcDateTime;
        if (requestedEnd.Second != 0 || requestedEnd.Millisecond != 0)
            return new(false, "End time must be a full minute.");

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);

        // Read the seat identifier before taking locks; bookings cannot change seats.
        int seatId;
        await using (var find = new SqlCommand("SELECT SeatId FROM dbo.Bookings WHERE BookingId=@id", connection))
        {
            find.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            var scalar = await find.ExecuteScalarAsync(ct);
            if (scalar is null) return new(false, "Booking not found.");
            seatId = (int)scalar;
        }

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Match BookingStore.CreateAsync locking order: seat first, booking second.
        await using (var seat = new SqlCommand(
            "SELECT SeatId FROM dbo.Seats WITH (UPDLOCK, HOLDLOCK) WHERE SeatId=@id AND IsActive=1",
            connection, tx))
        {
            seat.Parameters.Add("@id", SqlDbType.Int).Value = seatId;
            if (await seat.ExecuteScalarAsync(ct) is null)
                return new(false, "Seat is unavailable.");
        }

        DateTime start, oldEnd;
        int tariffId;
        string status;
        await using (var book = new SqlCommand(
            "SELECT StartUtc, EndUtc, TariffId, Status FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK) WHERE BookingId=@id AND SeatId=@seat",
            connection, tx))
        {
            book.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            book.Parameters.Add("@seat", SqlDbType.Int).Value = seatId;
            await using var reader = await book.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return new(false, "Booking not found.");
            start = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            oldEnd = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
            tariffId = reader.GetInt32(2);
            status = reader.GetString(3);
        }
        if (status != "ACTIVE") return new(false, "Booking is not active.");

        // Session must already have been started and must not be finished.
        await using (var check = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.Sessions WITH (UPDLOCK, HOLDLOCK) WHERE BookingId=@id AND FinishedUtc IS NULL",
            connection, tx))
        {
            check.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            if ((int)(await check.ExecuteScalarAsync(ct) ?? 0) != 1)
                return new(false, "No running session exists.");
        }

        TariffDto? tariff = null;
        await using (var cmd = new SqlCommand(
            "SELECT Name, TariffType, PriceRub, PackageMinutes, StartLocalTime, EndLocalTime FROM dbo.Tariffs WHERE TariffId=@id",
            connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = tariffId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                tariff = new TariffDto(tariffId, reader.GetString(0), reader.GetString(1),
                    reader.GetDecimal(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetTimeSpan(4),
                    reader.IsDBNull(5) ? null : reader.GetTimeSpan(5));
        }
        if (tariff is null) return new(false, "Tariff not found.");

        decimal extra;
        try { extra = ExtensionPolicy.CalculateExtra(tariff, start, oldEnd, requestedEnd, DateTime.UtcNow); }
        catch (ArgumentException ex) { return new(false, ex.Message); }

        const string conflicts = """
            SELECT COUNT_BIG(1) FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK)
            WHERE SeatId=@seat AND BookingId<>@booking AND Status='ACTIVE'
              AND StartUtc < @newEnd AND EndUtc > @oldEnd
            """;
        await using (var check = new SqlCommand(conflicts, connection, tx))
        {
            check.Parameters.Add("@seat", SqlDbType.Int).Value = seatId;
            check.Parameters.Add("@booking", SqlDbType.Int).Value = bookingId;
            check.Parameters.Add("@oldEnd", SqlDbType.DateTime2).Value = oldEnd;
            check.Parameters.Add("@newEnd", SqlDbType.DateTime2).Value = requestedEnd;
            if ((long)(await check.ExecuteScalarAsync(ct) ?? 0L) > 0)
                return new(false, "The seat is booked for part of the extension.", Conflict: true);
        }

        await using (var cmd = new SqlCommand(
            "UPDATE dbo.Bookings SET EndUtc=@end WHERE BookingId=@id", connection, tx))
        {
            cmd.Parameters.Add("@end", SqlDbType.DateTime2).Value = requestedEnd;
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = new SqlCommand(
            "UPDATE dbo.Sessions SET EndsUtc=@end WHERE BookingId=@id AND FinishedUtc IS NULL", connection, tx))
        {
            cmd.Parameters.Add("@end", SqlDbType.DateTime2).Value = requestedEnd;
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
                return new(false, "Session could not be extended.");
        }
        if (extra > 0)
        {
            await using var cmd = new SqlCommand(
                "INSERT INTO dbo.Payments(BookingId, AmountRub, PaymentStatus) VALUES (@id, @amount, 'PENDING')",
                connection, tx);
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            var amount = cmd.Parameters.Add("@amount", SqlDbType.Decimal);
            amount.Precision = 10;
            amount.Scale = 2;
            amount.Value = extra;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(true, "Session extended.", requestedEnd, extra);
    }
}
