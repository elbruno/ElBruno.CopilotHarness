namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed record SetupWizardState(
    bool IsCompleted,
    string DefaultProfile,
    DateTimeOffset? CompletedAtUtc);

public sealed record CompleteSetupWizardRequest(
    string LocalDeployment,
    string SmallDeployment,
    string BigDeployment,
    string DefaultProfile,
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
