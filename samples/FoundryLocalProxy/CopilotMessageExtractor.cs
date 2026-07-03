using System.Text.Json.Nodes;

namespace FoundryLocalProxy;

/// <summary>
/// Extracts the real user-typed message from a raw GitHub Copilot Chat request payload.
///
/// <para><b>Why this class exists</b><br/>
/// When GitHub Copilot Chat sends a request it wraps the user's one-line ask ("hi") inside
/// a large XML-like envelope that can be several kilobytes:
/// <code>
///   &lt;attachments&gt;...file contents...&lt;/attachments&gt;
///   &lt;context&gt;...editor context...&lt;/context&gt;
///   &lt;reminderInstructions&gt;...&lt;/reminderInstructions&gt;
///   &lt;userRequest&gt;hi&lt;/userRequest&gt;
/// </code>
/// If you naively read the last "user" message to decide routing or log the ask,
/// you see ~3 000 chars of boilerplate instead of the word "hi".
/// This class peels away the envelope so a harness can make cheap, accurate decisions.
/// </para>
///
/// <para><b>Source</b><br/>
/// Ported from <c>ElBruno.CopilotHarness.Router.Api/ModelRouter.cs</c> — same logic,
/// extracted into a self-contained file so this sample has zero dependency on the main project.
/// </para>
/// </summary>
public static class CopilotMessageExtractor
{
    private static readonly string[] CopilotWrapperTags =
    [
        "attachments",
        "context",
        "reminderInstructions",
        "environment_info",
        "editorContext",
        "currentEditor",
        "instructions",
        "toolResult",
        "tool-result"
    ];

    /// <summary>
    /// Extracts the actual text the user typed from the <b>last</b> "user" role message
    /// in an OpenAI-style <c>messages</c> array.
    /// </summary>
    public static string GetLastUserMessageText(JsonObject requestBody)
    {
        if (requestBody["messages"] is not JsonArray messages)
            return string.Empty;

        var lastUserMessage = messages
            .OfType<JsonObject>()
            .LastOrDefault(msg =>
                string.Equals(GetStringValue(msg["role"]), "user", StringComparison.OrdinalIgnoreCase));

        if (lastUserMessage is null)
            return string.Empty;

        return ExtractTypedUserMessage(GetMessageText(lastUserMessage).Trim());
    }

    public static string ExtractTypedUserMessage(string rawUserMessage)
    {
        if (string.IsNullOrWhiteSpace(rawUserMessage))
            return rawUserMessage ?? string.Empty;

        var userRequest = ExtractTagContent(rawUserMessage, "userRequest")
                       ?? ExtractTagContent(rawUserMessage, "user-request");

        if (!string.IsNullOrWhiteSpace(userRequest))
            return userRequest.Trim();

        var stripped = rawUserMessage;
        foreach (var tag in CopilotWrapperTags)
            stripped = RemoveTagBlock(stripped, tag);

        stripped = stripped.Trim();
        return string.IsNullOrWhiteSpace(stripped) ? rawUserMessage.Trim() : stripped;
    }

    private static string? ExtractTagContent(string text, string tag)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";
        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var contentStart = start + open.Length;
        var end = text.IndexOf(close, contentStart, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : text[contentStart..end];
    }

    private static string RemoveTagBlock(string text, string tag)
    {
        while (true)
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";
            var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return text;
            var closeIndex = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0) return text;
            text = text.Remove(start, closeIndex + close.Length - start);
        }
    }

    private static string GetMessageText(JsonObject message)
    {
        if (message["content"] is JsonValue singleContent &&
            singleContent.TryGetValue<string>(out var contentText))
            return contentText;

        if (message["content"] is not JsonArray multiPartContent)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var part in multiPartContent.OfType<JsonObject>())
        {
            if (part["text"] is JsonValue textPart && textPart.TryGetValue<string>(out var text))
                sb.Append(text).Append('\n');
        }
        return sb.ToString();
    }

    private static string? GetStringValue(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
            return stringValue;
        return null;
    }
}
