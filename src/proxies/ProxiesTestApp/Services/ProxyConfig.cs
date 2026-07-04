namespace ProxiesTestApp.Services;

/// <summary>Describes one BYOK proxy the test app can talk to.</summary>
public sealed record ProxyConfig(string Name, string Label, string ConfigKey, string DefaultUrl, string Color)
{
    public static readonly ProxyConfig Ollama = new(
        "ollama-proxy", "OllamaProxy", "Ollama", "http://localhost:5099", "#0d6efd");

    public static readonly ProxyConfig Foundry = new(
        "foundry-proxy", "FoundryProxy", "Foundry", "http://localhost:5100", "#198754");

    public static readonly ProxyConfig FoundryLocal = new(
        "foundry-local-proxy", "FoundryLocalProxy", "FoundryLocal", "http://localhost:5101", "#6f42c1");

    public static readonly IReadOnlyList<ProxyConfig> All =
        [Ollama, Foundry, FoundryLocal];
}
