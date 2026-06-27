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

        if (!await dbContext.ModelProfiles.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            var profiles = _bootstrapOptions.Profiles.Select(profile => new ModelProfileEntity
            {
                ProfileName = profile.Key,
                DisplayName = profile.Key,
                Deployment = profile.Value.Deployment,
                ApiVersion = profile.Value.ApiVersion,
                Enabled = profile.Value.Enabled,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            await dbContext.ModelProfiles.AddRangeAsync(profiles, cancellationToken);
        }

        if (!await dbContext.RoutingRuleSettings.AnyAsync(cancellationToken))
        {
            dbContext.RoutingRuleSettings.Add(new RoutingRuleSettingsEntity
            {
                Id = 1,
                DefaultProfile = _bootstrapOptions.DefaultProfile,
                BigPromptCharacterThreshold = _bootstrapOptions.Rules.BigPromptCharacterThreshold,
                BigProfile = _bootstrapOptions.Rules.BigProfile,
                StreamingProfile = _bootstrapOptions.Rules.StreamingProfile,
                PreferBigWhenSystemMessageExists = _bootstrapOptions.Rules.PreferBigWhenSystemMessageExists,
                PreferStreamingProfileWhenStreaming = _bootstrapOptions.Rules.PreferStreamingProfileWhenStreaming,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        if (!await dbContext.SetupState.AnyAsync(cancellationToken))
        {
            dbContext.SetupState.Add(new SetupStateEntity
            {
                Id = SetupStateEntity.DefaultId,
                IsCompleted = false,
                SelectedDefaultProfile = _bootstrapOptions.DefaultProfile
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
