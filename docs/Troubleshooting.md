# Troubleshooting

## Build or test fails on SQLite warnings

The current dependency graph emits `NU1903` for `SQLitePCLRaw.lib.e_sqlite3`.
This is a known dependency advisory warning in the current package set and does not block the build.

## Foundry secrets missing

If the AppHost asks for Foundry values, start it with `aspire run` and provide `FoundryEndpoint` and `FoundryApiKey` as Aspire external parameters or environment variables.

## Running with PostgreSQL + Redis (optional)

The harness defaults to SQLite — no Docker required. To use PostgreSQL and Redis instead (production-like setup), set `UseContainers=true` in `appsettings.json` of the AppHost or as an environment variable before running.

## Router returns 4xx for malformed input

The router uses OpenAI-style error envelopes for invalid JSON or unsupported request payloads. Check the request body shape and endpoint.

## Admin dashboard shows no activity

- Confirm the AppHost is running.
- Send a routed request through `Router.Api`.
- Refresh the dashboard after a few seconds.

## Model connectivity issues

Use **Test connection** on the Models page (or `POST /admin/models/{id}/test`) to probe a
model. Common failures:

- **Ollama not reachable** — confirm the Ollama server is running and the endpoint is
  correct (default `http://localhost:11434`). The provider calls
  `{endpoint}/v1/chat/completions`; ensure the model is pulled (`ollama pull llama3.2`).
- **Azure key/deployment errors** — verify the endpoint, the **deployment name** (not the
  base model name), the API version, and that the API key is set. A `401`/`403` means the
  key is missing or wrong; a `404` usually means the deployment name is incorrect.
- **Azure `404 Resource not found` with a doubled path** — the Azure endpoint should be the
  resource root, e.g. `https://<resource>.openai.azure.com`. The router appends
  `openai/deployments/{deployment}/chat/completions` itself, so a trailing `/openai` or
  `/openai/v1` segment is automatically stripped to avoid a doubled
  `/openai/v1/openai/deployments/...` path. Any of these forms work:
  `https://<resource>.openai.azure.com`, `.../openai`, or `.../openai/v1`.
- **Azure model falls back to shared Foundry config** — if a model's endpoint or key is
  blank, the Azure provider uses the shared `Foundry:Endpoint` / `Foundry:ApiKey`. Set the
  per-model values to override.

## Stored API keys stop working after a reset

API keys are encrypted with ASP.NET Core Data Protection. If the Data Protection key ring
is deleted or rotated (e.g. a clean machine or container without persisted keys), existing
encrypted keys can no longer be decrypted and routing treats them as empty. Re-enter the
affected model API keys on the Models page.

## Database file not found

The default admin database is created under `App_Data\copilotharness-admin.db` after first run. If needed, override `Persistence__DatabasePath`.
