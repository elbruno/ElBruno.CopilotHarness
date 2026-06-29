using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class LiveRoutingFeedTests
{
    [Fact]
    public void Redact_MasksEmailsBearerTokensAndApiKeys()
    {
        const string input = "contact me at jane.doe@example.com using Bearer abc123DEF token and key sk-ABCDEFGHIJKLMNOPQRSTUV";

        var redacted = PromptPrivacy.Redact(input);

        Assert.DoesNotContain("jane.doe@example.com", redacted);
        Assert.Contains("[redacted-email]", redacted);
        Assert.Contains("Bearer [redacted-token]", redacted);
        Assert.Contains("[redacted-key]", redacted);
        Assert.DoesNotContain("sk-ABCDEFGHIJKLMNOPQRSTUV", redacted);
    }

    [Fact]
    public void BuildPreview_TruncatesToMaxChars()
    {
        var options = new TelemetryOptions { CapturePromptText = true, PromptPreviewMaxChars = 10, RedactSecrets = false };

        var preview = PromptPrivacy.BuildPreview(new string('a', 50), options);

        Assert.Equal(11, preview.Length); // 10 chars + ellipsis
        Assert.EndsWith("…", preview);
    }

    [Fact]
    public void BuildPreview_RespectsRedactToggleOff()
    {
        var options = new TelemetryOptions { CapturePromptText = true, PromptPreviewMaxChars = 200, RedactSecrets = false };

        var preview = PromptPrivacy.BuildPreview("email me jane@example.com", options);

        Assert.Contains("jane@example.com", preview);
    }

    [Fact]
    public async Task Feed_WithCaptureEnabled_ReturnsPromptModelRuleAndExplanation()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        var routed = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "hello router, which model are you?" } }
        });
        routed.EnsureSuccessStatusCode();

        var feed = await client.GetFromJsonAsync<RoutingFeedResponse>("/admin/telemetry/feed");

        Assert.NotNull(feed);
        Assert.True(feed!.PromptCaptureEnabled);
        var entry = Assert.Single(feed.Requests);
        Assert.False(string.IsNullOrWhiteSpace(entry.SelectedModel));
        Assert.False(string.IsNullOrWhiteSpace(entry.Explanation));
        Assert.Contains("hello router", entry.PromptPreview ?? string.Empty);
    }

    [Fact]
    public async Task Feed_WithCaptureDisabled_OmitsPromptPreviewButKeepsDecision()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var routed = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "no capture please" } }
        });
        routed.EnsureSuccessStatusCode();

        var feed = await client.GetFromJsonAsync<RoutingFeedResponse>("/admin/telemetry/feed");

        Assert.NotNull(feed);
        Assert.False(feed!.PromptCaptureEnabled);
        var entry = Assert.Single(feed.Requests);
        Assert.True(string.IsNullOrEmpty(entry.PromptPreview));
        Assert.False(string.IsNullOrWhiteSpace(entry.SelectedModel));
        Assert.False(string.IsNullOrWhiteSpace(entry.Explanation));
    }

    [Fact]
    public async Task Feed_PopulatesClassifierSourceAndIntent()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        var routed = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "hi" } }
        });
        routed.EnsureSuccessStatusCode();

        var feed = await client.GetFromJsonAsync<RoutingFeedResponse>("/admin/telemetry/feed");

        var entry = Assert.Single(feed!.Requests);
        // Ollama is unreachable in tests, so the classifier falls back to deterministic.
        Assert.Equal("deterministic", entry.ClassifierSource);
        Assert.False(string.IsNullOrWhiteSpace(entry.ClassificationIntent));
    }
}

public sealed record RoutingFeedResponse(
    DateTimeOffset GeneratedAtUtc,
    bool PromptCaptureEnabled,
    IReadOnlyList<RoutedRequestViewTest> Requests);

public sealed record RoutedRequestViewTest(
    string TraceId,
    DateTimeOffset CreatedAtUtc,
    string ClientId,
    string ClientDisplayName,
    string Endpoint,
    bool Stream,
    string? RequestedModel,
    string SelectedModel,
    string Deployment,
    string? MatchedRuleName,
    string Reason,
    string Explanation,
    string? PromptPreview,
    int PromptCharacters,
    string ClassificationIntent,
    string ClassificationComplexity,
    string ClassifierSource,
    string? ProcessorModel,
    double ClassificationConfidence);
