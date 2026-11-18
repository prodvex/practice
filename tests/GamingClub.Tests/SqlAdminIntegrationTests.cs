using GamingClub.API;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GamingClub.Tests;

/// <summary>SQL integration tests for administrator tariff history.</summary>
public class SqlAdminIntegrationTests
{
    private static IConfiguration TestConfiguration()
    {
        var connection = Environment.GetEnvironmentVariable("GAMINGCLUB_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("GAMINGCLUB_TEST_SQL_CONNECTION is missing.");
        var builder = new SqlConnectionStringBuilder(connection);
        if (!string.Equals(builder.InitialCatalog, "GamingClubTest_18",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing any database except GamingClubTest_18.");
        return new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:GamingClub"] = connection }).Build();
    }

    [SqlServerFact]
    public async Task NewRateCanBeArchivedWithoutDeletingItsRecord()
    {
        var store = new AdminStore(TestConfiguration());
        var name = "AdminTest_" + Guid.NewGuid().ToString("N")[..12];
        var created = await store.CreateTariffAsync(
            new CreateTariffRequest(name, "HOURLY", 125.50m, null, null, null),
            CancellationToken.None);
        Assert.True(created.Success, created.Message);
        Assert.NotNull(created.Id);
        var id = created.Id!.Value;
        Assert.True(await store.DeactivateTariffAsync(id, CancellationToken.None));
        Assert.False(await store.DeactivateTariffAsync(id, CancellationToken.None));
        var archived = (await store.AllTariffsAsync(CancellationToken.None))
            .Single(t => t.TariffId == id);
        Assert.False(archived.IsActive);
        Assert.Equal(125.50m, archived.PriceRub);
    }

    [SqlServerFact]
    public async Task NightTariffAcceptsMidnightSpanningWindow()
    {
        var store = new AdminStore(TestConfiguration());
        var name = "NightTest_" + Guid.NewGuid().ToString("N")[..12];
        var created = await store.CreateTariffAsync(
            new CreateTariffRequest(name, "NIGHT", 80.00m, null, "22:00", "06:00"),
            CancellationToken.None);
        Assert.True(created.Success, created.Message);
        var rate = (await store.AllTariffsAsync(CancellationToken.None))
            .Single(t => t.TariffId == created.Id);
        Assert.Equal(new TimeSpan(22, 0, 0), rate.StartLocalTime);
        Assert.Equal(new TimeSpan(6, 0, 0), rate.EndLocalTime);
    }

    [SqlServerFact]
    public async Task DuplicateTariffNameIsRejected()
    {
        var store = new AdminStore(TestConfiguration());
        var name = "UniqueTest_" + Guid.NewGuid().ToString("N")[..12];
        var first = await store.CreateTariffAsync(
            new CreateTariffRequest(name, "PACKAGE", 300m, 180, null, null),
            CancellationToken.None);
        var second = await store.CreateTariffAsync(
            new CreateTariffRequest(name, "PACKAGE", 300m, 180, null, null),
            CancellationToken.None);
        Assert.True(first.Success, first.Message);
        Assert.False(second.Success);
    }

    [Fact]
    public async Task InvalidTariffIsRejectedBeforeDatabaseConnection()
    {
        var store = new AdminStore(new ConfigurationBuilder().Build());
        var result = await store.CreateTariffAsync(
            new CreateTariffRequest("Invalid", "PACKAGE", 100m, null, null, null),
            CancellationToken.None);
        Assert.False(result.Success);
    }
}
