using System.Net;
using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class TraceDeletionEndpointsTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TraceDeletionEndpointsTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DeleteTrace_RemovesExisting_AndReportsMissing()
    {
        var traceId = await SeedTraceAsync("delete single trace");

        var deleteResponse = await _client.DeleteAsync($"/admin/traces/{traceId}");
        deleteResponse.EnsureSuccessStatusCode();
        var deleted = await deleteResponse.Content.ReadFromJsonAsync<DeleteTraceResponse>();

        Assert.NotNull(deleted);
        Assert.True(deleted.Deleted);

        var missingResponse = await _client.DeleteAsync($"/admin/traces/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.OK, missingResponse.StatusCode);
        var missing = await missingResponse.Content.ReadFromJsonAsync<DeleteTraceResponse>();

        Assert.NotNull(missing);
        Assert.False(missing.Deleted);
    }

    [Fact]
    public async Task BulkDeleteTraces_ReturnsCountOfExistingTraces()
    {
        var firstTraceId = await SeedTraceAsync("bulk delete trace one");
        var secondTraceId = await SeedTraceAsync("bulk delete trace two");

        var request = new BulkDeleteTracesRequest(new[] { firstTraceId, secondTraceId, Guid.NewGuid().ToString("N") });

        var response = await _client.PostAsJsonAsync("/admin/traces/delete", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BulkDeleteResponse>();

        Assert.NotNull(result);
        Assert.Equal(2, result.DeletedCount);
    }

    [Fact]
    public async Task ClearTraces_RemovesAllTraces()
    {
        await SeedTraceAsync("clear traces one");
        await SeedTraceAsync("clear traces two");

        var response = await _client.DeleteAsync("/admin/traces");
        response.EnsureSuccessStatusCode();
        var cleared = await response.Content.ReadFromJsonAsync<ClearTracesResponse>();

        Assert.NotNull(cleared);
        Assert.True(cleared.Cleared);

        var remaining = await _client.GetFromJsonAsync<LiveRequestsResponse>("/admin/telemetry/requests");
        Assert.NotNull(remaining);
        Assert.Empty(remaining.Requests);
    }

    private async Task<string> SeedTraceAsync(string prompt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "small",
                messages = new[] { new { role = "user", content = prompt } }
            })
        };
        request.Headers.TryAddWithoutValidation("x-copilot-client", "copilot-cli");

        var chatResponse = await _client.SendAsync(request);
        chatResponse.EnsureSuccessStatusCode();

        var telemetry = await _client.GetFromJsonAsync<LiveRequestsResponse>("/admin/telemetry/requests");
        Assert.NotNull(telemetry);
        Assert.NotEmpty(telemetry.Requests);

        return telemetry.Requests[0].TraceId;
    }
}
