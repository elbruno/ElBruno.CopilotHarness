# Phase 4 Usage Analytics Web

The harness now has a separate C# analytics website for usage telemetry.

## What it shows

- Overall token totals
- Per-proxy / per-model breakdown
- Estimated USD for cloud rows
- Token-only labels for Ollama and Foundry Local
- Freshness, loading, empty, and error states
- Pricing catalog snapshot from the pricing endpoint

## Important note

This site is now wired into the **proxies AppHost** and starts with the proxy
orchestration. It is also still runnable directly for standalone development.

## How to run locally

Set the telemetry API base URL and start the project directly:

```powershell
cd src\proxies
aspire start
```

Standalone:

```powershell
dotnet run --project src\harness\ElBruno.CopilotHarness.Analytics.Web
```

Optional configuration:

- `TelemetryApi:BaseUrl` — Router.Api base URL
- `TelemetryApi:ApiKey` — admin API key if the router requires it

## Navigation

- Root path: `/`
- Alias: `/usage-analytics`
- Aspire AppHost port: `http://localhost:5103`
- Health endpoint: `/health/analytics`

## Operator guidance

USD values are estimates only. Cloud rows can show estimated USD when pricing cards are available; Ollama and Foundry Local remain token-only.
