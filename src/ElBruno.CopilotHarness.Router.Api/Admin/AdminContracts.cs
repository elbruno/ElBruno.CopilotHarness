using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public sealed record SetupWizardRequest(
    string DefaultModel,
    bool GenerateFirstRules);

/// <summary>State of the demo "routing footer" (response annotation) toggle.</summary>
public sealed record ResponseAnnotationSettingDto(bool Enabled);

public sealed record SetupWizardResponse(
    bool IsCompleted,
    string DefaultProfile,
    DateTimeOffset? CompletedAtUtc);

// ── Model registry (multi-provider connections) ──────────────────────────────

public sealed record ModelConnectionDto(
    string Id,
    string Name,
    string Type,
    string Endpoint,
    string ModelName,
    string ApiVersion,
    bool HasApiKey,
    bool Enabled,
    bool IsProcessor,
    bool SupportsCustomTemperature,
    bool SupportsToolCalling,
    DateTimeOffset UpdatedAtUtc);

public sealed record ModelConnectionUpsertRequest(
    string Name,
    string Type,
    string Endpoint,
    string ModelName,
    string ApiVersion,
    string? ApiKey,
    bool Enabled,
    bool IsProcessor = false,
    bool SupportsCustomTemperature = true,
    bool SupportsToolCalling = true);

public sealed record ModelConnectionTestResponse(
    bool Success,
    string Message,
    double LatencyMs);

// ── Condition-based routing rules ────────────────────────────────────────────

