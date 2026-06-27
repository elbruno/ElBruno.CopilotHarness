using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

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
