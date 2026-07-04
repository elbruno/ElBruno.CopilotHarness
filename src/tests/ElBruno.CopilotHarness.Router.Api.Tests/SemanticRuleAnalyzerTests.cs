using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// The semantic rules engine: each rule is a Name + a natural-language paragraph + a target LLM
/// engine. A single "rules analyzer" mega-prompt lists every rule and the processor model picks the
/// one that matches the user's request. These tests cover prompt construction, response parsing, and
/// rule collection (the parts that don't need a live model).
/// </summary>
public sealed class SemanticRuleAnalyzerTests
{
    private static readonly RoutingRuleDefinition GithubRule = new(
        1, "GitHub actions",
        "Captures all GitHub related actions, e.g. commit all the changes to GitHub.",
        RoutingRuleConditionType.SemanticMatch, "", "ollama", 10, true);

    private static readonly RoutingRuleDefinition LaunchRule = new(
        2, "Launch App actions",
        "Captures all actions where the user asks copilot to launch the app, e.g. start the app.",
        RoutingRuleConditionType.SemanticMatch, "", "ollama", 20, true);

    private static readonly RoutingRuleDefinition OthersRule = new(
        3, "Others actions",
        "Catch-all: captures all actions that do not fit into the other rules.",
        RoutingRuleConditionType.SemanticMatch, "", "gpt5mini", 30, true);

    private static readonly IReadOnlyList<RoutingRuleDefinition> Rules = [GithubRule, LaunchRule, OthersRule];

    [Fact]
    public void BuildAnalyzerSystemPrompt_ListsEveryRuleNameAndDescription()
    {
        var prompt = SemanticRuleAnalyzer.BuildAnalyzerSystemPrompt(Rules);

        Assert.Contains("GitHub actions", prompt);
        Assert.Contains("commit all the changes to GitHub", prompt);
        Assert.Contains("Launch App actions", prompt);
        Assert.Contains("Others actions", prompt);
        // The model is told to answer with the rule name as JSON.
        Assert.Contains("\"rule\"", prompt);
        Assert.Contains("catch-all", prompt);
    }

    [Fact]
    public void TryParseMatch_ResolvesRuleName_FromOpenAiEnvelope()
    {
        var response = """
        {
          "choices": [
            { "message": { "content": "{\"rule\":\"GitHub actions\",\"confidence\":0.91,\"reason\":\"asks to commit and push\"}" } }
          ]
        }
        """;

        var ok = SemanticRuleAnalyzer.TryParseMatch(response, Rules, out var rule, out var confidence, out var reason);

        Assert.True(ok);
        Assert.Equal("GitHub actions", rule);
        Assert.Equal(0.91, confidence, 3);
        Assert.Equal("asks to commit and push", reason);
    }

    [Fact]
    public void TryParseMatch_IsCaseInsensitive_OnRuleName()
    {
        var response = """{ "choices": [ { "message": { "content": "{\"rule\":\"others actions\",\"confidence\":0.4,\"reason\":\"no match\"}" } } ] }""";

        var ok = SemanticRuleAnalyzer.TryParseMatch(response, Rules, out var rule, out _, out _);

        Assert.True(ok);
        Assert.Equal("Others actions", rule);
    }

    [Fact]
    public void TryParseMatch_Fails_WhenRuleNameNotInList()
    {
        var response = """{ "choices": [ { "message": { "content": "{\"rule\":\"Nonexistent\",\"confidence\":0.9}" } } ] }""";

        Assert.False(SemanticRuleAnalyzer.TryParseMatch(response, Rules, out _, out _, out _));
    }

