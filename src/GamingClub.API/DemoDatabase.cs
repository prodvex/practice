using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

internal static partial class DemoDatabase
{
    internal const string AdminLogin = "club_admin_18";
    internal const string VisitorLogin = "club_visitor_18";

    internal static async Task InitializeAsync(string scripts, string dataDirectory)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GamingClub")
            ?? throw new InvalidOperationException("The demo database connection is missing.");
        var database = new SqlConnectionStringBuilder(connectionString);
        if (!DemoInstance().IsMatch(database.DataSource) || database.InitialCatalog != "GamingClub" ||
            !database.IntegratedSecurity || !string.IsNullOrEmpty(database.AttachDBFilename))
            throw new InvalidOperationException("Demo setup requires its own LocalDB instance.");

        var schema = SplitBatches(await File.ReadAllTextAsync(Path.Combine(scripts, "schema.sql")));
        var seed = SplitBatches(await File.ReadAllTextAsync(Path.Combine(scripts, "seed.sql")));
        if (!schema[0].Contains("CREATE DATABASE GamingClub;", StringComparison.Ordinal) ||
            !UseDatabase().IsMatch(schema[1]) || !UseDatabase().IsMatch(seed[0]))
            throw new InvalidOperationException("Unexpected GamingClub SQL script preamble.");
        var migration = await File.ReadAllTextAsync(Path.Combine(scripts,
            "migrations", "001_void_pending_payments.sql"));

        var master = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync();
            await using var exists = new SqlCommand("SELECT DB_ID(N'GamingClub');", connection);
            if (await exists.ExecuteScalarAsync() is DBNull)
                await CreateOrAttachAsync(connection, dataDirectory);
        }

        await using var club = new SqlConnection(database.ConnectionString);
        await club.OpenAsync();
        await using var transaction = (SqlTransaction)await club.BeginTransactionAsync();
        await ExecuteAsync(club, transaction, """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource=N'GamingClub.demo.v1',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=30000;
            IF @result < 0 THROW 51010, 'Cannot lock GamingClub demo database.', 1;
            """);
        await using var inventory = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo')", club, transaction);
        if (Convert.ToInt32(await inventory.ExecuteScalarAsync()) == 0)
        {
            foreach (var batch in schema.Skip(2))
                await ExecuteAsync(club, transaction, batch);
        }
        await ExecuteAsync(club, transaction, migration);
        foreach (var batch in seed.Skip(1))
            await ExecuteAsync(club, transaction, batch);

        await UpsertAccountAsync(club, transaction, AdminLogin, "ADMIN", "Администратор стенда",
            "GAMINGCLUB_DEMO_ADMIN_PASSWORD");
        await UpsertAccountAsync(club, transaction, VisitorLogin, "VISITOR", "Посетитель стенда",
            "GAMINGCLUB_DEMO_VISITOR_PASSWORD");
        await transaction.CommitAsync();
    }

    private static async Task CreateOrAttachAsync(SqlConnection connection, string dataDirectory)
    {
        var directory = Directory.CreateDirectory(Path.GetFullPath(dataDirectory)).FullName;
        var mdf = Path.Combine(directory, "GamingClub.mdf");
        var ldf = Path.Combine(directory, "GamingClub_log.ldf");
        var dataExists = File.Exists(mdf);
        if (dataExists != File.Exists(ldf))
            throw new InvalidOperationException("The demo database needs both its data file and its log.");

        // SQL Server requires literal filenames in CREATE DATABASE. Double apostrophes in paths.
        var dataPath = mdf.Replace("'", "''", StringComparison.Ordinal);
        var logPath = ldf.Replace("'", "''", StringComparison.Ordinal);
        var sql = dataExists
            ? $"CREATE DATABASE GamingClub ON (FILENAME=N'{dataPath}'), (FILENAME=N'{logPath}') FOR ATTACH;"
            : $"""
                CREATE DATABASE GamingClub
                ON PRIMARY (NAME=N'GamingClub', FILENAME=N'{dataPath}')
                LOG ON (NAME=N'GamingClub_log', FILENAME=N'{logPath}');
                """;
        await ExecuteAsync(connection, null, sql);
    }

    private static async Task UpsertAccountAsync(SqlConnection connection, SqlTransaction transaction,
        string login, string role, string name, string passwordVariable)
    {
        var password = Environment.GetEnvironmentVariable(passwordVariable);
        if (password is null || password.Length is < 12 or > 128)
            throw new InvalidOperationException("A valid demo password is required.");
        const string sql = """
            DECLARE @roleId int = (SELECT RoleId FROM dbo.Roles WHERE Code=@role);
            IF @roleId IS NULL THROW 51011, 'Demo role is missing.', 1;
            IF EXISTS (SELECT 1 FROM dbo.Users WHERE Login=@login AND RoleId<>@roleId)
                THROW 51012, 'A reserved demo login has an unexpected role.', 1;
            UPDATE dbo.Users SET PasswordHash=@hash WHERE Login=@login;
            IF @@ROWCOUNT = 0
                INSERT dbo.Users(RoleId, Login, PasswordHash, DisplayName)
                VALUES (@roleId, @login, @hash, @name);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@role", SqlDbType.NVarChar, 32).Value = role;
        command.Parameters.Add("@login", SqlDbType.NVarChar, 80).Value = login;
        command.Parameters.Add("@hash", SqlDbType.NVarChar, 255).Value = PasswordSecurity.Hash(password);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = name;
        await command.ExecuteNonQueryAsync();
    }

    private static string[] SplitBatches(string sql) => BatchSeparator().Split(sql)
        .Where(batch => !string.IsNullOrWhiteSpace(batch)).ToArray();

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    [GeneratedRegex(@"(?im)^[ \t]*GO[ \t]*\r?$")]
    private static partial Regex BatchSeparator();

    [GeneratedRegex(@"(?im)^[ \t]*USE GamingClub;[ \t]*\r?$")]
    private static partial Regex UseDatabase();

    [GeneratedRegex(@"^\(localdb\)\\GC[0-9a-f]{14}$", RegexOptions.IgnoreCase)]
    private static partial Regex DemoInstance();
}
