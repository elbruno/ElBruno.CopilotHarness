using ElBruno.CopilotHarness.Judge.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net;
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
    .AddOptions<JudgePersistenceOptions>()
    .Bind(builder.Configuration.GetSection(JudgePersistenceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<JudgeDbContext>((serviceProvider, options) =>
{
    var persistenceOptions = serviceProvider.GetRequiredService<IOptions<JudgePersistenceOptions>>().Value;
    options.UseSqlite(persistenceOptions.BuildConnectionString(builder.Environment.ContentRootPath));
    options.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddScoped<IPromptRecordStore, PromptRecordStore>();
builder.Services.AddScoped<IJudgeModelClient, FoundryJudgeModelClient>();
builder.Services.AddScoped<IJudgeScoringEngine, HeuristicJudgeScoringEngine>();
builder.Services.AddScoped<IBenchmarkRunner, BenchmarkRunner>();
builder.Services.AddScoped<JudgeDatabaseInitializer>();
builder.Services.AddScoped<IRecommendationAgent, RecommendationAgent>();

builder.Services
    .AddOptions<ContinuousBenchmarkOptions>()
    .Bind(builder.Configuration.GetSection(ContinuousBenchmarkOptions.SectionName));

builder.Services.AddSingleton<ContinuousBenchmarkScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ContinuousBenchmarkScheduler>());

builder.Services.AddHttpClient<FoundryJudgeModelClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FoundryOptions>>().Value;
    client.BaseAddress = FoundryOptions.GetNormalizedEndpoint(options.Endpoint);
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<JudgeDatabaseInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

app.MapGet("/", async (
    JudgeDbContext dbContext,
    IOptions<JudgePersistenceOptions> persistenceOptions,
    CancellationToken cancellationToken) =>
{
    var databaseReady = await dbContext.Database.CanConnectAsync(cancellationToken);
    var promptCount = await dbContext.PromptRecords.CountAsync(cancellationToken);
    var resultCount = await dbContext.BenchmarkResults.CountAsync(cancellationToken);
    var runCounts = await dbContext.BenchmarkRuns
        .AsNoTracking()
        .GroupBy(run => run.Status)
        .Select(group => new { Status = group.Key, Count = group.Count() })
        .ToListAsync(cancellationToken);
    var latestRun = (await dbContext.BenchmarkRuns
        .AsNoTracking()
        .ToListAsync(cancellationToken))
        .OrderByDescending(run => run.CreatedAtUtc)
        .FirstOrDefault();

    var storagePath = persistenceOptions.Value.BuildConnectionString(app.Environment.ContentRootPath).Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase);
    var html = new System.Text.StringBuilder();
    html.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />");
    html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
    html.AppendLine("<title>Judge Operations</title>");
    html.AppendLine("<style>");
    html.AppendLine("body{margin:0;font-family:Segoe UI,Arial,sans-serif;background:#f8fafc;color:#0f172a;padding:24px;}");
    html.AppendLine(".cards{display:grid;gap:12px;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));}");
    html.AppendLine(".card{background:#fff;border:1px solid #cbd5e1;border-radius:10px;padding:16px;}");
    html.AppendLine(".metric{font-size:2rem;font-weight:700;margin:6px 0;}");
    html.AppendLine("table{border-collapse:collapse;width:100%;margin-top:16px;background:#fff;}");
    html.AppendLine("th,td{border:1px solid #d1d5db;padding:8px;text-align:left;}");
    html.AppendLine(".muted{color:#64748b;font-size:.92rem;}");
    html.AppendLine("</style></head><body>");
    html.AppendLine("<h1>Judge Operations</h1>");
    html.AppendLine("<p class=\"muted\">Separate judge app for prompt replay and benchmark reporting.</p>");
    html.AppendLine("<div class=\"cards\">");
    html.AppendLine(JudgeDashboardHtml.RenderMetricCard("Storage", "SQLite", $"Path: {WebUtility.HtmlEncode(storagePath)}"));
    html.AppendLine(JudgeDashboardHtml.RenderMetricCard("Database", databaseReady ? "Connected" : "Unavailable", "JudgeDbContext connectivity"));
    html.AppendLine(JudgeDashboardHtml.RenderMetricCard("Prompt records", promptCount.ToString(), "Imported prompts available for replay"));
    html.AppendLine(JudgeDashboardHtml.RenderMetricCard("Benchmark results", resultCount.ToString(), "Stored scoring rows"));
    html.AppendLine("</div>");
    html.AppendLine("<div class=\"card\" style=\"margin-top:12px;\">");
    html.AppendLine("<h2>Run status</h2>");
    if (runCounts.Count == 0)
    {
        html.AppendLine("<p>No benchmark runs recorded yet.</p>");
    }
    else
    {
        html.AppendLine("<table><thead><tr><th>Status</th><th>Count</th></tr></thead><tbody>");
        foreach (var item in runCounts.OrderByDescending(item => item.Count))
        {
            html.AppendLine($"<tr><td>{WebUtility.HtmlEncode(item.Status)}</td><td>{item.Count}</td></tr>");
        }
        html.AppendLine("</tbody></table>");
    }
    html.AppendLine("</div>");
    html.AppendLine("<div class=\"card\" style=\"margin-top:12px;\">");
    html.AppendLine("<h2>Latest run</h2>");
    if (latestRun is null)
    {
        html.AppendLine("<p>No run has been started yet.</p>");
    }
    else
    {
        html.AppendLine($"<p><strong>{WebUtility.HtmlEncode(latestRun.Name)}</strong> · {WebUtility.HtmlEncode(latestRun.Status)}</p>");
        html.AppendLine($"<p class=\"muted\">Mode: {WebUtility.HtmlEncode(latestRun.Mode)} · Prompts: {latestRun.PromptRecordCount} · Models: {latestRun.ModelCount}</p>");
    }
    html.AppendLine("</div>");
    html.AppendLine("<div class=\"card\" style=\"margin-top:12px;\">");
    html.AppendLine("<h2>Phase 6 notes</h2>");
    html.AppendLine("<ul>");
    html.AppendLine("<li>Auth, rate limiting, and backoff live in the UI until production services are wired in.</li>");
    html.AppendLine("<li>Background jobs are represented by benchmark runs and future queue workers.</li>");
    html.AppendLine("<li>PostgreSQL and Redis are reserved for the production backend phase.</li>");
    html.AppendLine("</ul>");
    html.AppendLine("</div>");
    html.AppendLine("</body></html>");
    return Results.Content(html.ToString(), "text/html");
});

app.MapGet("/benchmarks", () => Results.Redirect("/", permanent: false));
app.MapGet("/favicon.ico", () => Results.NoContent());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapJudgeEndpoints();
app.MapContinuousEvalEndpoints();

app.Run();
return;

public partial class Program;

public static class JudgeDashboardHtml
{
    public static string RenderMetricCard(string title, string value, string details) =>
        $"<div class=\"card\"><h3>{WebUtility.HtmlEncode(title)}</h3><div class=\"metric\">{WebUtility.HtmlEncode(value)}</div><div class=\"muted\">{WebUtility.HtmlEncode(details)}</div></div>";
}