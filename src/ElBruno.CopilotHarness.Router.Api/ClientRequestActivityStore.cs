namespace ElBruno.CopilotHarness.Router.Api;

public static class HarnessClientKinds
{
    public const string VsCode = "vscode";
    public const string CopilotCli = "copilot-cli";
    public const string CopilotApp = "copilot-app";
    public const string Unknown = "unknown";
}

public sealed record ClientRequestStart(
    string Endpoint,
    string Client,
    bool Stream,
    string? RequestedModel);

/// <summary>
/// Final outcome of a forwarded client request, captured after the upstream call completes (or fails)
/// so the Live feed and traces can surface upstream status, latency, errors and tool-capability overrides.
/// </summary>
public sealed record RequestOutcome(
    int? StatusCode,
    double? LatencyMs,
    bool Succeeded,
    string? Error,
    bool HadTools,
    bool ToolOverrideApplied,
    string? OverrideReason,
    long? TokensIn = null,
    long? TokensOut = null,
    long? TokensTotal = null,
    string? ResponseModel = null)
{
    public static RequestOutcome None { get; } = new(null, null, true, null, false, false, null);

    public static RequestOutcome Failure(string? error) =>
        new(null, null, false, error, false, false, null);
}

public sealed record ConnectedClientSnapshot(
    string Client,
    bool IsConnected,
    int ActiveRequests,
    int RequestsLastFiveMinutes,
    DateTimeOffset? LastSeenAtUtc);

public sealed record LiveRequestSnapshot(
    string RequestId,
    string Endpoint,
    string Client,
    bool Stream,
    string? RequestedModel,
    string? SelectedProfile,
    string? SelectedDeployment,
    string? TraceId,
    DateTimeOffset StartedAtUtc,
    double ElapsedMs);

public sealed record ClientDashboardSnapshot(
    IReadOnlyList<ConnectedClientSnapshot> ConnectedClients,
    IReadOnlyList<LiveRequestSnapshot> LiveRequests);

public interface IClientRequestActivityStore
{
    string Start(ClientRequestStart request);
    void MarkRouted(string requestId, RoutingSelectionResult selection);
    void Complete(string requestId);
    void Complete(string requestId, RequestOutcome outcome);
    ClientDashboardSnapshot GetSnapshot(DateTimeOffset nowUtc);
}

public sealed class InMemoryClientRequestActivityStore : IClientRequestActivityStore
{
    private static readonly string[] KnownClients =
    [
        HarnessClientKinds.VsCode,
        HarnessClientKinds.CopilotCli,
        HarnessClientKinds.CopilotApp
    ];

    private static readonly TimeSpan ConnectedWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, ActiveRequestState> _activeById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<CompletedRequestState> _completedRequests = [];
    private readonly Lock _lock = new();

    public string Start(ClientRequestStart request)
    {
        var requestId = $"req-{Guid.NewGuid():N}";
        lock (_lock)
        {
            _activeById[requestId] = new ActiveRequestState(
                requestId,
                request.Endpoint,
                NormalizeClient(request.Client),
                request.Stream,
                request.RequestedModel,
                DateTimeOffset.UtcNow,
                null,
                null,
                null);
        }

        return requestId;
    }

    public void MarkRouted(string requestId, RoutingSelectionResult selection)
    {
        lock (_lock)
        {
            if (!_activeById.TryGetValue(requestId, out var state))
            {
                return;
            }

            _activeById[requestId] = state with
            {
                SelectedProfile = selection.Decision.ProfileName,
                SelectedDeployment = selection.Decision.Profile.Deployment,
                TraceId = selection.TraceId
            };
        }
    }

    public void Complete(string requestId) => Complete(requestId, RequestOutcome.None);

    public void Complete(string requestId, RequestOutcome outcome)
    {
        lock (_lock)
        {
            if (!_activeById.Remove(requestId, out var state))
            {
                return;
            }

            _completedRequests.Enqueue(new CompletedRequestState(
                state.RequestId,
                state.Endpoint,
                state.Client,
                state.Stream,
                state.RequestedModel,
                state.SelectedProfile,
                state.SelectedDeployment,
                state.TraceId,
                state.StartedAtUtc,
                DateTimeOffset.UtcNow,
                outcome));
        }
    }