public sealed record RoutingRuleDto(
    int Id,
    string Name,
    string Description,
    string ConditionType,
    string ConditionValue,
    string TargetModel,
    int Priority,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record RoutingRuleUpsertRequest(
    string Name,
    string Description,
    string ConditionType,
    string ConditionValue,
    string TargetModel,
    int Priority,
    bool Enabled);

public sealed record RuleTestRequest(
    string Prompt,
    string? SystemMessage,
    bool Stream,
    string? RequestedModel);

public sealed record RuleTestResponse(
    string? MatchedRuleName,
    string SelectedModel,
    string Reason,
    int PromptCharacters,
    string? UserRequest = null,
    bool IsSemantic = false,
    string DecisionSource = "deterministic",
    double Confidence = 0,
    string ClassificationIntent = "",
    string ClassificationComplexity = "",
    string? SemanticReason = null,
    string? AnalyzerPrompt = null);

/// <summary>
/// The exact "rules analyzer" mega-prompt that the local processor model receives to pick the
/// matching semantic rule, plus the processor model name and the number of semantic rules it covers.
/// Surfaced on the Rules page so users can see precisely how local routing decisions are made.
/// </summary>
public sealed record RulesAnalyzerPromptResponse(
    bool HasProcessorModel,
    string? ProcessorModel,
    int SemanticRuleCount,
    string SystemPrompt);

public sealed record DefaultModelDto(
    string ModelName,
    DateTimeOffset UpdatedAtUtc);

public sealed record SetDefaultModelRequest(string ModelName);

public sealed record ModelProfileDto(
    string Name,
    string Category,
    string Deployment,
    string ApiVersion,
    bool Enabled);

public sealed record RuleDto(
    int Id,
    string Name,
    string Description,
    string Condition,
    string TargetProfile,
    int Priority,
    bool Enabled);

public sealed record RuleUpsertRequest(
    string Name,
    string Description,
    string Condition,
    string TargetProfile,
    int Priority,
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

/// <summary>
/// Single live-routing row: prompt → model → rule → explanation. Shared shape consumed by both
/// the Admin.Web "Live Routing" page and the VS Code extension.
/// </summary>
public sealed record RoutedRequestView(
    string TraceId,
    DateTimeOffset CreatedAtUtc,
    string ClientId,
    string ClientDisplayName,
    string Endpoint,
    bool Stream,
    string? RequestedModel,
    string SelectedModel,
    string Deployment,
    string? MatchedRuleName,
    string Reason,
    string Explanation,
    string? PromptPreview,
    int PromptCharacters,
    string ClassificationIntent,
    string ClassificationComplexity,
    string ClassifierSource,
    string? ProcessorModel,
    string? ProcessorModelType,
    double ClassificationConfidence,
    int TotalPromptCharacters,
    bool HasSystemMessage,
    string? RawUserMessage = null,
    string? SemanticReason = null,
    int? UpstreamStatusCode = null,
    double? UpstreamLatencyMs = null,
    bool UpstreamSucceeded = true,
    string? UpstreamError = null,
    bool RequestHadTools = false,
    bool ToolCapabilityOverrideApplied = false,
    string? OverrideReason = null,
    long? TokensIn = null,
    long? TokensOut = null,
    long? TokensTotal = null,
    string? ResponseModel = null);

public sealed record RoutingFeedResponse(
    DateTimeOffset GeneratedAtUtc,
    bool PromptCaptureEnabled,
    IReadOnlyList<RoutedRequestView> Requests);

public sealed record ValidationCheck(
    string Name,
    bool Passed,
    string Message);

public sealed record ValidationResponse(IReadOnlyList<ValidationCheck> Checks);

public sealed record ConnectedClientDto(
    string Client,
    bool IsConnected,
    int ActiveRequests,
    int RequestsLastFiveMinutes,
    DateTimeOffset? LastSeenAtUtc);

public sealed record LiveRequestDto(
    string RequestId,
    string Endpoint,
    string Client,
    bool Stream,
    string? RequestedModel,
    string? SelectedProfile,
    string? SelectedDeployment,
    string? TraceId,
    DateTimeOffset StartedAtUtc,
    double ElapsedMs);

public sealed record DashboardSnapshotResponse(
    IReadOnlyList<ConnectedClientDto> ConnectedClients,
    IReadOnlyList<LiveRequestDto> LiveRequests,
    DateTimeOffset GeneratedAtUtc);

public sealed record OperationalSignalDto(
    string Name,
    string State,
    string Details,
    string Recommendation);

public sealed record InfrastructureStatusDto(
    string Storage,
    string Cache,
    string DatabasePath,
    string EnvironmentName);

public sealed record OperationalHealthDto(
    string Name,
    string Status,
    string Details);

public sealed record OperationsStatusResponse(
    DateTimeOffset GeneratedAtUtc,
    OperationalSignalDto Authentication,
    OperationalSignalDto RateLimiting,
    OperationalSignalDto Backoff,
    OperationalSignalDto BackgroundJobs,
    InfrastructureStatusDto Infrastructure,
    IReadOnlyList<OperationalHealthDto> HealthChecks);

public sealed record RoutingTraceContextFactDto(string Key, string Value);

public sealed record ClassificationTraceDto(string Intent, string Complexity, double Confidence, string Reasoning);

public sealed record RuleAdvisorTraceDto(string? SuggestedProfile, string Rationale);

public sealed record RoutingWorkflowStepDto(string Name, string Outcome);

public sealed record RoutingDecisionTraceDto(string Profile, string Deployment, string Reason);

public sealed record RoutingTraceResponse(
    string TraceId,
    DateTimeOffset CreatedAtUtc,
    string WorkflowEngine,
    ClassificationTraceDto Classification,
    RuleAdvisorTraceDto RuleAdvisor,
    RoutingDecisionTraceDto Decision,
    IReadOnlyList<RoutingTraceContextFactDto> Context,
    IReadOnlyList<RoutingWorkflowStepDto> Steps);

public sealed record ConnectedClientTelemetryDto(
    string ClientId,
    string DisplayName,
    string Source,
    string? Version,
    DateTimeOffset LastSeenUtc,
    int RequestsLastHour,
    string LastProfile,
    string LastDeployment);

public sealed record ConnectedClientsResponse(
    DateTimeOffset SnapshotUtc,
    IReadOnlyList<ConnectedClientTelemetryDto> Clients);

public sealed record LiveRequestTelemetryDto(
    string TraceId,
    DateTimeOffset CreatedAtUtc,
    string Endpoint,
    string ClientId,
    string ClientDisplayName,
    string? ClientVersion,
    string Profile,
    string Deployment,
    string Reason,
    string ClassificationIntent,
    string ClassificationComplexity);

public sealed record LiveRequestsResponse(
    DateTimeOffset SnapshotUtc,
    IReadOnlyList<LiveRequestTelemetryDto> Requests);

// ── Live routing trace deletion ──────────────────────────────────────────────

public sealed record DeleteTraceResponse(bool Deleted);

public sealed record BulkDeleteTracesRequest(IReadOnlyList<string> TraceIds);

public sealed record BulkDeleteResponse(int DeletedCount);

public sealed record ClearTracesResponse(bool Cleared);

// ── Phase 8 – Continuous Evaluation ──────────────────────────────────────────

public sealed record RuleRecommendationDto(
    string Id,
    string RuleKey,
    string CurrentValue,
    string RecommendedValue,
    string Rationale,
    double Confidence,
    string Status,
    DateTimeOffset CreatedAtUtc);

public sealed record RecommendationsResponse(IReadOnlyList<RuleRecommendationDto> Recommendations);

public sealed record ReviewRecommendationRequest(
    string RecommendationId,
    string Decision,
    string? Reason);

public sealed record AdminTeamProfileDto(
    string Name,
    string Description,
    IReadOnlyList<string> PreferredModels,
    bool IsDefault);

public sealed record AdminCreateTeamRequest(
    string Name,
    string Description,
    IReadOnlyList<string> PreferredModels,
    bool IsDefault);

public sealed record AdminProjectProfileDto(
    string Name,
    string TeamProfile,
    IReadOnlyList<string> Tags,
    string OverrideProfile);

public sealed record AdminCreateProjectRequest(
    string Name,
    string TeamProfile,
    IReadOnlyList<string> Tags,
    string OverrideProfile);

public sealed record AdminBenchmarkRunDto(
    string Id,
    string Status,
    string Trigger,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalTests,
    int PassedTests,
    int FailedTests);

public sealed record BenchmarkStatusResponse(
    string SchedulerStatus,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? NextRunAtUtc,
    IReadOnlyList<AdminBenchmarkRunDto> RecentRuns,
    IReadOnlyList<AdminBenchmarkResultDto> Results);

public sealed record AdminBenchmarkResultDto(
    string RunId,
    string Profile,
    double AvgLatencyMs,
    double AvgTokens,
    double JudgeScore,
    string Summary);

public sealed record AdminRuleConfidenceDto(
    string RuleKey,
    double Confidence,
    string Trend,
    DateTimeOffset LastEvaluatedAtUtc);

public sealed record RulesConfidenceResponse(IReadOnlyList<AdminRuleConfidenceDto> Items);
