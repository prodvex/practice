using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamingClub.API;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GamingClub.Tests;

/// <summary>
/// In-process HTTP integration tests. These routes require no SQL connection.
/// Database-backed end-to-end tests still need a running SQL Server.
/// </summary>
public sealed class ApiAuthorizationIntegrationTests : IClassFixture<ApiAuthorizationIntegrationTests.TestServer>
{
    private readonly HttpClient client;
    private readonly TokenService tokens;

    public sealed class TestServer : WebApplicationFactory<Program>
    {
        private static readonly Dictionary<string, string?> Settings = new()
        {
            ["GAMINGCLUB_JWT_SECRET"] = "Test_Secret_Only_Not_Production_49Characters_1234567"
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(Settings));
        }
    }

    public ApiAuthorizationIntegrationTests(TestServer server)
    {
        client = server.CreateClient();
        tokens = new TokenService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["GAMINGCLUB_JWT_SECRET"] = "Test_Secret_Only_Not_Production_49Characters_1234567"
            }).Build());
    }

    private void SetUser(string role = "VISITOR")
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.Issue(new AuthUser(41, "tester", "Test User", role)));
    }

    [Fact]
    public async Task Health_ReturnsSeedAndVariant()
    {
        var result = await client.GetFromJsonAsync<HealthResult>("/health");
        Assert.NotNull(result);
        Assert.Equal(18, result.Variant);
        Assert.Equal(241810, result.Seed);
    }

    [Fact]
    public async Task ProtectedProfileWithoutToken_ReturnsUnauthorized()
    {
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task ProtectedBookingWithoutToken_ReturnsUnauthorized()
    {
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/bookings", new
            {
                seatId = 1, tariffId = 1, startUtc = "2026-11-20T14:00:00Z",
                endUtc = "2026-11-20T15:00:00Z"
            })).StatusCode);
    }

    [Fact]
    public async Task ValidVisitorToken_ReturnsOwnIdentity()
    {
        SetUser();
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("tester", body);
        Assert.Contains("VISITOR", body);
    }

    [Fact]
    public async Task VisitorCannotAccessAdminReport()
    {
        SetUser("VISITOR");
        using var response = await client.GetAsync(
            "/api/admin/reports/occupancy?from=2026-11-20T14:00:00Z&to=2026-11-20T15:00:00Z");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentBookingRouteAbsentInProduction()
    {
        SetUser("ADMIN");
        using var response = await client.PostAsJsonAsync("/api/dev/bookings",
            new { userId = 1, seatId = 1, tariffId = 1,
                startUtc = "2026-11-20T14:00:00Z", endUtc = "2026-11-20T15:00:00Z" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvalidBearerToken_ReturnsUnauthorized()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", "invalid.jwt.token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task VisitorCannotReadAllBookings()
    {
        SetUser("VISITOR");
        using var response = await client.GetAsync("/api/admin/bookings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VisitorCannotReadArchivedTariffs()
    {
        SetUser("VISITOR");
        using var response = await client.GetAsync("/api/admin/tariffs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VisitorCannotCreateTariff()
    {
        SetUser("VISITOR");
        using var response = await client.PostAsJsonAsync("/api/admin/tariffs", new
        {
            name = "Unauthorized",
            tariffType = "HOURLY",
            priceRub = 100m
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VisitorCannotDeactivateTariff()
    {
        SetUser("VISITOR");
        using var response = await client.PostAsync("/api/admin/tariffs/1/deactivate", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCannotExportAdminCsv()
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.GetAsync(
            "/api/admin/reports/occupancy.csv?from=2026-11-20T14:00:00Z&to=2026-11-20T15:00:00Z");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record HealthResult(string Service, string Status, int Variant, int Seed);
}
