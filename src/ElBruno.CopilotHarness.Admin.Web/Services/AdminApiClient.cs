using System.Net.Http.Json;

namespace ElBruno.CopilotHarness.Admin.Web.Services;

public sealed class AdminApiClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<SetupWizardResponse> GetSetupStateAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<SetupWizardResponse>("/admin/setup/state", cancellationToken)
        ?? new SetupWizardResponse(false, string.Empty, null);

    public async Task<SetupWizardResponse> SaveSetupAsync(SetupWizardRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/setup/wizard", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SetupWizardResponse>(cancellationToken)
            ?? new SetupWizardResponse(false, string.Empty, null);
    }

    // ── Demo routing footer (response annotation) toggle ─────────────────────

    public async Task<ResponseAnnotationSettingDto> GetResponseAnnotationAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<ResponseAnnotationSettingDto>("/admin/settings/response-annotation", cancellationToken)
        ?? new ResponseAnnotationSettingDto(false);

    public async Task<ResponseAnnotationSettingDto> SetResponseAnnotationAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            "/admin/settings/response-annotation",
            new ResponseAnnotationSettingDto(enabled),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ResponseAnnotationSettingDto>(cancellationToken)
            ?? new ResponseAnnotationSettingDto(enabled);
    }

    // ── Model registry (multi-provider connections) ──────────────────────────

    public async Task<IReadOnlyList<ModelConnectionDto>> GetModelsAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<ModelConnectionDto>>("/admin/models", cancellationToken)
        ?? [];

    public async Task<ModelConnectionDto> CreateModelAsync(ModelConnectionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/models", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ModelConnectionDto>(cancellationToken)
            ?? throw new InvalidOperationException("Model response was empty.");
    }

    public async Task<ModelConnectionDto> UpdateModelAsync(string id, ModelConnectionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/admin/models/{Uri.EscapeDataString(id)}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ModelConnectionDto>(cancellationToken)
            ?? throw new InvalidOperationException("Model response was empty.");
    }

    public async Task DeleteModelAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/admin/models/{Uri.EscapeDataString(id)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ModelConnectionTestResponse> TestModelAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/admin/models/{Uri.EscapeDataString(id)}/test", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ModelConnectionTestResponse>(cancellationToken)
            ?? new ModelConnectionTestResponse(false, "No response.", 0);
    }

    public async Task<ModelStatusDto?> GetModelStatusAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/admin/models/{Uri.EscapeDataString(id)}/status", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ModelStatusDto>(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    // ── Condition-based routing rules ─────────────────────────────────────────

    public async Task<IReadOnlyList<RoutingRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<RoutingRuleDto>>("/admin/rules", cancellationToken)
        ?? [];

    public async Task<RoutingRuleDto> CreateRuleAsync(RoutingRuleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/rules", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RoutingRuleDto>(cancellationToken)
            ?? throw new InvalidOperationException("Rule response was empty.");
    }

    public async Task<RoutingRuleDto> UpdateRuleAsync(int id, RoutingRuleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/admin/rules/{id}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RoutingRuleDto>(cancellationToken)
            ?? throw new InvalidOperationException("Rule response was empty.");
    }

    public async Task DeleteRuleAsync(int id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/admin/rules/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<RoutingRuleDto>> GenerateStarterRulesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/admin/rules/wizard", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<RoutingRuleDto>>(cancellationToken)
            ?? [];
    }

    public async Task<RuleTestResponse> TestRuleAsync(RuleTestRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/rules/test", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RuleTestResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Rule test response was empty.");
    }

    public async Task<RulesAnalyzerPromptResponse> GetAnalyzerPromptAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<RulesAnalyzerPromptResponse>("/admin/rules/analyzer-prompt", cancellationToken)
            ?? new RulesAnalyzerPromptResponse(false, null, 0, string.Empty);
    }

    public async Task<DefaultModelDto> GetDefaultModelAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<DefaultModelDto>("/admin/rules/default", cancellationToken)
        ?? new DefaultModelDto(string.Empty, DateTimeOffset.UtcNow);

    public async Task<DefaultModelDto> SetDefaultModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/admin/rules/default", new SetDefaultModelRequest(modelName), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DefaultModelDto>(cancellationToken)
            ?? new DefaultModelDto(modelName, DateTimeOffset.UtcNow);
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

    public async Task<RoutingFeedResponse> GetRoutingFeedAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<RoutingFeedResponse>($"/admin/telemetry/feed?limit={limit}", cancellationToken)
                ?? new RoutingFeedResponse(DateTimeOffset.UtcNow, false, []);
        }
        catch
        {
            return new RoutingFeedResponse(DateTimeOffset.UtcNow, false, []);
        }
    }

    public async Task<bool> DeleteTraceAsync(string traceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/admin/traces/{Uri.EscapeDataString(traceId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DeleteTraceResponse>(cancellationToken);
        return result?.Deleted ?? false;
    }

    public async Task<int> DeleteTracesAsync(IEnumerable<string> traceIds, CancellationToken cancellationToken = default)
    {
        var request = new DeleteTracesRequest(traceIds.ToList());
        var response = await _httpClient.PostAsJsonAsync("/admin/traces/delete", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DeleteTracesResponse>(cancellationToken);
        return result?.DeletedCount ?? 0;
    }

    public async Task<bool> ClearTracesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync("/admin/traces", cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ClearTracesResponse>(cancellationToken);
        return result?.Cleared ?? false;
    }

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

    // ── Phase 8 – Continuous Evaluation ──────────────────────────────────────

    public async Task<RecommendationsResponse> GetPendingRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<RecommendationsResponse>("/admin/recommendations/pending", cancellationToken)
                ?? new RecommendationsResponse([]);
        }
        catch
        {
            return new RecommendationsResponse([]);
        }
    }

    public async Task ApproveRecommendationAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/recommendations/decision", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TeamProfileDto>> GetTeamProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<IReadOnlyList<TeamProfileDto>>("/admin/profiles/teams", cancellationToken)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task CreateTeamProfileAsync(CreateTeamProfileRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/profiles/teams", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTeamProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/admin/profiles/teams/{Uri.EscapeDataString(name)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ProjectProfileDto>> GetProjectProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<IReadOnlyList<ProjectProfileDto>>("/admin/profiles/projects", cancellationToken)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task CreateProjectProfileAsync(CreateProjectProfileRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/admin/profiles/projects", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteProjectProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/admin/profiles/projects/{Uri.EscapeDataString(name)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<BenchmarkStatusResponse> GetBenchmarkStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BenchmarkStatusResponse>("/admin/benchmarks/status", cancellationToken)
                ?? new BenchmarkStatusResponse("Not configured", null, null, [], []);
        }
        catch
        {
            return new BenchmarkStatusResponse("Not configured", null, null, [], []);
        }
    }

    public async Task<RulesConfidenceResponse> GetRulesConfidenceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<RulesConfidenceResponse>("/admin/rules/confidence", cancellationToken)
                ?? new RulesConfidenceResponse([]);
        }
        catch
        {
            return new RulesConfidenceResponse([]);
        }
    }
}
