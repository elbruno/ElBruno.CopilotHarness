using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class RoutingConfigurationStore(
    HarnessDbContext dbContext,
    IOptions<RoutingOptions> bootstrapOptions) : IRoutingConfigurationStore
{
    private readonly RoutingOptions _bootstrapOptions = bootstrapOptions.Value;

    public async Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken)
    {
        var profiles = await dbContext.ModelProfiles
            .AsNoTracking()
            .OrderBy(profile => profile.ProfileName)
            .ToListAsync(cancellationToken);

        var rules = await dbContext.RoutingRuleSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == 1, cancellationToken);

        if (profiles.Count == 0 || rules is null)
        {
            return _bootstrapOptions;
        }

        var mappedProfiles = profiles.ToDictionary(
            profile => profile.ProfileName,
            profile => new ModelProfileOptions
            {
                Deployment = profile.Deployment,
                ApiVersion = profile.ApiVersion,
                Enabled = profile.Enabled
            },
            StringComparer.OrdinalIgnoreCase);

        var defaultProfile = mappedProfiles.ContainsKey(rules.DefaultProfile)
            ? rules.DefaultProfile
            : _bootstrapOptions.DefaultProfile;

        return new RoutingOptions
        {
            DefaultProfile = defaultProfile,
            Profiles = mappedProfiles,
            Rules = new BasicRulesOptions
            {
                BigPromptCharacterThreshold = rules.BigPromptCharacterThreshold,
                BigProfile = rules.BigProfile,
                StreamingProfile = rules.StreamingProfile,
                PreferBigWhenSystemMessageExists = rules.PreferBigWhenSystemMessageExists,
                PreferStreamingProfileWhenStreaming = rules.PreferStreamingProfileWhenStreaming
            }
        };
    }

    public async Task<IReadOnlyList<ModelRegistryEntry>> GetModelRegistryAsync(CancellationToken cancellationToken)
    {
        var profiles = await dbContext.ModelProfiles
            .AsNoTracking()
            .OrderBy(profile => profile.ProfileName)
            .Select(profile => ToModelRegistryEntry(profile))
            .ToListAsync(cancellationToken);

        return profiles;
    }

    public async Task<SetupWizardState> GetSetupWizardStateAsync(CancellationToken cancellationToken)
    {
        var state = await dbContext.SetupState
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == SetupStateEntity.DefaultId, cancellationToken);

        return state is null
            ? new SetupWizardState(false, _bootstrapOptions.DefaultProfile, null)
            : new SetupWizardState(state.IsCompleted, state.SelectedDefaultProfile, state.CompletedAtUtc);
    }

    public async Task<SetupWizardState> CompleteSetupWizardAsync(CompleteSetupWizardRequest request, CancellationToken cancellationToken)
    {
        var deploymentByProfile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["local"] = request.LocalDeployment,
            ["small"] = request.SmallDeployment,
            ["big"] = request.BigDeployment
        };

        var profiles = await dbContext.ModelProfiles.ToListAsync(cancellationToken);
        foreach (var profile in profiles)
        {
            if (deploymentByProfile.TryGetValue(profile.ProfileName, out var deployment) &&
                !string.IsNullOrWhiteSpace(deployment))
            {
                profile.Deployment = deployment.Trim();
                profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        var setupState = await dbContext.SetupState
            .SingleOrDefaultAsync(entity => entity.Id == SetupStateEntity.DefaultId, cancellationToken);
        if (setupState is null)
        {
            setupState = new SetupStateEntity { Id = SetupStateEntity.DefaultId };
            dbContext.SetupState.Add(setupState);
        }

        var normalizedDefaultProfile = string.IsNullOrWhiteSpace(request.DefaultProfile)
            ? _bootstrapOptions.DefaultProfile
            : request.DefaultProfile.Trim();

        setupState.IsCompleted = true;
        setupState.SelectedDefaultProfile = normalizedDefaultProfile;
        setupState.CompletedAtUtc = DateTimeOffset.UtcNow;

        var rules = await dbContext.RoutingRuleSettings
            .SingleOrDefaultAsync(entity => entity.Id == 1, cancellationToken);
        if (rules is not null)
        {
            rules.DefaultProfile = normalizedDefaultProfile;
            rules.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (request.GenerateFirstRules)
            {
                rules.BigProfile = "big";
                rules.StreamingProfile = "small";
                rules.PreferBigWhenSystemMessageExists = true;
                rules.PreferStreamingProfileWhenStreaming = true;
                rules.BigPromptCharacterThreshold = _bootstrapOptions.Rules.BigPromptCharacterThreshold;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SetupWizardState(setupState.IsCompleted, setupState.SelectedDefaultProfile, setupState.CompletedAtUtc);
    }

    public async Task<ModelRegistryEntry> UpsertModelRegistryEntryAsync(
        string profileName,
        UpsertModelRegistryEntryRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedProfileName = profileName.Trim();
        var now = DateTimeOffset.UtcNow;

        var entity = await dbContext.ModelProfiles
            .SingleOrDefaultAsync(profile => profile.ProfileName == normalizedProfileName, cancellationToken);

        if (entity is null)
        {
            entity = new ModelProfileEntity
            {
                ProfileName = normalizedProfileName,
                CreatedAtUtc = now
            };
            dbContext.ModelProfiles.Add(entity);
        }

        entity.DisplayName = request.DisplayName.Trim();
        entity.Deployment = request.Deployment.Trim();
        entity.ApiVersion = request.ApiVersion.Trim();
        entity.Enabled = request.Enabled;
        entity.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToModelRegistryEntry(entity);
    }

    public async Task<BasicRulesConfiguration> GetBasicRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await dbContext.RoutingRuleSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == 1, cancellationToken);

        return rules is null
            ? ToBasicRulesConfiguration(_bootstrapOptions)
            : ToBasicRulesConfiguration(rules);
    }

    public async Task<BasicRulesConfiguration> UpdateBasicRulesAsync(UpdateBasicRulesRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rules = await dbContext.RoutingRuleSettings
            .SingleOrDefaultAsync(entity => entity.Id == 1, cancellationToken);

        if (rules is null)
        {
            rules = new RoutingRuleSettingsEntity { Id = 1 };
            dbContext.RoutingRuleSettings.Add(rules);
        }

        rules.DefaultProfile = request.DefaultProfile.Trim();
        rules.BigPromptCharacterThreshold = request.BigPromptCharacterThreshold;
        rules.BigProfile = request.BigProfile.Trim();
        rules.StreamingProfile = request.StreamingProfile.Trim();
        rules.PreferBigWhenSystemMessageExists = request.PreferBigWhenSystemMessageExists;
        rules.PreferStreamingProfileWhenStreaming = request.PreferStreamingProfileWhenStreaming;
        rules.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToBasicRulesConfiguration(rules);
    }

    public async Task<SystemValidationResult> ValidateSystemAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var profiles = await dbContext.ModelProfiles
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var rules = await GetBasicRulesAsync(cancellationToken);
        var setupState = await GetSetupWizardStateAsync(cancellationToken);

        if (!setupState.IsCompleted)
        {
            warnings.Add("Setup wizard has not been completed yet.");
        }

        foreach (var requiredProfile in new[] { "local", "small", "big" })
        {
            if (!profiles.Any(profile => string.Equals(profile.ProfileName, requiredProfile, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Missing required model profile '{requiredProfile}'.");
            }
        }

        if (profiles.Count(profile => profile.Enabled) == 0)
        {
            errors.Add("At least one enabled model profile is required.");
        }

        var profileNames = profiles
            .Select(profile => profile.ProfileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!profileNames.Contains(rules.DefaultProfile))
        {
            errors.Add($"Default profile '{rules.DefaultProfile}' does not exist in the model registry.");
        }

        if (!profileNames.Contains(rules.BigProfile))
        {
            errors.Add($"Big prompt profile '{rules.BigProfile}' does not exist in the model registry.");
        }

        if (!profileNames.Contains(rules.StreamingProfile))
        {
            errors.Add($"Streaming profile '{rules.StreamingProfile}' does not exist in the model registry.");
        }

        if (rules.BigPromptCharacterThreshold <= 0)
        {
            warnings.Add("Big prompt character threshold should be greater than zero.");
        }

        return new SystemValidationResult(errors.Count == 0, errors, warnings);
    }

    private static ModelRegistryEntry ToModelRegistryEntry(ModelProfileEntity profile) =>
        new(
            profile.ProfileName,
            profile.DisplayName,
            profile.Deployment,
            profile.ApiVersion,
            profile.Enabled,
            profile.UpdatedAtUtc);

    private static BasicRulesConfiguration ToBasicRulesConfiguration(RoutingRuleSettingsEntity rules) =>
        new(
            rules.DefaultProfile,
            rules.BigPromptCharacterThreshold,
            rules.BigProfile,
            rules.StreamingProfile,
            rules.PreferBigWhenSystemMessageExists,
            rules.PreferStreamingProfileWhenStreaming,
            rules.UpdatedAtUtc);

    private static BasicRulesConfiguration ToBasicRulesConfiguration(RoutingOptions options) =>
        new(
            options.DefaultProfile,
            options.Rules.BigPromptCharacterThreshold,
            options.Rules.BigProfile,
            options.Rules.StreamingProfile,
            options.Rules.PreferBigWhenSystemMessageExists,
            options.Rules.PreferStreamingProfileWhenStreaming,
            DateTimeOffset.MinValue);
}
