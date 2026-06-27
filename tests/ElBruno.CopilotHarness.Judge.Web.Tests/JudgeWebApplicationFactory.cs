using System.Net.Http.Json;
using ElBruno.CopilotHarness.Judge.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.CopilotHarness.Judge.Web.Tests;

public sealed class JudgeWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public JudgeWebApplicationFactory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "App_Data");
        Directory.CreateDirectory(directory);
        _dbPath = Path.Combine(directory, $"judge-tests-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Foundry:Endpoint"] = "https://unit.test",
                ["Foundry:ApiKey"] = "test-key",
                ["JudgePersistence:DatabasePath"] = _dbPath
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddScoped<IJudgeModelClient, StubJudgeModelClient>();
        });
    }

    private sealed class StubJudgeModelClient : IJudgeModelClient
    {
        public Task<JudgeModelResponse> GenerateAsync(JudgeModelExecutionRequest request, CancellationToken cancellationToken)
        {
            var responseText = $"{request.Model.ProfileName}:{request.Model.Deployment}:{request.PromptRecord.Prompt}";
            var response = new JudgeModelResponse(
                responseText,
                request.PromptRecord.Prompt.Length,
                responseText.Length / 2,
                20 + request.Model.ProfileName.Length);

            return Task.FromResult(response);
        }
    }
}
