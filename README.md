# ElBruno.CopilotHarness

BYOK harness for GitHub Copilot built with .NET 10 and .NET Aspire.

## What it does

- Runs an Aspire AppHost for local orchestration
- Exposes an OpenAI-compatible Router API
- Ships Admin and Judge web apps
- Uses EF Core persistence, OpenTelemetry, and Aspire external parameters
- Targets GitHub Copilot BYOK-style clients with a single `gpt-5-mini` deployment

## Fast start

### Prerequisites

- .NET 10 SDK
- Aspire CLI (`aspire`)
- GitHub Copilot access in the client you want to point at the router

### Run the system

```powershell
aspire run
```

### Configure BYOK

1. Start the AppHost with `aspire run`.
2. Open the Aspire dashboard and copy the Router.Api OpenAI-compatible endpoint.
3. Point your GitHub Copilot BYOK client at that endpoint.
4. Use the `gpt-5-mini` deployment name.
5. Keep the upstream Foundry endpoint and API key in Aspire external parameters.

### Configure Aspire parameters

Provide these values through Aspire external parameters or environment variables:

- `FoundryEndpoint`
- `FoundryApiKey`
- `AdminApiKey` (optional)

## Project layout

- `src/ElBruno.CopilotHarness.AppHost`
- `src/ElBruno.CopilotHarness.ServiceDefaults`
- `src/ElBruno.CopilotHarness.Router.Core`
- `src/ElBruno.CopilotHarness.Router.Api`
- `src/ElBruno.CopilotHarness.Admin.Web`
- `src/ElBruno.CopilotHarness.Judge.Web`
- `src/ElBruno.CopilotHarness.Evaluation.Worker`
- `src/ElBruno.CopilotHarness.VSCode`

## Docs

- `docs/Current_Progress.md`
- `docs/User_Manual.md`
- `docs/Docs_Index.md`
- `docs/Architecture.md`
- `docs/API_Reference.md`
- `docs/Troubleshooting.md`
- `docs/Runbook.md`

## Validate

```powershell
dotnet test .\ElBruno.CopilotHarness.slnx
```
