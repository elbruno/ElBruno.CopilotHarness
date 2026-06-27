# Contributing

## Local development

- Work under `src/` for production code.
- Work under `tests/` for automated tests.
- Keep docs under `docs/`.

## Project context

Before diving in, it helps to read the [PRD](PRD.md) — it defines Phases 0–8 and the goals behind each one. Understanding the phase structure makes it much easier to know where a change belongs.

## Running the full stack

The full harness uses .NET Aspire to orchestrate multiple services. Run everything with:

```powershell
aspire run
```

**No Docker required by default.** The harness uses SQLite for persistence out of the box. Docker is only needed if you want PostgreSQL + Redis for a production-like setup — set `"UseContainers": "true"` in `src/ElBruno.CopilotHarness.AppHost/appsettings.json` to opt in.

See [Runbook](Runbook.md) for a side-by-side comparison of both modes.

## Running just the router (no Docker)

The Router API can run standalone without Docker or the full Aspire stack. It uses a local SQLite database by default (see `src/ElBruno.CopilotHarness.Router.Api/appsettings.json` — `Persistence.DatabasePath`):

```powershell
cd src\ElBruno.CopilotHarness.Router.Api
dotnet run
```

You'll need to supply `FoundryEndpoint` and `FoundryApiKey` as environment variables:

```powershell
$env:Foundry__Endpoint = "https://your-foundry.openai.azure.com/"
$env:Foundry__ApiKey   = "your-api-key-here"
```

The router will be available at `https://localhost:7xxx/v1` — point your Copilot BYOK endpoint there.

## Expected workflow

1. Make the smallest phase-appropriate change.
2. Build the solution: `dotnet build .\ElBruno.CopilotHarness.slnx`
3. Run tests: `dotnet test .\ElBruno.CopilotHarness.slnx`
4. Update docs when behaviour or architecture changes.

## Rules

- Do not store secrets in source code or SQLite.
- Keep `README.md` as the front door.
- Keep phase docs focused on the phase they describe.

## Helpful commands

```powershell
aspire run                                     # start the full stack
dotnet build .\ElBruno.CopilotHarness.slnx    # build only
dotnet test  .\ElBruno.CopilotHarness.slnx    # run all tests
```

## Good first issue

Looking for somewhere to start? Here are low-risk, high-value contributions:

- **Add a screenshot** — run `aspire run`, navigate to any dashboard page, save a PNG to `docs/images/`, open a PR. No code required.
- **Fix a typo or clarify a doc** — any file in `docs/` is fair game.
- **Add a routing rule** — the rules engine in the Router API is data-driven; add a new rule in `appsettings.json` and document it in `docs/API_Reference.md`.
- **Write a test for an untested edge case** — look for `// TODO: test` comments in `tests/`.

