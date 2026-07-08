namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class SetupStateEntity
{
    public const int DefaultId = 1;

    public int Id { get; set; } = DefaultId;
    public bool IsCompleted { get; set; }
    public string SelectedDefaultProfile { get; set; } = "small";
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
