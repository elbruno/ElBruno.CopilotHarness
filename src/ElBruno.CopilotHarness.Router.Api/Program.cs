using System.Threading.RateLimiting;
using ElBruno.CopilotHarness.Router.Api.BackgroundJobs;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Api.Admin;
using ElBruno.CopilotHarness.Router.Api.Extension;
using ElBruno.CopilotHarness.Router.Api.Intelligence;
using ElBruno.CopilotHarness.Router.Api.Phase8;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using ElBruno.CopilotHarness.Router.Api.Infrastructure;
using ElBruno.CopilotHarness.Router.Api.RateLimiting;
using ElBruno.CopilotHarness.Router.Api.Security;
using ElBruno.CopilotHarness.Router.Api.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// GenAI OpenTelemetry: surface upstream chat-completion spans + token-usage metrics in the Aspire dashboard.
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
    tracing.AddSource(GenAiTelemetry.ActivitySourceName));
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
    metrics.AddMeter(GenAiTelemetry.MeterName));

builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
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
    .ValidateOnStart();

builder.Services
    .AddOptions<PersistenceOptions>()
    .Bind(builder.Configuration.GetSection(PersistenceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<TelemetryOptions>()
    .Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName));

builder.Services
    .AddOptions<ClassifierOptions>()
    .Bind(builder.Configuration.GetSection(ClassifierOptions.SectionName));

builder.Services
    .AddOptions<ResponseAnnotationOptions>()
    .Bind(builder.Configuration.GetSection(ResponseAnnotationOptions.SectionName));
builder.Services.AddSingleton<IResponseAnnotationState, ResponseAnnotationState>();

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
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IApiKeyProtector, DataProtectionApiKeyProtector>();
builder.Services.AddScoped<SqliteRoutingStoreInitializer>();
builder.Services.AddScoped<IExecutionTraceStore, PersistentExecutionTraceStore>();
builder.Services.AddScoped<IRequestContextProvider, RequestedModelContextProvider>();
builder.Services.AddScoped<IRequestContextProvider, StreamingContextProvider>();
builder.Services.AddScoped<IRequestContextProvider, PromptShapeContextProvider>();
builder.Services.AddScoped<DeterministicClassificationAgent>();
builder.Services.AddScoped<IClassificationAgent, ProcessorModelClassificationAgent>();
builder.Services.AddScoped<IRuleAdvisorAgent, DeterministicRuleAdvisorAgent>();
builder.Services.AddScoped<ISemanticRuleAnalyzer, SemanticRuleAnalyzer>();
builder.Services.AddScoped<IRoutingWorkflow, MicrosoftAgentFrameworkRoutingWorkflow>();
builder.Services.AddScoped<IRequestRoutingService, RequestRoutingService>();
builder.Services.AddSingleton<IClientRequestActivityStore, InMemoryClientRequestActivityStore>();

// Phase 8 – Continuous Evaluation stores
builder.Services.AddScoped<ShadowRoutingStore>();
builder.Services.AddScoped<IShadowRoutingStore>(sp => sp.GetRequiredService<ShadowRoutingStore>());
builder.Services.AddScoped<RuleConfidenceStore>();
builder.Services.AddScoped<IRuleConfidenceStore>(sp => sp.GetRequiredService<RuleConfidenceStore>());
builder.Services.AddScoped<BenchmarkStore>();
builder.Services.AddScoped<IBenchmarkStore>(sp => sp.GetRequiredService<BenchmarkStore>());
builder.Services.AddScoped<ApprovalWorkflowStore>();
builder.Services.AddScoped<IApprovalWorkflowStore>(sp => sp.GetRequiredService<ApprovalWorkflowStore>());
builder.Services.AddScoped<TeamProjectProfileStore>();
builder.Services.AddScoped<ITeamProjectProfileStore>(sp => sp.GetRequiredService<TeamProjectProfileStore>());

// Phase 8 – Intelligence services
builder.Services.AddScoped<IRequestContextProvider, TeamProfileContextProvider>();
builder.Services.AddScoped<IShadowRoutingService, ShadowRoutingService>();
builder.Services.AddScoped<IRecommendationAgent, DeterministicRecommendationAgent>();
builder.Services.AddScoped<IShadowRoutingService, ShadowRoutingService>();

