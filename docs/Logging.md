# Logging

> How to read the router's logs, what the structured **upstream** log line tells you, and
> how to filter the noise down to real errors.

The harness emits structured logs from `Router.Api` (and the other services) through the
standard .NET logging pipeline, surfaced in the **Aspire dashboard** and on the console.
Routing decisions are also persisted as traces and shown on the
[Live Routing](Live_Routing.md) page — logs are the lower-level, line-by-line view.

---

## Reading router logs

The fastest way to read logs while developing:

```bash
aspire logs router-api
```

This streams the `Router.Api` resource's stdout/stderr. Each line carries a level
(`Information`, `Warning`, `Error`), the category (logger name), and a message with
structured fields. The Aspire dashboard's **Structured logs** tab shows the same data with
per-field columns and filtering.

---

## The structured upstream log line

Every dispatch to an upstream model emits a single structured **upstream** log line after
the call completes (or fails). It mirrors the upstream-outcome fields shown on the
[Live Routing](Live_Routing.md) cards, so you can correlate a log line with a card by
`traceId`. Its fields:

| Field | Meaning |
|---|---|
| `traceId` | Correlates the log line with the routing trace and the Live Routing card |
| `selectedModel` | The model the request was dispatched to |
| `deployment` | The upstream deployment / model name |
| `upstreamStatusCode` | HTTP status the upstream returned (e.g. `200`, `400`, `500`) |
| `upstreamLatencyMs` | Round-trip time to the upstream, in milliseconds |
| `upstreamSucceeded` | `true`/`false` — whether the call succeeded |
| `upstreamError` | The upstream error body/message when the call failed |
| `requestHadTools` | `true` when the request asked the model to call tools (agentic request) |
| `toolCapabilityOverrideApplied` | `true` when the route was overridden to a tool-capable model |
| `overrideReason` | Plain-language reason for the tool-capability override |

A failed agentic request that was *not* overridden, for example, will show
`requestHadTools = true`, `toolCapabilityOverrideApplied = false`, and a non-2xx
`upstreamStatusCode` — exactly the situation described in
[Troubleshooting → Agentic / tool-calling request](Troubleshooting.md#tool-calling).

---

## Configured log levels

To keep the upstream and routing lines readable, two chatty framework categories are turned
down to `Warning` so their per-request noise doesn't bury real signal:

| Category | Level | Why |
|---|---|---|
| `Microsoft.EntityFrameworkCore` | `Warning` | Suppresses the per-query SQL `Information` logs EF Core emits for every trace read/write |
| `System.Net.Http.HttpClient` | `Warning` | Suppresses the per-request HTTP client start/stop `Information` logs for every upstream call |

Everything else uses the default level (`Information`). These levels live in the router's
`appsettings.json` under `Logging:LogLevel`; raise a category back to `Information` or
`Debug` temporarily when you need to see the raw SQL or HTTP traffic.

---

## Filtering logs for real errors

Because the upstream line is structured, you can filter `aspire logs router-api` down to the
lines that actually matter. Search for the error-ish keywords:

```bash
aspire logs router-api | Select-String -Pattern "error|exception|upstream|fail"
```

(On bash: `aspire logs router-api | grep -iE "error|exception|upstream|fail"`.)

- `error` / `exception` — surfaces `Error`-level lines and stack traces.
- `upstream` — surfaces every upstream dispatch line, so you can scan statuses/latencies.
- `fail` — catches `upstreamSucceeded=false` and other failure messages.

In the Aspire dashboard's **Structured logs** tab you can instead filter by level (`Error`)
or add a field filter such as `upstreamSucceeded = false` to jump straight to failed calls —
the same set the **Errors only** toggle shows on the [Live Routing](Live_Routing.md) page.

---

## Related docs

- [Live Routing](Live_Routing.md) — the visual, per-request view of the same upstream fields.
- [Troubleshooting](Troubleshooting.md) — symptom-driven fixes, including tool-calling failures.
- [Runbook](Runbook.md) — start, stop, inspect, and recover the harness.
