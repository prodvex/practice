using System.Data;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

/// <summary>Read-only session clock sourced from SQL Server, never the client clock.</summary>
public sealed record SessionClock(int SessionId, int BookingId, DateTime StartedUtc,
    DateTime EndsUtc, DateTime? FinishedUtc, int RemainingSeconds);

/// <summary>Manages development-only session clocks. Writes require authentication before production.</summary>
public sealed class SessionStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");

    /// <summary>Returns the current countdown calculated from database timestamps.</summary>
    public async Task<SessionClock?> GetClockAsync(int bookingId, CancellationToken ct)
    {
        if (bookingId <= 0) return null;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT s.SessionId, s.BookingId, s.StartedUtc, s.EndsUtc, s.FinishedUtc
            FROM dbo.Sessions s WHERE s.BookingId=@bookingId
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@bookingId", SqlDbType.Int).Value = bookingId;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var sessionId = reader.GetInt32(0);
        var start = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
        DateTime? finished = reader.IsDBNull(4) ? null :
            DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc);
        var remaining = finished.HasValue ? 0 :
            Math.Max(0, (int)Math.Ceiling((end - DateTime.UtcNow).TotalSeconds));
        return new SessionClock(sessionId, reader.GetInt32(1), start, end, finished, remaining);
    }

    /// <summary>
    /// Starts a booked session once. The session start time uses UTC from the application;
    /// the initial end is the existing booked end. Only valid during the reservation.
    /// </summary>
    public async Task<(bool Success, string Message)> StartAsync(int bookingId, CancellationToken ct)
    {
        if (bookingId <= 0) return (false, "Invalid booking ID.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        const string select = """
            SELECT StartUtc, EndUtc, Status FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK)
            WHERE BookingId=@id
            """;
        DateTime start;
        DateTime end;
        string status;
        await using (var cmd = new SqlCommand(select, connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return (false, "Booking not found.");
            start = reader.GetDateTime(0);
            end = reader.GetDateTime(1);
            status = reader.GetString(2);
        }
        var now = DateTime.UtcNow;
        if (status != "ACTIVE" || now < start || now >= end)
            return (false, "Booking must be active and within its scheduled interval.");

        const string insert = """
            INSERT INTO dbo.Sessions (BookingId, StartedUtc, EndsUtc)
            SELECT @id, @now, @end
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Sessions WITH (UPDLOCK, HOLDLOCK) WHERE BookingId=@id)
            """;
        await using (var cmd = new SqlCommand(insert, connection, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = bookingId;
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
            cmd.Parameters.Add("@end", SqlDbType.DateTime2).Value = end;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
                return (false, "Session already exists.");
        }
        await tx.CommitAsync(ct);
        return (true, "Session started.");
    }
}
