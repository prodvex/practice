using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GamingClub.API;

/// <summary>Issues short-lived bearer tokens based on verified server-side identity.</summary>
public sealed class TokenService(IConfiguration config)
{
    private readonly string issuer = "GamingClub.API";
    private readonly string audience = "GamingClub.Clients";

    public SymmetricSecurityKey Key
    {
        get
        {
            var secret = config["GAMINGCLUB_JWT_SECRET"];
            if (string.IsNullOrEmpty(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
                throw new InvalidOperationException(
                    "GAMINGCLUB_JWT_SECRET must contain at least 32 UTF-8 bytes; do not commit it.");
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        }
    }

    public string Issuer => issuer;
    public string Audience => audience;

    public string Issue(AuthUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("display_name", user.DisplayName)
        };
        var jwt = new JwtSecurityToken(issuer, audience, claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn,
    int UserId, string Login, string Role, string DisplayName);
