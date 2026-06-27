# ElBruno CopilotHarness VS Code extension

This extension adds the Phase 7 VS Code experience:

- status panel
- explain routing view/action
- chat participant: `@harness`
- language model tools
- dashboard links

## Install

1. Open `src/ElBruno.CopilotHarness.VSCode` in VS Code.
2. Run `npm install`.
3. Press `F5` to launch the Extension Development Host.

## Configure

Set the base URLs if your router or admin host runs elsewhere:

- `harness.routerBaseUrl`
- `harness.adminBaseUrl`

## Use

- **Harness: Show Status Panel** opens the status webview.
- **Harness: Explain Routing** opens a prompt and shows the routing decision.
- **Harness: Open Dashboard** opens the admin dashboard.
- **Harness: Open Trace** opens a specific trace in the browser.
- In chat, mention **@harness** for quick routing explanations and links.
