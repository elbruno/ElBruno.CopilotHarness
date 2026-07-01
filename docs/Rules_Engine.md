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
| `SemanticMatch` | *(ignored)* | The **processor model**, reading only the typed user request, selects this rule from the natural-language descriptions of all semantic rules. See [Semantic rules](#semantic-rules). |

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

### The `<userRequest>` wrapper

GitHub Copilot Chat does not send the typed text on its own — it wraps it inside a large
envelope built from the open editor, repo context, and tool reminders. A real `hi` arrives
looking roughly like:

```
<attachments> …repository files and context… </attachments>
<context> …date, open terminals… </context>
<reminderInstructions> …how to call tools… </reminderInstructions>
<userRequest>
hi
</userRequest>
```

The harness extracts the content of the **`<userRequest>`** block (case-insensitively) as the
real user ask. If that tag is absent it strips the known wrapper blocks (`<attachments>`,
`<context>`, `<reminderInstructions>`, `<environment_details>`) and otherwise falls back to
the raw last message. This extracted text is what the semantic analyzer, the intent
classifier, and the size/keyword/regex conditions all operate on, and it is what `/live`
shows as the **User message**. The original wrapped payload is preserved as the
`request.rawUserMessage` trace fact for inspection on the Live Routing page.

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
*"route `simple-chat` to ollama llama3.1"* and *"route `code-task` to foundry gpt-5-mini"*
explicitly and editably.

**Classifier source.** The classification is a real LLM call to the processor model. If the
processor is disabled, unreachable, times out, or returns an unusable answer, the harness
falls back to a built-in **deterministic** keyword classifier so routing never blocks. The
path used (`processor-model` vs `deterministic`) is shown on the
[Live Routing](Live_Routing.md) page.

> When intent classification is unavailable for a request (e.g. the classifier is disabled),
> `IntentEquals` rules simply do not match and evaluation continues with the remaining rules.

### Seeded intent rules

In the default starter set the only `IntentEquals` rule is **Code tasks** (`conditionValue:
code-task`, priority 6), which fast-tracks detected coding requests to the cloud model before
the broader semantic sweep. The remaining intent vocabulary entries (`github-actions`,
`launch-app`, `simple-chat`) are handled by `SemanticMatch` rules that rely on natural-language
descriptions rather than a fixed label. See [Default starter rules](#default-starter-rules) for
the complete seeded set.

---

## Semantic rules

Semantic rules are the recommended way to describe routing in plain language. Instead of a
mechanical condition, each semantic rule is just:

| Field | Meaning |
|---|---|
| **Name** | A short label, e.g. *"GitHub actions"*. Shown on `/live` as the matched rule. |
| **Rule paragraph** (`description`) | A natural-language description of the kind of request this rule captures, e.g. *"Captures all GitHub related actions, for example: commit all changes, push to GitHub, open a pull request."* |
| **LLM engine** (`targetModel`) | The registry model name to route to when this rule is chosen. |
| `priority` | Lower numbers are listed first; the **last enabled** semantic rule is treated as the **catch-all**. |
| `enabled` | Disabled rules are excluded from the analyzer prompt. |

A semantic rule is stored as an ordinary rule row with `conditionType = SemanticMatch`
(`conditionValue` is unused).

### How the analyzer works

When at least one enabled `SemanticMatch` rule exists, the harness builds a single **rules
analyzer mega-prompt** that lists every semantic rule's *name* and *paragraph* and asks the
**processor model** (the local model flagged `IsProcessor`; see
[Model Registry → Processor model](Model_Registry.md#processor-model)) to choose the single
best-matching rule and return JSON:

```json
{ "rule": "GitHub actions", "confidence": 0.91, "reason": "the user asks to commit and push" }
```

Key properties:

- **User request only.** The analyzer is given **only the typed user request** — the text
  GitHub Copilot wraps in `<userRequest>…</userRequest>` (see
  [User message vs. full payload](#user-message-vs-full-payload)). The huge system preamble,
  attachments, and tool reminders are *not* sent to the analyzer, so a one-word `hi` is
  classified as `hi`, not as 1,000 characters of boilerplate.
- **Full payload to the engine.** Once a rule is chosen, the **complete** original Copilot
  payload is forwarded to that rule's LLM engine. Only the *decision* uses the trimmed text.
- **Catch-all + fallback.** If the processor model is disabled, unreachable, times out, or
  returns an unparseable answer, the harness falls back to the **last enabled** semantic rule
  (the catch-all) so routing never blocks. The chosen rule, confidence, and reason are stored
  as trace facts (`semantic.matchedRule`, `semantic.confidence`, `semantic.reason`,
  `semantic.engine`) and rendered on [Live Routing](Live_Routing.md).

### Example rule set

| Rule | Paragraph (summary) | Engine |
|---|---|---|
| Simple chat | Greetings and small talk in any language (`hi`, `hola`, `thanks`) plus lightweight web-search lookups answered with Copilot's search tools. | `ollama llama3.1` (local) |
| GitHub actions | GitHub *actions* (commit / push / open or merge PRs, branches, tags, releases, labels) **and** read-only GitHub questions (open issues, PR status, repository status). | `ollama llama3.1` (local) |
| Launch App actions | Run / build / start / **stop** / restart the workspace application under test — never the harness itself. | `ollama llama3.1` (local) |
| Dev environment actions | Start / stop / restart local backing services & containers (database, Redis, Docker, `docker compose`). | `ollama llama3.1` (local) |
| Build and test actions | Build / compile / restore, run tests, run a linter or formatter. | `ollama llama3.1` (local) |
| Quick explanations | Short factual questions — explain a line, setting, command, error or concept — no code changes. | `ollama llama3.1` (local) |
| Short translations | Translate a short phrase, comment or message between human languages. | `ollama llama3.1` (local) |
| Commit messages and summaries | Draft a commit message, changelog entry, or a brief diff/change summary. | `ollama llama3.1` (local) |
| Others actions *(catch-all)* | Everything else, including complex coding tasks. | `foundry gpt-5-mini` (cloud) |

> The exact paragraphs, priorities, and engines for every seeded rule are documented in
> [Default starter rules](#default-starter-rules).

## Default starter rules

When no rules exist, the **Rules** page (and the Setup wizard) seeds the starter set below via
`GenerateStarterRulesAsync`. A fresh install produces exactly **13 rules** — the defaults are
good to go without further customisation. Rules are a mix of `SemanticMatch` rules (priorities
5 and 100–120) and deterministic condition guards (priorities 6–30). "local" = the small Ollama
model (`ollama llama3.1`, 🖥️); "cloud" = the large model (`foundry gpt-5-mini`, ☁️). Listed in
evaluation order (lowest priority first).

| # | Priority | Name | Condition type | Condition value | Target | Purpose |
|---|---|---|---|---|---|---|
| 1 | 5 | Simple chat | `SemanticMatch` | — | local 🖥️ | Greetings, small talk, and lightweight web-search lookups. |
| 2 | 6 | Code tasks | `IntentEquals` | `code-task` | cloud ☁️ | Writing, refactoring, or debugging code fast-tracks to the cloud model. |
| 3 | 10 | Large prompts | `PromptSizeAtLeast` | `2500` | cloud ☁️ | Any user message ≥ 2 500 characters. |
| 4 | 20 | System-guided prompts | `HasSystemMessage` | — | cloud ☁️ | Requests that carry a system message. |
| 5 | 30 | Streaming requests | `IsStreaming` | — | cloud ☁️ | Streaming (`stream: true`) requests. |
| 6 | 100 | GitHub actions | `SemanticMatch` | — | local 🖥️ | Any GitHub request — repo-changing actions **and** read-only repo/issue/PR questions. |
| 7 | 110 | Launch App actions | `SemanticMatch` | — | local 🖥️ | Launch / run / build / start / **stop** / restart the workspace app under test. |
| 8 | 112 | Dev environment actions | `SemanticMatch` | — | local 🖥️ | Start / stop / restart local dev services & containers (DB, Redis, Docker). |
| 9 | 114 | Build and test actions | `SemanticMatch` | — | local 🖥️ | Build / compile / restore, run tests, run a linter or formatter. |
| 10 | 116 | Quick explanations | `SemanticMatch` | — | local 🖥️ | Short factual questions with no code changes. |
| 11 | 118 | Short translations | `SemanticMatch` | — | local 🖥️ | Translate a short phrase/comment/message between human languages. |
| 12 | 119 | Commit messages and summaries | `SemanticMatch` | — | local 🖥️ | Draft a commit message, changelog entry, or brief change summary. |
| 13 | 120 | Others actions *(catch-all)* | `SemanticMatch` | — | cloud ☁️ | Everything else, including complex coding tasks. |

The semantic rules (rows 1, 6–13) match by their **Description** paragraph — `conditionValue`
is empty and the processor model picks the best-matching rule from the natural-language
descriptions. The deterministic rules (rows 2–5) are evaluated first and short-circuit the
semantic sweep when their condition fires.

> **Why route these to the local model?** Rows 6–12 are *lightweight, low-reasoning* intents —
> lifecycle/dev commands, running existing tooling, short factual answers, translations, and
> short generated summaries. A small local model (`ollama llama3.1`) handles them well, so
> keeping them local **saves cloud tokens**. ⚠️ The saving only fully applies to **tool-free
> (Ask-mode) turns**: in Copilot **Agent mode** the request carries a large tool payload that
> exceeds `Routing:Rules:LocalToolCallingMaxPromptCharacters`, so the
> [tool-routing guard](Troubleshooting.md#tool-calling) reroutes even a local-targeted rule to
> the cloud tool-capable model. See [Troubleshooting → local routing note](Troubleshooting.md#connection-refused).

The **full description paragraph** for each semantic rule (so they can be recreated from scratch):

**1 · Simple chat** — *local, priority 5*

> Captures short, simple, conversational prompts in any language - greetings and small talk
> such as 'hi', 'hello', 'hola', 'thanks', 'gracias', 'how are you' - that do not require
> reading or changing code. Also captures lightweight information lookups that GitHub Copilot
> answers using its web search tools, for example 'search the web for ...', 'look up ...',
> 'find online ...', 'what is the latest ...', or general questions about current facts or
> documentation.

**6 · GitHub actions** — *local, priority 100*

> Captures all GitHub-related requests - both actions that change the repository (commit all
> the changes, push to GitHub, create or merge a pull request, manage branches, tags, releases
> or labels) AND read-only questions about the repository or its GitHub state (are there any
> open issues, list or check issues, pull request status, repository status, list branches,
> labels, releases, who opened an issue, what PRs are open). Any request about GitHub issues,
> pull requests, or repository status belongs here.

**7 · Launch App actions** — *local, priority 110*

> Captures all application-lifecycle actions where the user asks Copilot to launch, run,
> build, start, **stop**, restart or kill the application **under test in the current
> workspace**. This never refers to the Copilot Harness router itself — its Aspire AppHost
> and `Router.Api` are background infrastructure and must not be stopped. If no application
> is running, *"stop the app"* is a no-op.

**8 · Dev environment actions** — *local, priority 112*

> Captures requests to start, stop, restart or check the local development services and
> containers the application depends on - the database, Redis, message queues, Docker containers,
> or 'docker compose up/down'. This is about backing services and infrastructure, not the
> application under test itself (that is Launch App), and never the Copilot Harness router.

**9 · Build and test actions** — *local, priority 114*

> Captures requests to build, compile, restore or install packages, run tests or the test suite,
> run a linter, run a formatter, or check code style/formatting - developer commands that run
> existing tooling and produce deterministic output rather than writing new code.

**10 · Quick explanations** — *local, priority 116*

> Captures short, self-contained factual questions that can be answered briefly without writing
> or changing code: a quick explanation of a single line, keyword, setting, command, error
> message or concept. Longer, multi-step, or code-producing questions do not belong here (they
> are the catch-all).

**11 · Short translations** — *local, priority 118*

> Captures requests to translate a short piece of text, a phrase, a comment or a message from one
> human (natural) language to another, for example 'translate this to Spanish' or 'how do you say
> this in French'.

**12 · Commit messages and summaries** — *local, priority 119*

> Captures requests to draft a commit message, write a short changelog or release note entry, or
> produce a brief summary of a diff, a set of changes or a file - short generated text that
> summarizes existing work rather than writing new code.

**13 · Others actions** *(catch-all)* — *cloud, priority 120*

> Catch-all rule. Captures every request that does not match the other rules, including complex
> coding tasks.

> Because *Others actions* is the **last enabled** semantic rule, it also doubles as the
> fallback when the processor model is unavailable (see [How the analyzer works](#how-the-analyzer-works)).

### Intent vs. rule (why the Live view shows both)

A request carries **two independent classification signals**, and they can legitimately differ:

- **Classifier intent** — a *secondary heuristic* label (`simple-chat`, `github-actions`,
  `launch-app`, `code-task`, `long-form`) produced by the quick intent classifier from the
  first ~200 characters. It is only a guess and is **not** what routes the request.
- **Matched rule** — the rule the **local processor model** actually picks by reading the
  semantic rule paragraphs (or the deterministic fallback). This is what determines the target
  engine and is shown in the **rule** and **model** columns.

For example, the prompt *"are there any open issues in the repo?"* may get the intent guess
`github-actions` while the analyzer correctly matches a different rule (e.g. the broadened
*GitHub actions* paragraph, or *Others actions*). The matched rule wins. For that reason the
[Live Routing](Live_Routing.md) page presents the **matched rule** as the dominant signal,
shows **how** it was decided (🧠 *decided by local model* vs ⚙️ *keyword/heuristic fallback*),
and renders the intent only as a small, muted `intent:` / `intent (guess):` hint so it is never
mistaken for the chosen rule. The intent-grouped badges near the top of the page are likewise
labelled *By classifier intent (heuristic)*.

---

## Editing rules in the Admin UI

The **Rules** page is built around the routing flow it represents:

- **Rules list** — each row shows the evaluation **Order** (priority), a 🧠 *semantic* /
  ⚙️ *condition* type chip, the rule name + paragraph, the match condition, and the
  **engine badge** of its target model (🖥️ *local* for Ollama, ☁️ *cloud* otherwise, with a
  *processor* hint on the local classifier model). Toggle a rule on/off inline with the
  **State** button — no need to open the editor.
- **Editor modal** — *Add semantic rule*, *Add condition rule*, or *Edit* open a centered
  **modal dialog** (instead of a form buried at the bottom of the page). Semantic rules show
  a large *Rule paragraph* field with guidance; condition rules show the condition-type
  picker and value. The selected engine is previewed with its badge as you choose it.
- **Local analyzer prompt** — a collapsible card shows the *exact* "rules analyzer" prompt
  that is sent to the local processor model (`GET /admin/rules/analyzer-prompt`). This is the
  single mega-prompt that lists every semantic rule and asks the model to pick one, using only
  the user's typed request. Seeing it makes local routing decisions fully transparent.

---

The first-run wizard on the **Rules** page seeds exactly this starter set when no rules
exist. You can then edit the paragraphs, change engines, reorder, or add new rules.

---



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
upstream. Enter a user request (and optionally a system message, streaming flag, or
requested model). The result panel shows the full routing flow — **user request → matched
rule → engine** — plus:

- whether the decision was **semantic** or a deterministic **condition**;
- the **decision source** (`local model (LLM)` when the processor model picked the rule, or
  `keyword fallback` when the model was unavailable and the deterministic overlap matcher ran);
- the **confidence** and the model's **reason**;
- for semantic matches, a *Show the analyzer prompt used for this test* toggle that reveals
  the exact prompt the local model received.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/admin/rules/test` | Evaluate a synthetic request; returns matched rule, selected model, reason, prompt size, the cleaned user request, decision source, confidence, classification, and (for semantic matches) the analyzer prompt. |
| `GET` | `/admin/rules/analyzer-prompt` | Return the current local analyzer mega-prompt, processor model name, and semantic-rule count. |

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
