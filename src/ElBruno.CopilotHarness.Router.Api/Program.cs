using System.Threading.RateLimiting;
using ElBruno.CopilotHarness.Router.Api.BackgroundJobs;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Api.Admin;
using ElBruno.CopilotHarness.Router.Api.Extension;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using ElBruno.CopilotHarness.Router.Api.Infrastructure;
using ElBruno.CopilotHarness.Router.Api.RateLimiting;
using ElBruno.CopilotHarness.Router.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddOptions<BackendOptions>()
    .Bind(builder.Configuration.GetSection(BackendOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
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

var persistenceOptions = builder.Configuration.GetSection(PersistenceOptions.SectionName).Get<PersistenceOptions>() ?? new PersistenceOptions();
var redisConnectionString = builder.Configuration.GetConnectionString("redis");
var postgresConnectionString = builder.Configuration.GetConnectionString("copilotharness");
var adminApiKey = builder.Configuration["Backend:Auth:AdminApiKey"];
var rateLimitingEnabled = builder.Configuration.GetValue("Backend:RateLimiting:Enabled", true);
var rateLimitingPermitLimit = builder.Configuration.GetValue("Backend:RateLimiting:PermitLimit", 200);
var rateLimitingWindowSeconds = builder.Configuration.GetValue("Backend:RateLimiting:WindowSeconds", 60);

if (persistenceOptions.Provider == DatabaseProvider.PostgreSql &&
    string.IsNullOrWhiteSpace(persistenceOptions.ConnectionString) &&
    !string.IsNullOrWhiteSpace(postgresConnectionString))
{
    persistenceOptions = new PersistenceOptions
    {
        Provider = persistenceOptions.Provider,
        DatabasePath = persistenceOptions.DatabasePath,
        ConnectionString = postgresConnectionString
    };
}

builder.Services.AddDbContext<HarnessDbContext>((_, options) =>
{
    var contentRootPath = builder.Environment.ContentRootPath;
    var connectionString = persistenceOptions.BuildConnectionString(contentRootPath);

    if (persistenceOptions.Provider == DatabaseProvider.PostgreSql)
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }

    options.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "copilotharness:";
    });
}

if (!string.IsNullOrWhiteSpace(adminApiKey))
{
    builder.Services
        .AddAuthentication(ApiKeyAuthenticationOptions.SchemeName)
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.SchemeName,
            options => options.ExpectedApiKey = adminApiKey!);

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser());
    });
}
else
{
    builder.Services.AddAuthorization();
}

builder.Services.AddRateLimiter(options =>
{
    if (!rateLimitingEnabled)
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
            RateLimitPartition.GetNoLimiter("disabled"));
        return;
    }

    var window = TimeSpan.FromSeconds(rateLimitingWindowSeconds);

    options.GlobalLimiter = BackendRateLimiter.CreateGlobalLimiter(rateLimitingPermitLimit, window);

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddScoped<IRoutingConfigurationStore, RoutingConfigurationStore>();
builder.Services.AddScoped<SqliteRoutingStoreInitializer>();
builder.Services.AddScoped<IExecutionTraceStore, PersistentExecutionTraceStore>();
builder.Services.AddScoped<IRequestContextProvider, RequestedModelContextProvider>();
builder.Services.AddScoped<IRequestContextProvider, StreamingContextProvider>();
builder.Services.AddScoped<IRequestContextProvider, PromptShapeContextProvider>();
builder.Services.AddScoped<IClassificationAgent, DeterministicClassificationAgent>();
builder.Services.AddScoped<IRuleAdvisorAgent, DeterministicRuleAdvisorAgent>();
builder.Services.AddScoped<IRoutingWorkflow, MicrosoftAgentFrameworkRoutingWorkflow>();
builder.Services.AddScoped<IRequestRoutingService, RequestRoutingService>();
builder.Services.AddSingleton<IClientRequestActivityStore, InMemoryClientRequestActivityStore>();

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
builder.Services.AddSingleton<IBackgroundJobQueue, ChannelBackgroundJobQueue>();
builder.Services.AddSingleton<BackendWarmupJob>();
builder.Services.AddHostedService<QueuedBackgroundJobProcessor>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<SqliteRoutingStoreInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

