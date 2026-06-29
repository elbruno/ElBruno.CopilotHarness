using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

public interface IRoutingWorkflow
{
    Task<RoutingSelectionResult> RouteAsync(
        JsonObject requestBody,
        RoutingOptions routingOptions,
        RoutingRequestMetadata? requestMetadata,
        CancellationToken cancellationToken);
}

public sealed record RoutingRequestMetadata(
    string Id,
    string Source,
    string? Version,
    string? UserAgent,
    string? RequestId,
    string? Endpoint);

public sealed record RoutingSelectionResult(RoutingDecision Decision, string TraceId, RoutingRequestMetadata Client);

public interface IRequestContextProvider
{
    string Name { get; }
    ValueTask<IReadOnlyList<RoutingContextFact>> ProvideAsync(
        JsonObject requestBody,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken);
}

public sealed record RoutingContextFact(string Key, string Value);

public sealed record RoutingContext(IReadOnlyList<RoutingContextFact> Facts)
{
    public bool TryGetBoolean(string key, out bool value)
    {
        var entry = Facts.FirstOrDefault(fact => string.Equals(fact.Key, key, StringComparison.OrdinalIgnoreCase));
        return bool.TryParse(entry?.Value, out value);
    }

    public bool TryGetInteger(string key, out int value)
    {
        var entry = Facts.FirstOrDefault(fact => string.Equals(fact.Key, key, StringComparison.OrdinalIgnoreCase));
        return int.TryParse(entry?.Value, out value);
    }
}

public interface IClassificationAgent
{
    Task<ClassificationResult> ClassifyAsync(
        JsonObject requestBody,
        RoutingContext context,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken);
}

public sealed record ClassificationResult(string Intent, string Complexity, double Confidence, string Reasoning)
{
    /// <summary>Which classifier produced this result: "processor-model" or "deterministic".</summary>
    public string Source { get; init; } = "deterministic";

    /// <summary>Name of the processor model that classified the request, when an LLM call was used.</summary>
    public string? ProcessorModel { get; init; }
}

public interface IRuleAdvisorAgent
{
    Task<RuleAdvisorResult> AdviseAsync(
        RoutingContext context,
        ClassificationResult classification,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken);
}

public sealed record RuleAdvisorResult(string? SuggestedProfile, string Rationale);

public interface IExecutionTraceStore
{
    void Store(RoutingExecutionTrace trace);
    bool TryGet(string traceId, out RoutingExecutionTrace trace);
    IReadOnlyList<RoutingExecutionTrace> GetRecent(int limit);
    bool Remove(string traceId);
    int RemoveMany(IEnumerable<string> traceIds);
    void Clear();

    /// <summary>
    /// Appends post-routing context facts (e.g. upstream status/latency/errors, tool-override info) onto an
    /// already-stored trace so the Live feed can surface the full request outcome. No-op when the trace is missing.
    /// </summary>
    void AppendFacts(string traceId, IReadOnlyList<RoutingContextFact> facts);
}

public sealed record RoutingWorkflowStep(string Name, string Outcome);

public sealed record RoutingExecutionTrace(
    string TraceId,
    DateTimeOffset CreatedAtUtc,
    string WorkflowEngine,
    IReadOnlyList<RoutingContextFact> Context,
    ClassificationResult Classification,
    RuleAdvisorResult RuleAdvisor,
    RoutingDecision Decision,
    IReadOnlyList<RoutingWorkflowStep> Steps);

public sealed class InMemoryExecutionTraceStore : IExecutionTraceStore
{
    private const int MaxTraces = 200;
    private readonly ConcurrentDictionary<string, RoutingExecutionTrace> _traces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _orderedTraceIds = [];
    private readonly Lock _lock = new();

    public void Store(RoutingExecutionTrace trace)
    {
        lock (_lock)
        {
            _traces[trace.TraceId] = trace;
            _orderedTraceIds.Enqueue(trace.TraceId);

            while (_orderedTraceIds.Count > MaxTraces)
            {
                var oldTraceId = _orderedTraceIds.Dequeue();
                _traces.TryRemove(oldTraceId, out _);
            }
        }
    }

