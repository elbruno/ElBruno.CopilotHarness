# Contributing

## Local development

- Work under `src/` for production code.
- Work under `tests/` for automated tests.
- Keep docs under `docs/`.

## Expected workflow

1. Make the smallest phase-appropriate change.
2. Build the solution.
3. Run tests.
4. Update docs when behavior or architecture changes.

## Rules

- Do not store secrets in source code or SQLite.
- Keep `README.md` as the front door.
- Keep phase docs focused on the phase they describe.

## Helpful commands

```powershell
dotnet build .\ElBruno.CopilotHarness.slnx
dotnet test .\ElBruno.CopilotHarness.slnx
```
