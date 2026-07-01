# Copilot repository instructions

When the user says **"start with the plan"** (or equivalent), you must:

1. Open and follow `docs/Copilot_Implementation_Prompt.md` exactly.
2. Treat that file as the authoritative implementation workflow for this repository.
3. Start from the first allowed phase/task in that document and do not skip ahead.

If there is any conflict, prioritize `docs/Copilot_Implementation_Prompt.md`.

## Application lifecycle — "launch / run / stop the app"

This repository **is** the Copilot Harness: a long-running BYOK router (Aspire
AppHost → `Router.Api`, `Admin.Web`, the Aspire dashboard) that Copilot itself is
configured to route through. Treat it as **background infrastructure**, not as the
"app under test".

When you receive a lifecycle request such as *"launch the app"*, *"run the app"*,
*"start the app"*, *"stop the app"*, *"kill the app"*, or *"restart the app"*:

1. **Never stop, kill, or restart the Copilot Harness itself.** Do **not** run
   `aspire stop`, kill `Router.Api`, or otherwise tear down the harness / its Aspire
   AppHost in response to these commands. Stopping the harness kills the very endpoint
   Copilot is routing through, which surfaces as `net::ERR_CONNECTION_REFUSED` on the
   next turn.
2. **"the app" means the user's application under test in the current workspace** — a
   sample/demo app the user explicitly started — **not** the harness. If the workspace
   has no such application, there is nothing for these commands to act on.
3. **If no application is currently running, "stop the app" is a no-op.** Do not start,
   stop, or restart anything. Simply reply that there is no running application to stop.
4. If it is genuinely ambiguous which application the user means, ask a brief
   clarifying question **before** running any lifecycle command — and never offer the
   Copilot Harness / Aspire AppHost as a stop target.

