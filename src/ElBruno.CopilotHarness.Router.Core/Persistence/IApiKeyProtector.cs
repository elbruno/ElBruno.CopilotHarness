namespace ElBruno.CopilotHarness.Router.Core.Persistence;

/// <summary>
/// Protects model API keys at rest. Implementations encrypt on write and decrypt on read so that
/// plaintext keys are never persisted to the database.
/// </summary>
public interface IApiKeyProtector
{
    /// <summary>Encrypts a plaintext API key. Returns null for null/empty input.</summary>
    string? Protect(string? plaintext);

    /// <summary>Decrypts a protected API key. Returns empty string for null/empty input or on failure.</summary>
    string Unprotect(string? protectedValue);
}

/// <summary>
/// Fallback protector used when no Data Protection provider is registered (e.g. some unit tests).
/// Stores values unchanged. Not used in production wiring.
/// </summary>
public sealed class PassthroughApiKeyProtector : IApiKeyProtector
{
    public string? Protect(string? plaintext) => string.IsNullOrEmpty(plaintext) ? null : plaintext;

    public string Unprotect(string? protectedValue) => protectedValue ?? string.Empty;
}
