using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Api.Admin;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

builder.Services
    .AddOptions<PersistenceOptions>()
    .Bind(builder.Configuration.GetSection(PersistenceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<HarnessDbContext>((serviceProvider, options) =>
{
    var persistenceOptions = serviceProvider.GetRequiredService<IOptions<PersistenceOptions>>().Value;
    var contentRootPath = builder.Environment.ContentRootPath;
    options.UseSqlite(persistenceOptions.BuildConnectionString(contentRootPath));
    options.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddScoped<IRoutingConfigurationStore, RoutingConfigurationStore>();
builder.Services.AddScoped<SqliteRoutingStoreInitializer>();
builder.Services.AddScoped<IRequestRoutingService, RequestRoutingService>();

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

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<SqliteRoutingStoreInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/v1/models", async (
    IRequestRoutingService routingService,
    CancellationToken cancellationToken) =>
{
    var routingOptions = await routingService.GetRoutingOptionsAsync(cancellationToken);
    return Results.Ok(OpenAiCompatibilityMapper.CreateModelsResponse(routingOptions));
});

app.MapPost("/v1/responses", async (
    HttpContext context,
    FoundryChatCompletionsClient client,
    IRequestRoutingService routingService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var (responsesRequest, parseError) = await OpenAiApiUtilities.ParseJsonObjectRequestAsync(context.Request, cancellationToken);
    if (parseError is not null)
    {
        return parseError;
    }

    if (!OpenAiCompatibilityMapper.TryBuildChatCompletionsRequest(
            responsesRequest!,
            out var chatPayload,
            out var mappingError))
    {
        return OpenAiApiUtilities.OpenAiBadRequest(mappingError ?? "Invalid responses request.");
    }

    var routingDecision = await routingService.SelectModelAsync(chatPayload, cancellationToken);
    chatPayload["model"] = routingDecision.Profile.Deployment;

    logger.LogInformation(
        "Responses compatibility routing selected profile {ProfileName} deployment {Deployment}. Reason: {Reason}",
        routingDecision.ProfileName,
        routingDecision.Profile.Deployment,
        routingDecision.Reason);

    using var upstreamResponse = await client.SendChatCompletionsAsync(
        chatPayload,
        routingDecision.Profile,
        stream: false,
        cancellationToken);

    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
    OpenAiApiUtilities.CopyHeaders(upstreamResponse, context.Response);
    OpenAiApiUtilities.AddRoutingHeaders(context.Response, routingDecision);

    var upstreamBody = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);
    if (!upstreamResponse.IsSuccessStatusCode)
    {
        await context.Response.WriteAsync(upstreamBody, cancellationToken);
        return Results.Empty;
    }

    var responsePayload = OpenAiCompatibilityMapper.CreateResponsesResponse(upstreamBody, routingDecision);
    await context.Response.WriteAsJsonAsync(responsePayload, cancellationToken);
    return Results.Empty;
});

app.MapPost("/v1/chat/completions", async (
    HttpContext context,
    FoundryChatCompletionsClient client,
    IRequestRoutingService routingService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var (requestBody, parseError) = await OpenAiApiUtilities.ParseJsonObjectRequestAsync(context.Request, cancellationToken);
    if (parseError is not null)
    {
        return parseError;
    }

    var routingDecision = await routingService.SelectModelAsync(requestBody!, cancellationToken);
    requestBody!["model"] = routingDecision.Profile.Deployment;
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
    OpenAiApiUtilities.CopyHeaders(upstreamResponse, context.Response);
    OpenAiApiUtilities.AddRoutingHeaders(context.Response, routingDecision);

    await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
    await responseStream.CopyToAsync(context.Response.Body, cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);

    return Results.Empty;
});

app.MapDefaultEndpoints();
app.MapAdminEndpoints();

app.Run();
return;

public partial class Program;
