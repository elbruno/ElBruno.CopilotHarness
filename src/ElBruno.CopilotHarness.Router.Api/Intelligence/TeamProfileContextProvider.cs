using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api.Intelligence;

/// <summary>
/// Request context provider that reads the <c>x-harness-team</c> header and
/// looks up the team's routing profile overrides from the
/// <see cref="ITeamProjectProfileStore"/>. When a team profile is found, the
/// team's <c>DefaultProfile</c> is injected as a routing context fact so that
/// <see cref="DeterministicRuleAdvisorAgent"/> can use it as the suggested profile.
/// </summary>
public sealed class TeamProfileContextProvider(
    ITeamProjectProfileStore teamStore,
    ILogger<TeamProfileContextProvider> logger) : IRequestContextProvider
{
    private readonly ITeamProjectProfileStore _teamStore = teamStore;
    private readonly ILogger<TeamProfileContextProvider> _logger = logger;

    public string Name => "team-profile";

    public async ValueTask<IReadOnlyList<RoutingContextFact>> ProvideAsync(
        JsonObject requestBody,
        RoutingOptions routingOptions,
        CancellationToken cancellationToken)
    {
        // Read team from request header via injected context (not available here directly).
        // The team is provided via the "x-harness-team" fact injected earlier by middleware.
        // We check if the context already has a team fact injected by the routing pre-processing.
        // For now we read it from the request body metadata.
        var teamId = requestBody["metadata"]?["team"]?.GetValue<string>()
                  ?? requestBody["client"]?["team"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(teamId))
        {
            return [];
        }

        var team = await _teamStore.GetTeamAsync(teamId.Trim().ToLowerInvariant(), cancellationToken);

        if (team is null || !team.Enabled)
        {
            return [];
        }

        _logger.LogDebug(
            "Team profile '{TeamId}' matched — injecting default profile '{DefaultProfile}'.",
            team.TeamId,
            team.DefaultProfile);

        return [new RoutingContextFact("request.team.id", team.TeamId),
                new RoutingContextFact("request.team.defaultProfile", team.DefaultProfile)];
    }
}
