# Future Phases

The following areas are explicitly deferred until requested.

## PostgreSQL Migration Cutover

Create and validate a provider-specific PostgreSQL migration path before using Render Postgres in production. Existing SQL Server migrations remain in place for current local development.

## Remaining Google Workspace Integrations

Google Shared Drive photo upload and Gmail user-delegated QC Summary sending are implemented. Remaining future work includes Gmail-generated attachments if needed, Google Drive archive workflows, and any broader Workspace automation beyond MVP 1 Receiving/QC.

## Admin-Reviewed Retention Archive/Delete

Build an Admin-reviewed archive/delete workflow before any retention purge is enabled. Database records are retained indefinitely by default, and photos are retained for at least 3 crop years after the current crop year. No automatic purge runs in MVP 1.

## Storage Inventory

Track inventory in storage, room state, movement, and related operational workflows.

## Room Controller Imports

Import room controller data after MVP 1 Receiving/QC is established.

## Mexico Qualification

Add Mexico qualification workflows in a later phase.

## Packout Imports

Add packout import workflows in a later phase.

## Pool Closing Imports

Add pool closing import workflows in a later phase.

## Long-Term Performance Analytics

Add historical analytics for long-term performance after operational data has been captured consistently.
