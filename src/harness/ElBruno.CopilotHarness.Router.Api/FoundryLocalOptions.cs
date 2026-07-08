namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Configuration for the Microsoft Foundry Local provider.
/// Foundry Local runs open-weight models (phi-4-mini, llama, etc.) locally via an
/// OpenAI-compatible REST server — no cloud account or API key required.
/// </summary>
public sealed class FoundryLocalOptions
{
    public const string SectionName = "FoundryLocal";

    /// <summary>
    /// Base URL of the Foundry Local REST endpoint.
    /// <list type="bullet">
    ///   <item><c>http://localhost:5101</c> — FoundryLocalProxy (stable external port, recommended for dev).</item>
    ///   <item><c>http://localhost:55588</c> — SDK-direct internal port (configurable in FoundryLocalProxy appsettings).</item>
    /// </list>
    /// Override via environment variable <c>FoundryLocal__Endpoint</c>.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:5101";

    /// <summary>
    /// Default model alias served by this Foundry Local instance.
    /// Used only as a display hint; the actual model name is stored per-model-entry in the registry.
    /// </summary>
    public string DefaultModel { get; init; } = "phi-4-mini";
}
