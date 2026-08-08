# End of Day Fill production schema compatibility package

Production has known EF migration-history drift. Fresh and normally tracked databases use EF migration `20260807044836_AddEndOfDayFillReporting`. Production must instead use this bounded PostgreSQL object-state package after the standard verified-backup and restored-copy gates.

1. Run `scripts/postgresql/preflight-end-of-day-fill-reporting.sql` read-only. Review every candidate room identity, capacity, user identity, and safe Gmail credential/scope boolean. The preflight must report exactly 42 WP candidates and 27 EBS candidates.
2. Confirm exactly one active warehouse identity for `DH`, `McDougall`, `WP`, and `EBS`. The scripts use exact normalized warehouse and Room codes only; they do not guess from room or location display names or seed every Room in a warehouse.
3. Run `scripts/postgresql/apply-end-of-day-fill-reporting-schema.sql`. It is transactional, advisory-locked, additive, and idempotent for the supported absent or complete object states.
4. Run `scripts/postgresql/verify-end-of-day-fill-reporting.sql`, then repeat apply and verify.
5. Run `dotnet CropQc.Web.dll --verify-schema=20260807044836_AddEndOfDayFillReporting` and inventory-deduction readiness against the restored copy.

The compatibility apply intentionally does **not** insert or repair `__EFMigrationsHistory`. Required state is verified by exact tables, columns, indexes, primary keys, foreign keys, checks, seeded configuration, assignments, and empty send/reservation history. It also fingerprints every Room capacity before applying and rolls back if any capacity changes.

`Rooms.EndOfDayFillReportGroupId` is the sole room-membership source; the draft join table was removed. The initial seed is an exact, reviewed 69-Room allowlist:

- WP (42): `DH-1` through `DH-22`; `MCD-01`; `MCD-3` through `MCD-16`; and `WP-4` through `WP-8` in the active `WP` warehouse.
- EBS (27): `LAMB-13` through `LAMB-17`; `EVANS-1` through `EVANS-12`; `EVANS-BACKSIDE`; `EVANS-BKT`; `EVANS-HALLWAY1`; `EVANS-HALLWAY2`; and `BM-1` through `BM-6`.

The preflight and apply fail closed if an expected Room is missing or ambiguous, a normalized Room code is duplicated in a reviewed warehouse, or the exact 42/27 resolution is not obtained. A first apply assigns only that allowlist. A repeat apply preserves the persisted Room master-data assignments rather than reseeding them, so later administrator changes remain authoritative. Runtime report validation uses the centralized facility context and persisted Room FK and does not infer membership from DH, McDougall, WP, Lamb, Evans, or BM names.

Stale send reservations are never expired automatically. At 15 minutes, normal Send remains blocked and the Master Data Admin recovery UI requires an administrator to verify the sender's Gmail Sent folder, record a reason, and choose Confirmed sent or Confirmed not sent. Confirmed sent uses the immutable attempted report and its original attempted timestamp as the best persisted sent-time evidence when Gmail does not provide a recoverable timestamp.

Reservation creation, finalization, and manual recovery share a PostgreSQL transaction advisory lock keyed to the report group, plus the modeled unique reservation and success keys. This serializes competing sends for the same group without making unrelated groups contend through broad serializable predicate locks. After reservation commit, Gmail dispatch uses an internal two-minute bound independent of browser cancellation; database finalization uses an independent bound and three retries. An unresolved result retains its Pending row and reservation for explicit recovery.
