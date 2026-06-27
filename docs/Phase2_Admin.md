# Phase 2 Admin UI

The Phase 2 admin surface is implemented in `src/ElBruno.CopilotHarness.Admin.Web` and backed by admin endpoints in `Router.Api`.

## What is included

- Setup Wizard (`/setup`)
- Model Registry (`/models`)
- Rules Editor (`/rules`)
- Playground (`/playground`)
- System Validation (`/validation`)
- “Generate my first rules” action (from Setup Wizard and Rules Editor)

## Data/storage

- SQLite via EF Core in `src/ElBruno.CopilotHarness.Router.Core` (`HarnessDbContext`).
- Local-first DB file (default):
  - `Persistence:DatabasePath = App_Data\copilotharness-admin.db`
- Override with environment variable:
  - `Persistence__DatabasePath`
- Startup applies EF Core migrations and then seeds:
  - model registry (`local`, `small`, `big`)
  - basic rules
  - setup wizard state

## Notes

- Setup Wizard does not ask for secrets.
- Routing Playground evaluates deterministic routing decisions without introducing Phase 3+ intelligence.
- Admin data is seeded from current router profile design (`local`, `small`, `big`) on startup.
