namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public interface IRoutingConfigurationStore
{
    Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken);
    Task<SetupWizardState> GetSetupWizardStateAsync(CancellationToken cancellationToken);
    Task<SetupWizardState> CompleteSetupWizardAsync(CompleteSetupWizardRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModelRegistryEntry>> GetModelRegistryAsync(CancellationToken cancellationToken);
    Task<ModelRegistryEntry> UpsertModelRegistryEntryAsync(string profileName, UpsertModelRegistryEntryRequest request, CancellationToken cancellationToken);
    Task<BasicRulesConfiguration> GetBasicRulesAsync(CancellationToken cancellationToken);
    Task<BasicRulesConfiguration> UpdateBasicRulesAsync(UpdateBasicRulesRequest request, CancellationToken cancellationToken);
    Task<SystemValidationResult> ValidateSystemAsync(CancellationToken cancellationToken);
}
