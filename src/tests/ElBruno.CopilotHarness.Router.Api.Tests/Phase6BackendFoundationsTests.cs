using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ElBruno.CopilotHarness.Router.Api.BackgroundJobs;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Api.RateLimiting;
using ElBruno.CopilotHarness.Router.Api.Security;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class Phase6BackendFoundationsTests
{
    [Fact]
    public void AdminAuthenticationValidator_RequiresMatchingBearerToken()
    {
        Assert.True(ApiKeyAuthenticationValidator.IsValidBearerToken("Bearer admin-test-key", "admin-test-key"));
        Assert.False(ApiKeyAuthenticationValidator.IsValidBearerToken(null, "admin-test-key"));
        Assert.False(ApiKeyAuthenticationValidator.IsValidBearerToken("Bearer wrong", "admin-test-key"));
    }

    [Fact]
    public async Task RateLimiting_CreatesPerClientPartitions()
    {
        var limiter = BackendRateLimiter.CreateGlobalLimiter(1, TimeSpan.FromMinutes(1));
        var context = new DefaultHttpContext();
        context.Request.Headers["x-copilot-client"] = "copilot-cli";

        using var firstLease = await limiter.AcquireAsync(context);
        using var secondLease = await limiter.AcquireAsync(context);

        Assert.True(firstLease.IsAcquired);
        Assert.False(secondLease.IsAcquired);
    }

    [Fact]
    public void PersistenceOptions_UsePostgreSqlConnectionString_WhenRequested()
    {
        var options = new PersistenceOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = "Host=localhost;Database=copilotharness;Username=postgres;Password=test"
        };

        var connectionString = options.BuildConnectionString(@"C:\src\ElBruno.CopilotHarness");

        Assert.Equal("Host=localhost;Database=copilotharness;Username=postgres;Password=test", connectionString);
    }

    [Fact]
    public async Task BackgroundJobQueue_ProcessesQueuedJob()
    {
        var queue = new ChannelBackgroundJobQueue();
        var processor = new QueuedBackgroundJobProcessor(queue, new TestScopeFactory(), NullLogger<QueuedBackgroundJobProcessor>.Instance);

        var ran = false;
        await queue.EnqueueAsync((_, _) =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        await processor.RunOnceAsync(CancellationToken.None);

        Assert.True(ran);
    }

    private sealed class TestScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestScope();

        private sealed class TestScope : IServiceScope
        {
            private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

            public IServiceProvider ServiceProvider => _serviceProvider;

            public void Dispose() => _serviceProvider.Dispose();
        }
    }

    private sealed class AdminAuthRouterApiWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = @"C:\src\ElBruno.CopilotHarness\src\ElBruno.CopilotHarness.Router.Api\App_Data\admin-auth-tests.db";

        public AdminAuthRouterApiWebApplicationFactory()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
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
                    ["Persistence:DatabasePath"] = _dbPath,
                    ["Backend:Auth:AdminApiKey"] = "admin-test-key"
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
    }

    private sealed class RateLimitedRouterApiWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = @"C:\src\ElBruno.CopilotHarness\src\ElBruno.CopilotHarness.Router.Api\App_Data\rate-limit-tests.db";

        public RateLimitedRouterApiWebApplicationFactory()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
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
                    ["Persistence:DatabasePath"] = _dbPath,
                    ["Backend:RateLimiting:PermitLimit"] = "1",
                    ["Backend:RateLimiting:WindowSeconds"] = "60"
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
    }

    private static HttpResponseMessage CreateResponse(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get)
        {
            var content = new StringContent("ok", Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
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
            var streamContent = new StringContent("data: {\"id\":\"evt-1\"}\n\n", Encoding.UTF8);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            var streamResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = streamContent
            };
            streamResponse.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            return streamResponse;
        }

        var jsonContent = new StringContent(
            "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"stubbed assistant reply\"}}],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":3,\"total_tokens\":7}}",
            Encoding.UTF8);
        jsonContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = jsonContent
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
