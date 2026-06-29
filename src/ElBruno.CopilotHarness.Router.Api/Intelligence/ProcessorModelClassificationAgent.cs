using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>Fixed intent vocabulary the classifier maps every request into. Mirrors <see cref="ClassifierIntentNames"/>.</summary>
public static class ClassifierIntents
{
    public const string SimpleChat = ClassifierIntentNames.SimpleChat;
    public const string GithubActions = ClassifierIntentNames.GithubActions;
    public const string LaunchApp = ClassifierIntentNames.LaunchApp;
    public const string CodeTask = ClassifierIntentNames.CodeTask;
    public const string LongForm = ClassifierIntentNames.LongForm;

    public static readonly IReadOnlyList<string> All = ClassifierIntentNames.All;

    public static bool IsKnown(string? intent) =>
        !string.IsNullOrWhiteSpace(intent) &&
        All.Any(known => string.Equals(known, intent, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? intent) =>
        All.FirstOrDefault(known => string.Equals(known, intent, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
}

/// <summary>
/// Options controlling the processor-model classification step. The processor model (a model flagged
/// <see cref="ModelProfileOptions.IsProcessor"/>, by default the local Ollama model) reads the first
/// <see cref="PreviewChars"/> characters of each prompt to decide an intent. Falls back to the
/// deterministic classifier when disabled, unavailable, or on error/timeout.
/// </summary>
public sealed class ClassifierOptions
{
    public const string SectionName = "Classifier";

    /// <summary>Enables the LLM-based processor classifier. When false, only the deterministic classifier runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Number of leading prompt characters sent to the processor model.</summary>
    public int PreviewChars { get; set; } = 200;

    /// <summary>Timeout for the processor model call before falling back to deterministic classification.</summary>
    public int TimeoutMs { get; set; } = 4000;
}

/// <summary>
/// Primary classifier. Calls the registry's processor model (default Ollama) on the first ~200 chars of
/// the prompt to classify the request into the fixed intent vocabulary. Falls back to the deterministic
/// classifier on any failure, when disabled, or when no enabled processor model is configured.
/// </summary>
public sealed class ProcessorModelClassificationAgent(
    IChatCompletionsProviderFactory providerFactory,
    DeterministicClassificationAgent fallback,
    IOptions<ClassifierOptions> classifierOptions,
    ILogger<ProcessorModelClassificationAgent> logger) : IClassificationAgent
{
    private readonly ClassifierOptions _options = classifierOptions.Value;

    public async Task<ClassificationResult> ClassifyAsync(
        JsonObject requestBody,
        RoutingContext context,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        var deterministic = DeterministicClassificationAgent.Classify(requestBody, context, routingOptions);

        if (!_options.Enabled)
        {
            return deterministic;
        }

        if (!TryResolveProcessorModel(routingOptions, out var processorName, out var processor))
        {
            logger.LogDebug("No enabled processor model configured; using deterministic classification.");
            return deterministic;
        }

        var promptText = BasicModelRouter.GetUserPromptText(requestBody);
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return deterministic;
        }

        var previewChars = _options.PreviewChars <= 0 ? 200 : _options.PreviewChars;
        var preview = promptText.Length > previewChars ? promptText[..previewChars] : promptText;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.TimeoutMs <= 0 ? 4000 : _options.TimeoutMs);

            var payload = BuildClassificationPayload(processor.Deployment, preview);
            var provider = providerFactory.GetProvider(processor);

            using var response = await provider.SendChatCompletionsAsync(payload, processor, stream: false, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Processor model '{Processor}' returned {Status}; falling back to deterministic classification.",
                    processorName,
                    (int)response.StatusCode);
                return deterministic;
            }

            var content = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (TryParseClassification(content, out var intent, out var complexity, out var confidence, out var reasoning))
            {
                return new ClassificationResult(intent, complexity, confidence, reasoning)
                {
                    Source = "processor-model",
                    ProcessorModel = processorName
                };
            }

            logger.LogWarning(
                "Processor model '{Processor}' returned an unparseable classification; using deterministic result.",
                processorName);
            return deterministic;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Processor model '{Processor}' classification timed out; using deterministic result.", processorName);
            return deterministic;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Processor model '{Processor}' classification failed; using deterministic result.", processorName);
            return deterministic;
        }
    }

    private static bool TryResolveProcessorModel(
        RoutingOptions routingOptions,
        out string processorName,
        out ModelProfileOptions processor)
    {
        foreach (var entry in routingOptions.Profiles)
        {
            if (entry.Value is { IsProcessor: true, Enabled: true })
            {
                processorName = entry.Key;
                processor = entry.Value;
                return true;
            }
        }

        processorName = string.Empty;
        processor = null!;
        return false;
    }

    private static JsonObject BuildClassificationPayload(string model, string preview)
    {
        var systemPrompt =
            "You are a routing classifier. Read the start of a user request and classify it into exactly one intent. " +
            "Allowed intents: simple-chat (short greetings/small talk/simple questions), github-actions (git/GitHub " +
            "operations like commit, push, open PR), launch-app (run/launch/start an application), code-task (writing, " +
            "refactoring, debugging code), long-form (large, complex, multi-step generation). " +
            "Respond ONLY with compact JSON: {\"intent\":\"<one-of-the-allowed>\",\"complexity\":\"low|medium|high\"," +
            "\"confidence\":0.0-1.0,\"reasoning\":\"short\"}.";

        return new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = preview }
            }
        };
    }

    private static bool TryParseClassification(
        string responseContent,
        out string intent,
        out string complexity,
        out double confidence,
        out string reasoning)
    {
        intent = string.Empty;
        complexity = "low";
        confidence = 0.6;
        reasoning = "Classified by processor model.";

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            // OpenAI-compatible chat completion → choices[0].message.content holds the JSON string.
            var messageContent = root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                ? choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentEl)
                    ? contentEl.GetString()
                    : null
                : null;

            var jsonText = ExtractJsonObject(messageContent) ?? ExtractJsonObject(responseContent);
            if (jsonText is null)
            {
                return false;
            }

            using var classification = JsonDocument.Parse(jsonText);
            var classRoot = classification.RootElement;

            var rawIntent = classRoot.TryGetProperty("intent", out var intentEl) ? intentEl.GetString() : null;
            var normalized = ClassifierIntents.Normalize(rawIntent);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            intent = normalized;
            if (classRoot.TryGetProperty("complexity", out var complexityEl) && complexityEl.ValueKind == JsonValueKind.String)
            {
                complexity = complexityEl.GetString() ?? "low";
            }

            if (classRoot.TryGetProperty("confidence", out var confidenceEl) &&
                confidenceEl.ValueKind == JsonValueKind.Number &&
                confidenceEl.TryGetDouble(out var parsedConfidence))
            {
                confidence = Math.Clamp(parsedConfidence, 0, 1);
            }

            if (classRoot.TryGetProperty("reasoning", out var reasoningEl) && reasoningEl.ValueKind == JsonValueKind.String)
            {
                reasoning = reasoningEl.GetString() ?? reasoning;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Extracts the first balanced JSON object substring from arbitrary model text.</summary>
    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text.Substring(start, end - start + 1);
    }
}
