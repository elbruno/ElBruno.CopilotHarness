using ElBruno.CopilotHarness.Router.Api.Security;
using Microsoft.AspNetCore.DataProtection;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class ApiKeyProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_RoundTripsPlaintext()
    {
        var protector = new DataProtectionApiKeyProtector(new EphemeralDataProtectionProvider());

        var encrypted = protector.Protect("super-secret-key");

        Assert.NotNull(encrypted);
        Assert.NotEqual("super-secret-key", encrypted);
        Assert.Equal("super-secret-key", protector.Unprotect(encrypted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Protect_NullOrEmpty_ReturnsNull(string? value)
    {
        var protector = new DataProtectionApiKeyProtector(new EphemeralDataProtectionProvider());

        Assert.Null(protector.Protect(value));
    }

    [Fact]
    public void Unprotect_Garbage_ReturnsEmpty_DoesNotThrow()
    {
        var protector = new DataProtectionApiKeyProtector(new EphemeralDataProtectionProvider());

        Assert.Equal(string.Empty, protector.Unprotect("not-a-valid-protected-blob"));
    }
}
