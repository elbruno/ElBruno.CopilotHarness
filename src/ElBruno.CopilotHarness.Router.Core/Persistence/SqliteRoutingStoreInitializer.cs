using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class SqliteRoutingStoreInitializer(
    HarnessDbContext dbContext,
    IOptions<RoutingOptions> bootstrapOptions)
{
    private readonly RoutingOptions _bootstrapOptions = bootstrapOptions.Value;

    private const string SeedDefaultModel = "foundry gpt-5-mini";

    private static IEnumerable<(string Id, string Name, int ProviderType, string Endpoint, string ModelName, string ApiVersion, bool Enabled)> SeedModels()
    {
        yield return (
            "seed-ollama-llama32",
            "ollama llama3.2",
            (int)ModelProviderType.Ollama,
            "http://localhost:11434",
            "llama3.2",
            "2024-10-21",
            true);

        yield return (
            "seed-foundry-gpt5mini",
            SeedDefaultModel,
            (int)ModelProviderType.AzureOpenAI,
            string.Empty,
            "gpt-5-mini",
            "2024-10-21",
            true);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS RoutingExecutionTraces (
                Id INTEGER NOT NULL CONSTRAINT PK_RoutingExecutionTraces PRIMARY KEY AUTOINCREMENT,
                TraceId TEXT NOT NULL,
                WorkflowEngine TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_RoutingExecutionTraces_TraceId ON RoutingExecutionTraces (TraceId);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RoutingExecutionTraces_CreatedAtUtc ON RoutingExecutionTraces (CreatedAtUtc);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS Models (
                Id TEXT NOT NULL CONSTRAINT PK_Models PRIMARY KEY,
                Name TEXT NOT NULL,
                ProviderType INTEGER NOT NULL DEFAULT 0,
                Endpoint TEXT NOT NULL DEFAULT '',
                ModelName TEXT NOT NULL DEFAULT '',
                ApiVersion TEXT NOT NULL DEFAULT '2024-10-21',
                ApiKeyProtected TEXT NULL,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Models_Name ON Models (Name);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS RoutingRules (
                Id INTEGER NOT NULL CONSTRAINT PK_RoutingRules PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                ConditionType INTEGER NOT NULL DEFAULT 0,
                ConditionValue TEXT NOT NULL DEFAULT '',
                TargetModel TEXT NOT NULL DEFAULT '',
                Priority INTEGER NOT NULL DEFAULT 0,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RoutingRules_Priority ON RoutingRules (Priority);",
            cancellationToken);

        foreach (var model in SeedModels())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT OR IGNORE INTO Models (
                     Id, Name, ProviderType, Endpoint, ModelName, ApiVersion, ApiKeyProtected, Enabled, CreatedAtUtc, UpdatedAtUtc
                 ) VALUES (
                     {model.Id},
                     {model.Name},
                     {model.ProviderType},
                     {model.Endpoint},
                     {model.ModelName},
                     {model.ApiVersion},
                     {(string?)null},
                     {model.Enabled},
                     {DateTimeOffset.UtcNow},
                     {DateTimeOffset.UtcNow}
                 );
                 """,
                cancellationToken);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT OR IGNORE INTO RoutingRuleSettings (
                 Id,
                 DefaultProfile,
                 BigPromptCharacterThreshold,
                 BigProfile,
                 StreamingProfile,
                 PreferBigWhenSystemMessageExists,
                 PreferStreamingProfileWhenStreaming,
                 UpdatedAtUtc
             ) VALUES (
                 {1},
                 {SeedDefaultModel},
                 {_bootstrapOptions.Rules.BigPromptCharacterThreshold},
                 {_bootstrapOptions.Rules.BigProfile},
                 {_bootstrapOptions.Rules.StreamingProfile},
                 {_bootstrapOptions.Rules.PreferBigWhenSystemMessageExists},
                 {_bootstrapOptions.Rules.PreferStreamingProfileWhenStreaming},
                 {DateTimeOffset.UtcNow}
             );
             """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT OR IGNORE INTO SetupState (
                 Id,
                 IsCompleted,
                 SelectedDefaultProfile
             ) VALUES (
                 {SetupStateEntity.DefaultId},
                 {false},
                 {SeedDefaultModel}
             );
             """,
            cancellationToken);

        // Phase 8 – Continuous Evaluation tables (CREATE IF NOT EXISTS is idempotent)
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ShadowRequests (
                Id INTEGER NOT NULL CONSTRAINT PK_ShadowRequests PRIMARY KEY AUTOINCREMENT,
                ShadowId TEXT NOT NULL,
                OriginalTraceId TEXT NOT NULL,
                PrimaryProfile TEXT NOT NULL,
                ShadowProfile TEXT NOT NULL,
                PromptHash TEXT NOT NULL,
                PrimaryLatencyMs REAL NOT NULL DEFAULT 0,
                ShadowLatencyMs REAL NOT NULL DEFAULT 0,
                PrimaryStatusCode INTEGER NOT NULL DEFAULT 0,
                ShadowStatusCode INTEGER NOT NULL DEFAULT 0,
                OutcomeLabel TEXT NOT NULL DEFAULT 'pending',
                CreatedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ShadowRequests_ShadowId ON ShadowRequests (ShadowId);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ShadowRequests_OriginalTraceId ON ShadowRequests (OriginalTraceId);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ShadowRequests_CreatedAtUtc ON ShadowRequests (CreatedAtUtc);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS RuleConfidenceScores (
                Id INTEGER NOT NULL CONSTRAINT PK_RuleConfidenceScores PRIMARY KEY AUTOINCREMENT,
                RuleKey TEXT NOT NULL,
                TotalInvocations INTEGER NOT NULL DEFAULT 0,
                SuccessfulInvocations INTEGER NOT NULL DEFAULT 0,
                ConfidenceScore REAL NOT NULL DEFAULT 0,
                WindowLabel TEXT NOT NULL,
                WindowStartUtc TEXT NOT NULL,
                WindowEndUtc TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RuleConfidenceScores_RecordedAtUtc ON RuleConfidenceScores (RecordedAtUtc);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS BenchmarkRuns (
                RunId TEXT NOT NULL CONSTRAINT PK_BenchmarkRuns PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                ProfilesJson TEXT NOT NULL DEFAULT '[]',
                Status TEXT NOT NULL DEFAULT 'pending',
                TotalItems INTEGER NOT NULL DEFAULT 0,
                CompletedItems INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                StartedAtUtc TEXT NULL,
                CompletedAtUtc TEXT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_BenchmarkRuns_Status ON BenchmarkRuns (Status);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_BenchmarkRuns_CreatedAtUtc ON BenchmarkRuns (CreatedAtUtc);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS BenchmarkResults (
                Id INTEGER NOT NULL CONSTRAINT PK_BenchmarkResults PRIMARY KEY AUTOINCREMENT,
                RunId TEXT NOT NULL,
                ItemId TEXT NOT NULL,
                Profile TEXT NOT NULL,
                Deployment TEXT NOT NULL DEFAULT '',
                PromptHash TEXT NOT NULL DEFAULT '',
                LatencyMs REAL NOT NULL DEFAULT 0,
                PromptTokens INTEGER NOT NULL DEFAULT 0,
                CompletionTokens INTEGER NOT NULL DEFAULT 0,
                StatusCode INTEGER NOT NULL DEFAULT 0,
                JudgeVerdict TEXT NOT NULL DEFAULT '',
                JudgeScore REAL NOT NULL DEFAULT 0,
                MetricsJson TEXT NOT NULL DEFAULT 'null',
                CreatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_BenchmarkResults_RunId ON BenchmarkResults (RunId);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ApprovalRequests (
                ApprovalId TEXT NOT NULL CONSTRAINT PK_ApprovalRequests PRIMARY KEY,
                ChangeType TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                PayloadJson TEXT NOT NULL DEFAULT 'null',
                Status TEXT NOT NULL DEFAULT 'pending',
                ReviewedBy TEXT NULL,
                ReviewNotes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                ReviewedAtUtc TEXT NULL,
                ExpiresAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ApprovalRequests_Status ON ApprovalRequests (Status);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ApprovalRequests_CreatedAtUtc ON ApprovalRequests (CreatedAtUtc);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS TeamProfiles (
                TeamId TEXT NOT NULL CONSTRAINT PK_TeamProfiles PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                DefaultProfile TEXT NOT NULL DEFAULT 'small',
                RulesJson TEXT NOT NULL DEFAULT 'null',
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ProjectProfiles (
                ProjectId TEXT NOT NULL CONSTRAINT PK_ProjectProfiles PRIMARY KEY,
                TeamId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                DefaultProfile TEXT NOT NULL DEFAULT 'small',
                RulesJson TEXT NOT NULL DEFAULT 'null',
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ProjectProfiles_TeamId ON ProjectProfiles (TeamId);",
            cancellationToken);
    }
}
