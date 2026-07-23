using DailyGate.Api.Auth;

namespace DailyGate.Api.Tests;

public sealed class SecurityServiceTests
{
    [Fact]
    public void Argon2HashRoundTripsAndRejectsWrongPassword()
    {
        var service = new PasswordService();
        var hash = service.Hash("A-secure-password1!");
        Assert.True(service.Verify("A-secure-password1!", hash));
        Assert.False(service.Verify("A-secure-password2!", hash));
    }

    [Fact]
    public void AdminPasswordAllowsEightCharacters()
    {
        var service = new PasswordService();
        var hash = service.HashAdmin("admin123");
        Assert.True(service.Verify("admin123", hash));
    }

}
