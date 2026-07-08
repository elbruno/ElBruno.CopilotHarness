using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Admin.Web.Services;

public sealed record SetupWizardRequest(
    string DefaultModel,
    bool GenerateFirstRules);

public sealed record SetupWizardResponse(
    bool IsCompleted,
    string DefaultProfile,
    DateTimeOffset? CompletedAtUtc);

/// <summary>State of the demo "routing footer" (response annotation) toggle.</summary>
public sealed record ResponseAnnotationSettingDto(bool Enabled);

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
    bool IsShadowProcessor,
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
    bool IsProcessor,
    bool IsShadowProcessor = false,
    bool SupportsCustomTemperature = true,
    bool SupportsToolCalling = true);

public sealed record ModelConnectionTestResponse(
    bool Success,
    string Message,
    double LatencyMs);

public sealed record ModelStatusDto(
    string Status,
    bool IsEndpointReachable,
    bool IsModelAvailable,
    string Details);

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

public sealed record RulesAnalyzerPromptResponse(
    bool HasProcessorModel,
    string? ProcessorModel,
    int SemanticRuleCount,
    string SystemPrompt);

public sealed record DefaultModelDto(
    string ModelName,
    DateTimeOffset UpdatedAtUtc);

public sealed record SetDefaultModelRequest(string ModelName);

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
    string ClassifierSource = "deterministic",
    string? ProcessorModel = null,
    string? ProcessorModelType = null,
    double ClassificationConfidence = 0,
    int TotalPromptCharacters = 0,
    bool HasSystemMessage = false,
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
    string? ResponseModel = null,
    // Shadow processor A/B fields
    string? ShadowIntent = null,
    string? ShadowProcessorModel = null,
    bool? ShadowAgreement = null);

public sealed record RoutingFeedResponse(
    DateTimeOffset GeneratedAtUtc,
    bool PromptCaptureEnabled,
    IReadOnlyList<RoutedRequestView> Requests);

// ── Trace deletion (mirrors Router.Api admin trace endpoints) ─────────────────

public sealed record DeleteTraceResponse(bool Deleted);

public sealed record DeleteTracesRequest(IReadOnlyList<string> TraceIds);

public sealed record DeleteTracesResponse(int DeletedCount);

public sealed record ClearTracesResponse(bool Cleared);

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

public sealed record RecommendationsResponse(
    IReadOnlyList<RuleRecommendationDto> Recommendations);

public sealed record ApprovalDecisionRequest(
    string RecommendationId,
    string Decision,
    string? Reason);

public sealed record TeamProfileDto(
    string Name,
    string Description,
    IReadOnlyList<string> PreferredModels,
    bool IsDefault);

public sealed record CreateTeamProfileRequest(
    string Name,
    string Description,
    IReadOnlyList<string> PreferredModels,
    bool IsDefault);

public sealed record ProjectProfileDto(
    string Name,
    string TeamProfile,
    IReadOnlyList<string> Tags,
    string OverrideProfile);

public sealed record CreateProjectProfileRequest(
    string Name,
    string TeamProfile,
    IReadOnlyList<string> Tags,
    string OverrideProfile);

public sealed record BenchmarkRunDto(
    string Id,
    string Status,
    string Trigger,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalTests,
    int PassedTests,
    int FailedTests);

public sealed record BenchmarkResultDto(
    string RunId,
    string Profile,
    double AvgLatencyMs,
    double AvgTokens,
    double JudgeScore,
    string Summary);

public sealed record BenchmarkStatusResponse(
    string SchedulerStatus,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? NextRunAtUtc,
    IReadOnlyList<BenchmarkRunDto> RecentRuns,
    IReadOnlyList<BenchmarkResultDto> Results);

public sealed record RuleConfidenceDto(
    string RuleKey,
    double Confidence,
    string Trend,
    DateTimeOffset LastEvaluatedAtUtc);

public sealed record RulesConfidenceResponse(
    IReadOnlyList<RuleConfidenceDto> Items);

// ── A/B Classifier comparison ──────────────────────────────────────────────

public sealed record AbIntentPairDto(
    string PrimaryIntent,
    string ShadowIntent,
    int Count,
    bool Agrees);

public sealed record AbClassifierSummaryResponse(
    int TotalTracesInWindow,
    int TracesWithShadow,
    int AgreementCount,
    int DisagreementCount,
    double? AgreementRate,
    IReadOnlyList<AbIntentPairDto> IntentBreakdown);

// ── Foundry Local SDK catalog ──────────────────────────────────────────────

public sealed record FoundryLocalSdkStatusDto(
    bool IsInitialized,
    string? WebServiceUrl,
    string? InitError);

public sealed record FoundryCatalogModelDto(
    string Alias,
    string DisplayName,
    string? Description,
    string ModelId,
    bool IsCached,
    bool IsLoaded,
    float? DownloadProgress);
