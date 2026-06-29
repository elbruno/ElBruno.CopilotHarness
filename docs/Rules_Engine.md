# Rules Engine

> The Rules Engine decides **which registered model** handles each incoming request.
> Rules are ordered, condition-based, and reference models from the
> [Model Registry](Model_Registry.md) by name.

---

## Concept

Routing is driven by an ordered list of **condition-based rules**. For each incoming
chat-completions request the router evaluates enabled rules in **priority order** (lowest
`priority` number first) and selects the target model of the **first matching rule**. If
no rule matches, the request falls back to the **default model**; if that is unavailable,
the first enabled model is used.

An explicit `model` named by the client always wins over the rules (the harness honors a
caller that pins a specific model).

### Evaluation order

1. **Explicit requested model** — if the request names a model that exists in the
   registry, use it. Reason: *"Explicit model profile requested by client."*
2. **Rule set** — evaluate enabled rules by ascending priority; first match wins.
   Reason: *"Matched rule '{name}'."*
3. **Default model** — Reason: *"Default model profile."*
4. **First enabled model** — last-resort fallback.

> If a matching rule targets a model that no longer exists, evaluation continues to the
> next rule rather than failing.

---

## Rule shape

| Field | Description |
|---|---|
| `id` | Stable integer identifier. |
| `name` | Human-friendly rule name (shown in test results as the matched rule). |
| `description` | Optional free text. |
| `conditionType` | One of the condition types below. |
| `conditionValue` | Value the condition compares against (size, keyword, regex, model name). |
| `targetModel` | Registry model **name** to route to when the condition matches. |
| `priority` | Lower numbers evaluate first. |
| `enabled` | Disabled rules are skipped entirely. |

---

## Condition types

| Condition | `conditionValue` | Matches when… |
|---|---|---|
| `Always` | *(ignored)* | Always — useful as a catch-all at the lowest priority. |
| `PromptSizeAtLeast` | integer | Total prompt character count ≥ the value. |
| `IsStreaming` | *(ignored)* | The request is a streaming request (`stream: true`). |
| `HasSystemMessage` | *(ignored)* | The request contains a `system` message. |
| `RequestedModelEquals` | model name | The client-requested model equals the value (case-insensitive). |
| `PromptContainsKeyword` | keyword | Any prompt text contains the keyword (case-insensitive). |
| `PromptMatchesRegex` | regex | Any prompt text matches the regular expression. Regex evaluation is bounded by a 250 ms timeout; an invalid or timing-out pattern simply does not match. |

---

## Default model

The default model is the fallback target when no rule matches. Manage it via:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/admin/rules/default` | Read the current default model name. |
| `PUT` | `/admin/rules/default` | Set the default model (must be a registry model name). |

In the Admin UI the default is chosen from a dropdown of registry models on the
**Models** and **Setup** pages.

---

## First-run wizard

When no rules exist yet, the **Rules** page offers a *Generate starter rules* action
(the same logic runs as part of the Setup wizard when *Generate first rules* is enabled).
This creates a sensible starter set that references the seeded models — for example, a
big-prompt rule, a streaming rule, and a catch-all default — so the harness routes
intelligently from the start. You can then edit, reorder, disable, or delete them.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/admin/rules/wizard` | Generate the starter rule set. |
| `POST` | `/admin/setup/generate-first-rules` | Same generation, exposed through Setup. |

---

## Testing a rule set

The **Test** panel on the Rules page lets you dry-run a request without sending it
upstream. Enter a prompt (and optionally a system message, streaming flag, or requested
model) and the engine reports **which rule matched** and **which model** would be
selected, plus the routing reason.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/admin/rules/test` | Evaluate a synthetic request; returns matched rule, selected model, reason, and prompt size. |

The general-purpose `/admin/playground/evaluate` endpoint performs the same dry-run
routing and additionally returns the fully routed request body.

---

## CRUD endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/admin/rules` | List all rules. |
| `GET` | `/admin/rules/{id}` | Get one rule. |
| `POST` | `/admin/rules` | Create a rule. |
| `PUT` | `/admin/rules/{id}` | Update a rule. |
| `DELETE` | `/admin/rules/{id}` | Delete a rule. |

See [API Reference](API_Reference.md) for request/response DTOs.

---

## Related docs

- [Model Registry](Model_Registry.md) — the models rules route to.
- [Phase 3 — Harness Intelligence](Phase3_Harness_Intelligence.md) — routing agents and workflow.
- [Phase 8 — Continuous Evaluation](Phase8_Continuous_Evaluation.md) — confidence scoring for rules.
- [API Reference](API_Reference.md) — full endpoint contracts.
