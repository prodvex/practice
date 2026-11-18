using System.Data;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

/// <summary>Reads SQL Server data for occupancy reporting.</summary>
public sealed class OccupancyStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");

    /// <summary>Generates a report using active seats and non-cancelled reservations.</summary>
    public async Task<OccupancyReport> GetReportAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var seats = new List<int>();
        var intervals = new List<OccupancyInterval>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using (var seatCmd = new SqlCommand(
            "SELECT SeatId FROM dbo.Seats WHERE IsActive=1", connection))
        await using (var reader = await seatCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) seats.Add(reader.GetInt32(0));
        }

        const string sql = """
            SELECT SeatId, StartUtc, EndUtc
              FROM dbo.Bookings
             WHERE Status IN ('ACTIVE', 'COMPLETED')
               AND StartUtc < @endUtc AND EndUtc > @startUtc
            """;
        await using (var cmd = new SqlCommand(sql, connection))
        {
            cmd.Parameters.Add("@startUtc", SqlDbType.DateTime2).Value = fromUtc;
            cmd.Parameters.Add("@endUtc", SqlDbType.DateTime2).Value = toUtc;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                intervals.Add(new OccupancyInterval(reader.GetInt32(0),
                    DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                    DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)));
        }
        return OccupancyCalculator.Calculate(fromUtc, toUtc, seats, intervals);
    }
}
