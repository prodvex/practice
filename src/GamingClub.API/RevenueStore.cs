using System.Data;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

public sealed record RevenueReport(DateTime FromUtc, DateTime ToUtc, decimal PaidRub, decimal PendingRub);

/// <summary>Summarizes local payment records for the selected UTC interval.</summary>
public sealed class RevenueStore(IConfiguration configuration)
{
    public async Task<RevenueReport> GetReportAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("GamingClub")
            ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT COALESCE(SUM(CASE WHEN PaymentStatus='PAID' THEN AmountRub END), 0),
                   COALESCE(SUM(CASE WHEN PaymentStatus='PENDING' THEN AmountRub END), 0)
            FROM dbo.Payments
            WHERE PaidUtc >= @fromUtc AND PaidUtc < @toUtc
              AND PaymentStatus IN ('PAID', 'PENDING');
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@fromUtc", SqlDbType.DateTime2).Value = fromUtc;
        command.Parameters.Add("@toUtc", SqlDbType.DateTime2).Value = toUtc;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        await reader.ReadAsync(ct);
        return new(fromUtc, toUtc, reader.GetDecimal(0), reader.GetDecimal(1));
    }
}
