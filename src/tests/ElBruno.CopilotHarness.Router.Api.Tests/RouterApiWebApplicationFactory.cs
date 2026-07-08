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
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly string? _adminApiKey;

    public RouterApiWebApplicationFactory()
        : this(null)
    {
    }

    private RouterApiWebApplicationFactory(IReadOnlyDictionary<string, string?>? configurationOverrides)
    {
        var directory = @"C:\src\ElBruno.CopilotHarness\src\ElBruno.CopilotHarness.Router.Api\App_Data";
        Directory.CreateDirectory(directory);
        _dbPath = Path.Combine(directory, $"admin-tests-{Guid.NewGuid():N}.db");
        _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        _adminApiKey = _configurationOverrides.TryGetValue("Backend:Auth:AdminApiKey", out var adminApiKey)
            ? adminApiKey
            : null;
    }

    public static RouterApiWebApplicationFactory Create(IReadOnlyDictionary<string, string?>? configurationOverrides = null) =>
        new(configurationOverrides);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(WebHostDefaults.ApplicationKey, Guid.NewGuid().ToString("N"));

        // These defaults MUST be set as host settings (UseSetting), not only via
        // ConfigureAppConfiguration: Program.cs reads Persistence:DatabasePath eagerly at
        // build time, which only sees host configuration. Setting them here guarantees each
        // test run uses an isolated database and never writes to the live admin database.
        builder.UseSetting("Foundry:Endpoint", "https://unit.test");
        builder.UseSetting("Foundry:ApiKey", "test-key");
        builder.UseSetting("Persistence:DatabasePath", _dbPath);

        foreach (var overrideEntry in _configurationOverrides)
        {
            builder.UseSetting(overrideEntry.Key, overrideEntry.Value);
        }

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Foundry:Endpoint"] = "https://unit.test",
                ["Foundry:ApiKey"] = "test-key",
                ["Persistence:DatabasePath"] = _dbPath
            };

            foreach (var overrideEntry in _configurationOverrides)
            {
                settings[overrideEntry.Key] = overrideEntry.Value;
            }

            configurationBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services
                .AddHttpClient("foundry-health")
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(CreateResponse));

            services
                .AddHttpClient("foundry-local-health")
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(CreateResponse));

            services
                .AddHttpClient<FoundryChatCompletionsClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(CreateResponse));

            services
                .AddHttpClient("model-provider")
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(CreateResponse));
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();

        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminApiKey);
        }

        return client;
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

        // Markers used by upstream-outcome tests to simulate a failing upstream provider.
        if (requestBody.Contains("force-upstream-timeout", StringComparison.OrdinalIgnoreCase))
        {
            throw new TaskCanceledException("Simulated upstream timeout.");
        }

        if (requestBody.Contains("force-upstream-error", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Simulated upstream connection failure.");
        }

        // Marker used by response-annotation tests to simulate a tool-calling (non-final) turn.
        if (requestBody.Contains("return-tool-calls", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"id\":\"chatcmpl-tc\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":3,\"total_tokens\":7}}",
                    Encoding.UTF8,
                    "application/json")
            };
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
