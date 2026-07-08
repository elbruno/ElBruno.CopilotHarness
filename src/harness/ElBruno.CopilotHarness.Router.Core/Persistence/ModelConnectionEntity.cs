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

    /// <summary>
    /// When true, this model is the "processor" used by the classifier to read the first chars of a
    /// prompt and decide intent/routing. At most one model should be the processor (default: the Ollama model).
    /// </summary>
    public bool IsProcessor { get; set; }

    /// <summary>
    /// When true, this model acts as a shadow processor: it runs in parallel with the primary processor
    /// and its classification result is recorded for A/B comparison, but does NOT influence routing.
    /// At most one model should be the shadow processor. Used to evaluate alternative classifier models.
    /// </summary>
    public bool IsShadowProcessor { get; set; }

    /// <summary>
    /// When false, the router strips non-default sampling parameters (e.g. <c>temperature</c>, <c>top_p</c>)
    /// before forwarding upstream. Required for models such as gpt-5 that only accept the default temperature.
    /// </summary>
    public bool SupportsCustomTemperature { get; set; } = true;

    /// <summary>
    /// When false, the model cannot perform tool/function calling reliably. The router redirects
    /// tool-calling requests away from such models to a tool-capable model (e.g. local Ollama models).
    /// </summary>
    public bool SupportsToolCalling { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