builder.Services.AddHttpClient<FoundryChatCompletionsClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FoundryOptions>>().Value;
    client.BaseAddress = FoundryOptions.GetNormalizedEndpoint(options.Endpoint);
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHttpClient("model-provider", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddSingleton<IChatCompletionsProvider, AzureFoundryChatCompletionsProvider>();
builder.Services.AddSingleton<IChatCompletionsProvider, OllamaChatCompletionsProvider>();
builder.Services.AddSingleton<IChatCompletionsProviderFactory, ChatCompletionsProviderFactory>();

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
    IChatCompletionsProviderFactory providerFactory,
    IRequestRoutingService routingService,
    IClientRequestActivityStore requestActivityStore,
    IExecutionTraceStore traceStore,
    IOptions<TelemetryOptions> telemetryOptions,
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

    RequestOutcome? capturedOutcome = null;

    try
    {
        requestMetadata = OpenAiApiUtilities.BuildRequestMetadata(context.Request, responsesRequest, requestActivityId);
        var routingSelection = await routingService.SelectModelWithTraceAsync(chatPayload, requestMetadata, cancellationToken);
        requestActivityStore.MarkRouted(requestActivityId, routingSelection);

        var routingDecision = routingSelection.Decision;
        var traceId = routingSelection.TraceId;
        chatPayload["model"] = routingDecision.Profile.Deployment;
        var requestHadTools = OpenAiApiUtilities.RequestHasTools(chatPayload);

        logger.LogInformation(
            "Responses compatibility routing selected profile {ProfileName} deployment {Deployment}. Reason: {Reason}",
            routingDecision.ProfileName,
            routingDecision.Profile.Deployment,
            routingDecision.Reason);

        var startedAt = DateTimeOffset.UtcNow;
        var genAiSystem = GenAiTelemetry.SystemFor(routingDecision.Profile.Type);
        using var chatActivity = GenAiTelemetry.StartChatSpan(
            routingDecision.Profile,
            routingDecision.ProfileName,
            traceId,
            stream: false,
            requestHadTools,
            toolOverrideApplied: false);
        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await providerFactory
                .GetProvider(routingDecision.Profile)
                .SendChatCompletionsAsync(
                    chatPayload,
                    routingDecision.Profile,
                    stream: false,
                    cancellationToken);
        }
        catch (Exception upstreamException) when (
            upstreamException is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            GenAiTelemetry.RecordError(chatActivity, upstreamException);
            var failedAtMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
            var isTimeout = upstreamException is OperationCanceledException;
            var statusCode = isTimeout ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway;
            var errorType = isTimeout ? "upstream_timeout" : "upstream_error";

            logger.LogError(
                upstreamException,
                "Upstream responses call failed for trace {TraceId}. client={Client} selectedModel={SelectedModel} deployment={Deployment} statusCode={StatusCode}",
                traceId,
                requestMetadata.Id,
                routingDecision.ProfileName,
                routingDecision.Profile.Deployment,
                statusCode);

            capturedOutcome = new RequestOutcome(
                statusCode,
                failedAtMs,
                Succeeded: false,
                Error: upstreamException.Message,
                HadTools: requestHadTools,
                ToolOverrideApplied: false,
                OverrideReason: null);
            traceStore.AppendFacts(traceId, OpenAiApiUtilities.BuildUpstreamFacts(capturedOutcome));

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    message = isTimeout
                        ? "The upstream model did not respond in time."
                        : "The upstream model could not be reached.",
                    type = errorType
                }
            }, cancellationToken);
            return Results.Empty;
        }

        using (upstreamResponse)
        {
            var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

            context.Response.StatusCode = (int)upstreamResponse.StatusCode;
            OpenAiApiUtilities.CopyHeaders(upstreamResponse, context.Response);
            OpenAiApiUtilities.AddRoutingHeaders(context.Response, routingSelection);

            var upstreamBody = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);

            TokenUsage? usage = null;
            if (telemetryOptions.Value.CaptureTokenUsage && upstreamResponse.IsSuccessStatusCode)
            {
                usage = UpstreamResponseForwarder.ExtractNonStreamingUsage(upstreamBody);
                if (usage is not null)
                {
                    GenAiTelemetry.RecordUsage(chatActivity, usage, genAiSystem, routingDecision.Profile.Deployment);
                }
            }

            capturedOutcome = new RequestOutcome(
                (int)upstreamResponse.StatusCode,
                elapsedMs,
                Succeeded: upstreamResponse.IsSuccessStatusCode,
                Error: null,
                HadTools: requestHadTools,
                ToolOverrideApplied: false,
                OverrideReason: null,
                TokensIn: usage?.InputTokens,
                TokensOut: usage?.OutputTokens,
                TokensTotal: usage?.TotalTokens,
                ResponseModel: usage?.ResponseModel);
            traceStore.AppendFacts(traceId, OpenAiApiUtilities.BuildUpstreamFacts(capturedOutcome));

            if (!upstreamResponse.IsSuccessStatusCode)
            {
                await context.Response.WriteAsync(upstreamBody, cancellationToken);
                return Results.Empty;
            }

            var responsePayload = OpenAiCompatibilityMapper.CreateResponsesResponse(upstreamBody, routingDecision);
            await context.Response.WriteAsJsonAsync(responsePayload, cancellationToken);
            return Results.Empty;
        }
    }
    finally
    {
        requestActivityStore.Complete(
            requestActivityId,
            capturedOutcome ?? RequestOutcome.Failure("Request did not complete."));
    }
});

