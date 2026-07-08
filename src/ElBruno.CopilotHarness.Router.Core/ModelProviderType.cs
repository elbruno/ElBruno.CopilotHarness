namespace ElBruno.CopilotHarness.Router.Core;

/// <summary>
/// Supported large language model provider connection types in the model registry.
/// </summary>
public enum ModelProviderType
{
    /// <summary>Azure OpenAI / Azure AI Foundry deployment (uses an api-key header and a deployment name).</summary>
    AzureOpenAI = 0,

    /// <summary>Local or remote Ollama server exposing an OpenAI-compatible chat completions endpoint.</summary>
    Ollama = 1,

    /// <summary>
    /// Microsoft Foundry Local — a local model runtime exposing an OpenAI-compatible REST server.
    /// Default endpoint: http://localhost:5101 (FoundryLocalProxy) or http://localhost:55588 (SDK-direct).
    /// No API key required. Supports phi-4-mini and other open-weight models via CPU/GPU/NPU.
    /// </summary>
    FoundryLocal = 2
}

/// <summary>Extension helpers for <see cref="ModelProviderType"/>.</summary>
public static class ModelProviderTypeExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> for provider types that run locally on the user's machine
    /// (no cloud API key required). Both <see cref="ModelProviderType.Ollama"/> and
    /// <see cref="ModelProviderType.FoundryLocal"/> are local providers.
    /// </summary>
    public static bool IsLocalProvider(this ModelProviderType type) =>
        type is ModelProviderType.Ollama or ModelProviderType.FoundryLocal;
}
