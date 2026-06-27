# Runbook

## Start local environment

```powershell
dotnet run --project .\src\ElBruno.CopilotHarness.AppHost
```

This starts the Router, Admin, and Judge apps together.

## Stop local environment

Stop the AppHost process from the terminal or IDE.

## Reset local admin data

Delete the local SQLite file if you want a fresh seed:

```powershell
Remove-Item .\App_Data\copilotharness-admin.db
```

## Validate

```powershell
dotnet test .\ElBruno.CopilotHarness.slnx
```

## Inspect telemetry

- Open the Admin dashboard.
- Review the connected clients and live requests sections.
- Inspect trace details from `/admin/traces/{traceId}`.
- Open the Judge app to replay historical prompts and review evaluation reports.
