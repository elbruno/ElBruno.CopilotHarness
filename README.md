# ElBruno.CopilotHarness

BYOK harness for GitHub Copilot built with .NET 10 and .NET Aspire.

## Phase 0 projects

- `src/ElBruno.CopilotHarness.AppHost`
- `src/ElBruno.CopilotHarness.ServiceDefaults`
- `src/ElBruno.CopilotHarness.Router.Api`

## Local run

Configure secrets (AppHost user-secrets):

```powershell
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryEndpoint https://<your-foundry-endpoint>
dotnet user-secrets --project .\src\ElBruno.CopilotHarness.AppHost set Parameters:FoundryApiKey <your-api-key>
```

Then run:

```powershell
dotnet run --project .\src\ElBruno.CopilotHarness.AppHost
```
