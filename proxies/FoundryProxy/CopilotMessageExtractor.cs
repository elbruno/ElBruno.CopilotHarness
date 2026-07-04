using System.Text.Json.Nodes;

namespace FoundryProxy;

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
/// <para><b>Quick usage example</b><br/>
/// <code>
///   // Given a parsed OpenAI request body:
///   string typed = CopilotMessageExtractor.GetLastUserMessageText(requestBody);
///   Console.WriteLine($"[copilot ask] {typed}");   // → "hi"
/// </code>
/// </para>
///
/// <para><b>Source</b><br/>
/// Ported from <c>ElBruno.CopilotHarness.Router.Api/ModelRouter.cs</c> — same logic,
/// extracted into a self-contained file so this sample has zero dependency on the main project.
/// </para>
/// </summary>
public static class CopilotMessageExtractor
{
    // -------------------------------------------------------------------------
    // Known Copilot Chat wrapper tags that surround (but are NOT) the user ask.
    // We strip all of these before returning what's left as "the typed message."
    // -------------------------------------------------------------------------
    private static readonly string[] CopilotWrapperTags =
    [
        "attachments",          // attached files / snippets
        "context",              // additional context blocks
        "reminderInstructions", // standing instructions from workspace settings
        "environment_info",     // OS, editor version, etc.
        "editorContext",        // open file / selection
        "currentEditor",        // active tab metadata
        "instructions",         // custom instructions
        "toolResult",           // result from a previous tool call
        "tool-result"           // alternate spelling used in some versions
    ];

    // -------------------------------------------------------------------------
    // PUBLIC API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the actual text the user typed from the <b>last</b> "user" role message
    /// in an OpenAI-style <c>messages</c> array.
    ///
    /// <para>
    /// Why the <em>last</em> user message? Copilot Chat sends the full conversation history
    /// on every turn, prepended by a large system preamble. The last user message is the
    /// current ask; everything before it is history or boilerplate.
    /// </para>
    ///
    /// <para>
    /// Both content shapes are handled:
    /// <list type="bullet">
    ///   <item><description>Simple string: <c>"content": "hello"</c></description></item>
    ///   <item><description>Multi-part array: <c>"content": [{"type":"text","text":"hello"}]</c></description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="requestBody">The parsed OpenAI <c>/v1/chat/completions</c> request body.</param>
    /// <returns>The trimmed, unwrapped user ask; empty string if no user message is found.</returns>
    public static string GetLastUserMessageText(JsonObject requestBody)
    {
        if (requestBody["messages"] is not JsonArray messages)
        {
            return string.Empty;
        }

        // Walk backwards to find the last message whose role == "user".
        var lastUserMessage = messages
            .OfType<JsonObject>()
            .LastOrDefault(msg =>
                string.Equals(GetStringValue(msg["role"]), "user", StringComparison.OrdinalIgnoreCase));

        if (lastUserMessage is null)
        {
            return string.Empty;
        }

        // Pull the raw text out of the message (handles both content shapes).
        var rawText = GetMessageText(lastUserMessage).Trim();

        // Now unwrap the Copilot Chat XML envelope to get just the typed ask.
        return ExtractTypedUserMessage(rawText);
    }

