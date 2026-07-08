namespace ElBruno.CopilotHarness.Router.Core.Persistence;

/// <summary>Shadow routing request – parallel fire-and-forget call for comparison.</summary>
public sealed class ShadowRequestEntity
{
    public long Id { get; set; }
    public string ShadowId { get; set; } = string.Empty;
    public string OriginalTraceId { get; set; } = string.Empty;
    public string PrimaryProfile { get; set; } = string.Empty;
    public string ShadowProfile { get; set; } = string.Empty;
    public string PromptHash { get; set; } = string.Empty;
    public double PrimaryLatencyMs { get; set; }
    public double ShadowLatencyMs { get; set; }
    public int PrimaryStatusCode { get; set; }
    public int ShadowStatusCode { get; set; }
    public string OutcomeLabel { get; set; } = "pending";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

/// <summary>Persisted confidence snapshot for a routing rule over a rolling window.</summary>
public sealed class RuleConfidenceScoreEntity
{
    public long Id { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public int TotalInvocations { get; set; }
    public int SuccessfulInvocations { get; set; }
    public double ConfidenceScore { get; set; }
    public string WindowLabel { get; set; } = string.Empty;
    public DateTimeOffset WindowStartUtc { get; set; }
    public DateTimeOffset WindowEndUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A named benchmark run targeting one or more profiles.</summary>
public sealed class BenchmarkRunEntity
{
    public string RunId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProfilesJson { get; set; } = "[]";
    public string Status { get; set; } = "pending";
    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

/// <summary>Individual result for one prompt inside a benchmark run.</summary>
public sealed class BenchmarkResultEntity
{
    public long Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string PromptHash { get; set; } = string.Empty;
    public double LatencyMs { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int StatusCode { get; set; }
    public string JudgeVerdict { get; set; } = string.Empty;
    public double JudgeScore { get; set; }
    public string MetricsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Human approval request for a proposed rule or config change.</summary>
public sealed class ApprovalRequestEntity
{
    public string ApprovalId { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public string? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
}

/// <summary>Team-level routing profile override.</summary>
public sealed class TeamProfileEntity
{
    public string TeamId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DefaultProfile { get; set; } = string.Empty;
    public string RulesJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Project-level routing profile override (scoped beneath a team).</summary>
public sealed class ProjectProfileEntity
{
    public string ProjectId { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DefaultProfile { get; set; } = string.Empty;
    public string RulesJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
