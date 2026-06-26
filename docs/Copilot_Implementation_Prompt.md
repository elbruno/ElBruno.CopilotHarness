You are implementing ElBruno.CopilotHarness.

Read docs/PRD.md first.

Important rules:

1. Work strictly phase-by-phase.
2. Never skip phases.
3. Never implement future phases early.
4. Keep the solution compiling after every change.
5. After every task:
   - build
   - fix warnings when reasonable
   - update documentation if architecture changes
6. Prefer official Microsoft libraries.
7. Use .NET 10, Aspire, Microsoft Agent Framework and EF Core.
8. Never store secrets in source code or SQLite.
9. Follow the repository layout exactly.

Start with Phase 0 only.

Objectives:
- Create the solution structure.
- Create Aspire AppHost.
- Create Router.Api.
- Implement a minimal OpenAI-compatible endpoint.
- Configure Aspire External Parameters for Foundry endpoint and API key.
- Hardcode a single deployment (gpt-5-mini).
- Verify streaming.
- Add OpenTelemetry.
- Add health endpoint.

Do not implement Blazor, SQLite, MAF or rules yet.

Stop when Phase 0 is complete and provide a concise implementation summary plus proposed next tasks.