using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// The result of analyzing a user request against the configured semantic rules.
/// </summary>
/// <param name="RuleName">The matched rule's name.</param>
/// <param name="TargetModel">The model/engine the matched rule routes to.</param>
/// <param name="Reason">A short, human-readable reason the rule was chosen.</param>
/// <param name="Confidence">Classifier confidence in the match (0–1).</param>
/// <param name="Source">"processor-model" (real LLM call) or "deterministic" (fallback).</param>
public sealed record SemanticRuleMatch(
    string RuleName,
    string TargetModel,
    string Reason,
    double Confidence,
    string Source);

/// <summary>
/// Analyzes the user's request against natural-language semantic rules. Each semantic rule has a
/// name, a paragraph describing what it captures, and a target LLM engine. A single "rules analyzer"
/// mega-prompt lists every rule and asks the processor model (default local Ollama) to pick the one
/// rule that best matches — using ONLY the user's typed request, never the full Copilot payload.
/// </summary>
public interface ISemanticRuleAnalyzer
{
    /// <summary>True when at least one enabled semantic rule is configured.</summary>
    bool HasSemanticRules(RoutingOptions routingOptions);

    /// <summary>
    /// Picks the matching semantic rule for the given (already-cleaned) user request. Returns
    /// <c>null</c> when no semantic rules exist; otherwise always returns a match (falling back to the
    /// catch-all rule when the model is unavailable or returns nothing usable).
    /// </summary>
    Task<SemanticRuleMatch?> AnalyzeAsync(
        string userRequest,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken);
}