app.MapPost("/v1/chat/completions", async (
    HttpContext context,
    IChatCompletionsProviderFactory providerFactory,
    IRequestRoutingService routingService,
    IClientRequestActivityStore requestActivityStore,
    IShadowRoutingService shadowRoutingService,
    IExecutionTraceStore traceStore,
    IOptions<TelemetryOptions> telemetryOptions,
    IResponseAnnotationState annotationState,
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

    RequestOutcome? capturedOutcome = null;
    HttpResponseMessage? upstreamResponse = null;

    try
    {
        requestMetadata = OpenAiApiUtilities.BuildRequestMetadata(context.Request, requestPayload, requestActivityId);
        var routingSelection = await routingService.SelectModelWithTraceAsync(requestPayload, requestMetadata, cancellationToken);
        requestActivityStore.MarkRouted(requestActivityId, routingSelection);
        var routingDecision = routingSelection.Decision;
        var traceId = routingSelection.TraceId;

        var selectedProfileName = routingDecision.ProfileName;
        var selectedProfile = routingDecision.Profile;
        requestPayload["model"] = selectedProfile.Deployment;

        logger.LogInformation(
            "Routing decision selected profile {ProfileName} deployment {Deployment}. Reason: {Reason}",
            routingDecision.ProfileName,
            routingDecision.Profile.Deployment,
            routingDecision.Reason);

        // Tool-calling capability guard + size-aware routing + local-route token clamp.
        // See ToolRoutingGuard for the full rationale.
        var routingOptions = await routingService.GetRoutingOptionsAsync(cancellationToken);
        var guardResult = ToolRoutingGuard.Apply(
            requestPayload,
            selectedProfileName,
            selectedProfile,
            routingOptions,
            traceId,
            logger);

        selectedProfileName = guardResult.ProfileName;
        selectedProfile = guardResult.Profile;
        var requestHadTools = guardResult.HadTools;
        var toolOverrideApplied = guardResult.OverrideApplied;
        var overrideReason = guardResult.OverrideReason;

        var captureUsage = telemetryOptions.Value.CaptureTokenUsage;
        // Demo routing footer: opt-in, runtime-toggled, and skipped for tool/agentic calls.
        var annotate = annotationState.Enabled && !requestHadTools;
        if ((captureUsage || annotate) && stream)
        {
            OpenAiApiUtilities.EnsureStreamUsageRequested(requestPayload);
        }

        var genAiSystem = GenAiTelemetry.SystemFor(selectedProfile.Type);
        using var chatActivity = GenAiTelemetry.StartChatSpan(
            selectedProfile,
            selectedProfileName,
            traceId,
            stream,
            requestHadTools,
            toolOverrideApplied);

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            upstreamResponse = await providerFactory
                .GetProvider(selectedProfile)
                .SendChatCompletionsAsync(
                    requestPayload,
                    selectedProfile,
                    stream,
                    cancellationToken);
        }
        catch (Exception upstreamException) when (
            upstreamException is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            GenAiTelemetry.RecordError(chatActivity, upstreamException);
            var failedAtMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
            var isTimeout = upstreamException is OperationCanceledException;
            var statusCode = isTimeout ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway;
            var errorType = isTimeout ? "upstream_timeout" : "upstream_error";

            logger.LogError(
                upstreamException,
                "Upstream chat completion call failed for trace {TraceId}. client={Client} selectedModel={SelectedModel} deployment={Deployment} stream={Stream} statusCode={StatusCode}",
                traceId,
                requestMetadata.Id,
                selectedProfileName,
                selectedProfile.Deployment,
                stream,
                statusCode);

            capturedOutcome = new RequestOutcome(
                statusCode,
                failedAtMs,
                Succeeded: false,
                Error: upstreamException.Message,
                HadTools: requestHadTools,
                ToolOverrideApplied: toolOverrideApplied,
                OverrideReason: overrideReason);
            traceStore.AppendFacts(traceId, OpenAiApiUtilities.BuildUpstreamFacts(capturedOutcome));

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    message = isTimeout
                        ? "The upstream model did not respond in time."
                        : "The upstream model could not be reached.",
                    type = errorType
                }
            }, cancellationToken);
            return Results.Empty;
        }

        var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        var upstreamSucceeded = upstreamResponse.IsSuccessStatusCode;

        logger.LogInformation(
            "Upstream chat completion finished. traceId={TraceId} client={Client} selectedModel={SelectedModel} deployment={Deployment} stream={Stream} statusCode={StatusCode} latencyMs={LatencyMs} hadTools={HadTools} toolOverride={ToolOverride}",
            traceId,
            requestMetadata.Id,
            selectedProfileName,
            selectedProfile.Deployment,
            stream,
            (int)upstreamResponse.StatusCode,
            elapsedMs,
            requestHadTools,
            toolOverrideApplied);

        capturedOutcome = new RequestOutcome(
            (int)upstreamResponse.StatusCode,
            elapsedMs,
            Succeeded: upstreamSucceeded,
            Error: null,
            HadTools: requestHadTools,
            ToolOverrideApplied: toolOverrideApplied,
            OverrideReason: overrideReason);

        // Phase 8: fire shadow routing comparison in background (non-streaming only)
        if (!stream)
        {
            shadowRoutingService.FireAndForget(
                requestPayload,
                selectedProfileName,
                routingSelection.TraceId,
                elapsedMs,
                (int)upstreamResponse.StatusCode);
        }

        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        OpenAiApiUtilities.CopyHeaders(upstreamResponse, context.Response);
        OpenAiApiUtilities.AddRoutingHeaders(context.Response, routingSelection);

        // Forward the body to the client, capturing GenAI token usage on the way through (best-effort).
        // When the demo routing footer is enabled for a plain (non-tool) successful response, inject the
        // rule/model/source/token footer into the assistant message so it shows in the Copilot chat window.
        Func<TokenUsage?, string>? annotationFactory = null;
        if (annotate && upstreamSucceeded)
        {
            annotationFactory = usageForFooter => RoutingAnnotation.BuildFooter(
                selectedProfileName,
                selectedProfile.Deployment,
                routingDecision.Reason,
                requestMetadata.Id,
                toolOverrideApplied,
                usageForFooter);
        }

        var usage = await UpstreamResponseForwarder.ForwardAsync(
            upstreamResponse,
            context.Response,
            stream,
            captureUsage && upstreamSucceeded,
            cancellationToken,
            annotationFactory);

        if (usage is not null)
        {
            GenAiTelemetry.RecordUsage(chatActivity, usage, genAiSystem, selectedProfile.Deployment);
            capturedOutcome = capturedOutcome with
            {
                TokensIn = usage.InputTokens,
                TokensOut = usage.OutputTokens,
                TokensTotal = usage.TotalTokens,
                ResponseModel = usage.ResponseModel
            };
        }

        traceStore.AppendFacts(traceId, OpenAiApiUtilities.BuildUpstreamFacts(capturedOutcome));

        return Results.Empty;
    }
    finally
    {
        upstreamResponse?.Dispose();
        requestActivityStore.Complete(
            requestActivityId,
            capturedOutcome ?? RequestOutcome.Failure("Request did not complete."));
    }
});

app.MapDefaultEndpoints();

app.MapExtensionEndpoints();
app.MapVsCodeConnectEndpoints();
app.MapAdminEndpoints(adminAuthEnabled);
app.MapPhase8Endpoints(adminAuthEnabled);

app.Run();
return;

public partial class Program;
