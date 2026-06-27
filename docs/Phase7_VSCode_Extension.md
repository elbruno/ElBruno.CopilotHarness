# Phase 7 - VS Code Extension

## Scope

- Status panel
- Explain routing view/action
- Chat participant: `@harness`
- Language model tools
- Dashboard links

## What ships

- A separate VS Code extension project at `src/ElBruno.CopilotHarness.VSCode`
- A status webview with quick actions
- Routing explanation action backed by `/admin/playground/evaluate`
- Trace links backed by `/admin/traces/{traceId}`
- Dashboard links backed by `/admin/dashboard/snapshot`
- Chat participant and language model tools for reuse in chat/workflows

## Install and use

1. Open `src/ElBruno.CopilotHarness.VSCode` in VS Code.
2. Run `npm install`.
3. Press `F5` to launch the extension host.
4. Start the AppHost so the router and admin endpoints are reachable.

## Configuration

- `harness.routerBaseUrl`
- `harness.adminBaseUrl`

## Notes

- This phase stays separate from the existing Aspire apps.
- Phase 8 functionality is intentionally out of scope.
