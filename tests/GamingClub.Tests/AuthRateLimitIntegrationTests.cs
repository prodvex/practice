using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GamingClub.Tests;

/// <summary>HTTP regression for the in-memory authentication rate limiter.</summary>
public class AuthRateLimitIntegrationTests
{
    [Fact]
    public async Task NinthInvalidRegistrationFromOneClientIsRateLimited()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["GAMINGCLUB_JWT_SECRET"] =
                            "Test_Secret_Only_For_Rate_Limiter_Integration_2026"
                    }));
            });
        using var client = factory.CreateClient();
        // Invalid input is rejected BEFORE any SQL connection is opened.
        var invalid = new { login = "a", password = "short", displayName = "" };
        for (var i = 0; i < 8; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var rejected = await client.PostAsJsonAsync("/api/auth/register", invalid);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }
}
