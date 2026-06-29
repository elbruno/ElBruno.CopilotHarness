using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace ElBruno.CopilotHarness.Router.Api.Security;

/// <summary>
/// Protects model API keys at rest using ASP.NET Core Data Protection. Keys are encrypted before
/// being persisted to the database and decrypted on read.
/// </summary>
public sealed class DataProtectionApiKeyProtector : IApiKeyProtector
{
    private const string Purpose = "ElBruno.CopilotHarness.ModelRegistry.ApiKey.v1";

    private readonly IDataProtector _protector;

    public DataProtectionApiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        return _protector.Protect(plaintext);
    }

    public string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Key ring rotated or value corrupted — treat as no usable key rather than crashing routing.
            return string.Empty;
        }
    }
}
