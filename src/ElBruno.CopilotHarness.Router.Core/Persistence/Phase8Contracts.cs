namespace ElBruno.CopilotHarness.Router.Core.Persistence;

// ─── Shadow Routing ───────────────────────────────────────────────────────────

public sealed record ShadowRoutingConfig(
    bool Enabled,
    string ShadowProfile,
    double SamplingRate);

public sealed record ShadowRequestRecord(
    string ShadowId,
    string OriginalTraceId,
    string PrimaryProfile,
    string ShadowProfile,
    string PromptHash,
    double PrimaryLatencyMs,
    double ShadowLatencyMs,
    int PrimaryStatusCode,
    int ShadowStatusCode,
    string OutcomeLabel,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public interface IShadowRoutingStore
{
    Task<ShadowRoutingConfig> GetConfigAsync(CancellationToken cancellationToken);
    Task<ShadowRoutingConfig> SaveConfigAsync(ShadowRoutingConfig config, CancellationToken cancellationToken);
    Task<string> RecordShadowRequestAsync(
        string originalTraceId,
        string primaryProfile,
        string shadowProfile,
        string promptHash,
        CancellationToken cancellationToken);
    Task UpdateShadowOutcomeAsync(
        string shadowId,
        double primaryLatencyMs,
        double shadowLatencyMs,
        int primaryStatusCode,
        int shadowStatusCode,
        string outcomeLabel,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ShadowRequestRecord>> GetRecentResultsAsync(int count, CancellationToken cancellationToken);
}

// ─── Rule Confidence ──────────────────────────────────────────────────────────

public sealed record RuleConfidenceSummary(
    string RuleKey,
    int TotalInvocations,
    int SuccessfulInvocations,
    double ConfidenceScore,
    string WindowLabel,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateTimeOffset RecordedAtUtc);

public interface IRuleConfidenceStore
{
    Task RecordInvocationAsync(
        string ruleKey,
        bool successful,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<RuleConfidenceSummary>> GetCurrentScoresAsync(CancellationToken cancellationToken);
    Task<RuleConfidenceSummary?> GetScoreAsync(string ruleKey, CancellationToken cancellationToken);
}

// ─── Benchmarks ───────────────────────────────────────────────────────────────

public sealed record CreateBenchmarkRunRequest(
    string Name,
    string Description,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<BenchmarkPromptItem> Items);

public sealed record BenchmarkPromptItem(
    string ItemId,
    string Prompt,
    string? SystemMessage);

public sealed record BenchmarkRunSummary(
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

public sealed record BenchmarkResultRecord(
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

public sealed record RecordBenchmarkResultRequest(
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

public interface IBenchmarkStore
{
    Task<BenchmarkRunSummary> CreateRunAsync(CreateBenchmarkRunRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<BenchmarkRunSummary?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<BenchmarkRunSummary> UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken);
    Task<BenchmarkResultRecord> RecordResultAsync(string runId, RecordBenchmarkResultRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<BenchmarkResultRecord>> GetResultsAsync(string runId, CancellationToken cancellationToken);
}

// ─── Human Approval Workflow ──────────────────────────────────────────────────

public sealed record CreateApprovalRequest(
    string ChangeType,
    string Title,
    string Description,
    string PayloadJson,
    DateTimeOffset? ExpiresAtUtc);

public sealed record ReviewApprovalRequest(
    bool Approved,
    string ReviewedBy,
    string? ReviewNotes);

public sealed record ApprovalRequestSummary(
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

public interface IApprovalWorkflowStore
{
    Task<ApprovalRequestSummary> CreateAsync(CreateApprovalRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApprovalRequestSummary>> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<ApprovalRequestSummary?> GetAsync(string approvalId, CancellationToken cancellationToken);
    Task<ApprovalRequestSummary> ReviewAsync(string approvalId, ReviewApprovalRequest review, CancellationToken cancellationToken);
    Task<int> ExpireOverdueAsync(CancellationToken cancellationToken);
}

// ─── Team & Project Profiles ──────────────────────────────────────────────────

public sealed record TeamProfileSummary(
    string TeamId,
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertTeamProfileRequest(
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled);

public sealed record ProjectProfileSummary(
    string ProjectId,
    string TeamId,
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertProjectProfileRequest(
    string TeamId,
    string DisplayName,
    string DefaultProfile,
    string RulesJson,
    bool Enabled);

public interface ITeamProjectProfileStore
{
    Task<IReadOnlyList<TeamProfileSummary>> ListTeamsAsync(CancellationToken cancellationToken);
    Task<TeamProfileSummary?> GetTeamAsync(string teamId, CancellationToken cancellationToken);
    Task<TeamProfileSummary> UpsertTeamAsync(string teamId, UpsertTeamProfileRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteTeamAsync(string teamId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectProfileSummary>> ListProjectsAsync(string? teamId, CancellationToken cancellationToken);
    Task<ProjectProfileSummary?> GetProjectAsync(string projectId, CancellationToken cancellationToken);
    Task<ProjectProfileSummary> UpsertProjectAsync(string projectId, UpsertProjectProfileRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken);
}
