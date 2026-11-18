using System.Data;
using GamingClub.API;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GamingClub.Tests;

/// <summary>
/// Runs only when GAMINGCLUB_TEST_SQL_CONNECTION is explicitly supplied.
/// Never use the production GamingClub database.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("GAMINGCLUB_TEST_SQL_CONNECTION")))
            Skip = "Requires an isolated GamingClubTest_18 SQL Server and GAMINGCLUB_TEST_SQL_CONNECTION.";
    }
}

/// <summary>Real SQL Server integration tests; each scenario creates its own hall/seat/user/tariff.</summary>
public class SqlBookingIntegrationTests
{
    private sealed record Scenario(int UserId, int SeatId, int TariffId);
    private static readonly DateTimeOffset FixedStart =
        new(2030, 1, 5, 12, 0, 0, TimeSpan.Zero);

    private static string ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("GAMINGCLUB_TEST_SQL_CONNECTION")
                ?? throw new InvalidOperationException("Set GAMINGCLUB_TEST_SQL_CONNECTION.");
            var parsed = new SqlConnectionStringBuilder(value);
            if (!string.Equals(parsed.InitialCatalog, "GamingClubTest_18",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Refusing tests against anything except database GamingClubTest_18.");
            return value;
        }
    }

    private static IConfiguration Configuration => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:GamingClub"] = ConnectionString
        }).Build();

    private static async Task<SqlConnection> OpenSafeAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        try
        {
            await using var cmd = new SqlCommand(
                "SELECT DB_NAME(), OBJECT_ID(N'dbo.Bookings'), OBJECT_ID(N'dbo.Tariffs')", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync() ||
                !string.Equals(reader.GetString(0), "GamingClubTest_18", StringComparison.OrdinalIgnoreCase) ||
                reader.IsDBNull(1) || reader.IsDBNull(2))
                throw new InvalidOperationException(
                    "Test database must be GamingClubTest_18 with schema-integration-tests.sql installed.");
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        return connection;
    }

    private static async Task<Scenario> NewScenarioAsync()
    {
        await using var connection = await OpenSafeAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Code=N'VISITOR')
                INSERT dbo.Roles(Code,DisplayName) VALUES (N'VISITOR',N'Test visitor');
            DECLARE @role INT=(SELECT RoleId FROM dbo.Roles WHERE Code=N'VISITOR');
            INSERT dbo.Users(RoleId,Login,PasswordHash,DisplayName)
                VALUES(@role,@login,N'INTEGRATION_TEST_ONLY',N'Test account');
            DECLARE @user INT=CONVERT(INT,SCOPE_IDENTITY());
            INSERT dbo.Halls(Name) VALUES(@hall);
            DECLARE @hallId INT=CONVERT(INT,SCOPE_IDENTITY());
            INSERT dbo.Seats(HallId,SeatNumber,PosX,PosY) VALUES (@hallId,1,20,20);
            DECLARE @seat INT=CONVERT(INT,SCOPE_IDENTITY());
            INSERT dbo.Tariffs(Name,TariffType,PriceRub)
                VALUES (@tariff,'HOURLY',120.00);
            DECLARE @tariffId INT=CONVERT(INT,SCOPE_IDENTITY());
            SELECT @user,@seat,@tariffId;
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@login", SqlDbType.NVarChar, 80).Value = "itest_" + suffix;
        cmd.Parameters.Add("@hall", SqlDbType.NVarChar, 80).Value = "hall_" + suffix;
        cmd.Parameters.Add("@tariff", SqlDbType.NVarChar, 100).Value = "rate_" + suffix;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new Scenario(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private static CreateBookingRequest Request(Scenario s, DateTimeOffset from, DateTimeOffset to) =>
        new(s.UserId, s.SeatId, s.TariffId, from, to);

    private static DateTimeOffset MinuteNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
    }

    private static async Task<int> CountAsync(string sql, int bookingId)
    {
        await using var connection = await OpenSafeAsync();
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@booking", SqlDbType.Int).Value = bookingId;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [SqlServerFact]
    public async Task BookingCreatesPendingPayment()
    {
        var s = await NewScenarioAsync();
        var result = await new BookingStore(Configuration).CreateAsync(
            Request(s, FixedStart, FixedStart.AddHours(1)), CancellationToken.None);
        Assert.Equal(BookingError.None, result.Error);
        Assert.NotNull(result.Booking);
        Assert.Equal(120m, result.Booking.PriceRub);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(1) FROM dbo.Payments WHERE BookingId=@booking AND PaymentStatus='PENDING'",
            result.Booking.BookingId));
    }

    [SqlServerFact]
    public async Task OverlappingReservationIsRejected()
    {
        var s = await NewScenarioAsync();
        var store = new BookingStore(Configuration);
        var first = await store.CreateAsync(Request(s, FixedStart, FixedStart.AddHours(1)), CancellationToken.None);
        var second = await store.CreateAsync(Request(s, FixedStart.AddMinutes(30),
            FixedStart.AddMinutes(90)), CancellationToken.None);
        Assert.Equal(BookingError.None, first.Error);
        Assert.Equal(BookingError.Conflict, second.Error);
    }

    [SqlServerFact]
    public async Task AdjacentReservationsAreAllowed()
    {
        var s = await NewScenarioAsync();
        var store = new BookingStore(Configuration);
        var a = await store.CreateAsync(Request(s, FixedStart, FixedStart.AddHours(1)), CancellationToken.None);
        var b = await store.CreateAsync(Request(s, FixedStart.AddHours(1),
            FixedStart.AddHours(2)), CancellationToken.None);
        Assert.Equal(BookingError.None, a.Error);
        Assert.Equal(BookingError.None, b.Error);
    }

    [SqlServerFact]
    public async Task TwoSimultaneousClientsCannotDoubleBookSeat()
    {
        var s = await NewScenarioAsync();
        var store = new BookingStore(Configuration);
        var request = Request(s, FixedStart, FixedStart.AddHours(1));
        var results = await Task.WhenAll(
            store.CreateAsync(request, CancellationToken.None),
            store.CreateAsync(request, CancellationToken.None));
        Assert.Equal(1, results.Count(x => x.Error == BookingError.None));
        Assert.Equal(1, results.Count(x => x.Error == BookingError.Conflict));
    }

    [SqlServerFact]
    public async Task InactiveSeatCannotBeBooked()
    {
        var s = await NewScenarioAsync();
        await using (var connection = await OpenSafeAsync())
        await using (var cmd = new SqlCommand("UPDATE dbo.Seats SET IsActive=0 WHERE SeatId=@id", connection))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = s.SeatId;
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
        var result = await new BookingStore(Configuration).CreateAsync(
            Request(s, FixedStart, FixedStart.AddHours(1)), CancellationToken.None);
        Assert.Equal(BookingError.NotFound, result.Error);
    }

    [SqlServerFact]
    public async Task SessionExtensionConflictRollsBackBookingAndPayment()
    {
        var s = await NewScenarioAsync();
        var store = new BookingStore(Configuration);
        var now = MinuteNow();
        var start = now.AddMinutes(-5);
        var end = now.AddMinutes(30);
        var current = await store.CreateAsync(Request(s, start, end), CancellationToken.None);
        var next = await store.CreateAsync(Request(s, end, end.AddMinutes(45)), CancellationToken.None);
        Assert.Equal(BookingError.None, current.Error);
        Assert.Equal(BookingError.None, next.Error);
        var id = current.Booking!.BookingId;
        var started = await new SessionStore(Configuration).StartAsync(id, CancellationToken.None);
        Assert.True(started.Success, started.Message);
        var before = await CountAsync(
            "SELECT COUNT(1) FROM dbo.Payments WHERE BookingId=@booking", id);
        var result = await new SessionExtensionStore(Configuration).ExtendAsync(
            id, end.AddMinutes(20), CancellationToken.None);
        Assert.False(result.Success);
        Assert.True(result.Conflict);
        Assert.Equal(before, await CountAsync(
            "SELECT COUNT(1) FROM dbo.Payments WHERE BookingId=@booking", id));
        await using var connection = await OpenSafeAsync();
        await using var cmd = new SqlCommand(
            "SELECT EndUtc FROM dbo.Bookings WHERE BookingId=@booking", connection);
        cmd.Parameters.Add("@booking", SqlDbType.Int).Value = id;
        Assert.Equal(end.UtcDateTime, (DateTime)(await cmd.ExecuteScalarAsync())!);
    }

    [SqlServerFact]
    public async Task SuccessfulExtensionPersistsExtraPayment()
    {
        var s = await NewScenarioAsync();
        var now = MinuteNow();
        var start = now.AddMinutes(-5);
        var end = now.AddMinutes(30);
        var booking = await new BookingStore(Configuration).CreateAsync(
            Request(s, start, end), CancellationToken.None);
        Assert.Equal(BookingError.None, booking.Error);
        var id = booking.Booking!.BookingId;
        var started = await new SessionStore(Configuration).StartAsync(id, CancellationToken.None);
        Assert.True(started.Success, started.Message);
        var extension = await new SessionExtensionStore(Configuration).ExtendAsync(
            id, end.AddMinutes(30), CancellationToken.None);
        Assert.True(extension.Success, extension.Message);
        Assert.Equal(60m, extension.ExtraChargeRub);
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(1) FROM dbo.Payments WHERE BookingId=@booking AND PaymentStatus='PENDING'",
            id));
    }
    [SqlServerFact]
    public async Task CancellingFutureBookingVoidsPendingPaymentAndReleasesSeat()
    {
        var s = await NewScenarioAsync();
        var store = new BookingStore(Configuration);
        var booking = await store.CreateAsync(Request(s, FixedStart, FixedStart.AddHours(1)),
            CancellationToken.None);
        Assert.Equal(BookingError.None, booking.Error);
        var id = booking.Booking!.BookingId;

        var cancellation = await new CancellationStore(Configuration).CancelAsync(
            id, CancellationToken.None);
        Assert.Equal(CancellationError.None, cancellation.Error);
        Assert.Equal(1, cancellation.VoidedInvoices);

        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(1) FROM dbo.Bookings WHERE BookingId=@booking AND Status='CANCELLED'", id));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(1) FROM dbo.Payments WHERE BookingId=@booking AND PaymentStatus='VOID'", id));

        var replacement = await store.CreateAsync(Request(s, FixedStart, FixedStart.AddHours(1)),
            CancellationToken.None);
        Assert.Equal(BookingError.None, replacement.Error);
    }

    [SqlServerFact]
    public async Task CancellingTwiceReturnsConflict()
    {
        var s = await NewScenarioAsync();
        var booking = await new BookingStore(Configuration).CreateAsync(
            Request(s, FixedStart, FixedStart.AddHours(1)), CancellationToken.None);
        var cancel = new CancellationStore(Configuration);
        Assert.Equal(CancellationError.None,
            (await cancel.CancelAsync(booking.Booking!.BookingId, CancellationToken.None)).Error);
        Assert.Equal(CancellationError.Conflict,
            (await cancel.CancelAsync(booking.Booking.BookingId, CancellationToken.None)).Error);
    }

    [SqlServerFact]
    public async Task PaidBookingCancellationIsRefusedWithoutFakeRefund()
    {
        var s = await NewScenarioAsync();
        var booking = await new BookingStore(Configuration).CreateAsync(
            Request(s, FixedStart, FixedStart.AddHours(1)), CancellationToken.None);
        Assert.Equal(BookingError.None, booking.Error);
        var id = booking.Booking!.BookingId;
        await using (var connection = await OpenSafeAsync())
        await using (var cmd = new SqlCommand(
            "UPDATE dbo.Payments SET PaymentStatus='PAID' WHERE BookingId=@booking", connection))
        {
            cmd.Parameters.Add("@booking", SqlDbType.Int).Value = id;
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
        var result = await new CancellationStore(Configuration).CancelAsync(id, CancellationToken.None);
        Assert.Equal(CancellationError.Conflict, result.Error);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(1) FROM dbo.Bookings WHERE BookingId=@booking AND Status='ACTIVE'", id));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(1) FROM dbo.Payments WHERE BookingId=@booking AND PaymentStatus='PAID'", id));
    }

}
