using System.ComponentModel.DataAnnotations;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    PostgreSql
}

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public DatabaseProvider Provider { get; init; } = DatabaseProvider.Sqlite;

    [Required]
    public string DatabasePath { get; init; } = @"App_Data\copilotharness-admin.db";

    public string? ConnectionString { get; init; }

    public string BuildConnectionString(string contentRootPath)
    {
        if (Provider == DatabaseProvider.PostgreSql)
        {
            return !string.IsNullOrWhiteSpace(ConnectionString)
                ? ConnectionString
                : throw new InvalidOperationException("A PostgreSQL connection string is required when the PostgreSql provider is selected.");
        }

        var resolvedPath = Path.IsPathRooted(DatabasePath)
            ? DatabasePath
            : Path.GetFullPath(Path.Combine(contentRootPath, DatabasePath));

        return $"Data Source={resolvedPath}";
    }
}
