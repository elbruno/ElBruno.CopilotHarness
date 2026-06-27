using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services
    .AddOptions<FoundryOptions>()
    .Bind(builder.Configuration.GetSection(FoundryOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<FoundryChatCompletionsClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FoundryOptions>>().Value;
    client.BaseAddress = FoundryOptions.GetNormalizedEndpoint(options.Endpoint);
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/v1/chat/completions", async (HttpContext context, FoundryChatCompletionsClient client, CancellationToken cancellationToken) =>
{
    var payload = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
    if (payload is not JsonObject requestBody)
    {
        return Results.BadRequest(new { error = "The request body must be a valid JSON object." });
    }

    requestBody["model"] = FoundryOptions.DeploymentName;
    var stream = requestBody["stream"]?.GetValue<bool>() ?? false;

    using var upstreamResponse = await client.SendChatCompletionsAsync(requestBody, stream, cancellationToken);

    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
    CopyHeaders(upstreamResponse, context.Response);

    await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
    await responseStream.CopyToAsync(context.Response.Body, cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);

    return Results.Empty;
});

app.MapHealthChecks("/health");

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
