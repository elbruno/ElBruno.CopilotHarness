# Phase 5 - AI Judge

## What is included

- Separate Judge Web app
- Historical prompt replay
- Multi-model benchmark orchestration
- AI Judge scoring across PRD dimensions
- Evaluation reports
- Manual benchmark flow

## Judge routes

- Judge app root: open `judge-web` from the Aspire dashboard
- `GET /`
- `GET /judge/prompt-records`
- `POST /judge/prompt-records/import`
- `GET /replay`
- `GET /benchmarks`
- `GET /reports`
- `GET /manual`
- `POST /judge/benchmarks/manual`
- `GET /judge/benchmarks/runs`
- `GET /judge/reports/{runId}`

## Scoring dimensions

- Correctness
- Completeness
- Security
- Best practices
- Cost
- Latency
- Tokens

## Notes

- Cost is part of the score, but never the only recommendation signal.
- The Judge app stays independent from the Router API.
- Phase 6+ backend, auth, and background-job work is still out of scope.
