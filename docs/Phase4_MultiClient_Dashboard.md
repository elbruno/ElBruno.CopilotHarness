# Phase 4 Multi-client Dashboard

Phase 4 adds runtime telemetry for multi-client usage and surfaces it in the Admin Web dashboard.

## Scope implemented

- Client detection for routed requests:
  - VS Code
  - Copilot CLI
  - Copilot App
- Connected client + live request telemetry from in-memory runtime activity.
- Admin API telemetry endpoint:
  - `GET /admin/dashboard/snapshot`
- Admin Web dashboard updates on `/` with auto-refresh (2s).

## Dashboard usage

1. Start the AppHost and open `admin-web`.
2. Navigate to `/` (Dashboard).
3. Review:
   - **Connected clients** cards for active requests, recent requests (5m), and last seen.
   - **Live requests** table for endpoint, requested/selected profile, stream flag, elapsed time, and trace id.

## Notes

- Telemetry is phase-appropriate and operational (no Phase 5+ evaluation features).
- Data uses additive request activity tracking and existing routed request metadata.
