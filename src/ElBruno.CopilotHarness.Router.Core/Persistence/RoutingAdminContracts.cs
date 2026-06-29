namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed record SetupWizardState(
    bool IsCompleted,
    string DefaultProfile,
    DateTimeOffset? CompletedAtUtc);

public sealed record CompleteSetupWizardRequest(
    string DefaultModel,
    bool GenerateFirstRules);

public sealed record ModelRegistryEntry(
    string ProfileName,
    string DisplayName,
    string Deployment,
    string ApiVersion,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertModelRegistryEntryRequest(
    string DisplayName,
    string Deployment,
    string ApiVersion,
    bool Enabled);

public sealed record BasicRulesConfiguration(
    string DefaultProfile,
    int BigPromptCharacterThreshold,
    string BigProfile,
    string StreamingProfile,
    bool PreferBigWhenSystemMessageExists,
    bool PreferStreamingProfileWhenStreaming,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpdateBasicRulesRequest(
    string DefaultProfile,
    int BigPromptCharacterThreshold,
    string BigProfile,
    string StreamingProfile,
    bool PreferBigWhenSystemMessageExists,
    bool PreferStreamingProfileWhenStreaming);

public sealed record SystemValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

// ── Model registry (multi-provider) ──────────────────────────────────────────

public sealed record ModelConnectionRecord(
    string Id,
    string Name,
    ModelProviderType ProviderType,
    string Endpoint,
    string ModelName,
    string ApiVersion,
    bool HasApiKey,
    bool Enabled,
    bool IsProcessor,
    bool SupportsCustomTemperature,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Create/update request for a model connection. On update, a null <see cref="ApiKey"/> leaves the
/// stored key unchanged; an empty string clears it; a non-empty value replaces it.
/// </summary>
public sealed record UpsertModelConnectionRequest(
    string Name,
    ModelProviderType ProviderType,
    string Endpoint,
    string ModelName,
    string ApiVersion,
    string? ApiKey,
    bool Enabled,
    bool IsProcessor,
    bool SupportsCustomTemperature);

public sealed record ModelConnectionTestResult(
    bool Success,
    string Message,
    double LatencyMs);

// ── Condition-based routing rules ────────────────────────────────────────────

public sealed record RoutingRuleRecord(
    int Id,
    string Name,
    string Description,
    RoutingRuleConditionType ConditionType,
    string ConditionValue,
    string TargetModel,
    int Priority,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertRoutingRuleRequest(
    string Name,
    string Description,
    RoutingRuleConditionType ConditionType,
    string ConditionValue,
    string TargetModel,
    int Priority,
    bool Enabled);

public sealed record RoutingDefaultModel(string ModelName, DateTimeOffset UpdatedAtUtc);

