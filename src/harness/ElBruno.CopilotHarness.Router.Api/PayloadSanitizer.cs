using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Adjusts an outgoing chat/completions payload so it stays compatible with the resolved model.
/// Some models (notably the gpt-5 family) reject any non-default <c>temperature</c>/<c>top_p</c>;
/// when the model declares <see cref="ModelProfileOptions.SupportsCustomTemperature"/> = false those
/// parameters are stripped before the request is forwarded upstream.
/// </summary>
public static class PayloadSanitizer
{
    /// <summary>
    /// Returns a payload safe to forward to <paramref name="model"/>. When sanitization is required the
    /// input is cloned so the caller's object is never mutated; otherwise the original instance is returned.
    /// </summary>
    public static JsonObject Sanitize(JsonObject payload, ModelProfileOptions model)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(model);

        if (model.SupportsCustomTemperature)
        {
            return payload;
        }

        var hasTemperature = payload.ContainsKey("temperature");
        var hasTopP = payload.ContainsKey("top_p");
        if (!hasTemperature && !hasTopP)
        {
            return payload;
        }

        var clone = (JsonObject)payload.DeepClone();
        clone.Remove("temperature");
        clone.Remove("top_p");
        return clone;
    }
}
