using ElBruno.CopilotHarness.Judge.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapJudgeEndpoints();

app.Run();
return;

public partial class Program;