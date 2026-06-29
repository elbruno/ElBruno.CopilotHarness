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

0. **Intent classification** — the processor model (or deterministic fallback) labels the
   request intent so `IntentEquals` rules can match. See [Intent classification](#intent-classification).
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
| `PromptSizeAtLeast` | integer | The **user message** character count ≥ the value. See [User message vs. full payload](#user-message-vs-full-payload). |
| `IsStreaming` | *(ignored)* | The request is a streaming request (`stream: true`). |
| `HasSystemMessage` | *(ignored)* | The request contains a `system` message. |
| `RequestedModelEquals` | model name | The client-requested model equals the value (case-insensitive). |
| `PromptContainsKeyword` | keyword | The **user message** contains the keyword (case-insensitive). |
| `PromptMatchesRegex` | regex | The **user message** matches the regular expression. Regex evaluation is bounded by a 250 ms timeout; an invalid or timing-out pattern simply does not match. |
| `IntentEquals` | intent label | The **processor model's** classification of the prompt equals the value (case-insensitive). See [Intent classification](#intent-classification). |

---

## User message vs. full payload

GitHub Copilot prepends a large boilerplate **system preamble** (e.g. *"You are an
expert AI programming assistant…"*) to **every** request. If size/keyword/regex
conditions looked at the whole payload, every Copilot request would look "large" and
route to the cloud model — even a one-word `hi`.

To avoid this, `PromptSizeAtLeast`, `PromptContainsKeyword`, and `PromptMatchesRegex`
evaluate the **last user message** (the actual turn the caller typed), not the system
preamble or prior conversation turns. The classifier and the `/live` preview use the
same user-message text. The full payload size is still surfaced for visibility (see
[Live Routing](Live_Routing.md)) but is not used for routing decisions.

---

## Intent classification

Before the rules are evaluated, the harness asks the **processor model** (see
[Model Registry → Processor model](Model_Registry.md#processor-model)) to classify the
request from its first ~200 characters into a fixed intent vocabulary:

| Intent | Typical request |
|---|---|
| `simple-chat` | Short conversational prompts (`hi`, `thanks`). |
| `github-actions` | Git/GitHub operations (`push to gh`, `open a PR`). |
| `launch-app` | Running/starting the app (`launch the app`, `dotnet run`). |
| `code-task` | Deeper code work (refactors, multi-file edits, debugging). |
| `long-form` | Large prompts / long-form generation. |

The chosen intent is exposed to the rules via the `IntentEquals` condition, so you can say
*"route `simple-chat` to ollama llama3.2"* and *"route `code-task` to foundry gpt-5-mini"*
explicitly and editably.

**Classifier source.** The classification is a real LLM call to the processor model. If the
processor is disabled, unreachable, times out, or returns an unusable answer, the harness
falls back to a built-in **deterministic** keyword classifier so routing never blocks. The
path used (`processor-model` vs `deterministic`) is shown on the
[Live Routing](Live_Routing.md) page.

> When intent classification is unavailable for a request (e.g. the classifier is disabled),
> `IntentEquals` rules simply do not match and evaluation continues with the remaining rules.

### Seeded intent rules

The starter rule set (and the first-run wizard) seeds intent rules that mirror the
vocabulary above:

| Rule | Condition | Target |
|---|---|---|
| Simple chat | `IntentEquals simple-chat` | `ollama llama3.2` |
| GitHub actions | `IntentEquals github-actions` | `ollama llama3.2` |
| Launch app | `IntentEquals launch-app` | `ollama llama3.2` |
| Code tasks | `IntentEquals code-task` | `foundry gpt-5-mini` |
| Large prompts | `PromptSizeAtLeast` | `foundry gpt-5-mini` |

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
