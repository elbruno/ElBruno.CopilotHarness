using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Admin.Web.Services;

public sealed record SetupWizardRequest(
    string LocalDeployment,
    string SmallDeployment,
    string BigDeployment,
    string DefaultProfile,
    bool GenerateFirstRules);

public sealed record SetupWizardResponse(
    bool IsCompleted,
    string DefaultProfile,
    DateTimeOffset? CompletedAtUtc);

public sealed record ModelProfileDto(
    string Name,
    string Category,
    string Deployment,
    string ApiVersion,
    bool Enabled);

public sealed record BasicRulesDto(
    string DefaultProfile,
    int BigPromptCharacterThreshold,
    string BigProfile,
    string StreamingProfile,
    bool PreferBigWhenSystemMessageExists,
    bool PreferStreamingProfileWhenStreaming,
    DateTimeOffset UpdatedAtUtc);

public sealed record BasicRulesUpdateRequest(
    string DefaultProfile,
    int BigPromptCharacterThreshold,
    string BigProfile,
    string StreamingProfile,
    bool PreferBigWhenSystemMessageExists,
    bool PreferStreamingProfileWhenStreaming);

public sealed record PlaygroundRequest(
    string Prompt,
    string? SystemMessage,
    bool Stream,
    string? RequestedProfile);

public sealed record PlaygroundResponse(
    string Profile,
    string Deployment,
    string Reason,
    int PromptCharacters,
    JsonObject RoutedRequest);

public sealed record ValidationCheck(
    string Name,
    bool Passed,
    string Message);

public sealed record ValidationResponse(IReadOnlyList<ValidationCheck> Checks);
