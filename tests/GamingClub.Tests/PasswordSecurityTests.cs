using GamingClub.API;
using Xunit;

namespace GamingClub.Tests;

/// <summary>Pure authentication hash tests, no SQL Server required.</summary>
public class PasswordSecurityTests
{
    [Fact]
    public void CorrectPassword_Verifies()
    {
        var encoded = PasswordSecurity.Hash("correct-strong-password");
        Assert.True(PasswordSecurity.Verify("correct-strong-password", encoded));
    }

    [Fact]
    public void WrongPassword_Fails()
    {
        var encoded = PasswordSecurity.Hash("correct-strong-password");
        Assert.False(PasswordSecurity.Verify("different-strong-password", encoded));
    }

    [Fact]
    public void SamePassword_GetsDifferentSalt()
    {
        var a = PasswordSecurity.Hash("correct-strong-password");
        var b = PasswordSecurity.Hash("correct-strong-password");
        Assert.NotEqual(a, b);
        Assert.True(PasswordSecurity.Verify("correct-strong-password", a));
        Assert.True(PasswordSecurity.Verify("correct-strong-password", b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NO_LOGIN_PROTOTYPE_ACCOUNT")]
    [InlineData("pbkdf2-sha256:100:bad:bad")]
    [InlineData("pbkdf2-sha256:210000:::")]
    public void InvalidHash_IsRejected(string value) =>
        Assert.False(PasswordSecurity.Verify("correct-strong-password", value));
}
