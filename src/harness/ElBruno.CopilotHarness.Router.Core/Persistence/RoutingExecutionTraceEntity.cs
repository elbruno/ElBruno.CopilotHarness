namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class RoutingExecutionTraceEntity
{
    public long Id { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string WorkflowEngine { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
