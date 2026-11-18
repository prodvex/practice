using GamingClub.API;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace GamingClub.Tests;

public class TokenServiceTests
{
    private static TokenService Create() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GAMINGCLUB_JWT_SECRET"] = "LocalTestingOnly_SecretKey_AtLeast32Bytes_2026"
        }).Build());

    [Fact]
    public void TokenContainsIdentityAndRole()
    {
        var token = Create().Issue(new AuthUser(42, "sample", "Sample User", "VISITOR"));
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(parsed.Claims, claim => claim.Value == "42");
        Assert.Contains(parsed.Claims, claim => claim.Value == "VISITOR");
    }

    [Fact]
    public void TokenExpiresInTwoHours()
    {
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(
            Create().Issue(new AuthUser(42, "sample", "Sample User", "VISITOR")));
        Assert.InRange(parsed.ValidTo, DateTime.UtcNow.AddHours(1).AddMinutes(59),
            DateTime.UtcNow.AddHours(2).AddMinutes(1));
    }

    [Fact]
    public void MissingJwtSecret_IsRejected()
    {
        var service = new TokenService(new ConfigurationBuilder().Build());
        Assert.Throws<InvalidOperationException>(() => service.Key);
    }

    [Fact]
    public void ShortJwtSecret_IsRejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["GAMINGCLUB_JWT_SECRET"] = "short" }).Build();
        Assert.Throws<InvalidOperationException>(() => new TokenService(config).Key);
    }
}
