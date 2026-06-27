using System.ComponentModel.DataAnnotations;

namespace ElBruno.CopilotHarness.Router.Api;

public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";
    public const string DeploymentName = "gpt-5-mini";
    public const string ApiVersion = "2024-10-21";

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
}
