using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class SqliteRoutingStoreInitializer(
    HarnessDbContext dbContext,
    IOptions<RoutingOptions> bootstrapOptions)
{
    private readonly RoutingOptions _bootstrapOptions = bootstrapOptions.Value;

    private const string SeedDefaultModel = "foundry gpt-5-mini";
    private const string SeedLocalModelName = "ollama llama3.1";
    private const string SeedLocalModelDeployment = "llama3.1:8b";
    private const string SeedLocalModelEndpoint = "http://localhost:11434";
    private const string SeedFoundryLocalModelName = "foundry local phi-4-mini";
    private const string SeedFoundryLocalModelDeployment = "phi-4-mini";
    private const string SeedFoundryLocalModelEndpoint = "http://localhost:5101";

    private static IEnumerable<(string Id, string Name, int ProviderType, string Endpoint, string ModelName, string ApiVersion, bool Enabled, bool IsProcessor, bool SupportsCustomTemperature, bool SupportsToolCalling)> SeedModels()
    {
        // Default processor model: phi-4-mini via Foundry Local (FoundryLocalProxy on port 5101).
        // No Ollama install required. Override endpoint via FoundryLocal__Endpoint env var.
        // phi-4-mini handles temperature=0, structured JSON, and is tool-call capable.
        yield return (
            "seed-foundry-local-phi4mini",
            SeedFoundryLocalModelName,
            (int)ModelProviderType.FoundryLocal,
            SeedFoundryLocalModelEndpoint,
            SeedFoundryLocalModelDeployment,
            "2024-10-21",
            true,
            true,   // processor model (classifier + semantic rule analyzer)
            true,   // supports custom temperature
            true);  // tool-calling capable

        // Ollama llama3.1:8b — kept as an alternative local model (local rule target + tool-caller).
        // IsProcessor=false by default; user can promote it via Admin UI if preferred over Foundry Local.
        // Requires: ollama pull llama3.1:8b (run: ollama serve).
        yield return (
            "seed-ollama-llama31",
            SeedLocalModelName,
            (int)ModelProviderType.Ollama,
            SeedLocalModelEndpoint,
            SeedLocalModelDeployment,
            "2024-10-21",
            true,
            false,  // not processor by default; promote via Admin UI if Foundry Local is unavailable
            true,   // supports custom temperature
            true);  // tool-calling capable

        yield return (
            "seed-foundry-gpt5mini",
            SeedDefaultModel,
            (int)ModelProviderType.AzureOpenAI,
            string.Empty,
            "gpt-5-mini",
            "2024-10-21",
            true,
            false,
            false, // gpt-5 family only accepts the default temperature
            true); // tool-calling capable
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
                IsProcessor INTEGER NOT NULL DEFAULT 0,
                SupportsCustomTemperature INTEGER NOT NULL DEFAULT 1,
                SupportsToolCalling INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Models_Name ON Models (Name);",
            cancellationToken);

        // Idempotent column upgrades for databases created before these columns existed.
        await AddColumnIfMissingAsync("Models", "IsProcessor", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync("Models", "SupportsCustomTemperature", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        // The SupportsToolCalling column defaults to 1 (true) for every row, including Ollama models. The local
        // default model (llama3.1:8b) is a capable tool-caller, so we no longer force Ollama rows to 0 on
        // upgrade — the tool-calling size guard reroutes oversized agentic payloads to the cloud regardless.
        await AddColumnIfMissingAsync("Models", "SupportsToolCalling", "INTEGER NOT NULL DEFAULT 1", cancellationToken);

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
                     Id, Name, ProviderType, Endpoint, ModelName, ApiVersion, ApiKeyProtected, Enabled, IsProcessor, SupportsCustomTemperature, SupportsToolCalling, CreatedAtUtc, UpdatedAtUtc
                 ) VALUES (
                     {model.Id},
                     {model.Name},
                     {model.ProviderType},
                     {model.Endpoint},
                     {model.ModelName},
                     {model.ApiVersion},
                     {(string?)null},
                     {model.Enabled},
                     {model.IsProcessor},
                     {model.SupportsCustomTemperature},
                     {model.SupportsToolCalling},
                     {DateTimeOffset.UtcNow},
                     {DateTimeOffset.UtcNow}
                 );
                 """,
                cancellationToken);
        }

        // Upgrade fixup: on existing databases the Ollama model may still be IsProcessor=1 (set before
        // phi-4-mini was introduced). If phi-4-mini (FoundryLocal) is now the active processor, demote
        // Ollama so there is exactly one processor. Safe and idempotent: only runs when both the
        // seed-foundry-local-phi4mini row exists with IsProcessor=1 AND seed-ollama-llama31 is still marked
        // as processor. User-created models are not affected.
        await dbContext.Database.ExecuteSqlRawAsync(
            $"""
             UPDATE Models
             SET IsProcessor = 0, UpdatedAtUtc = '{DateTimeOffset.UtcNow:O}'
             WHERE Id = 'seed-ollama-llama31'
               AND IsProcessor = 1
               AND EXISTS (
                   SELECT 1 FROM Models
                   WHERE Id = 'seed-foundry-local-phi4mini' AND IsProcessor = 1 AND Enabled = 1
               );
             """,
            cancellationToken);

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

    private async Task<bool> AddColumnIfMissingAsync(string table, string column, string definition, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            bool exists;
            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = $"PRAGMA table_info({table});";
                exists = false;
                await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    // column 1 ("name") holds the column name.
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }

            return !exists;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
