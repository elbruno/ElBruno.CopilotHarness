using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapPlaygroundEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/playground/evaluate", async (
            PlaygroundRequest request,
            IRequestRoutingService routingService,
            CancellationToken cancellationToken) =>
        {
            var jsonRequest = new JsonObject
            {
                ["stream"] = request.Stream
            };

            if (!string.IsNullOrWhiteSpace(request.RequestedProfile))
            {
                jsonRequest["model"] = request.RequestedProfile.Trim();
            }

            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(request.SystemMessage))
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = request.SystemMessage.Trim()
                });
            }

            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = request.Prompt
            });
            jsonRequest["messages"] = messages;

            var routingSelection = await routingService.SelectModelWithTraceAsync(jsonRequest, cancellationToken);
            var decision = routingSelection.Decision;
            jsonRequest["model"] = decision.Profile.Deployment;

            var promptCharacters = request.Prompt.Length + (request.SystemMessage?.Length ?? 0);
            return Results.Ok(new PlaygroundResponse(
                decision.ProfileName,
                decision.Profile.Deployment,
                decision.Reason,
                promptCharacters,
                jsonRequest));
        });
    }
}
