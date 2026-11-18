using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace GamingClub.API;

public sealed record RegisterRequest(string Login, string Password, string DisplayName);
public sealed record LoginRequest(string Login, string Password);
public sealed record AuthUser(int UserId, string Login, string DisplayName, string Role);
public sealed record AuthOperation(AuthUser? User, string? Error);

/// <summary>PBKDF2-SHA256 hash with per-account cryptographic random salt.</summary>
public static class PasswordSecurity
{
    private const int Iterations = 210_000;
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split(':');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" ||
                !int.TryParse(parts[1], out var iterations) || iterations < 100_000 || iterations > 1_000_000)
                return false;
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>SQL Server account creation and password verification; public registration creates VISITOR only.</summary>
public sealed class AuthStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("GamingClub")
        ?? throw new InvalidOperationException("Set ConnectionStrings__GamingClub.");

    public async Task<AuthOperation> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var login = request.Login?.Trim() ?? "";
        var name = request.DisplayName?.Trim() ?? "";
        if (login.Length is < 3 or > 80 || !login.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
            name.Length is < 1 or > 100 || request.Password is null ||
            request.Password.Length < 12 || request.Password.Length > 128)
            return new(null, "Login must be 3-80 ASCII letters/digits/_/-; name 1-100; password 12-128.");
        var hashed = PasswordSecurity.Hash(request.Password);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            INSERT INTO dbo.Users (RoleId, Login, PasswordHash, DisplayName)
            OUTPUT INSERTED.UserId
            SELECT RoleId, @login, @hash, @name FROM dbo.Roles WHERE Code='VISITOR'
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@login", SqlDbType.NVarChar, 80).Value = login;
        cmd.Parameters.Add("@hash", SqlDbType.NVarChar, 255).Value = hashed;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = name;
        try
        {
            var id = await cmd.ExecuteScalarAsync(ct);
            return id is int userId
                ? new(new AuthUser(userId, login, name, "VISITOR"), null)
                : new(null, "Visitor role is missing from SQL seed.");
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return new(null, "Login already exists.");
        }
    }

    public async Task<AuthOperation> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        // Always perform a hash comparison even for unknown users to reduce user enumeration by timing.
        var login = request.Login?.Trim() ?? "";
        if (login.Length > 80 || request.Password is null || request.Password.Length > 128)
            return new(null, "Invalid credentials.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT u.UserId, u.Login, u.DisplayName, u.PasswordHash, r.Code
            FROM dbo.Users u JOIN dbo.Roles r ON u.RoleId=r.RoleId
            WHERE u.Login=@login
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@login", SqlDbType.NVarChar, 80).Value = login;
        int id = 0; string storedLogin = "", name = "", hash = "", role = "";
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                id = reader.GetInt32(0);
                storedLogin = reader.GetString(1);
                name = reader.GetString(2);
                hash = reader.GetString(3);
                role = reader.GetString(4);
            }
        }
        // Dummy hash is generated once per process so missing users are still expensive to check.
        if (!PasswordSecurity.Verify(request.Password, id > 0 ? hash : DummyHash))
            return new(null, "Invalid credentials.");
        return new(new AuthUser(id, storedLogin, name, role), null);
    }

    private static readonly string DummyHash = PasswordSecurity.Hash("a-random-unusable-dummy-password");
}
