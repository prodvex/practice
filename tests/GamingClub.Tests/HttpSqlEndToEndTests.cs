using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamingClub.API;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GamingClub.Tests;

/// <summary>
/// Production-mode ASP.NET TestServer with actual SQL Server 2022 and JWT.
/// Checks HTTP -> auth -> SQL mutations, not a mock database.
/// </summary>
public sealed class HttpSqlEndToEndTests
{
    private const string Secret = "GamingClub_Api_SQL_Integration_Secret_Only_2026_Passkey";
    private static readonly DateTimeOffset Future = new(2032, 3, 15, 12, 0, 0, TimeSpan.Zero);

    private static string SqlConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("GAMINGCLUB_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("Set SQL test connection.");
        if (!string.Equals(new SqlConnectionStringBuilder(value).InitialCatalog, "GamingClubTest_18",
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Never run HTTP integration tests against a real club database.");
        return value;
    }

    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["GAMINGCLUB_JWT_SECRET"] = Secret,
                    ["ConnectionStrings:GamingClub"] = SqlConnectionString()
                }));
        });

    private static async Task<(int SeatId, int TariffId)> NewSeatAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = new SqlConnection(SqlConnectionString());
        await db.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Halls(Name) VALUES (@hall);
            DECLARE @hallId INT=CONVERT(INT,SCOPE_IDENTITY());
            INSERT dbo.Seats(HallId,SeatNumber,PosX,PosY) VALUES (@hallId,1,10,10);
            DECLARE @seat INT=CONVERT(INT,SCOPE_IDENTITY());
            INSERT dbo.Tariffs(Name,TariffType,PriceRub) VALUES (@tariff,'HOURLY',120);
            SELECT @seat,CONVERT(INT,SCOPE_IDENTITY());
            """, db);
        command.Parameters.Add("@hall", SqlDbType.NVarChar, 80).Value = "HttpHall_" + suffix;
        command.Parameters.Add("@tariff", SqlDbType.NVarChar, 100).Value = "HttpTariff_" + suffix;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<(int Id, string Token, string Login)> RegisterAsync(HttpClient client)
    {
        var login = "http_" + Guid.NewGuid().ToString("N")[..15];
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            login, displayName = "HTTP Visitor", password = "IntegrationStrongPassword_2026!"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (data.GetProperty("userId").GetInt32(),
            data.GetProperty("accessToken").GetString()!, login);
    }

    private static void As(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [SqlServerFact]
    public async Task VisitorCanRegisterReadSeatsBookAndCancelOverRealSql()
    {
        var (seat, tariff) = await NewSeatAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        var visitor = await RegisterAsync(client);
        As(client, visitor.Token);

        using var seats = await client.GetAsync("/api/seats");
        Assert.Equal(HttpStatusCode.OK, seats.StatusCode);
        var seatJson = await seats.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(seatJson.EnumerateArray(), x => x.GetProperty("seatId").GetInt32() == seat);

        using var tariffs = await client.GetAsync("/api/tariffs");
        Assert.Equal(HttpStatusCode.OK, tariffs.StatusCode);
        var tariffJson = await tariffs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(tariffJson.EnumerateArray(), x => x.GetProperty("tariffId").GetInt32() == tariff);

        using var bookingResponse = await client.PostAsJsonAsync("/api/bookings", new
        {
            seatId = seat, tariffId = tariff, startUtc = Future, endUtc = Future.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.OK, bookingResponse.StatusCode);
        var booking = await bookingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = booking.GetProperty("bookingId").GetInt32();

        using var myResponse = await client.GetAsync("/api/bookings/mine");
        Assert.Equal(HttpStatusCode.OK, myResponse.StatusCode);
        var mine = await myResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(mine.EnumerateArray(), x => x.GetProperty("bookingId").GetInt32() == id);

        using var conflict = await client.PostAsJsonAsync("/api/bookings", new
        {
            seatId = seat, tariffId = tariff, startUtc = Future, endUtc = Future.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var cancel = await client.PostAsync($"/api/bookings/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        using var after = await client.GetAsync("/api/bookings/mine");
        var updated = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(updated.EnumerateArray(),
            x => x.GetProperty("bookingId").GetInt32() == id &&
                 x.GetProperty("status").GetString() == "CANCELLED");

        using var replacement = await client.PostAsJsonAsync("/api/bookings", new
        {
            seatId = seat, tariffId = tariff, startUtc = Future, endUtc = Future.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
    }

    [SqlServerFact]
    public async Task OwnerPolicyAndAdminTariffAndReportEndpointsUseActualSql()
    {
        var (seat, tariff) = await NewSeatAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client);
        As(client, owner.Token);
        using var created = await client.PostAsJsonAsync("/api/bookings", new
        {
            seatId = seat, tariffId = tariff, startUtc = Future, endUtc = Future.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var booking = await created.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = booking.GetProperty("bookingId").GetInt32();

        var intruder = await RegisterAsync(client);
        As(client, intruder.Token);
        using var forbidden = await client.PostAsync($"/api/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var adminForbidden = await client.GetAsync("/api/admin/tariffs");
        Assert.Equal(HttpStatusCode.Forbidden, adminForbidden.StatusCode);

        // Promotion is a test-only SQL fixture action. Public HTTP registration
        // must never allow choosing ADMIN itself.
        await using (var db = new SqlConnection(SqlConnectionString()))
        {
            await db.OpenAsync();
            await using var cmd = new SqlCommand("""
                UPDATE dbo.Users
                   SET RoleId=(SELECT RoleId FROM dbo.Roles WHERE Code='ADMIN')
                 WHERE UserId=@id
                """, db);
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = intruder.Id;
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
        using var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            login = intruder.Login, password = "IntegrationStrongPassword_2026!"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ADMIN", auth.GetProperty("role").GetString());
        As(client, auth.GetProperty("accessToken").GetString()!);

        using var allBookings = await client.GetAsync("/api/admin/bookings");
        Assert.Equal(HttpStatusCode.OK, allBookings.StatusCode);
        var bookingList = await allBookings.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(bookingList.EnumerateArray(),
            x => x.GetProperty("bookingId").GetInt32() == bookingId);

        using var adminTariffs = await client.GetAsync("/api/admin/tariffs");
        Assert.Equal(HttpStatusCode.OK, adminTariffs.StatusCode);
        var tariffs = await adminTariffs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(tariffs.EnumerateArray(), x => x.GetProperty("tariffId").GetInt32() == tariff);

        var newName = "HTTP_RATE_" + Guid.NewGuid().ToString("N")[..10];
        using var newTariff = await client.PostAsJsonAsync("/api/admin/tariffs", new
        {
            name = newName, tariffType = "HOURLY", priceRub = 99.50m
        });
        Assert.Equal(HttpStatusCode.Created, newTariff.StatusCode);
        var tariffCreated = await newTariff.Content.ReadFromJsonAsync<JsonElement>();
        var newTariffId = tariffCreated.GetProperty("id").GetInt32();
        using var disabled = await client.PostAsync(
            $"/api/admin/tariffs/{newTariffId}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);

        var range = $"from={Uri.EscapeDataString(Future.ToString("O"))}&to={Uri.EscapeDataString(Future.AddHours(1).ToString("O"))}";
        using var csv = await client.GetAsync("/api/admin/reports/occupancy.csv?" + range);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Contains("241810", await csv.Content.ReadAsStringAsync());

        using var report = await client.GetAsync("/api/admin/reports/occupancy?" + range);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportBody = await report.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(reportBody.GetProperty("reservedMinutes").GetDecimal() >= 60m);
    }
}