    [Fact]
    public void GetSemanticRules_ReturnsOnlyEnabledSemanticRules_OrderedByPriority()
    {
        var options = new RoutingOptions
        {
            DefaultProfile = "ollama",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new() { Deployment = "llama3.1:8b", Enabled = true, IsProcessor = true },
                ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Enabled = true }
            },
            RuleSet =
            [
                OthersRule,
                GithubRule,
                LaunchRule with { Enabled = false },
                new RoutingRuleDefinition(4, "Size rule", "", RoutingRuleConditionType.PromptSizeAtLeast, "100", "gpt5mini", 5, true)
            ]
        };

        var semantic = BasicModelRouter.GetSemanticRules(options);

        // Only enabled SemanticMatch rules, ordered by priority → GitHub(10), Others(30).
        Assert.Equal(2, semantic.Count);
        Assert.Equal("GitHub actions", semantic[0].Name);
        Assert.Equal("Others actions", semantic[1].Name);
    }

    [Fact]
    public void KeywordMatch_PicksRuleSharingWordsWithRequest()
    {
        var match = SemanticRuleAnalyzer.KeywordMatch(Rules, "commit and push to GitHub now", "fallback");

        Assert.Equal("GitHub actions", match.RuleName);
        Assert.Equal("ollama", match.TargetModel);
        Assert.Equal("deterministic", match.Source);
    }

    [Fact]
    public void KeywordMatch_FallsBackToCatchAll_WhenNoKeywordsOverlap()
    {
        var match = SemanticRuleAnalyzer.KeywordMatch(Rules, "explain quantum entanglement", "fallback");

        // "Others actions" is the catch-all (last rule).
        Assert.Equal("Others actions", match.RuleName);
        Assert.Equal("gpt5mini", match.TargetModel);
    }

    // ── New local-routing scenarios (Dev env / Build+test / Explain / Translate / Commit) ──
    // Fixtures mirror the seeded rule descriptions so the keyword fallback reflects real routing.
    private static readonly IReadOnlyList<RoutingRuleDefinition> LocalScenarioRules =
    [
        new(1, "GitHub actions",
            "Captures all GitHub related actions, e.g. commit all the changes to GitHub, push, open a pull request.",
            RoutingRuleConditionType.SemanticMatch, "", "ollama", 100, true),
        new(2, "Launch App actions",
            "Captures launch, run, build, start, stop, restart or kill the application under test in the current workspace.",
            RoutingRuleConditionType.SemanticMatch, "", "ollama", 110, true),
        new(3, "Dev environment actions",
            "Captures requests to start, stop, restart or check the local development services and containers - the database, Redis, message queues, Docker containers, or docker compose up/down.",
            RoutingRuleConditionType.SemanticMatch, "", "ollama", 112, true),
        new(4, "Build and test actions",
            "Captures requests to build, compile, restore or install packages, run tests or the test suite, run a linter, run a formatter, or check code style.",
            RoutingRuleConditionType.SemanticMatch, "", "ollama", 114, true),
        new(5, "Quick explanations",
            "Captures short factual questions answered briefly without changing code: a quick explanation of a single line, setting, command, error message or concept.",
            RoutingRuleConditionType.SemanticMatch, "", "ollama", 116, true),
        new(6, "Short translations",
            "Captures requests to translate a short piece of text, a phrase, a comment or a message from one human language to another, for example translate this to Spanish.",
            RoutingRuleConditionType.SemanticMatch, "", "ollama", 118, true),
        new(7, "Commit messages and summaries",
            "Captures requests to draft a commit message, write a short changelog entry, or produce a brief summary of a diff or a set of changes.",
            RoutingRuleConditionType.SemanticMatch, "", "ollama", 119, true),
        new(8, "Others actions",
            "Catch-all: captures all actions that do not fit into the other rules, including complex coding tasks.",
            RoutingRuleConditionType.SemanticMatch, "", "gpt5mini", 120, true),
    ];

    [Theory]
    [InlineData("start the database and redis", "Dev environment actions")]
    [InlineData("stop the docker containers", "Dev environment actions")]
    [InlineData("run the unit tests", "Build and test actions")]
    [InlineData("restore the packages and build the solution", "Build and test actions")]
    [InlineData("translate this comment to Spanish", "Short translations")]
    [InlineData("write a commit message for these changes", "Commit messages and summaries")]
    public void KeywordMatch_RoutesNewLocalScenarios_ToLocalModel(string prompt, string expectedRule)
    {
        var match = SemanticRuleAnalyzer.KeywordMatch(LocalScenarioRules, prompt, "fallback");

        Assert.Equal(expectedRule, match.RuleName);
        Assert.Equal("ollama", match.TargetModel);
    }
}