    /// <summary>
    /// Extracts the actual user-typed text from a raw Copilot Chat user message string.
    ///
    /// <para><b>Algorithm (in priority order):</b>
    /// <list type="number">
    ///   <item><description>
    ///     Blank input → return as-is (nothing to extract).
    ///   </description></item>
    ///   <item><description>
    ///     <c>&lt;userRequest&gt;...&lt;/userRequest&gt;</c> (or <c>&lt;user-request&gt;</c>) present
    ///     → return the tag's inner content, trimmed.
    ///     This is the primary Copilot Chat convention; VS Code always puts the user's words here.
    ///   </description></item>
    ///   <item><description>
    ///     No <c>&lt;userRequest&gt;</c> → strip all known wrapper blocks and return whatever remains.
    ///     Handles future/alternate Copilot versions that omit the tag.
    ///   </description></item>
    ///   <item><description>
    ///     Stripping leaves nothing → fall back to the trimmed raw message unchanged.
    ///     This path is hit for non-Copilot clients (e.g. curl, OpenAI SDK) — they send plain text.
    ///   </description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="rawUserMessage">The raw text content of the last user message.</param>
    /// <returns>The unwrapped user ask.</returns>
    public static string ExtractTypedUserMessage(string rawUserMessage)
    {
        // Step 1: nothing to do for blank input.
        if (string.IsNullOrWhiteSpace(rawUserMessage))
        {
            return rawUserMessage ?? string.Empty;
        }

        // Step 2: look for <userRequest> first, then the hyphenated variant.
        // VS Code Copilot Chat always wraps the typed text in this tag.
        var userRequest = ExtractTagContent(rawUserMessage, "userRequest")
                       ?? ExtractTagContent(rawUserMessage, "user-request");

        if (!string.IsNullOrWhiteSpace(userRequest))
        {
            return userRequest.Trim();
        }

        // Step 3: no <userRequest> tag — strip every known wrapper block.
        // What's left (if anything) is the typed ask.
        var stripped = rawUserMessage;
        foreach (var tag in CopilotWrapperTags)
        {
            stripped = RemoveTagBlock(stripped, tag);
        }

        stripped = stripped.Trim();

        // Step 4: if stripping removed everything, fall back to the raw message.
        // This preserves plain-text asks from non-Copilot clients.
        return string.IsNullOrWhiteSpace(stripped) ? rawUserMessage.Trim() : stripped;
    }

    // -------------------------------------------------------------------------
    // PRIVATE HELPERS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the inner text of the first occurrence of <c>&lt;<paramref name="tag"/>&gt;</c>
    /// in <paramref name="text"/>, or <c>null</c> if the tag is absent or unclosed.
    /// The search is case-insensitive so <c>&lt;UserRequest&gt;</c> and
    /// <c>&lt;userrequest&gt;</c> both match.
    /// </summary>
    private static string? ExtractTagContent(string text, string tag)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";

        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null; // tag not present
        }

        var contentStart = start + open.Length;
        var end = text.IndexOf(close, contentStart, StringComparison.OrdinalIgnoreCase);

        // Unclosed tag — don't try to extract partial content.
        return end < 0 ? null : text[contentStart..end];
    }

    /// <summary>
    /// Removes ALL occurrences of <c>&lt;<paramref name="tag"/>&gt;...&lt;/<paramref name="tag"/>&gt;</c>
    /// from <paramref name="text"/> (loop handles repeated blocks, e.g. multiple
    /// <c>&lt;toolResult&gt;</c> entries in one message). Case-insensitive.
    /// </summary>
    private static string RemoveTagBlock(string text, string tag)
    {
        while (true) // repeat until no more occurrences
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";

            var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return text; // no more occurrences
            }

            var closeIndex = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0)
            {
                return text; // unclosed tag — leave it alone
            }

            // Remove from the opening '<' to the end of the closing '>'
            text = text.Remove(start, closeIndex + close.Length - start);
        }
    }

    /// <summary>
    /// Reads the plain text from a single message object, handling both
    /// <c>"content": "string"</c> and <c>"content": [{type, text}, ...]</c> shapes.
    /// </summary>
    private static string GetMessageText(JsonObject message)
    {
        // Simple string content — the common case for plain API clients.
        if (message["content"] is JsonValue singleContent &&
            singleContent.TryGetValue<string>(out var contentText))
        {
            return contentText;
        }

        // Multi-part content array — used by Copilot Chat and vision-capable clients.
        if (message["content"] is not JsonArray multiPartContent)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var part in multiPartContent.OfType<JsonObject>())
        {
            // Each part has a "type" field; we only care about type=="text".
            if (part["text"] is JsonValue textPart &&
                textPart.TryGetValue<string>(out var text))
            {
                builder.Append(text).Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>Safely reads a string value from a <see cref="JsonNode"/>.</summary>
    private static string? GetStringValue(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return null;
    }
}
