using System.Text;
using ElBruno.CopilotHarness.Router.Api.Telemetry;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Seed configuration for the demo "routing footer" feature. The live ON/OFF value is held by
/// <see cref="IResponseAnnotationState"/> so a presenter can toggle it at runtime without a restart;
/// this option only provides the initial value when the app starts.
/// </summary>
public sealed class ResponseAnnotationOptions
{
    public const string SectionName = "ResponseAnnotation";

    /// <summary>Initial ON/OFF value for the routing footer at startup. Defaults to off.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Runtime ON/OFF switch for the routing footer, so it can be flipped live during a demo via the
/// Admin API / Admin dashboard. Seeded from <see cref="ResponseAnnotationOptions"/> at startup and
/// reset to that value on restart. Registered as a singleton.
/// </summary>
public interface IResponseAnnotationState
{
    bool Enabled { get; set; }
}

/// <inheritdoc />
public sealed class ResponseAnnotationState : IResponseAnnotationState
{
    private volatile bool _enabled;

    public ResponseAnnotationState(IOptions<ResponseAnnotationOptions> options)
    {
        _enabled = options.Value.Enabled;
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}

/// <summary>
/// Builds the small routing footer that is appended to plain (non-tool) chat responses so the
/// selected rule, model, request source and token count are visible directly in the Copilot chat window.
/// </summary>
public static class RoutingAnnotation
{
    public static string BuildFooter(
        string profileName,
        string deployment,
        string reason,
        string clientDisplayName,
        bool toolOverride,
        TokenUsage? usage)
    {
        var builder = new StringBuilder();
        builder.Append("\n\n---\n🧭 Copilot Harness");

        var rule = ExtractRuleName(reason);
        if (!string.IsNullOrWhiteSpace(rule))
        {
            builder.Append(" · rule ‘").Append(rule).Append('’');
        }

        builder.Append(" → ").Append(profileName);
        if (!string.IsNullOrWhiteSpace(deployment))
        {
            builder.Append(" (").Append(deployment).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(clientDisplayName))
        {
            builder.Append(" · src: ").Append(clientDisplayName);
        }

        if (toolOverride)
        {
            builder.Append(" · tool-override");
        }

        if (usage?.TotalTokens is long total)
        {
            builder.Append(" · ").Append(total).Append(" tok");
            if (usage.InputTokens is long input && usage.OutputTokens is long output)
            {
                builder.Append(" (").Append(input).Append('→').Append(output).Append(')');
            }
        }

        return builder.ToString();
    }

    private static string? ExtractRuleName(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        // Reason strings look like: "Matched rule 'Simple chat'. …"
        const string marker = "rule '";
        var start = reason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = reason.IndexOf('\'', start);
        return end > start ? reason[start..end] : null;
    }
}
