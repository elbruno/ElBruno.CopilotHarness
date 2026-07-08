namespace ElBruno.CopilotHarness.Router.Api.Phase8;

// ─── Shadow Routing ───────────────────────────────────────────────────────────

public sealed record ShadowConfigDto(bool Enabled, string ShadowProfile, double SamplingRate);

public sealed record ShadowResultDto(
    string ShadowId,
    string OriginalTraceId,
    string PrimaryProfile,
    string ShadowProfile,
    double PrimaryLatencyMs,
    double ShadowLatencyMs,
    int PrimaryStatusCode,
    int ShadowStatusCode,
    string OutcomeLabel,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

// ─── Rule Confidence ──────────────────────────────────────────────────────────

public sealed record RuleConfidenceDto(
    string RuleKey,
    int TotalInvocations,
    int SuccessfulInvocations,
    double ConfidenceScore,
    string WindowLabel,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateTimeOffset RecordedAtUtc);

// ─── Benchmarks ───────────────────────────────────────────────────────────────

public sealed record CreateBenchmarkRunDto(
    string Name,
    string Description,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<BenchmarkPromptItemDto> Items);

public sealed record BenchmarkPromptItemDto(string ItemId, string Prompt, string? SystemMessage);

public sealed record BenchmarkRunDto(
    string RunId,
    string Name,
    string Description,
    IReadOnlyList<string> Profiles,
    string Status,
    int TotalItems,
    int CompletedItems,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record RecordBenchmarkResultDto(
    string ItemId,
    string Profile,
    string Deployment,
    string PromptHash,
    double LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int StatusCode,
    string JudgeVerdict,
    double JudgeScore,
    string MetricsJson);

public sealed record BenchmarkResultDto(
    long Id,
    string RunId,
    string ItemId,
    string Profile,
    string Deployment,
    double LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int StatusCode,
    string JudgeVerdict,
    double JudgeScore,
    DateTimeOffset CreatedAtUtc);

// ─── Human Approval Workflow ──────────────────────────────────────────────────

public sealed record CreateApprovalDto(
    string ChangeType,
    string Title,
    string Description,
    string PayloadJson,
    DateTimeOffset? ExpiresAtUtc);

public sealed record ReviewApprovalDto(bool Approved, string ReviewedBy, string? ReviewNotes);

public sealed record ApprovalRequestDto(
    string ApprovalId,
    string ChangeType,
    string Title,
    string Description,
    string PayloadJson,
    string Status,
    string? ReviewedBy,
    string? ReviewNotes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset ExpiresAtUtc);

// ─── Team & Project Profiles ──────────────────────────────────────────────────

public sealed record TeamProfileDto(
    string TeamId,
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertTeamProfileDto(
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled);

public sealed record ProjectProfileDto(
    string ProjectId,
    string TeamId,
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertProjectProfileDto(
    string TeamId,
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled);
