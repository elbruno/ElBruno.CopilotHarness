using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class PayloadSanitizerTests
{
    [Fact]
    public void Sanitize_StripsTemperatureAndTopP_WhenModelDoesNotSupportCustomTemperature()
    {
        var payload = new JsonObject
        {
            ["model"] = "gpt-5-mini",
            ["temperature"] = 0.1,
            ["top_p"] = 0.5,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "hi" })
        };
        var model = new ModelProfileOptions { Deployment = "gpt-5-mini", SupportsCustomTemperature = false };

        var sanitized = PayloadSanitizer.Sanitize(payload, model);

        Assert.False(sanitized.ContainsKey("temperature"));
        Assert.False(sanitized.ContainsKey("top_p"));
        Assert.True(sanitized.ContainsKey("messages"));
        // Original payload is not mutated.
        Assert.True(payload.ContainsKey("temperature"));
    }

    [Fact]
    public void Sanitize_KeepsTemperature_WhenModelSupportsCustomTemperature()
    {
        var payload = new JsonObject
        {
            ["temperature"] = 0.1,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "hi" })
        };
        var model = new ModelProfileOptions { Deployment = "llama3.2", SupportsCustomTemperature = true };

        var sanitized = PayloadSanitizer.Sanitize(payload, model);

        Assert.Same(payload, sanitized);
        Assert.True(sanitized.ContainsKey("temperature"));
    }

    [Fact]
    public void Sanitize_NoOp_WhenNoSamplingParametersPresent()
    {
        var payload = new JsonObject
        {
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "hi" })
        };
        var model = new ModelProfileOptions { Deployment = "gpt-5-mini", SupportsCustomTemperature = false };

        var sanitized = PayloadSanitizer.Sanitize(payload, model);

        Assert.Same(payload, sanitized);
    }
}
