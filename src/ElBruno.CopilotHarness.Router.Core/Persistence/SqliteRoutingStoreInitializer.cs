using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class SqliteRoutingStoreInitializer(
    HarnessDbContext dbContext,
    IOptions<RoutingOptions> bootstrapOptions)
{
    private readonly RoutingOptions _bootstrapOptions = bootstrapOptions.Value;

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

        foreach (var profile in _bootstrapOptions.Profiles)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT OR IGNORE INTO ModelProfiles (
                     ProfileName,
                     DisplayName,
                     Deployment,
                     ApiVersion,
                     Enabled,
                     CreatedAtUtc,
                     UpdatedAtUtc
                 ) VALUES (
                     {profile.Key},
                     {profile.Key},
                     {profile.Value.Deployment},
                     {profile.Value.ApiVersion},
                     {profile.Value.Enabled},
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
                 {_bootstrapOptions.DefaultProfile},
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
                 {_bootstrapOptions.DefaultProfile}
             );
             """,
            cancellationToken);
    }
}
