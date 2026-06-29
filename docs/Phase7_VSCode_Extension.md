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
- A **Live Routing** webview (`Harness: Show Live Routing`) backed by `/admin/telemetry/feed`, showing prompt → model → rule → explanation, with click-through to traces
- A status-bar item that shows the most recently routed model (click to open Live Routing)
- Routing explanation action backed by `/admin/playground/evaluate`
- Trace links backed by `/admin/traces/{traceId}`
- Dashboard links backed by `/admin/dashboard/snapshot`
- Chat participant and language model tools for reuse in chat/workflows

> The default `harness.routerBaseUrl` / `harness.adminBaseUrl` is `http://localhost:5117`
> (the router serves both `/v1/*` and `/admin/*`). Prompt previews in Live Routing require
> `Telemetry:CapturePromptText=true` on the router — see [Live Routing](Live_Routing.md).

## Install and use

1. Open `src/ElBruno.CopilotHarness.VSCode` in VS Code.
2. Run `npm install`.
3. Press `F5` to launch the extension host.
4. Start the AppHost so the router and admin endpoints are reachable.

## Configuration

- `harness.routerBaseUrl` (default `http://localhost:5117`)
- `harness.adminBaseUrl` (default `http://localhost:5117`)
- `harness.adminApiKey` — Bearer token for the router's `/admin/*` endpoints. **Required when the router is started with `Backend:Auth:AdminApiKey` set**, which the local Aspire AppHost does (default `Harness123`). Without it, the Status panel, Explain Routing, Live Routing, and trace views return **401 Unauthorized**. Leave empty only if the router has no admin key.

## Notes

- This phase stays separate from the existing Aspire apps.
- Phase 8 functionality is intentionally out of scope.
