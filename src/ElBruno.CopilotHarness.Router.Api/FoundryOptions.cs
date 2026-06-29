using System.ComponentModel.DataAnnotations;

namespace ElBruno.CopilotHarness.Router.Api;

public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";
    public const string DefaultApiVersion = "2024-10-21";

    [Required]
    public string Endpoint { get; init; } = string.Empty;

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public static Uri GetNormalizedEndpoint(string endpoint)
    {
        var baseAddress = endpoint.EndsWith("/", StringComparison.Ordinal)
            ? endpoint
            : $"{endpoint}/";

        return new Uri(baseAddress, UriKind.Absolute);
    }

    /// <summary>
    /// Returns the Azure OpenAI resource root (with a trailing slash) for an Azure endpoint, tolerating
    /// values that already include an <c>/openai</c> or <c>/openai/v1</c> suffix. This prevents a doubled
    /// path (e.g. <c>/openai/v1/openai/deployments/...</c>) when the deployments path is appended.
    /// </summary>
    public static Uri GetAzureResourceBase(string endpoint)
    {
        var trimmed = (endpoint ?? string.Empty).Trim().TrimEnd('/');

        if (trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"/openai/v1".Length];
        }
        else if (trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"/openai".Length];
        }

        return new Uri($"{trimmed}/", UriKind.Absolute);
    }
}
