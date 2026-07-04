using System.Text.Json.Nodes;
using Proxies.Common;
using Xunit;

namespace Proxies.Common.Tests;

/// <summary>
/// Unit tests for <see cref="CopilotMessageExtractor"/> — the shared utility that
/// unwraps the GitHub Copilot Chat XML envelope from user messages.
///
/// <para>
/// GitHub Copilot Chat wraps every user message in an XML-like envelope:
/// <code>
///   &lt;attachments&gt;...&lt;/attachments&gt;
///   &lt;context&gt;...&lt;/context&gt;
///   &lt;reminderInstructions&gt;...&lt;/reminderInstructions&gt;
///   &lt;userRequest&gt;hi&lt;/userRequest&gt;
/// </code>
/// Without extraction, logging and routing see ~3 KB of boilerplate instead of "hi".
/// These tests verify the extractor handles all documented input shapes correctly
/// and degrades gracefully for non-Copilot callers (curl, OpenAI SDK, etc.).
/// </para>
/// </summary>
public sealed class CopilotMessageExtractorTests
{
    // =========================================================================
    // ExtractTypedUserMessage — string-level extraction
    // =========================================================================

    [Fact]
    public void ExtractTypedUserMessage_ReturnsUserRequestContent_WhenTagPresent()
    {
        var raw =
            "<attachments><attachment>repo info</attachment></attachments>\n" +
            "<context>date: 2026-07-04</context>\n" +
            "<userRequest>hi</userRequest>";

        Assert.Equal("hi", CopilotMessageExtractor.ExtractTypedUserMessage(raw));
    }

    [Fact]
    public void ExtractTypedUserMessage_IsCaseInsensitive_OnTagName()
    {
        // VS Code sends lowercase; defensive test for uppercase / mixed.
        Assert.Equal("build the login page",
            CopilotMessageExtractor.ExtractTypedUserMessage("<UserRequest>build the login page</UserRequest>"));
    }

    [Fact]
    public void ExtractTypedUserMessage_AcceptsHyphenatedVariant()
    {
        // Some Copilot versions emit <user-request> instead of <userRequest>.
        Assert.Equal("hello",
            CopilotMessageExtractor.ExtractTypedUserMessage("<user-request>hello</user-request>"));
    }

    [Fact]
    public void ExtractTypedUserMessage_PrefersUserRequestTag_OverStripping()
    {
        // Both <userRequest> and wrapper blocks present — <userRequest> wins.
        var raw =
            "<context>lots of context</context>" +
            "<userRequest>the real ask</userRequest>" +
            "<reminderInstructions>don't forget</reminderInstructions>";

        Assert.Equal("the real ask",
            CopilotMessageExtractor.ExtractTypedUserMessage(raw));
    }

    [Fact]
    public void ExtractTypedUserMessage_StripsWrapperBlocks_WhenNoUserRequestTag()
    {
        // No <userRequest> — strip all known wrappers and return what remains.
        var raw =
            "<attachments><attachment>file contents here</attachment></attachments>\n" +
            "<context>The current date is 2026-07-04.</context>\n" +
            "please add logging";

        Assert.Equal("please add logging",
            CopilotMessageExtractor.ExtractTypedUserMessage(raw));
    }

    [Fact]
    public void ExtractTypedUserMessage_ReturnsRawText_ForNonCopilotClients()
    {
        // curl / OpenAI SDK sends plain text — no wrappers at all.
        Assert.Equal("hello there",
            CopilotMessageExtractor.ExtractTypedUserMessage("hello there"));
    }

    [Fact]
    public void ExtractTypedUserMessage_ReturnsEmpty_ForBlankInput()
    {
        // Empty string → empty string
        Assert.Equal(string.Empty, CopilotMessageExtractor.ExtractTypedUserMessage(""));
        // Whitespace-only → returned as-is (caller can trim if needed)
        Assert.True(string.IsNullOrWhiteSpace(CopilotMessageExtractor.ExtractTypedUserMessage("   ")));
    }

