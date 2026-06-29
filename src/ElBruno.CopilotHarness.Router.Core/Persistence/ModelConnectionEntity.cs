namespace ElBruno.CopilotHarness.Router.Core.Persistence;

/// <summary>
/// A model connection in the registry. Each row is a distinct LLM endpoint (Azure OpenAI / Foundry
/// or Ollama) with its own type, endpoint, model/deployment name and optional encrypted API key.
/// </summary>
public sealed class ModelConnectionEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Unique, human-friendly model name referenced by routing rules (e.g. "foundry gpt-5-mini").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider type stored as an integer (0 = AzureOpenAI, 1 = Ollama).</summary>
    public int ProviderType { get; set; } = (int)ModelProviderType.AzureOpenAI;

    /// <summary>Connection endpoint URL. May be empty for Azure connections that reuse the shared Foundry endpoint.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Azure deployment name or Ollama model name.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Azure API version. Ignored for Ollama.</summary>
    public string ApiVersion { get; set; } = "2024-10-21";

    /// <summary>API key encrypted at rest via ASP.NET Core Data Protection. Null when no key is configured.</summary>
    public string? ApiKeyProtected { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