    public bool TryGet(string traceId, out RoutingExecutionTrace trace) =>
        _traces.TryGetValue(traceId, out trace!);

    public bool Remove(string traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_traces.TryRemove(traceId, out _))
            {
                return false;
            }

            RebuildOrderedQueueExcluding(traceId);
            return true;
        }
    }

    public int RemoveMany(IEnumerable<string> traceIds)
    {
        if (traceIds is null)
        {
            return 0;
        }

        var ids = traceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ids.Count == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var removed = 0;
            foreach (var id in ids)
            {
                if (_traces.TryRemove(id, out _))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                RebuildOrderedQueueExcluding(ids);
            }

            return removed;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _traces.Clear();
            _orderedTraceIds.Clear();
        }
    }

    public void AppendFacts(string traceId, IReadOnlyList<RoutingContextFact> facts)
    {
        if (string.IsNullOrWhiteSpace(traceId) || facts is null || facts.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (!_traces.TryGetValue(traceId, out var trace))
            {
                return;
            }

            var merged = trace.Context.Concat(facts).ToList();
            _traces[traceId] = trace with { Context = merged };
        }
    }

    private void RebuildOrderedQueueExcluding(string excludedTraceId) =>
        RebuildOrderedQueueExcluding(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { excludedTraceId });

    private void RebuildOrderedQueueExcluding(HashSet<string> excludedTraceIds)
    {
        var retained = _orderedTraceIds
            .Where(id => !excludedTraceIds.Contains(id))
            .ToList();

        _orderedTraceIds.Clear();
        foreach (var id in retained)
        {
            _orderedTraceIds.Enqueue(id);
        }
    }

    public IReadOnlyList<RoutingExecutionTrace> GetRecent(int limit)
    {
        var normalizedLimit = limit <= 0 ? 50 : limit;
        lock (_lock)
        {
            return _orderedTraceIds
                .Reverse()
                .Take(normalizedLimit)
                .Select(traceId => _traces.TryGetValue(traceId, out var trace) ? trace : null)
                .Where(trace => trace is not null)
                .Cast<RoutingExecutionTrace>()
                .ToList();
        }
    }
}

public sealed class StreamingContextProvider : IRequestContextProvider
{
    public string Name => "streaming";

    public ValueTask<IReadOnlyList<RoutingContextFact>> ProvideAsync(
        JsonObject requestBody,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        var stream = requestBody["stream"]?.GetValue<bool>() ?? false;
        return ValueTask.FromResult<IReadOnlyList<RoutingContextFact>>(
        [
            new RoutingContextFact("request.stream", stream.ToString())
        ]);
    }
}

public sealed class PromptShapeContextProvider(IOptions<TelemetryOptions> telemetryOptions) : IRequestContextProvider
{
    private readonly TelemetryOptions _telemetryOptions = telemetryOptions.Value;

    public string Name => "prompt-shape";

