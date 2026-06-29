namespace ElBruno.CopilotHarness.Router.Core.Persistence;

/// <summary>
/// A condition-based routing rule. Rules are evaluated in ascending <see cref="Priority"/> order;
/// the first enabled rule whose condition matches selects its <see cref="TargetModel"/>.
/// </summary>
public sealed class RoutingRuleEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Condition type stored as an integer (see <see cref="RoutingRuleConditionType"/>).</summary>
    public int ConditionType { get; set; }

    /// <summary>Condition argument (e.g. threshold value, keyword, regex, requested model name).</summary>
    public string ConditionValue { get; set; } = string.Empty;

    /// <summary>Name of the model in the registry to route to when this rule matches.</summary>
    public string TargetModel { get; set; } = string.Empty;

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
