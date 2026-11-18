using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

// This utility only creates the dedicated GamingClubTest_18 test database.
// It never drops a database or runs on any existing business database.
var connectionString = Environment.GetEnvironmentVariable("GAMINGCLUB_TEST_SQL_CONNECTION")
    ?? throw new InvalidOperationException("Set GAMINGCLUB_TEST_SQL_CONNECTION.");
var builder = new SqlConnectionStringBuilder(connectionString);
if (!string.Equals(builder.InitialCatalog, "GamingClubTest_18", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Refusing to bootstrap any database except GamingClubTest_18.");
if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass database/schema-integration-tests.sql as the only argument.");

var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
await using var connection = new SqlConnection(master.ConnectionString);
Exception? lastFailure = null;
for (var attempt = 0; attempt < 50; attempt++)
{
    try
    {
        await connection.OpenAsync();
        lastFailure = null;
        break;
    }
    catch (SqlException e)
    {
        lastFailure = e;
        Console.WriteLine($"SQL Server not ready ({attempt + 1}/50): {e.Number}");
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}
if (lastFailure != null) throw new InvalidOperationException("SQL Server failed to start.", lastFailure);
Console.WriteLine("SQL Server is ready. Provisioning GamingClubTest_18.");
var script = await File.ReadAllTextAsync(args[0]);
// SQLCMD's GO is a client-side batch delimiter, not T-SQL.
var batches = Regex.Split(script, @"(?im)^\s*GO\s*(?:--[^\r\n]*)?$");
var completed = 0;
foreach (var text in batches)
{
    if (string.IsNullOrWhiteSpace(text)) continue;
    await using var cmd = new SqlCommand(text, connection) { CommandTimeout = 45 };
    await cmd.ExecuteNonQueryAsync();
    completed++;
}
await using var verify = new SqlCommand(
    "SELECT COUNT(*) FROM GamingClubTest_18.sys.tables WHERE name IN ('Roles','Users','Halls','Seats','Tariffs','Bookings','Sessions','Payments')",
    connection);
var count = Convert.ToInt32(await verify.ExecuteScalarAsync());
if (count != 8) throw new InvalidOperationException($"Expected 8 tables; found {count}.");
Console.WriteLine($"GamingClubTest_18 initialized: {completed} batches, {count} tables.");