    [Fact]
    public void ExtractTypedUserMessage_FallsBackToRaw_WhenStrippingRemovesEverything()
    {
        // Every word is inside a wrapper tag — stripping would leave nothing.
        // Fallback: return the trimmed raw message so the caller gets something useful.
        var raw =
            "<attachments>the only content</attachments>" +
            "<context>more content</context>";

        var result = CopilotMessageExtractor.ExtractTypedUserMessage(raw);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void ExtractTypedUserMessage_HandlesMultipleToolResultBlocks()
    {
        // Agentic conversations can have multiple <toolResult> blocks; all are stripped.
        var raw =
            "<toolResult>result one</toolResult>" +
            "<toolResult>result two</toolResult>" +
            "what is the next step?";

        Assert.Equal("what is the next step?",
            CopilotMessageExtractor.ExtractTypedUserMessage(raw));
    }

    [Fact]
    public void ExtractTypedUserMessage_TrimsLeadingAndTrailingWhitespace()
    {
        // <userRequest> inner content often has newlines — trim them.
        var raw = "<userRequest>\n  refactor the auth module  \n</userRequest>";

        Assert.Equal("refactor the auth module",
            CopilotMessageExtractor.ExtractTypedUserMessage(raw));
    }

    // =========================================================================
    // Real Copilot payload — mirrors actual captured traces
    // =========================================================================

    private const string RealCopilotUserMessage =
        "<attachments>\n" +
        "<attachment id=\"elbruno/openclawnet-plan\">\n" +
        "Information about the current repository.\n" +
        "Repository name: openclawnet-plan\nOwner: elbruno\nCurrent branch: main\n" +
        "</attachment>\n\n</attachments>\n" +
        "<context>\nThe current date is 2026-07-04.\nTerminals:\nTerminal: pwsh\n\n</context>\n" +
        "<reminderInstructions>\n" +
        "When using the insert_edit_into_file tool, avoid repeating existing code.\n" +
        "</reminderInstructions>\n" +
        "<userRequest>\nhi\n</userRequest>";

    [Fact]
    public void ExtractTypedUserMessage_ReturnsHi_FromRealCopilotPayload()
    {
        Assert.Equal("hi",
            CopilotMessageExtractor.ExtractTypedUserMessage(RealCopilotUserMessage));
    }

    [Fact]
    public void ExtractTypedUserMessage_HandlesCodeTask_InsideUserRequest()
    {
        var raw =
            "<attachments><attachment id=\"x\">repo info</attachment></attachments>\n" +
            "<userRequest>\nrefactor the auth module and fix the failing tests\n</userRequest>";

        Assert.Equal(
            "refactor the auth module and fix the failing tests",
            CopilotMessageExtractor.ExtractTypedUserMessage(raw));
    }

    // =========================================================================
    // GetLastUserMessageText — JSON request body parsing
    // =========================================================================

    [Fact]
    public void GetLastUserMessageText_ReturnsLastUserMessage_SimpleStringContent()
    {
        var body = ParseObject("""
        {
          "messages": [
            { "role": "system", "content": "You are an AI assistant." },
            { "role": "user",   "content": "what does this function do?" }
          ]
        }
        """);

        Assert.Equal("what does this function do?",
            CopilotMessageExtractor.GetLastUserMessageText(body));
    }

    [Fact]
    public void GetLastUserMessageText_ExtractsTypedText_FromCopilotWrappedBody()
    {
        var body = ParseObject($$"""
        {
          "messages": [
            { "role": "system", "content": "You are an expert AI assistant." },
            { "role": "user",   "content": {{System.Text.Json.JsonSerializer.Serialize(RealCopilotUserMessage)}} }
          ]
        }
        """);

        Assert.Equal("hi", CopilotMessageExtractor.GetLastUserMessageText(body));
    }

    [Fact]
    public void GetLastUserMessageText_HandlesMultiPartContentArray()
    {
        // Vision-capable and structured requests use content as an array.
        var body = ParseObject("""
        {
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "explain this code" },
                { "type": "image_url", "image_url": { "url": "data:image/png;base64,..." } }
              ]
            }
          ]
        }
        """);

        Assert.Equal("explain this code",
            CopilotMessageExtractor.GetLastUserMessageText(body));
    }

    [Fact]
    public void GetLastUserMessageText_ReturnsEmpty_WhenNoMessages()
    {
        var body = ParseObject("""{ "model": "gpt-4o" }""");
        Assert.Equal(string.Empty, CopilotMessageExtractor.GetLastUserMessageText(body));
    }

    [Fact]
    public void GetLastUserMessageText_ReturnsEmpty_WhenNoUserRolePresent()
    {
        var body = ParseObject("""
        {
          "messages": [
            { "role": "system", "content": "You are an AI assistant." },
            { "role": "assistant", "content": "How can I help?" }
          ]
        }
        """);

        Assert.Equal(string.Empty, CopilotMessageExtractor.GetLastUserMessageText(body));
    }

    [Fact]
    public void GetLastUserMessageText_ReturnsLastUserMessage_FromConversationHistory()
    {
        // Multi-turn conversation — must return the LAST user message, not the first.
        var body = ParseObject("""
        {
          "messages": [
            { "role": "user",      "content": "first question" },
            { "role": "assistant", "content": "first answer" },
            { "role": "user",      "content": "follow-up question" }
          ]
        }
        """);

        Assert.Equal("follow-up question",
            CopilotMessageExtractor.GetLastUserMessageText(body));
    }

    [Fact]
    public void GetLastUserMessageText_IsCaseInsensitive_OnRoleName()
    {
        // Role values in the wild sometimes have unexpected casing.
        var body = ParseObject("""
        {
          "messages": [
            { "role": "User", "content": "case test" }
          ]
        }
        """);

        Assert.Equal("case test",
            CopilotMessageExtractor.GetLastUserMessageText(body));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject
        ?? throw new InvalidOperationException("Expected a JSON object.");
}
