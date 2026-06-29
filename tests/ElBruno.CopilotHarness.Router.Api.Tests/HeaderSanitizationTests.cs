using ElBruno.CopilotHarness.Router.Api;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Routing reasons surface in the `x-harness-routing-reason` response header. Kestrel rejects any
/// non-ASCII or control character in a header value, so reasons containing arrows (→) or other
/// Unicode produced by the semantic analyzer must be sanitized to ASCII first.
/// </summary>
public sealed class HeaderSanitizationTests
{
    [Fact]
    public void SanitizeHeaderValue_ReplacesArrowWithAscii()
    {
        var result = OpenAiApiUtilities.SanitizeHeaderValue("Semantic rule 'GitHub actions' matched → routed to 'ollama'.");

        Assert.Equal("Semantic rule 'GitHub actions' matched -> routed to 'ollama'.", result);
        Assert.DoesNotContain('\u2192', result);
    }

    [Fact]
    public void SanitizeHeaderValue_NormalizesSmartQuotesAndDashes()
    {
        var result = OpenAiApiUtilities.SanitizeHeaderValue("\u201Cpush\u201D \u2014 done\u2026 it\u2019s fine");

        Assert.Equal("\"push\" - done... it's fine", result);
    }

    [Fact]
    public void SanitizeHeaderValue_DropsRemainingNonAsciiAndControlChars()
    {
        var result = OpenAiApiUtilities.SanitizeHeaderValue("routed \u00e9\u4e2d\ud83d\ude00 ok\nnext");

        // Accented, CJK, emoji, and the newline are removed; printable ASCII is kept.
        Assert.Equal("routed  oknext", result);
        Assert.All(result, ch => Assert.InRange(ch, (char)0x20, (char)0x7E));
    }

    [Fact]
    public void SanitizeHeaderValue_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, OpenAiApiUtilities.SanitizeHeaderValue(null));
        Assert.Equal(string.Empty, OpenAiApiUtilities.SanitizeHeaderValue(string.Empty));
    }
}