    public ValueTask<IReadOnlyList<RoutingContextFact>> ProvideAsync(
        JsonObject requestBody,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        var hasSystemMessage = requestBody["messages"] is JsonArray messages &&
                               messages.OfType<JsonObject>()
                                   .Any(message => string.Equals(
                                       message["role"]?.GetValue<string>(),
                                       "system",
                                       StringComparison.OrdinalIgnoreCase));

        var totalPromptCharacters = BasicModelRouter.GetPromptCharacterCount(requestBody);
        var userPromptCharacters = BasicModelRouter.GetUserPromptCharacterCount(requestBody);
        var facts = new List<RoutingContextFact>
        {
            new("request.hasSystemMessage", hasSystemMessage.ToString()),
            // request.promptCharacters is the routing-relevant size (the user's actual
            // message). The full payload size is surfaced separately so the live view can
            // explain that routing ignores Copilot's large system preamble.
            new("request.promptCharacters", userPromptCharacters.ToString()),
            new("request.userPromptCharacters", userPromptCharacters.ToString()),
            new("request.totalPromptCharacters", totalPromptCharacters.ToString())
        };

        if (_telemetryOptions.CapturePromptText)
        {
            var preview = PromptPrivacy.BuildPreview(BasicModelRouter.GetUserPromptText(requestBody), _telemetryOptions);
            if (!string.IsNullOrEmpty(preview))
            {
                facts.Add(new RoutingContextFact(PromptPrivacy.PromptPreviewFactKey, preview));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<RoutingContextFact>>(facts);
    }
}

public sealed class RequestedModelContextProvider : IRequestContextProvider
{
    public string Name => "requested-model";

    public ValueTask<IReadOnlyList<RoutingContextFact>> ProvideAsync(
        JsonObject requestBody,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        var requestedModel = requestBody["model"] is JsonValue modelValue &&
                             modelValue.TryGetValue<string>(out var model)
            ? model.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            return ValueTask.FromResult<IReadOnlyList<RoutingContextFact>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<RoutingContextFact>>(
        [
            new RoutingContextFact("request.requestedModel", requestedModel)
        ]);
    }
}

public sealed class DeterministicClassificationAgent : IClassificationAgent
{
    public Task<ClassificationResult> ClassifyAsync(
        JsonObject requestBody,
        RoutingContext context,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Classify(requestBody, context, routingOptions));
    }

    /// <summary>
    /// Heuristic classification into the fixed intent vocabulary (<see cref="ClassifierIntents"/>).
    /// Shared by the deterministic agent and used as the fallback for the processor-model classifier.
    /// </summary>
    public static ClassificationResult Classify(
        JsonObject requestBody,
        RoutingContext context,
        RoutingOptions routingOptions)
    {
        var promptText = BasicModelRouter.GetUserPromptText(requestBody);
        var preview = (promptText.Length > 200 ? promptText[..200] : promptText).ToLowerInvariant();

        var promptCharacters = BasicModelRouter.GetUserPromptCharacterCount(requestBody);

        // Source-control / GitHub actions intent.
        if (ContainsAny(preview, "push to gh", "git push", "git commit", "commit and push", "open a pr", "pull request", "push to github"))
        {
            return new ClassificationResult(
                Intent: ClassifierIntents.GithubActions,
                Complexity: "low",
                Confidence: 0.7,
                Reasoning: "Prompt mentions a source-control / GitHub action.")
            { Source = "deterministic" };
        }

        // Launch / run the application intent.
        if (ContainsAny(preview, "launch the app", "run the app", "start the app", "aspire run", "dotnet run", "start aspire"))
        {
            return new ClassificationResult(
                Intent: ClassifierIntents.LaunchApp,
                Complexity: "low",
                Confidence: 0.7,
                Reasoning: "Prompt asks to launch or run the application.")
            { Source = "deterministic" };
        }

        // Large prompts → long-form.
        if (promptCharacters >= routingOptions.Rules.BigPromptCharacterThreshold)
        {
            return new ClassificationResult(
                Intent: ClassifierIntents.LongForm,
                Complexity: "high",
                Confidence: 0.85,
                Reasoning: "Prompt size crossed the big-prompt threshold.")
            { Source = "deterministic" };
        }

        // Code-oriented work.
        if (ContainsAny(preview, "refactor", "implement", "fix the bug", "write a function", "add a test", "debug", "stack trace", "exception"))
        {
            return new ClassificationResult(
                Intent: ClassifierIntents.CodeTask,
                Complexity: "medium",
                Confidence: 0.7,
                Reasoning: "Prompt describes a code task.")
            { Source = "deterministic" };
        }

        // Default: short, simple conversational prompt.
        return new ClassificationResult(
            Intent: ClassifierIntents.SimpleChat,
            Complexity: "low",
            Confidence: 0.75,
            Reasoning: "Short prompt with no complex indicators.")
        { Source = "deterministic" };
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

public sealed class DeterministicRuleAdvisorAgent : IRuleAdvisorAgent
{
    public Task<RuleAdvisorResult> AdviseAsync(
        RoutingContext context,
        ClassificationResult classification,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        var suggestedProfile = classification.Complexity switch
        {
            "high" => routingOptions.Rules.BigProfile,
            _ => routingOptions.DefaultProfile
        };

        if (context.TryGetBoolean("request.stream", out var stream) &&
            stream &&
            routingOptions.Rules.PreferStreamingProfileWhenStreaming)
        {
            suggestedProfile = routingOptions.Rules.StreamingProfile;
        }

        if (context.Facts.FirstOrDefault(fact => fact.Key == "request.requestedModel") is { } requestedModel)
        {
            suggestedProfile = requestedModel.Value;
        }

        return Task.FromResult(new RuleAdvisorResult(
            SuggestedProfile: suggestedProfile,
            Rationale: "Deterministic advisor aligned to current Phase 1/2 rules to preserve routing contracts."));
    }
}

public sealed class MicrosoftAgentFrameworkRoutingWorkflow(
    IEnumerable<IRequestContextProvider> contextProviders,
    IClassificationAgent classificationAgent,
    IRuleAdvisorAgent ruleAdvisorAgent,
    ISemanticRuleAnalyzer semanticRuleAnalyzer,
    IExecutionTraceStore executionTraceStore,
    ILogger<MicrosoftAgentFrameworkRoutingWorkflow> logger) : IRoutingWorkflow
{
    private readonly IReadOnlyList<IRequestContextProvider> _contextProviders = contextProviders.ToList();
    private readonly IClassificationAgent _classificationAgent = classificationAgent;
    private readonly IRuleAdvisorAgent _ruleAdvisorAgent = ruleAdvisorAgent;
    private readonly ISemanticRuleAnalyzer _semanticRuleAnalyzer = semanticRuleAnalyzer;
    private readonly IExecutionTraceStore _executionTraceStore = executionTraceStore;
    private readonly ILogger<MicrosoftAgentFrameworkRoutingWorkflow> _logger = logger;

    public async Task<RoutingSelectionResult> RouteAsync(
        JsonObject requestBody,
        RoutingOptions routingOptions,
        RoutingRequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var state = new WorkflowState(
            requestBody,
            routingOptions,
            requestMetadata ?? new RoutingRequestMetadata("unknown", "unknown", null, null, null, null));

        var contextExecutor = new ContextExecutor(_contextProviders);
        var classificationExecutor = new ClassificationExecutor(_classificationAgent);
        var ruleAdvisorExecutor = new RuleAdvisorExecutor(_ruleAdvisorAgent);
        var decisionExecutor = new DecisionExecutor();

        var workflow = new WorkflowBuilder(contextExecutor)
            .AddEdge(contextExecutor, classificationExecutor)
            .AddEdge(classificationExecutor, ruleAdvisorExecutor)
            .AddEdge(ruleAdvisorExecutor, decisionExecutor)
            .WithOutputFrom(decisionExecutor)
            .Build();

        await using Run run = await InProcessExecution.Lockstep.RunAsync(workflow, state);
        foreach (var eventItem in run.NewEvents.OfType<ExecutorCompletedEvent>())
        {
            state.Steps.Add(new RoutingWorkflowStep("workflow-event", $"Executor '{eventItem.ExecutorId}' completed."));
        }

        var context = new RoutingContext(state.Facts);
        var classification = state.Classification
            ?? await _classificationAgent.ClassifyAsync(requestBody, context, routingOptions, cancellationToken);
        var advisorResult = state.AdvisorResult
            ?? await _ruleAdvisorAgent.AdviseAsync(context, classification, routingOptions, cancellationToken);
        var decision = state.Decision ?? BasicModelRouter.SelectModel(requestBody, routingOptions, classification.Intent);

        // Semantic rules engine: when natural-language rules are configured, the processor model
        // picks the matching rule by name from the CLEAN user request (the <userRequest> content),
        // and the request is routed to that rule's engine. The full Copilot payload is still
        // forwarded to the selected engine downstream.
        if (_semanticRuleAnalyzer.HasSemanticRules(routingOptions))
        {
            var userRequest = BasicModelRouter.GetUserPromptText(requestBody);
            var rawUserMessage = BasicModelRouter.GetRawUserMessageText(requestBody);
            var match = await _semanticRuleAnalyzer.AnalyzeAsync(userRequest, routingOptions, cancellationToken);

            if (match is not null &&
                BasicModelRouter.TryResolveProfile(routingOptions, match.TargetModel, out var profileName, out var profile))
            {
                decision = new RoutingDecision(
                    profileName,
                    profile,
                    $"Semantic rule '{match.RuleName}' matched → routed to '{profileName}'. {match.Reason}");

                state.Facts.Add(new RoutingContextFact("semantic.matchedRule", match.RuleName));
                state.Facts.Add(new RoutingContextFact("semantic.reason", match.Reason));
                state.Facts.Add(new RoutingContextFact("semantic.engine", profileName));
                state.Facts.Add(new RoutingContextFact("semantic.confidence", match.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
                state.Facts.Add(new RoutingContextFact("classifier.source", match.Source));
                state.Facts.Add(new RoutingContextFact("semantic.source", match.Source));
                state.Steps.Add(new RoutingWorkflowStep(
                    "semantic-rule-analyzer",
                    $"Semantic rule '{match.RuleName}' selected by {match.Source} → '{profileName}'."));

                if (!string.IsNullOrWhiteSpace(rawUserMessage) &&
                    !string.Equals(rawUserMessage, userRequest, StringComparison.Ordinal))
                {
                    var rawCapped = rawUserMessage.Length > 4000 ? rawUserMessage[..4000] : rawUserMessage;
                    state.Facts.Add(new RoutingContextFact("request.rawUserMessage", rawCapped));
                }
            }
        }

        _logger.LogInformation(
            "Routing workflow classification intent={Intent}, complexity={Complexity}, confidence={Confidence}",
            classification.Intent,
            classification.Complexity,
            classification.Confidence);

        _logger.LogInformation(
            "Routing workflow selected profile {ProfileName} ({Deployment}). Reason={Reason}",
            decision.ProfileName,
            decision.Profile.Deployment,
            decision.Reason);

        var traceId = $"trace-{Guid.NewGuid():N}";
        _executionTraceStore.Store(new RoutingExecutionTrace(
            TraceId: traceId,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            WorkflowEngine: "microsoft-agent-framework-workflow",
            Context: state.Facts,
            Classification: classification,
            RuleAdvisor: advisorResult,
            Decision: RedactDecision(decision),
            Steps: state.Steps));

        _logger.LogInformation("Routing execution trace persisted with id {TraceId}.", traceId);

        return new RoutingSelectionResult(decision, traceId, state.Client);
    }

    /// <summary>
    /// Produces a copy of the routing decision with the model API key removed, so persisted traces
    /// never store plaintext secrets.
    /// </summary>
    private static RoutingDecision RedactDecision(RoutingDecision decision)
    {
        var profile = decision.Profile;
        var redacted = new ModelProfileOptions
        {
            Type = profile.Type,
            Endpoint = profile.Endpoint,
            Deployment = profile.Deployment,
            ApiVersion = profile.ApiVersion,
            ApiKey = string.IsNullOrEmpty(profile.ApiKey) ? string.Empty : "***redacted***",
            Enabled = profile.Enabled,
            IsProcessor = profile.IsProcessor,
            SupportsCustomTemperature = profile.SupportsCustomTemperature,
            SupportsToolCalling = profile.SupportsToolCalling
        };

        return new RoutingDecision(decision.ProfileName, redacted, decision.Reason);
    }

    private sealed class WorkflowState
    {
        public WorkflowState(JsonObject requestBody, RoutingOptions routingOptions, RoutingRequestMetadata client)
        {
            RequestBody = requestBody;
            RoutingOptions = routingOptions;
            Client = client;

            Facts.Add(new RoutingContextFact("request.client.id", Client.Id));
            Facts.Add(new RoutingContextFact("request.client.source", Client.Source));

            if (!string.IsNullOrWhiteSpace(Client.Version))
            {
                Facts.Add(new RoutingContextFact("request.client.version", Client.Version!));
            }

            if (!string.IsNullOrWhiteSpace(Client.UserAgent))
            {
                Facts.Add(new RoutingContextFact("request.client.userAgent", Client.UserAgent!));
            }

            if (!string.IsNullOrWhiteSpace(Client.RequestId))
            {
                Facts.Add(new RoutingContextFact("request.id", Client.RequestId!));
            }

            if (!string.IsNullOrWhiteSpace(Client.Endpoint))
            {
                Facts.Add(new RoutingContextFact("request.endpoint", Client.Endpoint!));
            }

            Steps.Add(new RoutingWorkflowStep("client-metadata", $"Captured client id '{Client.Id}' from {Client.Source}."));
        }

        public JsonObject RequestBody { get; }
        public RoutingOptions RoutingOptions { get; }
        public RoutingRequestMetadata Client { get; }
        public List<RoutingContextFact> Facts { get; } = [];
        public List<RoutingWorkflowStep> Steps { get; } = [];
        public ClassificationResult? Classification { get; set; }
        public RuleAdvisorResult? AdvisorResult { get; set; }
        public RoutingDecision? Decision { get; set; }
    }

    private sealed class ContextExecutor(IReadOnlyList<IRequestContextProvider> contextProviders)
        : Executor<WorkflowState, WorkflowState>("context-providers")
    {
        private readonly IReadOnlyList<IRequestContextProvider> _contextProviders = contextProviders;

        public override async ValueTask<WorkflowState> HandleAsync(
            WorkflowState message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            foreach (var provider in _contextProviders)
            {
                var providedFacts = await provider.ProvideAsync(message.RequestBody, message.RoutingOptions, cancellationToken);
                message.Facts.AddRange(providedFacts);
                message.Steps.Add(new RoutingWorkflowStep(provider.Name, $"Collected {providedFacts.Count} context fact(s)."));
            }

            return message;
        }
    }

    private sealed class ClassificationExecutor(IClassificationAgent classificationAgent)
        : Executor<WorkflowState, WorkflowState>("classification-agent")
    {
        private readonly IClassificationAgent _classificationAgent = classificationAgent;

        public override async ValueTask<WorkflowState> HandleAsync(
            WorkflowState message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            message.Classification = await _classificationAgent.ClassifyAsync(
                message.RequestBody,
                new RoutingContext(message.Facts),
                message.RoutingOptions,
                cancellationToken);
            message.Facts.Add(new RoutingContextFact("request.intent", message.Classification.Intent));
            message.Facts.Add(new RoutingContextFact("classifier.source", message.Classification.Source));
            if (!string.IsNullOrWhiteSpace(message.Classification.ProcessorModel))
            {
                message.Facts.Add(new RoutingContextFact("classifier.processorModel", message.Classification.ProcessorModel!));
            }
            message.Facts.Add(new RoutingContextFact(
                "classifier.confidence",
                message.Classification.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            message.Steps.Add(new RoutingWorkflowStep(
                "classification-agent",
                $"{message.Classification.Intent} / {message.Classification.Complexity} ({message.Classification.Source})"));
            return message;
        }
    }

    private sealed class RuleAdvisorExecutor(IRuleAdvisorAgent ruleAdvisorAgent)
        : Executor<WorkflowState, WorkflowState>("rule-advisor-agent")
    {
        private readonly IRuleAdvisorAgent _ruleAdvisorAgent = ruleAdvisorAgent;

        public override async ValueTask<WorkflowState> HandleAsync(
            WorkflowState message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            message.AdvisorResult = await _ruleAdvisorAgent.AdviseAsync(
                new RoutingContext(message.Facts),
                message.Classification ?? new ClassificationResult("unknown", "low", 0, "Classification unavailable."),
                message.RoutingOptions,
                cancellationToken);

            var advisorOutcome = string.IsNullOrWhiteSpace(message.AdvisorResult.SuggestedProfile)
                ? "No profile suggestion."
                : $"Suggested profile '{message.AdvisorResult.SuggestedProfile}'.";
            message.Steps.Add(new RoutingWorkflowStep("rule-advisor-agent", advisorOutcome));
            return message;
        }
    }

    private sealed class DecisionExecutor() : Executor<WorkflowState, WorkflowState>("routing-decision")
    {
        public override ValueTask<WorkflowState> HandleAsync(
            WorkflowState message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            message.Decision = BasicModelRouter.SelectModel(
                message.RequestBody,
                message.RoutingOptions,
                message.Classification?.Intent);
            message.Steps.Add(new RoutingWorkflowStep(
                "routing-decision",
                $"{message.Decision.ProfileName} => {message.Decision.Profile.Deployment}"));
            return ValueTask.FromResult(message);
        }
    }
}
