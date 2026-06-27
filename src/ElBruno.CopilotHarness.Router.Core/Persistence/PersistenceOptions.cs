using System.ComponentModel.DataAnnotations;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    [Required]
    public string DatabasePath { get; init; } = @"App_Data\copilotharness-admin.db";

    public string BuildConnectionString(string contentRootPath)
    {
        var resolvedPath = Path.IsPathRooted(DatabasePath)
            ? DatabasePath
            : Path.GetFullPath(Path.Combine(contentRootPath, DatabasePath));

        return $"Data Source={resolvedPath}";
    }
}
