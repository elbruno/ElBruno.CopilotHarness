namespace ElBruno.CopilotHarness.Judge.Web;

public sealed record RecommendationDto(
    Guid Id,
    string Type,
    string Summary,
    string Rationale,
    double Confidence,
    string SuggestedAction,
    string? SuggestedProfileName,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewNotes);

public sealed record ReviewRecommendationRequest(
    string Status,
    string? ReviewNotes);

public sealed record ContinuousBenchmarkScheduleDto(
    bool Enabled,
    int IntervalMinutes,
    int BatchSize,
    DateTimeOffset? LastRunAtUtc,
    string? LastRunStatus);
