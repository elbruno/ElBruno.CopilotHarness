namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public interface IRoutingConfigurationStore
{
    Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken);
    Task<SetupWizardState> GetSetupWizardStateAsync(CancellationToken cancellationToken);
    Task<SetupWizardState> CompleteSetupWizardAsync(CompleteSetupWizardRequest request, CancellationToken cancellationToken);

    // Model registry (multi-provider connections)
    Task<IReadOnlyList<ModelConnectionRecord>> GetModelsAsync(CancellationToken cancellationToken);
    Task<ModelConnectionRecord?> GetModelAsync(string id, CancellationToken cancellationToken);
    Task<ModelConnectionRecord> UpsertModelAsync(string? id, UpsertModelConnectionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteModelAsync(string id, CancellationToken cancellationToken);
    Task<ModelProfileOptions?> ResolveModelConnectionAsync(string id, CancellationToken cancellationToken);

    // Condition-based routing rules
    Task<IReadOnlyList<RoutingRuleRecord>> GetRulesAsync(CancellationToken cancellationToken);
    Task<RoutingRuleRecord?> GetRuleAsync(int id, CancellationToken cancellationToken);
    Task<RoutingRuleRecord> CreateRuleAsync(UpsertRoutingRuleRequest request, CancellationToken cancellationToken);
    Task<RoutingRuleRecord?> UpdateRuleAsync(int id, UpsertRoutingRuleRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoutingRuleRecord>> GenerateStarterRulesAsync(CancellationToken cancellationToken);
    Task<RoutingDefaultModel> GetDefaultModelAsync(CancellationToken cancellationToken);
    Task<RoutingDefaultModel> SetDefaultModelAsync(string modelName, CancellationToken cancellationToken);

    // Legacy basic rules (retained for Phase 8 confidence + advisor back-compat)
    Task<BasicRulesConfiguration> GetBasicRulesAsync(CancellationToken cancellationToken);
    Task<BasicRulesConfiguration> UpdateBasicRulesAsync(UpdateBasicRulesRequest request, CancellationToken cancellationToken);

    Task<SystemValidationResult> ValidateSystemAsync(CancellationToken cancellationToken);
}
