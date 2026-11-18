using System.Data;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

/// <summary>Centralized ownership checks for authenticated session actions.</summary>
public sealed class BookingAccess(IConfiguration config)
{
    private string ConnectionString => config.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");

    public async Task<bool> IsOwnerAsync(int bookingId, int userId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.Bookings WHERE BookingId=@booking AND UserId=@user", connection);
        cmd.Parameters.Add("@booking", SqlDbType.Int).Value = bookingId;
        cmd.Parameters.Add("@user", SqlDbType.Int).Value = userId;
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0) > 0;
    }

    public async Task<IReadOnlyList<BookingDto>> MineAsync(int userId, CancellationToken ct)
    {
        var list = new List<BookingDto>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT b.BookingId, b.UserId, b.SeatId, b.TariffId, b.StartUtc, b.EndUtc, b.Status,
                   (SELECT COALESCE(SUM(p.AmountRub),0) FROM dbo.Payments p
                    WHERE p.BookingId=b.BookingId AND p.PaymentStatus <> 'REFUNDED')
            FROM dbo.Bookings b WHERE b.UserId=@user ORDER BY b.StartUtc DESC
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@user", SqlDbType.Int).Value = userId;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new BookingDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                reader.GetString(6), reader.GetDecimal(7)));
        return list;
    }
}
