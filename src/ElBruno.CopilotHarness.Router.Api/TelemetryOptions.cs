using System.Text.RegularExpressions;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Controls how much request detail the routing telemetry/visibility surface captures.
/// Prompt-text capture is OPT-IN and privacy-first: disabled by default, truncated, and
/// optionally redacted so the harness never persists raw secrets.
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>When true, a truncated/redacted preview of the prompt is captured into the routing trace.</summary>
    public bool CapturePromptText { get; set; }

    /// <summary>Maximum number of characters kept in the captured prompt preview.</summary>
    public int PromptPreviewMaxChars { get; set; } = 2000;

    /// <summary>When true, obvious secrets (emails, API keys, bearer tokens) are masked in the preview.</summary>
    public bool RedactSecrets { get; set; } = true;
}

public static partial class PromptPrivacy
{
    /// <summary>Context fact key used to carry the captured prompt preview through the routing trace.</summary>
    public const string PromptPreviewFactKey = "request.promptPreview";

    public static string BuildPreview(string promptText, TelemetryOptions options)
    {
        if (string.IsNullOrEmpty(promptText))
        {
            return string.Empty;
        }

        var text = promptText.Replace("\r\n", "\n").Trim();

        if (options.RedactSecrets)
        {
            text = Redact(text);
        }

        var maxChars = options.PromptPreviewMaxChars <= 0 ? 2000 : options.PromptPreviewMaxChars;
        if (text.Length > maxChars)
        {
            text = text[..maxChars] + "…";
        }

        return text;
    }

    public static string Redact(string text)
    {
        text = EmailRegex().Replace(text, "[redacted-email]");
        text = BearerRegex().Replace(text, "Bearer [redacted-token]");
        text = ApiKeyRegex().Replace(text, "[redacted-key]");
        return text;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BearerRegex();

    // sk-/ghp_/xoxb- style tokens and long opaque secrets.
    [GeneratedRegex(@"\b(?:sk|ghp|gho|xoxb|xoxp)[-_][A-Za-z0-9]{16,}\b", RegexOptions.Compiled)]
    private static partial Regex ApiKeyRegex();
}
