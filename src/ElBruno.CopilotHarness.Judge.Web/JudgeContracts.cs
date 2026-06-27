using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Judge.Web;

public sealed record ImportPromptRecordsRequest(
    IReadOnlyList<PromptRecordImportRequest> Records);

public sealed record PromptRecordImportRequest(
    string Source,
    string ClientId,
    string Endpoint,
    string Prompt,
    string? SystemMessage,
    string? RequestedModel,
    string? ReferenceAnswer,
    JsonObject? Metadata);

public sealed record PromptRecordResponse(
    Guid Id,
    string Source,
    string ClientId,
    string Endpoint,
    string Prompt,
    string? SystemMessage,
    string? RequestedModel,
    string? ReferenceAnswer,
    DateTimeOffset ImportedAtUtc);

public sealed record ReplayPromptRecordsRequest(
    string Name,
    IReadOnlyList<Guid> PromptRecordIds,
    IReadOnlyList<BenchmarkModelRequest> Models);

public sealed record HistoricalPromptSuiteSummaryDto(
    string Id,
    string Name,
    string Description,
    int PromptCount);

public sealed record HistoricalPromptSuiteDto(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<PromptRecordImportRequest> Prompts);

public sealed record ReplayHistoricalSuiteRequest(
    string? Name,
    IReadOnlyList<BenchmarkModelRequest> Models);

public sealed record ManualBenchmarkRequest(
    string Name,
    string Prompt,
    string? SystemMessage,
    IReadOnlyList<BenchmarkModelRequest> Models,
    string? ReferenceAnswer);

public sealed record BenchmarkModelRequest(
    string ProfileName,
    string Deployment,
    string? ApiVersion);

public sealed record JudgeScoresDto(
    double Correctness,
    double Completeness,
    double Security,
    double BestPractices,
    double Cost,
    double Latency,
    double Tokens,
    double Overall);

public sealed record BenchmarkResultDto(
    Guid Id,
    Guid PromptRecordId,
    string ProfileName,
    string Deployment,
    bool IsWinner,
    int? InputTokens,
    int? OutputTokens,
    double? LatencyMs,
    string ResponseText,
    JudgeScoresDto Scores);

public sealed record BenchmarkRunSummaryDto(
    Guid Id,
    string Name,
    string Mode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PromptRecordCount,
    int ModelCount,
    string? FailureReason);

public sealed record BenchmarkPromptReportDto(
    Guid PromptRecordId,
    string Prompt,
    string? ReferenceAnswer,
    IReadOnlyList<BenchmarkResultDto> Results);

public sealed record BenchmarkModelReportDto(
    string ProfileName,
    string Deployment,
    int Attempts,
    int Wins,
    double AverageOverallScore,
    double AverageLatencyMs,
    double AverageTokenScore);

public sealed record BenchmarkRunReportDto(
    BenchmarkRunSummaryDto Run,
    IReadOnlyList<BenchmarkPromptReportDto> Prompts,
    IReadOnlyList<BenchmarkModelReportDto> Models,
    DateTimeOffset GeneratedAtUtc);
