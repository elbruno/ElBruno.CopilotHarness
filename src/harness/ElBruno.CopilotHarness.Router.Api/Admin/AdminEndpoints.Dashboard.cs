using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapDashboardEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/dashboard/snapshot", (IClientRequestActivityStore requestActivityStore) =>
        {
            var now = DateTimeOffset.UtcNow;
            var snapshot = requestActivityStore.GetSnapshot(now);
            return Results.Ok(new DashboardSnapshotResponse(
                snapshot.ConnectedClients.Select(ToConnectedClientDto).ToList(),
                snapshot.LiveRequests.Select(ToLiveRequestDto).ToList(),
                now));
        });

        group.MapGet("/operations/status", async (
            HealthCheckService healthCheckService,
            IOptions<PersistenceOptions> persistenceOptions,
            IHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            var report = await healthCheckService.CheckHealthAsync(cancellationToken);
            var healthChecks = report.Entries
                .Select(entry => new OperationalHealthDto(
                    entry.Key,
                    entry.Value.Status.ToString(),
                    entry.Value.Description ?? entry.Value.Exception?.Message ?? "No additional details."))
                .ToList();

            return Results.Ok(new OperationsStatusResponse(
                DateTimeOffset.UtcNow,
                new OperationalSignalDto(
                    "Authentication",
                    "Not configured",
                    "The Phase 6 auth surface is not enabled in this build.",
                    "Wire an identity provider before turning on admin authentication."),
                new OperationalSignalDto(
                    "Rate limiting",
                    "Disabled",
                    "No rate limiter is registered yet.",
                    "Add a request budget and expose counters from the gateway."),
                new OperationalSignalDto(
                    "Retry / backoff",
                    "Tuned",
                    "HTTP resilience is tuned for LLM calls: 5-minute attempt timeout, 10-minute total, retries disabled to avoid duplicating expensive model requests.",
                    "Revisit per-endpoint policies if non-model traffic needs different budgets."),
                new OperationalSignalDto(
                    "Background jobs",
                    "Not configured",
                    "No schedulers or workers are registered in the current app graph.",
                    "Add a queue-backed worker and surface queue depth here."),
                new InfrastructureStatusDto(
                    "SQLite",
                    "None",
                    persistenceOptions.Value.DatabasePath,
                    environment.EnvironmentName),
                healthChecks));
        });

        group.MapGet("/clients/connected", (IClientRequestActivityStore requestActivityStore) =>
        {
            var snapshot = requestActivityStore.GetSnapshot(DateTimeOffset.UtcNow);
            return Results.Ok(snapshot.ConnectedClients.Select(ToConnectedClientDto).ToList());
        });

        group.MapGet("/requests/live", (IClientRequestActivityStore requestActivityStore) =>
        {
            var snapshot = requestActivityStore.GetSnapshot(DateTimeOffset.UtcNow);
            return Results.Ok(snapshot.LiveRequests.Select(ToLiveRequestDto).ToList());
        });
    }
}
