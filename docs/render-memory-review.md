# Render memory review

This review covers the projection, actual-run reconciliation, uploaded-report parsing,
and Excel export paths introduced or exercised by the packout workflow. It does not
attribute the Render restart to a specific request because the service alert did not
include a managed-memory dump or route-level allocation trace.

## Existing high-risk operations

- `POST /BinsRun/Projections/{id}/Packout` previously allowed a 210 MB multipart
  body, copied every file into a `MemoryStream`, copied it again with `ToArray`, and
  wrote another copy for OCR. PDF fallback rendered every page before processing
  any page.
- `GET /BinsRun/Packout/{id}/Download` and finalization loaded several collection
  navigations in one EF query and built a row collection plus a complete worksheet
  XML string before compressing the workbook.
- the orchard-contact XLSX import copied the upload into memory, copied it again to
  a byte array, and materialized every worksheet row before validation.
- QC email MIME construction still creates a complete encoded Gmail message in
  memory. Existing photo-byte limits bound inline images, but a large combination
  of HTML, inline images, and attachments remains a potential short-lived peak.
- production backup package creation contains several in-memory archive/copy paths.
  It normally runs in the separate Render cron service, but a manually invoked
  backup inside the web service would be a memory risk.

## Applied bounds

The `PackoutProcessing` configuration section supplies centralized defaults:

- 20 MB per file
- 50 MB total multipart body
- 10 files per upload
- 25 PDF pages
- 40,000,000 pixels per image
- 25,000 spreadsheet rows
- 25,000 parsed detail rows
- 50,000 generated workbook rows

Invalid or excessive inputs are rejected before parsing. Original uploads are
streamed to uniquely named temporary files, processed sequentially, and deleted in
`finally` blocks. PDF OCR renders, processes, and removes one page at a time.
Spreadsheet readers enumerate rows and stop at the configured limit.

Read-only projection and reconciliation queries use no-tracking where applicable,
split collection queries to avoid cartesian result growth, and apply source-search
filters and a hard query bound before materialization. Per-run in-process operation
leases reject duplicate upload, finalization, or workbook jobs caused by repeated
clicks. The leases do not replace database concurrency tokens and are intentionally
not a distributed lock across multiple application instances.

Structured logs record elapsed time, working-set delta, uploaded/output bytes, file
and page counts, parsed/spreadsheet/workbook rows, samples, receipts, and run
components. They do not contain file contents, credentials, or email bodies.

## Remaining risks

- Gmail's API requires a base64url-encoded RFC 2822 message. The current sender
  builds that bounded message in memory; moving it to a streaming upload would be a
  separate Gmail-transport change.
- Workbook download and Gmail attachment contracts still require the final,
  compressed XLSX as one byte array. Worksheet creation is now streamed and row
  bounded, so there is no second full worksheet representation.
- the operation coordinator is process-local. A future multi-instance deployment
  would need a database-backed idempotency claim, not an unbounded in-memory lock
  table.
- OCR tools are external processes whose native peak memory is visible in container
  working set but not managed-GC allocation counters.
- backup packaging warrants a separate bounded-streaming review if backups are ever
  moved from the dedicated cron service into the production web process.
