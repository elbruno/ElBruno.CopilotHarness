# Troubleshooting

## Build or test fails on SQLite warnings

The current dependency graph emits `NU1903` for `SQLitePCLRaw.lib.e_sqlite3`.
This is a known dependency advisory warning in the current package set and does not block the build.

## Foundry secrets missing

If the AppHost asks for Foundry values, start it with `aspire run` and provide `FoundryEndpoint` and `FoundryApiKey` as Aspire external parameters or environment variables.

## Router returns 4xx for malformed input

The router uses OpenAI-style error envelopes for invalid JSON or unsupported request payloads. Check the request body shape and endpoint.

## Admin dashboard shows no activity

- Confirm the AppHost is running.
- Send a routed request through `Router.Api`.
- Refresh the dashboard after a few seconds.

## Database file not found

The default admin database is created under `App_Data\copilotharness-admin.db` after first run. If needed, override `Persistence__DatabasePath`.
