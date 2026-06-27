using System.Net.Http.Json;

namespace ElBruno.CopilotHarness.Admin.Web.Services;

public sealed class AdminApiClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<SetupWizardResponse> GetSetupStateAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<SetupWizardResponse>("/admin/setup/state", cancellationToken)
        ?? new SetupWizardResponse(false, "small", null);

    public async Task<SetupWizardResponse> SaveSetupAsync(SetupWizardRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/setup/wizard", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SetupWizardResponse>(cancellationToken)
            ?? new SetupWizardResponse(false, "small", null);
    }

    public async Task GenerateFirstRulesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/admin/setup/generate-first-rules", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ModelProfileDto>> GetModelsAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<ModelProfileDto>>("/admin/models", cancellationToken)
        ?? [];

    public async Task UpdateModelAsync(ModelProfileDto model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/admin/models/{model.Name}", model, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<BasicRulesDto> GetBasicRulesAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<BasicRulesDto>("/admin/rules/basic", cancellationToken)
        ?? throw new InvalidOperationException("Rules response was empty.");

    public async Task<BasicRulesDto> UpdateBasicRulesAsync(BasicRulesUpdateRequest rules, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/admin/rules/basic", rules, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BasicRulesDto>(cancellationToken)
            ?? throw new InvalidOperationException("Rules response was empty.");
    }

    public async Task<PlaygroundResponse> EvaluatePlaygroundAsync(PlaygroundRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/playground/evaluate", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlaygroundResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Playground response was empty.");
    }

    public async Task<ValidationResponse> GetValidationAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<ValidationResponse>("/admin/system/validation", cancellationToken)
        ?? new ValidationResponse([]);

    public async Task<DashboardSnapshotResponse> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<DashboardSnapshotResponse>("/admin/dashboard/snapshot", cancellationToken)
        ?? new DashboardSnapshotResponse([], [], DateTimeOffset.UtcNow);

    public async Task<OperationsStatusResponse> GetOperationsStatusAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<OperationsStatusResponse>("/admin/operations/status", cancellationToken)
        ?? new OperationsStatusResponse(
            DateTimeOffset.UtcNow,
            new OperationalSignalDto("Authentication", "Unknown", "No response was returned.", "Retry the request."),
            new OperationalSignalDto("Rate limiting", "Unknown", "No response was returned.", "Retry the request."),
            new OperationalSignalDto("Retry / backoff", "Unknown", "No response was returned.", "Retry the request."),
            new OperationalSignalDto("Background jobs", "Unknown", "No response was returned.", "Retry the request."),
            new InfrastructureStatusDto("Unknown", "Unknown", "Unknown", "Unknown"),
            []);
}
