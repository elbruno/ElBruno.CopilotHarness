using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ElBruno.CopilotHarness.Router.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class RouterApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public RouterApiWebApplicationFactory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "App_Data");
        Directory.CreateDirectory(directory);
        _dbPath = Path.Combine(directory, $"admin-tests-{Guid.NewGuid():N}.db");
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
                ["Persistence:DatabasePath"] = _dbPath
            });
        });

        builder.ConfigureServices(services =>
        {
            services
                .AddHttpClient("foundry-health")
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(CreateResponse));

            services
                .AddHttpClient<FoundryChatCompletionsClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(CreateResponse));
        });
    }

    private static HttpResponseMessage CreateResponse(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok", Encoding.UTF8, "text/plain")
            };
        }

        var requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        if (requestBody.Contains("slow-live-check", StringComparison.OrdinalIgnoreCase))
        {
            Thread.Sleep(400);
        }

        var acceptsStream = request.Headers.Accept.Any(static header =>
            string.Equals(header.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase));

        if (acceptsStream)
        {
            var streamResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"id\":\"evt-1\"}\n\n", Encoding.UTF8, "text/event-stream")
            };
            streamResponse.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            return streamResponse;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"stubbed assistant reply\"}}],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":3,\"total_tokens\":7}}",
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
