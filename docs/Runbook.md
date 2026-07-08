# Runbook

## Persistence modes

The harness supports two modes. Choose based on your needs:

| | **No-Docker (default)** | **Docker (production-like)** |
|---|---|---|
| Persistence | SQLite (`App_Data\copilotharness-admin.db`) | PostgreSQL container |
| Cache | In-memory | Redis container |
| Prerequisites | .NET 10 SDK, Aspire CLI | + Docker Desktop or Podman |
| How to enable | Default — nothing to set | Set `UseContainers=true` in `src/AppHost/appsettings.json` |
| Data shared across restarts | ✅ SQLite file persists | ✅ Docker volume persists |

## Start local environment

### No-Docker (default)

```powershell
aspire run
```

### Docker mode

1. Start Docker Desktop (or Podman).
2. Edit `src/harness/ElBruno.CopilotHarness.AppHost/appsettings.json` — set `"UseContainers": "true"`.
3. Run:

```powershell
aspire run
```

Aspire will pull and start `postgres` and `redis` containers automatically.

## Stop local environment

Stop the AppHost process from the terminal or IDE. Docker containers (if running) stop with it.

## Reset local admin data

**SQLite mode** — delete the file:

```powershell
Remove-Item .\src\ElBruno.CopilotHarness.Router.Api\App_Data\copilotharness-admin.db
```

Deleting the database recreates the seeded model registry (example Ollama + Foundry
connections), starter rules, and setup state on next start.

**Docker mode** — remove the named Docker volumes:

```powershell
docker volume rm copilotharness-postgres-data copilotharness-redis-data
```

## Manage models and rules

- Add/edit/test model connections on the Admin **Models** page (or via
  `/admin/models*`). API keys are encrypted at rest with ASP.NET Core Data Protection.
- Manage routing rules and the default model on the Admin **Rules** page (or via
  `/admin/rules*`). Use **Generate starter rules** for a first set, and the **Test** panel
  to dry-run routing.
- **Data Protection key ring**: encrypted API keys are tied to the local Data Protection
  key ring. If it is lost or rotated, re-enter affected model API keys. See
  [Troubleshooting](Troubleshooting.md).

## Validate

```powershell
dotnet test .\ElBruno.CopilotHarness.slnx
```

Tests always run against SQLite in-process — no Docker needed.

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
