using ElBruno.CopilotHarness.Router.Api.Admin;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api;

public static class RoutingTraceResponseMapper
{
    public static RoutingTraceResponse ToResponse(RoutingExecutionTrace trace) =>
        new(
            trace.TraceId,
            trace.CreatedAtUtc,
            trace.WorkflowEngine,
            new ClassificationTraceDto(
                trace.Classification.Intent,
                trace.Classification.Complexity,
                trace.Classification.Confidence,
                trace.Classification.Reasoning),
            new RuleAdvisorTraceDto(
                trace.RuleAdvisor.SuggestedProfile,
                trace.RuleAdvisor.Rationale),
            new RoutingDecisionTraceDto(
                trace.Decision.ProfileName,
                trace.Decision.Profile.Deployment,
                trace.Decision.Reason),
            trace.Context.Select(contextFact => new RoutingTraceContextFactDto(contextFact.Key, contextFact.Value)).ToList(),
            trace.Steps.Select(step => new RoutingWorkflowStepDto(step.Name, step.Outcome)).ToList());
}
