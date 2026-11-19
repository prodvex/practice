using System.Data;
using GamingClub.API;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GamingClub.Tests;

/// <summary>
/// Covers real SQL-backed read paths, login, ownership checks, reporting and session clock.
/// Runs only against the ephemeral GamingClubTest_18 database created in SQL CI.
/// </summary>
public sealed class SqlReadAndAuthIntegrationTests
{
    private static readonly DateTimeOffset Future =
        new(2031, 4, 12, 12, 0, 0, TimeSpan.Zero);

    private static IConfiguration Config
    {
        get
        {
            var cs = Environment.GetEnvironmentVariable("GAMINGCLUB_TEST_SQL_CONNECTION")
                ?? throw new InvalidOperationException("GAMINGCLUB_TEST_SQL_CONNECTION is required.");
            var parsed = new SqlConnectionStringBuilder(cs);
            if (!string.Equals(parsed.InitialCatalog, "GamingClubTest_18", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("These tests may only write GamingClubTest_18.");
            return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:GamingClub"] = cs }).Build();
        }
    }

    private sealed record Scenario(int UserId, int SeatId, int TariffId, string Login);

    private static async Task<Scenario> CreateScenarioAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var login = "read_" + suffix;
        var result = await new AuthStore(Config).RegisterAsync(
            new RegisterRequest(login, "IntegrationPassword_2026!", "Test Visitor"),
            CancellationToken.None);
        Assert.Null(result.Error);
        Assert.NotNull(result.User);
        var userId = result.User.UserId;

        await using var connection = new SqlConnection(Config.GetConnectionString("GamingClub"));
        await connection.OpenAsync();
        const string sql = """
            INSERT dbo.Halls(Name) VALUES (@hall);
            DECLARE @hallId INT=CONVERT(INT,SCOPE_IDENTITY());
            INSERT dbo.Seats(HallId,SeatNumber,PosX,PosY) VALUES (@hallId,1,100,150);
            DECLARE @seatId INT=CONVERT(INT,SCOPE_IDENTITY());
            INSERT dbo.Tariffs(Name,TariffType,PriceRub) VALUES (@tariff,'HOURLY',120.00);
            DECLARE @tariffId INT=CONVERT(INT,SCOPE_IDENTITY());
            SELECT @seatId,@tariffId;
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@hall", SqlDbType.NVarChar, 80).Value = "ReadHall_" + suffix;
        cmd.Parameters.Add("@tariff", SqlDbType.NVarChar, 100).Value = "ReadRate_" + suffix;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new Scenario(userId, reader.GetInt32(0), reader.GetInt32(1), login);
    }

    [SqlServerFact]
    public async Task RegistrationLoginAndDuplicateUsername_WorkWithSQL()
    {
        var s = await CreateScenarioAsync();
        var auth = new AuthStore(Config);
        var user = await auth.LoginAsync(new LoginRequest(s.Login, "IntegrationPassword_2026!"),
            CancellationToken.None);
        Assert.Null(user.Error);
        Assert.NotNull(user.User);
        Assert.Equal(s.UserId, user.User.UserId);
        Assert.Equal("VISITOR", user.User.Role);

        var wrong = await auth.LoginAsync(new LoginRequest(s.Login, "wrong-credentials"),
            CancellationToken.None);
        Assert.Null(wrong.User);
        Assert.NotNull(wrong.Error);

        var missing = await auth.LoginAsync(new LoginRequest("does-not-exist-2031", "any-password"),
            CancellationToken.None);
        Assert.Null(missing.User);

        var duplicate = await auth.RegisterAsync(
            new RegisterRequest(s.Login, "IntegrationPassword_2026!", "Different name"),
            CancellationToken.None);
        Assert.Null(duplicate.User);
        Assert.Contains("exists", duplicate.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerFact]
    public async Task SeatTariffBookingAndOwnershipReads_UseSQLRows()
    {
        var s = await CreateScenarioAsync();
        var bookings = new BookingStore(Config);
        Assert.Contains(await bookings.GetSeatsAsync(CancellationToken.None),
            x => x.SeatId == s.SeatId && x.IsActive);
        Assert.Contains(await bookings.GetTariffsAsync(CancellationToken.None),
            x => x.TariffId == s.TariffId && x.PriceRub == 120m);

        var result = await bookings.CreateAsync(
            new CreateBookingRequest(s.UserId, s.SeatId, s.TariffId,
                Future, Future.AddHours(1)), CancellationToken.None);
        Assert.Equal(BookingError.None, result.Error);
        var id = result.Booking!.BookingId;

        var bySeat = await bookings.GetBookingsAsync(s.SeatId, CancellationToken.None);
        Assert.Contains(bySeat, x => x.BookingId == id && x.PriceRub == 120m);

        var access = new BookingAccess(Config);
        Assert.True(await access.IsOwnerAsync(id, s.UserId, CancellationToken.None));
        Assert.False(await access.IsOwnerAsync(id, s.UserId + 99999, CancellationToken.None));
        Assert.Contains(await access.MineAsync(s.UserId, CancellationToken.None),
            x => x.BookingId == id);
        Assert.DoesNotContain(await access.MineAsync(s.UserId + 99999, CancellationToken.None),
            x => x.BookingId == id);

        var recent = await new AdminStore(Config).RecentBookingsAsync(CancellationToken.None);
        Assert.Contains(recent, x => x.BookingId == id && x.Login == s.Login);
    }

    [SqlServerFact]
    public async Task OccupancyStore_ReadsActiveBookingsForRange()
    {
        var s = await CreateScenarioAsync();
        var store = new BookingStore(Config);
        var result = await store.CreateAsync(
            new CreateBookingRequest(s.UserId, s.SeatId, s.TariffId,
                Future, Future.AddMinutes(30)), CancellationToken.None);
        Assert.Equal(BookingError.None, result.Error);
        var report = await new OccupancyStore(Config).GetReportAsync(
            Future.UtcDateTime, Future.AddHours(1).UtcDateTime, CancellationToken.None);
        Assert.True(report.SeatCount >= 1);
        Assert.True(report.ReservedMinutes >= 30m);
        Assert.True(report.CapacityMinutes >= 60m);
        Assert.InRange(report.OccupancyPercent, 0m, 100m);
        Assert.Contains("occupancy_percent", System.Text.Encoding.UTF8.GetString(
            CsvReport.Export(report)));
    }

    [SqlServerFact]
    public async Task SessionStore_ClockReflectsActualServerState()
    {
        var s = await CreateScenarioAsync();
        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(
            now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var start = minute.AddMinutes(-3);
        var end = minute.AddMinutes(45);
        var booking = await new BookingStore(Config).CreateAsync(
            new CreateBookingRequest(s.UserId, s.SeatId, s.TariffId, start, end),
            CancellationToken.None);
        Assert.Equal(BookingError.None, booking.Error);
        var id = booking.Booking!.BookingId;

        var sessions = new SessionStore(Config);
        Assert.Null(await sessions.GetClockAsync(id, CancellationToken.None));
        Assert.Null(await sessions.GetClockAsync(0, CancellationToken.None));
        var started = await sessions.StartAsync(id, CancellationToken.None);
        Assert.True(started.Success, started.Message);
        var clock = await sessions.GetClockAsync(id, CancellationToken.None);
        Assert.NotNull(clock);
        Assert.Equal(id, clock.BookingId);
        Assert.True(clock.SessionId > 0);
        Assert.InRange(clock.RemainingSeconds, 1, 3600);
        var repeated = await sessions.StartAsync(id, CancellationToken.None);
        Assert.False(repeated.Success);

        var finished = await sessions.FinishAsync(id, CancellationToken.None);
        Assert.True(finished.Success, finished.Message);
        var finishedAgain = await sessions.FinishAsync(id, CancellationToken.None);
        Assert.False(finishedAgain.Success);
        await using var verify = new SqlConnection(Config.GetConnectionString("GamingClub"));
        await verify.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Status, FinishedUtc FROM dbo.Bookings b JOIN dbo.Sessions s ON s.BookingId=b.BookingId WHERE b.BookingId=@id",
            verify);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("COMPLETED", row.GetString(0));
        Assert.False(row.IsDBNull(1));
    }
}
