# Crop QC Email Redesign

## Scope and authority

Owner Wes authorized implementation, review, normal merge and production release in
"TASK: Crop QC Email Redesign — Implement, Validate, and Prepare Production Release",
2026-10-12, task `01a117ce-8381-7602-b396-3c44cc35402e`, attachment
`45984f48-7c4b-47f1-ae3d-3df7814b2aca`. All technical release gates remain mandatory.
This is a reporting implementation change under specification 1.1.0, QC-001,
FIELD-001, AUD-001, OPS-001 and GOV-001 / ADR-007; no foundational policy changes.

Base: `622321137cc04bfdd336cfd592914798db96c50b` (PR #278). The shared composer
serves Receiving, Door, Lot, Room, Line and Transfer sample reports, browser
previews, ordinary sends, override sends and resends. Receiptless Field Sample
reports retain their separate orchard/block and 30-day trend workflow.

## Behavior and evidence boundaries

- The complete original identification table, including all 13 existing fields,
  is now last, under Receiving Sample Details. Photos and fruit measurements are
  retained. Summary precedes the charts and fruit overview.
- Defect summary contains defect names/counts, or None. Individual fruit rows
  have empty defect/notes cells when absent; plain text omits absent labels too.
- Current and entire-lot charts count recorded fruit size observations, never
  percentages or averages of sample averages. Active commercial categories come
  from `FruitSizeConversionThresholds` for the receipt's fruit type. Valid
  intermediate categories have zero bars; invalid recorded categories remain
  visible in fruit detail but are not invented as commercial chart categories.
  Partial rows without a valid recorded size are disclosed separately.
- Average Box Size uses `18143.6948 / average(positive observed weights)` and
  ceilings to a configured commercial category. An estimate below the smallest
  count maps to that smallest category; above the largest is Undersized. No valid
  weights/categories yields NotCalculated. Recorded observations are unchanged.
- Lot identity uses the established canonical crop year, GrowerLot (or normalized
  legacy grower/lot), and FruitProfile (including variety/production identity).
  Historical receipts lacking a GrowerLot link are considered through the same
  canonical matching rules. Location is not fruit identity. Ambiguous legacy
  identity is explicitly unresolved and only the current sample is shown.
- No arbitrary latest-sample limit is used. Deleted samples/receipts, receiptless
  observations, another test-data partition, future sample/creation timestamps,
  and unmatched identities are excluded. Current sample rows are included once;
  persisted observation IDs are deduplicated, never row numbers across samples.
- The snapshot is captured before photo I/O. PNG bytes, HTML and plain text are
  generated once per composition and embedded in the existing MIME message.
  No chart URLs or JavaScript can change a sent email. A later preview or resend
  intentionally generates a new snapshot. Existing Gmail messages and audit logs
  are not rewritten. Send/status/audit behavior and recipients are unchanged.

The PNG renderer uses the existing ImageSharp dependency, a deterministic numeric
bitmap alphabet, explicit HTML axis labels and descriptive image alternative text.
It requires no platform fonts, drawing server or new package. Charts share the
existing 12 MB inline-image budget; photo orientation/resize/fallback logic remains.

## Validation and release plan

Focused regression areas: section order and field preservation; defect counts;
actual observation counts/order/zero categories; commercial ceiling/boundaries;
partial and invalid readings; cross-receipt lot aggregation and identity isolation;
deduplication; immutable previously composed PNGs; conditional defect/notes;
PNG decode and actual Gmail MIME construction. Query tests prove no tracked changes
or inventory writes. Inventory writers, treatment writers, receipt transactions,
QC capture and email transport are outside the implementation diff.

No schema, migration, configuration, package or QC Station installer change.
The live predecessor was independently checked on 2026-10-12: `6223211`, deployment
`dep-db5ru959fdbs73dtfe1g`, auto-deploy off and maintenance off. Docker Linux/amd64
29.4.3 is available; the earlier engine-startup blocker is not present.

Required release evidence remains: exact reviewed/merged SHA and successful CI;
Linux/PostgreSQL affected report rehearsal; fresh independently verified production
backup; compatible predecessor application rollback; read-only readiness and
protected QC/email history fingerprints; exact-SHA deployment; authenticated
production report generation and final health/readiness. No synthetic production
movement or email send, separate RED reconstruction, or historical data repair is
included. A production report preview can verify generated content without sending
an unsolicited email to configured recipients.

Restore reports pre-existing ImageSharp 3.1.12 NuGet security advisories. No package
version is changed here; the new renderer creates images from numeric observations.
Visual browser/MIME checks are not a claim of exhaustive Outlook/Gmail client testing.

Safe before/after previews and final release evidence are linked in the PR.
