# Model Registry

> The Model Registry is the catalog of **LLM connections** the harness can route to.
> Each entry is a concrete, callable model endpoint — not an abstract role.

---

## Concept

Earlier versions of the harness exposed three fixed routing **profiles** (`small`,
`big`, `local`) that all shared a single Azure AI Foundry endpoint and API key. That
model conflated *routing intent* with *connection details* and could only ever talk to
one upstream service.

The Model Registry replaces that with a **flat collection of model connections**. Each
connection is an independent, fully-described endpoint:

| Field | Description |
|---|---|
| `id` | Stable identifier (GUID string). Generated on create. |
| `name` | Unique, human-friendly name (e.g. `ollama llama3.2`, `foundry gpt-5-mini`). **Rules and the default selector reference models by this name.** |
| `type` | Provider type: `ollama` or `azure-openai`. |
| `endpoint` | Base URL of the provider. For Ollama, the server URL (e.g. `http://localhost:11434`). For Azure OpenAI, the resource endpoint. When empty for Azure, the shared Foundry endpoint is used. |
| `modelName` | The upstream model identifier — the Ollama model name **or** the Azure deployment name. |
| `apiVersion` | Azure API version (Azure only; ignored for Ollama). |
| `apiKey` | API key, **encrypted at rest** (Azure only; Ollama needs none). Never returned to the UI in plaintext. |
| `enabled` | Whether the connection is eligible for routing. |

The registry is the single source of truth for "which models exist." Routing rules and
the default-model selector simply point at registry entries by `name`.

---

## Provider types

### Ollama (`type: ollama`)

- Targets the OpenAI-compatible endpoint Ollama exposes at
  `{endpoint}/v1/chat/completions`.
- No API key required.
- `modelName` is the Ollama model tag (e.g. `llama3.2`).
- Typical `endpoint`: `http://localhost:11434`.

### Azure OpenAI / Azure AI Foundry (`type: azure-openai`)

- Targets `{endpoint}/openai/deployments/{modelName}/chat/completions?api-version={apiVersion}`.
- The `endpoint` should be the resource root (e.g. `https://<resource>.openai.azure.com`).
  A trailing `/openai` or `/openai/v1` segment is automatically stripped before the
  deployments path is appended, so those forms work too.
- Requires an `apiKey` (sent as the `api-key` header) unless it falls back to the shared
  Foundry configuration.
- `modelName` is the **deployment name** (e.g. `gpt-5-mini`, `gpt-5.5`).
- `apiVersion` defaults to `2024-10-21`.

---

## API-key encryption

API keys are encrypted at rest using **ASP.NET Core Data Protection**
(`DataProtectionApiKeyProtector`, purpose string
`ElBruno.CopilotHarness.ModelRegistry.ApiKey.v1`):

- **On write** — a non-empty key is encrypted before being persisted to SQLite.
- **On read for routing** — the key is decrypted just-in-time when a request is dispatched.
- **On read for the UI** — the plaintext key is **never** returned. DTOs expose only a
  `hasApiKey` boolean so the UI can show whether a key is configured.
- If the Data Protection key ring is rotated or lost, stored keys can no longer be
  decrypted; `Unprotect` fails closed (returns empty) rather than crashing routing — you
  must re-enter affected keys.

### Upsert semantics for `apiKey`

| `apiKey` value sent | Effect on stored key |
|---|---|
| `null` (omitted) | **Unchanged** — keeps the existing key. |
| `""` (empty string) | **Cleared** — removes the stored key. |
| non-empty | **Replaced** — encrypts and stores the new key. |

---

## CRUD + test flow

All endpoints live under `/admin/models` (see [API Reference](API_Reference.md)):

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/admin/models` | List all connections (no plaintext keys). |
| `GET` | `/admin/models/{id}` | Get one connection. |
| `POST` | `/admin/models` | Create a connection. |
| `PUT` | `/admin/models/{id}` | Update a connection. |
| `DELETE` | `/admin/models/{id}` | Delete a connection. |
| `POST` | `/admin/models/{id}/test` | Connectivity probe — sends a minimal chat completion and reports success + latency. |

In the Admin UI (**Models** page), the form is **type-aware**: choosing *Azure OpenAI*
shows deployment, API version, and API key; choosing *Ollama* shows only the model name.
A **Test connection** button calls the probe endpoint and shows the result inline.

---

## Seeded examples

A fresh database is seeded with two example connections so routing works out of the box:

| Name | Type | Endpoint | Model / Deployment |
|---|---|---|---|
| `ollama llama3.2` | `ollama` | `http://localhost:11434` | `llama3.2` |
| `foundry gpt-5-mini` | `azure-openai` | *(shared Foundry endpoint)* | `gpt-5-mini` |

`foundry gpt-5-mini` is also the seeded **default model**.

> Adapt the seeded entries to your environment: point the Ollama entry at your local
> server and the Foundry entry at your Azure deployment (and add its key).

---

## Related docs

- [Rules Engine](Rules_Engine.md) — how requests are matched to a registry model.
- [Architecture](Architecture.md) — provider abstraction and dispatch pipeline.
- [API Reference](API_Reference.md) — full endpoint contracts.
- [Troubleshooting](Troubleshooting.md) — provider connectivity issues.
