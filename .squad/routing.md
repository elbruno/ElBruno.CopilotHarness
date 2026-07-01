# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, scope, sequencing | Morpheus | Phase decomposition, ADRs, dependency planning |
| Router API and backend services | Neo | ASP.NET endpoints, routing core boundaries, persistence adapters |
| Admin web and UX | Trinity | Blazor wizard, model registry UI, rules editor, playground |
| AI policy and model intelligence | Niobe | Routing policies, explainability data, MAF workflows |
| GitHub Copilot integrations and BYOK spec validation | Oracle | Copilot client integration checks, OpenAI/BYOK compatibility drift checks |
| Aspire app topology and platform wiring | Dozer | AppHost resources, parameters/secrets wiring, Aspire diagnostics/deployment paths |
| Developer experience, README, docs UX, visual assets | Switch | README rewrites, onboarding flow, screenshots, badges, issue templates |
| DevRel, storytelling, promotion, pitches, social, slides/animations, generated imagery, docs coherence | Seraph | Blog posts, 5-min pitch, LinkedIn/X copy, HTML slide decks & explainer animations, t2i images, narrative accuracy review |
| Testing and quality gates | Link | Unit/integration tests, acceptance criteria, regression coverage |
| Isolated issue execution (bugfixes/docs/tests) | @copilot 🤖 | Async issue implementation with PR creation |
| Code review | Morpheus | Review PRs, check quality, suggest improvements |
| Testing | Link | Write tests, find edge cases, verify fixes |
| Scope & priorities | Morpheus | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Morpheus |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.
