# End of Day Fill production schema compatibility package

Production has known EF migration-history drift. Fresh and normally tracked databases use EF migration `20260807044836_AddEndOfDayFillReporting`. Production must instead use this bounded PostgreSQL object-state package after the standard verified-backup and restored-copy gates.

1. Run `scripts/postgresql/preflight-end-of-day-fill-reporting.sql` read-only. Review every candidate room identity, capacity, user identity, and safe Gmail credential/scope boolean.
2. Confirm exactly one active warehouse identity for `DH`, `McDougall`, and `EBS`. The scripts use exact normalized warehouse codes only; they do not guess from room or location display names.
3. Run `scripts/postgresql/apply-end-of-day-fill-reporting-schema.sql`. It is transactional, advisory-locked, additive, and idempotent for the supported absent or complete object states.
4. Run `scripts/postgresql/verify-end-of-day-fill-reporting.sql`, then repeat apply and verify.
5. Run `dotnet CropQc.Web.dll --verify-schema=20260807044836_AddEndOfDayFillReporting` and inventory-deduction readiness against the restored copy.

The compatibility apply intentionally does **not** insert or repair `__EFMigrationsHistory`. Required state is verified by exact tables, columns, indexes, primary keys, foreign keys, checks, seeded configuration, assignments, and empty send/reservation history.

Initial membership is explicit after apply: active rooms from exact active `DH` and `McDougall` warehouses enter the WP group; active rooms from exact active `EBS` enter the EBS group. Runtime report validation uses the centralized facility context and persisted membership and does not recognize DH, McDougall, Lamb, Evans, or BM itself.

Stale send reservations are never expired automatically. At 15 minutes, normal Send remains blocked and the Master Data Admin recovery UI requires an administrator to verify the sender's Gmail Sent folder, record a reason, and choose Confirmed sent or Confirmed not sent. Confirmed sent uses the immutable attempted report and its original attempted timestamp as the best persisted sent-time evidence when Gmail does not provide a recoverable timestamp.

Reservation creation, finalization, and manual recovery share a PostgreSQL transaction advisory lock keyed to the report group, plus the modeled unique reservation and success keys. This serializes competing sends for the same group without making unrelated groups contend through broad serializable predicate locks. After reservation commit, Gmail dispatch uses an internal two-minute bound independent of browser cancellation; database finalization uses an independent bound and three retries. An unresolved result retains its Pending row and reservation for explicit recovery.