    public ClientDashboardSnapshot GetSnapshot(DateTimeOffset nowUtc)
    {
        lock (_lock)
        {
            while (_completedRequests.TryPeek(out var completed) &&
                   nowUtc - completed.CompletedAtUtc > RetentionWindow)
            {
                _completedRequests.Dequeue();
            }

            var activeStates = _activeById.Values.ToList();
            var recentCompleted = _completedRequests.ToList();
            var allClients = KnownClients
                .Concat(activeStates.Select(state => state.Client))
                .Concat(recentCompleted.Select(item => item.Client))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(client => client, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var connectedClients = allClients
                .Select(client =>
                {
                    var activeCount = activeStates.Count(state => string.Equals(state.Client, client, StringComparison.OrdinalIgnoreCase));
                    var recentCount = recentCompleted.Count(item =>
                        string.Equals(item.Client, client, StringComparison.OrdinalIgnoreCase) &&
                        nowUtc - item.CompletedAtUtc <= ConnectedWindow);
                    var lastSeen = activeStates
                        .Where(state => string.Equals(state.Client, client, StringComparison.OrdinalIgnoreCase))
                        .Select(state => state.StartedAtUtc)
                        .Concat(recentCompleted
                            .Where(item => string.Equals(item.Client, client, StringComparison.OrdinalIgnoreCase))
                            .Select(item => item.CompletedAtUtc))
                        .DefaultIfEmpty()
                        .Max();

                    DateTimeOffset? normalizedLastSeen = lastSeen == default ? null : lastSeen;
                    var isConnected = normalizedLastSeen is not null &&
                                      nowUtc - normalizedLastSeen.Value <= ConnectedWindow;

                    return new ConnectedClientSnapshot(
                        client,
                        isConnected,
                        activeCount,
                        recentCount + activeCount,
                        normalizedLastSeen);
                })
                .ToList();

            var liveActive = activeStates
                .OrderByDescending(state => state.StartedAtUtc)
                .Select(state => new LiveRequestSnapshot(
                    state.RequestId,
                    state.Endpoint,
                    state.Client,
                    state.Stream,
                    state.RequestedModel,
                    state.SelectedProfile,
                    state.SelectedDeployment,
                    state.TraceId,
                    state.StartedAtUtc,
                    (nowUtc - state.StartedAtUtc).TotalMilliseconds))
                .ToList();
            var liveRecent = recentCompleted
                .Where(item => nowUtc - item.CompletedAtUtc <= LiveWindow)
                .OrderByDescending(item => item.CompletedAtUtc)
                .Select(item => new LiveRequestSnapshot(
                    item.RequestId,
                    item.Endpoint,
                    item.Client,
                    item.Stream,
                    item.RequestedModel,
                    item.SelectedProfile,
                    item.SelectedDeployment,
                    item.TraceId,
                    item.StartedAtUtc,
                    (item.CompletedAtUtc - item.StartedAtUtc).TotalMilliseconds))
                .ToList();

            var liveRequests = liveActive
                .Concat(liveRecent)
                .OrderByDescending(request => request.StartedAtUtc)
                .Take(50)
                .ToList();

            return new ClientDashboardSnapshot(connectedClients, liveRequests);
        }
    }

    private static string NormalizeClient(string? client)
    {
        if (string.IsNullOrWhiteSpace(client))
        {
            return HarnessClientKinds.Unknown;
        }

        return client.Trim().ToLowerInvariant() switch
        {
            "vs-code" => HarnessClientKinds.VsCode,
            "visual-studio-code" => HarnessClientKinds.VsCode,
            _ => client.Trim().ToLowerInvariant()
        };
    }

    private sealed record ActiveRequestState(
        string RequestId,
        string Endpoint,
        string Client,
        bool Stream,
        string? RequestedModel,
        DateTimeOffset StartedAtUtc,
        string? SelectedProfile,
        string? SelectedDeployment,
        string? TraceId);

    private sealed record CompletedRequestState(
        string RequestId,
        string Endpoint,
        string Client,
        bool Stream,
        string? RequestedModel,
        string? SelectedProfile,
        string? SelectedDeployment,
        string? TraceId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        RequestOutcome Outcome);
}
