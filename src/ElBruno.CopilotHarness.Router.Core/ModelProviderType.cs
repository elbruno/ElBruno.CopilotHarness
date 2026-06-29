namespace ElBruno.CopilotHarness.Router.Core;

/// <summary>
/// Supported large language model provider connection types in the model registry.
/// </summary>
public enum ModelProviderType
{
    /// <summary>Azure OpenAI / Azure AI Foundry deployment (uses an api-key header and a deployment name).</summary>
    AzureOpenAI = 0,

    /// <summary>Local or remote Ollama server exposing an OpenAI-compatible chat completions endpoint.</summary>
    Ollama = 1
}
