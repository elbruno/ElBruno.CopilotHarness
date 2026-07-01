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
- Startup applies schema initialization and then seeds:
  - model registry (example connections: `ollama llama3.1`, `foundry gpt-5-mini`)
  - condition-based routing rules
  - setup wizard state (default model = `foundry gpt-5-mini`)

## Notes

- Setup Wizard does not ask for model API keys; keys are managed per model on the Models page and encrypted at rest.
- Routing Playground / rule test evaluates deterministic routing decisions without introducing Phase 3+ intelligence.
- Admin data is seeded as a multi-provider model registry (Ollama + Azure OpenAI/Foundry) on startup. See [Model Registry](Model_Registry.md) and [Rules Engine](Rules_Engine.md).