public sealed class SemanticRuleAnalyzer(
    IChatCompletionsProviderFactory providerFactory,
    IOptions<ClassifierOptions> classifierOptions,
    ILogger<SemanticRuleAnalyzer> logger) : ISemanticRuleAnalyzer
{
    private readonly ClassifierOptions _options = classifierOptions.Value;

    public bool HasSemanticRules(RoutingOptions routingOptions) =>
        BasicModelRouter.GetSemanticRules(routingOptions).Count > 0;

    public async Task<SemanticRuleMatch?> AnalyzeAsync(
        string userRequest,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        var rules = BasicModelRouter.GetSemanticRules(routingOptions);
        if (rules.Count == 0)
        {
            return null;
        }

        // The catch-all is the last enabled semantic rule (highest priority number).
        var catchAll = rules[^1];

        if (!_options.Enabled ||
            string.IsNullOrWhiteSpace(userRequest) ||
            !TryResolveProcessorModel(routingOptions, out var processor))
        {
            return KeywordMatch(rules, userRequest, "Processor model unavailable; matched the closest rule by keywords.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.SemanticTimeoutMs <= 0 ? 15000 : _options.SemanticTimeoutMs);

            var payload = BuildAnalyzerPayload(processor.Deployment, rules, userRequest);
            var provider = providerFactory.GetProvider(processor);

            using var response = await provider.SendChatCompletionsAsync(payload, processor, stream: false, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Semantic analyzer: processor model returned {Status}; matching by keywords.",
                    (int)response.StatusCode);
                return KeywordMatch(rules, userRequest, "Processor model call failed; matched the closest rule by keywords.");
            }

            var content = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (TryParseMatch(content, rules, out var ruleName, out var confidence, out var reason))
            {
                var matched = rules.First(rule => string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase));
                return new SemanticRuleMatch(matched.Name, matched.TargetModel, reason, confidence, "processor-model");
            }

            logger.LogWarning("Semantic analyzer: unparseable response; matching by keywords.");
            return KeywordMatch(rules, userRequest, "Processor model returned no usable match; matched the closest rule by keywords.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Semantic analyzer timed out; matching by keywords.");
            return KeywordMatch(rules, userRequest, "Processor model timed out; matched the closest rule by keywords.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Semantic analyzer failed; matching by keywords.");
            return KeywordMatch(rules, userRequest, "Processor model error; matched the closest rule by keywords.");
        }
    }

    private static SemanticRuleMatch DeterministicMatch(RoutingRuleDefinition catchAll, string reason) =>
        new(catchAll.Name, catchAll.TargetModel, reason, 0.5, "deterministic");

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "to", "of", "in", "on", "for", "with", "all", "my", "me",
        "you", "your", "please", "that", "this", "it", "is", "are", "be", "do", "can", "should",
        "rule", "captures", "actions", "action", "user", "asks", "copilot", "request", "every",
        "other", "others", "not", "match", "does", "where", "into", "them", "example", "related"
    };

    /// <summary>
    /// Deterministic fallback used when the processor model is unavailable, slow, or unparseable.
    /// Scores each non-catch-all rule by how many significant words from its name + description appear
    /// in the user request, and returns the best match. Falls back to the catch-all when nothing overlaps.
    /// Public for unit testing.
    /// </summary>
    public static SemanticRuleMatch KeywordMatch(
        IReadOnlyList<RoutingRuleDefinition> rules,
        string userRequest,
        string reason)
    {
        var catchAll = rules[^1];
        if (string.IsNullOrWhiteSpace(userRequest) || rules.Count == 1)
        {
            return DeterministicMatch(catchAll, reason);
        }

        var requestWords = Tokenize(userRequest);
        if (requestWords.Count == 0)
        {
            return DeterministicMatch(catchAll, reason);
        }

        RoutingRuleDefinition? best = null;
        var bestScore = 0;
        // Skip the last rule (catch-all) — it is the default, not a keyword target.
        for (var i = 0; i < rules.Count - 1; i++)
        {
            var rule = rules[i];
            var ruleWords = Tokenize($"{rule.Name} {rule.Description}");
            var score = ruleWords.Count(requestWords.Contains);
            if (score > bestScore)
            {
                bestScore = score;
                best = rule;
            }
        }

        if (best is { } matched && bestScore > 0)
        {
            return new SemanticRuleMatch(
                matched.Name,
                matched.TargetModel,
                $"{reason} ('{matched.Name}' shared {bestScore} keyword(s) with the request).",
                0.5,
                "deterministic");
        }

        return DeterministicMatch(catchAll, reason);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = new string(token.Where(char.IsLetterOrDigit).ToArray());
            if (word.Length >= 3 && !StopWords.Contains(word))
            {
                words.Add(word);
            }
        }

        return words;
    }

    /// <summary>
    /// Builds the "rules analyzer" mega-prompt that lists every semantic rule (name + paragraph) and
    /// asks the model to pick exactly one by name. Public for unit testing.
    /// </summary>
    public static string BuildAnalyzerSystemPrompt(IReadOnlyList<RoutingRuleDefinition> rules)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "You are a routing analyzer. Read the user's request and choose the ONE rule below that best " +
            "captures what the user is asking for. Each rule has a name and a description of what it captures.");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            builder.Append(i + 1).Append(". Name: ").AppendLine(rule.Name);
            builder.Append("   Captures: ").AppendLine(string.IsNullOrWhiteSpace(rule.Description) ? "(no description)" : rule.Description.Trim());
        }

        builder.AppendLine();
        builder.AppendLine(
            "Respond ONLY with compact JSON: {\"rule\":\"<exact rule name from the list>\"," +
            "\"confidence\":0.0-1.0,\"reason\":\"short justification\"}.");
        builder.Append(
            "If no rule clearly matches, choose the catch-all rule (the one whose description says it captures " +
            "everything that does not fit the others). The \"rule\" value MUST exactly equal one of the names above.");

        return builder.ToString();
    }

    private static JsonObject BuildAnalyzerPayload(string model, IReadOnlyList<RoutingRuleDefinition> rules, string userRequest)
    {
        return new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = BuildAnalyzerSystemPrompt(rules) },
                new JsonObject { ["role"] = "user", ["content"] = userRequest }
            }
        };
    }

    private static bool TryResolveProcessorModel(RoutingOptions routingOptions, out ModelProfileOptions processor)
    {
        foreach (var entry in routingOptions.Profiles)
        {
            if (entry.Value is { IsProcessor: true, Enabled: true })
            {
                processor = entry.Value;
                return true;
            }
        }

        processor = null!;
        return false;
    }

    public static bool TryParseMatch(
        string responseContent,
        IReadOnlyList<RoutingRuleDefinition> rules,
        out string ruleName,
        out double confidence,
        out string reason)
    {
        ruleName = string.Empty;
        confidence = 0.6;
        reason = "Selected by the rules analyzer.";

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var messageContent = root.TryGetProperty("choices", out var choices) &&
                                 choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
                                 choices[0].TryGetProperty("message", out var message) &&
                                 message.TryGetProperty("content", out var contentEl)
                ? contentEl.GetString()
                : null;

            var jsonText = ExtractJsonObject(messageContent) ?? ExtractJsonObject(responseContent);
            if (jsonText is null)
            {
                return false;
            }

            using var match = JsonDocument.Parse(jsonText);
            var matchRoot = match.RootElement;

            var rawRule = matchRoot.TryGetProperty("rule", out var ruleEl) ? ruleEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(rawRule))
            {
                return false;
            }

            var resolved = rules.FirstOrDefault(rule => string.Equals(rule.Name, rawRule.Trim(), StringComparison.OrdinalIgnoreCase));
            if (resolved is null)
            {
                return false;
            }

            ruleName = resolved.Name;

            if (matchRoot.TryGetProperty("confidence", out var confidenceEl) &&
                confidenceEl.ValueKind == JsonValueKind.Number &&
                confidenceEl.TryGetDouble(out var parsedConfidence))
            {
                confidence = Math.Clamp(parsedConfidence, 0, 1);
            }

            if (matchRoot.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String)
            {
                reason = reasonEl.GetString() ?? reason;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start < 0 || end <= start ? null : text.Substring(start, end - start + 1);
    }
}
