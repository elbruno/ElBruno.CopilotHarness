# Runbook

## Start local environment

```powershell
aspire run
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

## Inspect Phase 6 operations

- Open the Admin **Operations** page at `/operations`.
- Review auth, rate limiting, retry/backoff, and background job readiness.
- Check the infrastructure card for current storage/caching assumptions.
- Open the Judge root page (`/`) to confirm benchmark storage and run status.
