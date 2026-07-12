namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class UsageTelemetryEventEntity
{
    public long Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset IngestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Proxy { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string RequestModel { get; set; } = string.Empty;
    public string? ResponseModel { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string Operation { get; set; } = "chat";
    public int? StatusCode { get; set; }
    public bool Succeeded { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
}
