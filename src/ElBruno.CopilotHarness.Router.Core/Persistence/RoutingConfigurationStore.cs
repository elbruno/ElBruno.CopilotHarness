using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class RoutingConfigurationStore(
    HarnessDbContext dbContext,
    IApiKeyProtector apiKeyProtector,
    IOptions<RoutingOptions> bootstrapOptions) : IRoutingConfigurationStore
{
    private readonly RoutingOptions _bootstrapOptions = bootstrapOptions.Value;

    public async Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken)
    {
        var models = await dbContext.Models
            .AsNoTracking()
            .OrderBy(model => model.Name)
            .ToListAsync(cancellationToken);

        if (models.Count == 0)
        {
            return _bootstrapOptions;
        }

        var ruleEntities = await dbContext.RoutingRules
            .AsNoTracking()
            .OrderBy(rule => rule.Priority)
            .ToListAsync(cancellationToken);

        var settings = await dbContext.RoutingRuleSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == 1, cancellationToken);

        var profiles = models.ToDictionary(
            model => model.Name,
            model => new ModelProfileOptions
            {
                Type = (ModelProviderType)model.ProviderType,
                Endpoint = model.Endpoint,
                Deployment = model.ModelName,
                ApiVersion = model.ApiVersion,
                ApiKey = apiKeyProtector.Unprotect(model.ApiKeyProtected),
                Enabled = model.Enabled,
                IsProcessor = model.IsProcessor,
                SupportsCustomTemperature = model.SupportsCustomTemperature
            },
            StringComparer.OrdinalIgnoreCase);

        var defaultModel = settings?.DefaultProfile ?? string.Empty;
        if (!profiles.ContainsKey(defaultModel))
        {
            defaultModel = models.FirstOrDefault(model => model.Enabled)?.Name
                ?? models[0].Name;
        }

        var ruleSet = ruleEntities
            .Select(rule => new RoutingRuleDefinition(
                rule.Id,
                rule.Name,
                rule.Description,
                (RoutingRuleConditionType)rule.ConditionType,
                rule.ConditionValue,
                rule.TargetModel,
                rule.Priority,
                rule.Enabled))
            .ToList();

        return new RoutingOptions
        {
            DefaultProfile = defaultModel,
            Profiles = profiles,
            RuleSet = ruleSet,
            Rules = settings is null
                ? _bootstrapOptions.Rules
                : new BasicRulesOptions
                {
                    BigPromptCharacterThreshold = settings.BigPromptCharacterThreshold,
                    BigProfile = settings.BigProfile,
                    StreamingProfile = settings.StreamingProfile,
                    PreferBigWhenSystemMessageExists = settings.PreferBigWhenSystemMessageExists,
                    PreferStreamingProfileWhenStreaming = settings.PreferStreamingProfileWhenStreaming
                }
        };
    }

    // ── Setup wizard ─────────────────────────────────────────────────────────

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
        var defaultModel = string.IsNullOrWhiteSpace(request.DefaultModel)
            ? _bootstrapOptions.DefaultProfile
            : request.DefaultModel.Trim();

        var setupState = await dbContext.SetupState
            .SingleOrDefaultAsync(entity => entity.Id == SetupStateEntity.DefaultId, cancellationToken);
        if (setupState is null)
        {
            setupState = new SetupStateEntity { Id = SetupStateEntity.DefaultId };
            dbContext.SetupState.Add(setupState);
        }

        setupState.IsCompleted = true;
        setupState.SelectedDefaultProfile = defaultModel;
        setupState.CompletedAtUtc = DateTimeOffset.UtcNow;

        await EnsureSettingsAsync(defaultModel, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (request.GenerateFirstRules)
        {
            await GenerateStarterRulesAsync(cancellationToken);
        }

        return new SetupWizardState(setupState.IsCompleted, setupState.SelectedDefaultProfile, setupState.CompletedAtUtc);
    }

    // ── Model registry ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ModelConnectionRecord>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var models = await dbContext.Models
            .AsNoTracking()
            .OrderBy(model => model.Name)
            .ToListAsync(cancellationToken);

        return models.Select(ToRecord).ToList();
    }

    public async Task<ModelConnectionRecord?> GetModelAsync(string id, CancellationToken cancellationToken)
    {
        var model = await dbContext.Models
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        return model is null ? null : ToRecord(model);
    }

    public async Task<ModelProfileOptions?> ResolveModelConnectionAsync(string id, CancellationToken cancellationToken)
    {
        var model = await dbContext.Models
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (model is null)
        {
            return null;
        }

        return new ModelProfileOptions
        {
            Type = (ModelProviderType)model.ProviderType,
            Endpoint = model.Endpoint,
            Deployment = model.ModelName,
            ApiVersion = model.ApiVersion,
            ApiKey = apiKeyProtector.Unprotect(model.ApiKeyProtected),
            Enabled = model.Enabled,
            IsProcessor = model.IsProcessor,
            SupportsCustomTemperature = model.SupportsCustomTemperature
        };
    }

    public async Task<ModelConnectionRecord> UpsertModelAsync(string? id, UpsertModelConnectionRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        ModelConnectionEntity? entity = null;

        if (!string.IsNullOrWhiteSpace(id))
        {
            entity = await dbContext.Models.SingleOrDefaultAsync(model => model.Id == id, cancellationToken);
        }

        if (entity is null)
        {
            entity = new ModelConnectionEntity
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id!,
                CreatedAtUtc = now
            };
            dbContext.Models.Add(entity);
        }

        entity.Name = request.Name.Trim();
        entity.ProviderType = (int)request.ProviderType;
        entity.Endpoint = request.Endpoint.Trim();
        entity.ModelName = request.ModelName.Trim();
        entity.ApiVersion = string.IsNullOrWhiteSpace(request.ApiVersion) ? "2024-10-21" : request.ApiVersion.Trim();
        entity.Enabled = request.Enabled;
        entity.IsProcessor = request.IsProcessor;
        entity.SupportsCustomTemperature = request.SupportsCustomTemperature;
        entity.UpdatedAtUtc = now;

        // null = leave key unchanged; empty = clear; value = replace.
        if (request.ApiKey is not null)
        {
            entity.ApiKeyProtected = string.IsNullOrEmpty(request.ApiKey)
                ? null
                : apiKeyProtector.Protect(request.ApiKey);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Single-processor invariant: when this model becomes the processor, clear the flag on every other model.
        if (entity.IsProcessor)
        {
            var others = await dbContext.Models
                .Where(model => model.Id != entity.Id && model.IsProcessor)
                .ToListAsync(cancellationToken);
            if (others.Count > 0)
            {
                foreach (var other in others)
                {
                    other.IsProcessor = false;
                    other.UpdatedAtUtc = now;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return ToRecord(entity);
    }

    public async Task<bool> DeleteModelAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Models.SingleOrDefaultAsync(model => model.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.Models.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ── Routing rules ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RoutingRuleRecord>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await dbContext.RoutingRules
            .AsNoTracking()
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

        return rules.Select(ToRecord).ToList();
    }

    public async Task<RoutingRuleRecord?> GetRuleAsync(int id, CancellationToken cancellationToken)
    {
        var rule = await dbContext.RoutingRules
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        return rule is null ? null : ToRecord(rule);
    }

    public async Task<RoutingRuleRecord> CreateRuleAsync(UpsertRoutingRuleRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new RoutingRuleEntity { CreatedAtUtc = now };
        ApplyRule(entity, request);
        entity.UpdatedAtUtc = now;
        dbContext.RoutingRules.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<RoutingRuleRecord?> UpdateRuleAsync(int id, UpsertRoutingRuleRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.RoutingRules.SingleOrDefaultAsync(rule => rule.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        ApplyRule(entity, request);
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.RoutingRules.SingleOrDefaultAsync(rule => rule.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.RoutingRules.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RoutingRuleRecord>> GenerateStarterRulesAsync(CancellationToken cancellationToken)
    {
        var existing = await dbContext.RoutingRules.AnyAsync(cancellationToken);
        if (existing)
        {
            return await GetRulesAsync(cancellationToken);
        }

        var models = await dbContext.Models
            .AsNoTracking()
            .Where(model => model.Enabled)
            .OrderBy(model => model.Name)
            .ToListAsync(cancellationToken);

        if (models.Count == 0)
        {
            return [];
        }

        var smallModel = PickModel(models, "mini", "small", "llama", "3.2") ?? models[0];
        var largeModel = PickModel(models, "5.5", "large", "big", "gpt-5") ?? models[^1];
        var now = DateTimeOffset.UtcNow;

        dbContext.RoutingRules.AddRange(
            new RoutingRuleEntity
            {
                Name = "Simple chat",
                Description = "Short greetings and small talk stay on the local processor model.",
                ConditionType = (int)RoutingRuleConditionType.IntentEquals,
                ConditionValue = ClassifierIntentNames.SimpleChat,
                TargetModel = smallModel.Name,
                Priority = 10,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new RoutingRuleEntity
            {
                Name = "GitHub actions",
                Description = "Git/GitHub operations (commit, push, open PR) route to the local model. Copilot drives the tools.",
                ConditionType = (int)RoutingRuleConditionType.IntentEquals,
                ConditionValue = ClassifierIntentNames.GithubActions,
                TargetModel = smallModel.Name,
                Priority = 20,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new RoutingRuleEntity
            {
                Name = "Launch app",
                Description = "Requests to run or launch the application route to the local model.",
                ConditionType = (int)RoutingRuleConditionType.IntentEquals,
                ConditionValue = ClassifierIntentNames.LaunchApp,
                TargetModel = smallModel.Name,
                Priority = 30,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new RoutingRuleEntity
            {
                Name = "Code tasks",
                Description = "Writing, refactoring, or debugging code routes to the cloud model.",
                ConditionType = (int)RoutingRuleConditionType.IntentEquals,
                ConditionValue = ClassifierIntentNames.CodeTask,
                TargetModel = largeModel.Name,
                Priority = 40,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new RoutingRuleEntity
            {
                Name = "Large prompts",
                Description = "Route prompts over 2500 characters to the larger model.",
                ConditionType = (int)RoutingRuleConditionType.PromptSizeAtLeast,
                ConditionValue = "2500",
                TargetModel = largeModel.Name,
                Priority = 50,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new RoutingRuleEntity
            {
                Name = "System-guided prompts",
                Description = "Route requests that include a system message to the larger model.",
                ConditionType = (int)RoutingRuleConditionType.HasSystemMessage,
                ConditionValue = string.Empty,
                TargetModel = largeModel.Name,
                Priority = 60,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new RoutingRuleEntity
            {
                Name = "Streaming requests",
                Description = "Route streaming requests to the faster model.",
                ConditionType = (int)RoutingRuleConditionType.IsStreaming,
                ConditionValue = string.Empty,
                TargetModel = smallModel.Name,
                Priority = 70,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        await EnsureSettingsAsync(smallModel.Name, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetRulesAsync(cancellationToken);
    }

    public async Task<RoutingDefaultModel> GetDefaultModelAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.RoutingRuleSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == 1, cancellationToken);

        return settings is null
            ? new RoutingDefaultModel(_bootstrapOptions.DefaultProfile, DateTimeOffset.MinValue)
            : new RoutingDefaultModel(settings.DefaultProfile, settings.UpdatedAtUtc);
    }

    public async Task<RoutingDefaultModel> SetDefaultModelAsync(string modelName, CancellationToken cancellationToken)
    {
        var settings = await EnsureSettingsAsync(modelName.Trim(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RoutingDefaultModel(settings.DefaultProfile, settings.UpdatedAtUtc);
    }

    // ── Legacy basic rules (Phase 8 + advisor back-compat) ────────────────────

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
        rules.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToBasicRulesConfiguration(rules);
    }

    public async Task<SystemValidationResult> ValidateSystemAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var models = await dbContext.Models.AsNoTracking().ToListAsync(cancellationToken);
        var rules = await dbContext.RoutingRules.AsNoTracking().ToListAsync(cancellationToken);
        var setupState = await GetSetupWizardStateAsync(cancellationToken);
        var defaultModel = await GetDefaultModelAsync(cancellationToken);

        if (!setupState.IsCompleted)
        {
            warnings.Add("Setup wizard has not been completed yet.");
        }

        if (models.Count == 0)
        {
            errors.Add("At least one model connection must be configured.");
        }

        if (models.Count > 0 && models.All(model => !model.Enabled))
        {
            errors.Add("At least one model connection must be enabled.");
        }

        var modelNames = models.Select(model => model.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (models.Count > 0 && !modelNames.Contains(defaultModel.ModelName))
        {
            errors.Add($"Default model '{defaultModel.ModelName}' does not exist in the model registry.");
        }

        foreach (var model in models.Where(model => (ModelProviderType)model.ProviderType == ModelProviderType.AzureOpenAI))
        {
            if (string.IsNullOrWhiteSpace(model.ModelName))
            {
                errors.Add($"Azure model '{model.Name}' is missing a deployment name.");
            }
        }

        foreach (var rule in rules.Where(rule => rule.Enabled))
        {
            if (!modelNames.Contains(rule.TargetModel))
            {
                errors.Add($"Rule '{rule.Name}' targets unknown model '{rule.TargetModel}'.");
            }
        }

        if (rules.Count == 0)
        {
            warnings.Add("No routing rules are configured. Requests will fall back to the default model.");
        }

        return new SystemValidationResult(errors.Count == 0, errors, warnings);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<RoutingRuleSettingsEntity> EnsureSettingsAsync(string defaultModel, CancellationToken cancellationToken)
    {
        var settings = await dbContext.RoutingRuleSettings
            .SingleOrDefaultAsync(entity => entity.Id == 1, cancellationToken);

        if (settings is null)
        {
            settings = new RoutingRuleSettingsEntity { Id = 1 };
            dbContext.RoutingRuleSettings.Add(settings);
        }

        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            settings.DefaultProfile = defaultModel;
        }

        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return settings;
    }

    private static ModelConnectionEntity? PickModel(IEnumerable<ModelConnectionEntity> models, params string[] keywords) =>
        models.FirstOrDefault(model => keywords.Any(keyword =>
            model.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            model.ModelName.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

    private static void ApplyRule(RoutingRuleEntity entity, UpsertRoutingRuleRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.ConditionType = (int)request.ConditionType;
        entity.ConditionValue = request.ConditionValue.Trim();
        entity.TargetModel = request.TargetModel.Trim();
        entity.Priority = request.Priority;
        entity.Enabled = request.Enabled;
    }

    private static ModelConnectionRecord ToRecord(ModelConnectionEntity model) =>
        new(
            model.Id,
            model.Name,
            (ModelProviderType)model.ProviderType,
            model.Endpoint,
            model.ModelName,
            model.ApiVersion,
            !string.IsNullOrEmpty(model.ApiKeyProtected),
            model.Enabled,
            model.IsProcessor,
            model.SupportsCustomTemperature,
            model.UpdatedAtUtc);

    private static RoutingRuleRecord ToRecord(RoutingRuleEntity rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.Description,
            (RoutingRuleConditionType)rule.ConditionType,
            rule.ConditionValue,
            rule.TargetModel,
            rule.Priority,
            rule.Enabled,
            rule.UpdatedAtUtc);

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
