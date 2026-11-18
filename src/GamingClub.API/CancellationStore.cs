using System.Data;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

public enum CancellationError { None, NotFound, Conflict }

public sealed record CancellationResult(CancellationError Error, string Message, int VoidedInvoices = 0);

/// <summary>
/// Cancels an upcoming reservation and voids ONLY unpaid invoices.
/// Access control is enforced by the authorized HTTP endpoint, not by this store.
/// </summary>
public sealed class CancellationStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");

    public async Task<CancellationResult> CancelAsync(int bookingId, CancellationToken ct)
    {
        if (bookingId <= 0)
            return new(CancellationError.NotFound, "Booking not found.");

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);

        // Read immutable seat association before taking transaction locks.
        // Follow the same lock ordering as creation/extension: seat -> booking.
        int seatId;
        await using (var cmd = new SqlCommand(
            "SELECT SeatId FROM dbo.Bookings WHERE BookingId=@bookingId", connection))
        {
            cmd.Parameters.Add("@bookingId", SqlDbType.Int).Value = bookingId;
            var value = await cmd.ExecuteScalarAsync(ct);
            if (value is null) return new(CancellationError.NotFound, "Booking not found.");
            seatId = (int)value;
        }

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        await using (var cmd = new SqlCommand(
            "SELECT SeatId FROM dbo.Seats WITH (UPDLOCK, HOLDLOCK) WHERE SeatId=@seat", connection, tx))
        {
            cmd.Parameters.Add("@seat", SqlDbType.Int).Value = seatId;
            if (await cmd.ExecuteScalarAsync(ct) is null)
                return new(CancellationError.NotFound, "Seat not found.");
        }

        DateTime startUtc;
        string status;
        await using (var cmd = new SqlCommand(
            "SELECT StartUtc, Status FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK) WHERE BookingId=@id AND SeatId=@seat",
            connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            cmd.Parameters.Add("@seat", SqlDbType.Int).Value = seatId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return new(CancellationError.NotFound, "Booking not found.");
            startUtc = reader.GetDateTime(0);
            status = reader.GetString(1);
        }

        if (status != "ACTIVE")
            return new(CancellationError.Conflict, "Only active reservations can be cancelled.");
        if (startUtc <= DateTime.UtcNow)
            return new(CancellationError.Conflict, "Reservation has already started.");

        // Any session row indicates a session already started; don't erase accounting history.
        await using (var cmd = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.Sessions WITH (UPDLOCK, HOLDLOCK) WHERE BookingId=@id",
            connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            if ((int)(await cmd.ExecuteScalarAsync(ct) ?? 0) != 0)
                return new(CancellationError.Conflict, "Started sessions require administrator review.");
        }

        // Never claim a refund happened. A payment already PAID or REFUNDED needs
        // a separate audited financial workflow, so simple cancellation is refused.
        await using (var cmd = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK) WHERE BookingId=@id AND PaymentStatus IN ('PAID','REFUNDED')",
            connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            if ((int)(await cmd.ExecuteScalarAsync(ct) ?? 0) != 0)
                return new(CancellationError.Conflict, "Paid bookings require separate refund processing.");
        }

        await using (var cmd = new SqlCommand(
            "UPDATE dbo.Bookings SET Status='CANCELLED' WHERE BookingId=@id AND Status='ACTIVE'",
            connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
                return new(CancellationError.Conflict, "Booking state changed; retry.");
        }

        int voided;
        await using (var cmd = new SqlCommand(
            "UPDATE dbo.Payments SET PaymentStatus='VOID' WHERE BookingId=@id AND PaymentStatus='PENDING'",
            connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            voided = await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new(CancellationError.None, "Reservation cancelled; unpaid invoices voided.", voided);
    }
}