using (var scope = app.Services.CreateScope())
{
    var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
    await queue.EnqueueAsync(
        async (services, cancellationToken) =>
        {
            var warmup = services.GetRequiredService<BackendWarmupJob>();
            await warmup.RunAsync(services, cancellationToken);
        },
        CancellationToken.None);
}

var adminAuthEnabled = !string.IsNullOrWhiteSpace(adminApiKey);
if (adminAuthEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseRateLimiter();

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
    IClientRequestActivityStore requestActivityStore,
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

    var requestMetadata = OpenAiApiUtilities.BuildRequestMetadata(context.Request, responsesRequest);
    var requestActivityId = requestActivityStore.Start(new ClientRequestStart(
        "/v1/responses",
        requestMetadata.Id,
        false,
        OpenAiApiUtilities.GetRequestedModel(chatPayload)));

    try
    {
        requestMetadata = OpenAiApiUtilities.BuildRequestMetadata(context.Request, responsesRequest, requestActivityId);
        var routingSelection = await routingService.SelectModelWithTraceAsync(chatPayload, requestMetadata, cancellationToken);
        requestActivityStore.MarkRouted(requestActivityId, routingSelection);

        var routingDecision = routingSelection.Decision;
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
        OpenAiApiUtilities.AddRoutingHeaders(context.Response, routingSelection);

        var upstreamBody = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!upstreamResponse.IsSuccessStatusCode)
        {
            await context.Response.WriteAsync(upstreamBody, cancellationToken);
            return Results.Empty;
        }

        var responsePayload = OpenAiCompatibilityMapper.CreateResponsesResponse(upstreamBody, routingDecision);
        await context.Response.WriteAsJsonAsync(responsePayload, cancellationToken);
        return Results.Empty;
    }
    finally
    {
        requestActivityStore.Complete(requestActivityId);
    }
});

app.MapPost("/v1/chat/completions", async (
    HttpContext context,
    FoundryChatCompletionsClient client,
    IRequestRoutingService routingService,
    IClientRequestActivityStore requestActivityStore,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var (requestBody, parseError) = await OpenAiApiUtilities.ParseJsonObjectRequestAsync(context.Request, cancellationToken);
    if (parseError is not null)
    {
        return parseError;
    }

    var requestPayload = requestBody!;
    var stream = requestPayload["stream"]?.GetValue<bool>() ?? false;
    var requestMetadata = OpenAiApiUtilities.BuildRequestMetadata(context.Request, requestPayload);
    var requestActivityId = requestActivityStore.Start(new ClientRequestStart(
        "/v1/chat/completions",
        requestMetadata.Id,
        stream,
        OpenAiApiUtilities.GetRequestedModel(requestPayload)));

    try
    {
        requestMetadata = OpenAiApiUtilities.BuildRequestMetadata(context.Request, requestPayload, requestActivityId);
        var routingSelection = await routingService.SelectModelWithTraceAsync(requestPayload, requestMetadata, cancellationToken);
        requestActivityStore.MarkRouted(requestActivityId, routingSelection);
        var routingDecision = routingSelection.Decision;
        requestPayload["model"] = routingDecision.Profile.Deployment;

        logger.LogInformation(
            "Routing decision selected profile {ProfileName} deployment {Deployment}. Reason: {Reason}",
            routingDecision.ProfileName,
            routingDecision.Profile.Deployment,
            routingDecision.Reason);

        var startedAt = DateTimeOffset.UtcNow;
        using var upstreamResponse = await client.SendChatCompletionsAsync(
            requestPayload,
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
        OpenAiApiUtilities.AddRoutingHeaders(context.Response, routingSelection);

        await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(context.Response.Body, cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);

        return Results.Empty;
    }
    finally
    {
        requestActivityStore.Complete(requestActivityId);
    }
});

app.MapDefaultEndpoints();

app.MapExtensionEndpoints();
app.MapAdminEndpoints(adminAuthEnabled);

app.Run();
return;

public partial class Program;
