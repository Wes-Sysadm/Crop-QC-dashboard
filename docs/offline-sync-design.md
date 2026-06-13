# Offline Capture And Sync Design

## Direction

Reliable offline work needs an offline-capable local client. The live web site requires an internet connection for normal saves. Browser-only offline hacks are not safe enough for production QC data because photos, pressure rows, sample edits, and conflict handling need durable local state.

The recommended path is to extend Crop QC Station, or a small local companion app, with an offline queue backed by SQLite. QC Station already owns local FTA pressure capture, station identity, and the station config, so it is the right boundary for offline capture.

## Local Data Store

The local store should keep:

- station id/code and environment kind (`Production` or `Staging`)
- operator/user identity when known
- sample id and receipt id when the sample already exists
- client-generated change id / idempotency key for every queued change
- fruit row values: pressure, weight, grade, starch, defects, notes, row number
- photo file references and pending upload metadata
- timestamps captured locally and last attempted sync time
- sync state: `Pending Sync`, `Synced`, `Sync Failed`, `Conflict`
- server version/updated timestamp used for conflict detection

Local pending data must not be deleted until the server confirms successful sync.

## Sync Behavior

When online, QC Station should send queued changes to the live API and mark each item synced only after success. When offline, the app should keep accepting local entries and show pending sync status clearly.

Required behavior:

- create/update fruit rows offline
- queue pressure rows and manual row edits
- queue photo uploads without changing the original photo file
- retry failed uploads safely
- preserve local data until confirmed by the server
- show sample-level and row-level sync status
- prevent staging/test station configs from syncing into production

## API Support Needed

Future API work should add:

- idempotency key support for QC Station saves
- client-generated change IDs
- versioned DTOs for offline sync
- upsert pressure row and fruit row endpoints
- photo upload retry endpoint
- sync status endpoint
- conflict response when server data changed while the station was offline

## Conflict Rules

The server remains the source of truth after sync. If a user changed the same sample/row on the web while QC Station was offline, QC Station must not overwrite silently.

Suggested conflict flow:

1. Server returns conflict with current server values and the local attempted change.
2. QC Station marks the item `Conflict`.
3. Operator/admin reviews and chooses server value, local value, or a manual merged correction.
4. The resolved change is submitted with a new idempotency key.

## Safety

- Never delete local pending data until the server confirms it synced.
- Never sync fake/staging data into production.
- Station config must include the environment kind/base URL so the operator can see whether they are on Production or Staging.
- Offline mode should not change FTA low-level capture behavior.
- Until offline sync is implemented, operators should verify dashboard connection before relying on online saves.

## Phased Implementation

1. Add versioned sync DTOs and idempotency support to existing QC Station pressure save paths.
2. Add a local SQLite queue in QC Station for pressures and row edits.
3. Add queued photo metadata and pending photo upload support.
4. Add visible sync status and conflict review screens.
5. Add server-side sync status and conflict endpoints.
