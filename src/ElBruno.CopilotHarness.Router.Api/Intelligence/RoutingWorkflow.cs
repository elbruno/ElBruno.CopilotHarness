using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Agents.AI.Workflows;

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
        RoutingContext context,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken);
}

public sealed record ClassificationResult(string Intent, string Complexity, double Confidence, string Reasoning);

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

public sealed class PromptShapeContextProvider : IRequestContextProvider
{
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

        var promptCharacters = BasicModelRouter.GetPromptCharacterCount(requestBody);
        return ValueTask.FromResult<IReadOnlyList<RoutingContextFact>>(
        [
            new RoutingContextFact("request.hasSystemMessage", hasSystemMessage.ToString()),
            new RoutingContextFact("request.promptCharacters", promptCharacters.ToString())
        ]);
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
        RoutingContext context,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        if (context.TryGetInteger("request.promptCharacters", out var promptCharacters) &&
            promptCharacters >= routingOptions.Rules.BigPromptCharacterThreshold)
        {
            return Task.FromResult(new ClassificationResult(
                Intent: "long-form-generation",
                Complexity: "high",
                Confidence: 0.93,
                Reasoning: "Prompt size crossed big prompt threshold."));
        }

        if (context.TryGetBoolean("request.hasSystemMessage", out var hasSystemMessage) && hasSystemMessage)
        {
            return Task.FromResult(new ClassificationResult(
                Intent: "policy-guided-conversation",
                Complexity: "medium",
                Confidence: 0.86,
                Reasoning: "System message indicates structured guidance."));
        }

        return Task.FromResult(new ClassificationResult(
            Intent: "standard-conversation",
            Complexity: "low",
            Confidence: 0.81,
            Reasoning: "No high-complexity indicators found."));
    }
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
    IExecutionTraceStore executionTraceStore,
    ILogger<MicrosoftAgentFrameworkRoutingWorkflow> logger) : IRoutingWorkflow
{
    private readonly IReadOnlyList<IRequestContextProvider> _contextProviders = contextProviders.ToList();
    private readonly IClassificationAgent _classificationAgent = classificationAgent;
    private readonly IRuleAdvisorAgent _ruleAdvisorAgent = ruleAdvisorAgent;
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
            ?? await _classificationAgent.ClassifyAsync(context, routingOptions, cancellationToken);
        var advisorResult = state.AdvisorResult
            ?? await _ruleAdvisorAgent.AdviseAsync(context, classification, routingOptions, cancellationToken);
        var decision = state.Decision ?? BasicModelRouter.SelectModel(requestBody, routingOptions);

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
            Decision: decision,
            Steps: state.Steps));

        _logger.LogInformation("Routing execution trace persisted with id {TraceId}.", traceId);

        return new RoutingSelectionResult(decision, traceId, state.Client);
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
                new RoutingContext(message.Facts),
                message.RoutingOptions,
                cancellationToken);
            message.Steps.Add(new RoutingWorkflowStep(
                "classification-agent",
                $"{message.Classification.Intent} / {message.Classification.Complexity}"));
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
            message.Decision = BasicModelRouter.SelectModel(message.RequestBody, message.RoutingOptions);
            message.Steps.Add(new RoutingWorkflowStep(
                "routing-decision",
                $"{message.Decision.ProfileName} => {message.Decision.Profile.Deployment}"));
            return ValueTask.FromResult(message);
        }
    }
}
