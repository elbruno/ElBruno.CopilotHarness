using System.ComponentModel.DataAnnotations;

namespace ElBruno.CopilotHarness.Judge.Web;

public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";
    public const string DefaultApiVersion = "2024-10-21";

    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public static Uri GetNormalizedEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Foundry endpoint must be an absolute URI.");
        }

        return uri;
    }
}

public sealed class JudgePersistenceOptions
{
    public const string SectionName = "JudgePersistence";

    [Required]
    public string DatabasePath { get; set; } = @"App_Data\copilotharness-judge.db";

    public string BuildConnectionString(string contentRootPath)
    {
        var path = Path.IsPathRooted(DatabasePath)
            ? DatabasePath
            : Path.GetFullPath(Path.Combine(contentRootPath, DatabasePath));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={path}";
    }
}
