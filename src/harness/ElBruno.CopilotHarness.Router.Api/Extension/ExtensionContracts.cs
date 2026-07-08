using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Extension;

public sealed record DashboardLinkDto(
    string Id,
    string Label,
    string Description,
    string Path,
    bool RequiresAdminAuth);

public sealed record LanguageModelToolMetadataDto(
    string Name,
    string Description,
    JsonObject ParametersSchema);

public sealed record ExtensionStatusSurfaceDto(
    DateTimeOffset GeneratedAtUtc,
    string State,
    string Summary,
    string? LatestTraceId,
    string? LatestProfile,
    string? LatestDeployment,
    IReadOnlyList<DashboardLinkDto> DashboardLinks,
    IReadOnlyList<OperationalHealthDto> HealthChecks);

public sealed record ExtensionExplainRoutingSurfaceDto(
    string Title,
    string Description,
    string EndpointTemplate,
    string TraceIdParameterName,
    DashboardLinkDto DashboardLink);

public sealed record ExtensionChatParticipantSurfaceDto(
    string ParticipantName,
    string DisplayName,
    string Description,
    IReadOnlyList<string> TriggerPhrases);

public sealed record ExtensionCapabilitiesResponse(
    DateTimeOffset GeneratedAtUtc,
    ExtensionStatusSurfaceDto Status,
    ExtensionExplainRoutingSurfaceDto ExplainRouting,
    ExtensionChatParticipantSurfaceDto ChatParticipant,
    IReadOnlyList<DashboardLinkDto> DashboardLinks,
    IReadOnlyList<LanguageModelToolMetadataDto> LanguageModelTools);
