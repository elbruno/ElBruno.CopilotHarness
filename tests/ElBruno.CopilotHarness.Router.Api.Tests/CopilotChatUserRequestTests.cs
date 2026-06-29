using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// VS Code GitHub Copilot Chat wraps the actual typed text in a &lt;userRequest&gt; tag and
/// surrounds it with &lt;attachments&gt;, &lt;context&gt;, and &lt;reminderInstructions&gt;
/// blocks. The router must extract the typed text so a one-word "hi" is not inflated to ~1000
/// characters of repo/tool context (which otherwise causes the LLM classifier to misroute it).
/// This mirrors a real captured trace.
/// </summary>
public sealed class CopilotChatUserRequestTests
{
    private const string RealCopilotUserMessage =
        "<attachments>\n" +
        "<attachment id=\"elbruno/openclawnet-plan\">\n" +
        "Information about the current repository. You can use this information when you need to calculate diffs.\n" +
        "Repository name: openclawnet-plan\nOwner: elbruno\nCurrent branch: main\nDefault branch: main\n" +
        "</attachment>\n\n</attachments>\n" +
        "<context>\nThe current date is 2026-06-29.\nTerminals:\nTerminal: pwsh\n\n</context>\n" +
        "<reminderInstructions>\n" +
        "When using the insert_edit_into_file tool, avoid repeating existing code.\n" +
        "Prefer the replace_string_in_file tool for making edits.\n" +
        "</reminderInstructions>\n" +
        "<userRequest>\nhi\n</userRequest>";

    [Fact]
    public void ExtractTypedUserMessage_ReturnsUserRequestContent_FromRealCopilotPayload()
    {
        Assert.Equal("hi", BasicModelRouter.ExtractTypedUserMessage(RealCopilotUserMessage));
    }

    [Fact]
    public void GetUserPromptText_ExtractsTypedText_FromCopilotWrappedMessage()
    {
        var body = WrapAsRequest(RealCopilotUserMessage);

        Assert.Equal("hi", BasicModelRouter.GetUserPromptText(body));
        // The wrapper is ~600+ chars, but the typed ask is 2 chars — routing must see 2.
        Assert.Equal(2, BasicModelRouter.GetUserPromptCharacterCount(body));
        Assert.True(BasicModelRouter.GetRawUserMessageText(body).Length > 200);
    }

    [Fact]
    public void ExtractTypedUserMessage_ExtractsRealRequest_WhenUserRequestHoldsACodeTask()
    {
        var raw =
            "<attachments><attachment id=\"x\">repo info</attachment></attachments>\n" +
            "<userRequest>\nrefactor the auth module and fix the failing tests\n</userRequest>";

        Assert.Equal(
            "refactor the auth module and fix the failing tests",
            BasicModelRouter.ExtractTypedUserMessage(raw));
    }

    [Fact]
    public void ExtractTypedUserMessage_StripsWrapperBlocks_WhenNoUserRequestTag()
    {
        var raw =
            "<attachments>\n<attachment id=\"x\">lots of repo context</attachment>\n</attachments>\n" +
            "<context>The current date is 2026-06-29.</context>\n" +
            "please add logging";

        Assert.Equal("please add logging", BasicModelRouter.ExtractTypedUserMessage(raw));
    }

    [Fact]
    public void ExtractTypedUserMessage_ReturnsRawText_ForNonCopilotClients()
    {
        // A plain client (e.g. curl/PowerShell) sends no wrapper — leave the text untouched.
        Assert.Equal("hello there", BasicModelRouter.ExtractTypedUserMessage("hello there"));
    }

    [Fact]
    public void ExtractTypedUserMessage_IsCaseInsensitive_OnTagName()
    {
        var raw = "<UserRequest>build the login page</UserRequest>";

        Assert.Equal("build the login page", BasicModelRouter.ExtractTypedUserMessage(raw));
    }

    private static JsonObject WrapAsRequest(string userContent)
    {
        var body = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = "You are an expert AI programming assistant." },
                new JsonObject { ["role"] = "user", ["content"] = userContent }
            }
        };

        return body;
    }
}
