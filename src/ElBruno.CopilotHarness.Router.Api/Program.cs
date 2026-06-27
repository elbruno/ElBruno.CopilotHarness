using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services
    .AddOptions<FoundryOptions>()
    .Bind(builder.Configuration.GetSection(FoundryOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RoutingOptions>()
    .Bind(builder.Configuration.GetSection(RoutingOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => options.Profiles.Count >= 3,
        "Routing must define at least three model profiles.")
    .Validate(
        options => options.Profiles.ContainsKey(options.DefaultProfile),
        "Routing default profile must reference a configured model profile.")
    .Validate(
        options => options.Profiles.Values.All(profile => !string.IsNullOrWhiteSpace(profile.Deployment)),
        "Each routing profile must include a deployment name.")
    .Validate(
        options => options.Profiles.Values.Any(profile => profile.Enabled),
        "At least one routing profile must be enabled.")
    .ValidateOnStart();

builder.Services.AddSingleton<IModelRouter, BasicModelRouter>();

builder.Services.AddHttpClient<FoundryChatCompletionsClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FoundryOptions>>().Value;
    client.BaseAddress = FoundryOptions.GetNormalizedEndpoint(options.Endpoint);
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHttpClient("foundry-health", (_, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHealthChecks()
    .AddCheck<FoundryEndpointHealthCheck>("foundry-endpoint", HealthStatus.Degraded, ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/v1/chat/completions", async (
    HttpContext context,
    FoundryChatCompletionsClient client,
    IModelRouter router,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var payload = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
    if (payload is not JsonObject requestBody)
    {
        return Results.BadRequest(new { error = "The request body must be a valid JSON object." });
    }

    var routingDecision = router.SelectModel(requestBody);
    requestBody["model"] = routingDecision.Profile.Deployment;
    var stream = requestBody["stream"]?.GetValue<bool>() ?? false;

    logger.LogInformation(
        "Routing decision selected profile {ProfileName} deployment {Deployment}. Reason: {Reason}",
        routingDecision.ProfileName,
        routingDecision.Profile.Deployment,
        routingDecision.Reason);

    var startedAt = DateTimeOffset.UtcNow;
    using var upstreamResponse = await client.SendChatCompletionsAsync(
        requestBody,
        routingDecision.Profile,
        stream,
        cancellationToken);
    var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

    logger.LogInformation(
        "Upstream chat completion call finished with status {StatusCode} in {ElapsedMs} ms. Stream={Stream}",
        (int)upstreamResponse.StatusCode,
        elapsedMs,
        stream);

    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
    CopyHeaders(upstreamResponse, context.Response);
    context.Response.Headers["x-harness-model-profile"] = routingDecision.ProfileName;
    context.Response.Headers["x-harness-model-deployment"] = routingDecision.Profile.Deployment;
    context.Response.Headers["x-harness-routing-reason"] = routingDecision.Reason;

    await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
    await responseStream.CopyToAsync(context.Response.Body, cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);

    return Results.Empty;
});

app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});

app.Run();
return;

static void CopyHeaders(HttpResponseMessage source, HttpResponse target)
{
    foreach (var header in source.Headers)
    {
        target.Headers[header.Key] = header.Value.ToArray();
    }

    foreach (var header in source.Content.Headers)
    {
        target.Headers[header.Key] = header.Value.ToArray();
    }

    target.Headers.Remove("transfer-encoding");
}

public partial class Program;
