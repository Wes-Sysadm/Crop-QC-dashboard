# Phase 1 verification record

Scope: shared inventory read extraction, immutable canonical proof, custody adapters and admin-only shadow comparisons. The user explicitly requested a full suite; relocating the existing ledger implementation also justified broad regression coverage. Existing ledger arithmetic and operational treatment gates remain unchanged.

- Solution restore: passed after rerunning with network access for NuGet (initial sandbox attempt could not access NuGet).
- Solution build: passed, zero errors; 63 existing compiler warnings (nullable references and unread parameters). No MSI was needed: QC Station/WinForms code is unchanged.
- Phase 1 tests: **40 passed, zero failed/skipped**, extracted from the final complete suite, including host parity, HTTP authorization, immutable read state, proof, custody and provider query-count tests.
- Full suite: **1,991 passed, 14 skipped, zero failed; 2,005 total**. The 14 skipped optional provider/release fixtures are listed below. This is a completed successful run, not the aborted attempt.
- Supplemental isolated PostgreSQL: existing normalization transaction tests **2/2 passed**; FruitProfile identity/audit test **1/1 passed**; canonical performance test **1/1 passed**, measuring all four scope sizes.
- Model check: no pending changes. No entities, migrations, schema or data backfill changed.
- `dotnet format ... --verify-no-changes` passed on changed C# files after applying whitespace-only fixes. `git diff --check` passed.
- The original ledger service was compared with its relocated Data implementation after namespace/owner/type substitutions: arithmetic/query code is unchanged.

## Explicit limitations and unsuccessful attempts

An expanded optional-provider full run stalled and was stopped. Its partial results are not counted as a full-suite pass. The isolated legacy receipt-date PostgreSQL theory also stalled; stopping its local test host produced an aborted run with one captured passing result. Its full provider coverage remains **unverified**; the cause was not established and no unrelated fixture changes were made. The ordinary receipt-date regression cases passed in the completed full suite. The 11 Truck Receipt historical schema/worker/adoption test methods require their own retained release fixtures; they were not recreated for this read-only phase. Two other skipped methods were subsequently exercised successfully in the isolated normalization/fruit-profile runs described above.

The first final live diagnostic retry used an incorrect local working directory and failed writing its scratch evidence file. Rerunning from the task root succeeded. Both attempts used explicit read-only database transactions. No production changes occurred.

Performance results were recaptured in an isolated test process to avoid allocation/timing contamination from the concurrent full suite. Allocated bytes are process allocation deltas, not peak retained memory; the first sample includes cold compilation. There is no arbitrary latency SLA.

No new endpoint was deployed, no operational UI smoke test of the undeployed Phase 1 code is claimed, and no fresh production restore/write rehearsal was performed. Later command/cutover phases need their own concurrency, rollback, full fresh-restore and release gates. These results do not clear the blocked live Bartlett run.

## Full-suite skipped methods

- `CropQc.Api.Tests.TruckReceiptWorkerCompatibilityTests.New_backup_manifest_reads_old_schema_while_new_web_gate_refuses_it`
- `CropQc.Api.Tests.TruckReceiptPostgresTests.Exact_multi_variety_receiving_and_reopen_conserve_every_identity`
- `CropQc.Api.Tests.TruckReceiptGrandfatheringTests.PostgreSql_creation_mode_survives_activation_pause_and_reactivation`
- `CropQc.Api.Tests.TruckReceiptAdoptionTests.Any_bad_load_aborts_the_entire_batch`
- `CropQc.Api.Tests.TruckReceiptPostgresTests.Failure_after_destination_writes_rolls_back_all_inventory_history_status_and_audit`
- `CropQc.Api.Tests.TruckReceiptSchemaContractTests.Other_deployment_or_feature_requirements_still_fail_closed`
- `CropQc.Api.Tests.FruitProfileIdentityGuardPostgreSqlTests.PostgreSql_SaveTimeReferences_Concurrency_Cosmetics_AndAuditAtomicity`
- `CropQc.Api.Tests.TruckReceiptSchemaContractTests.Both_gates_reject_incompatible_index`
- `CropQc.Api.Tests.TruckReceiptWorkerCompatibilityTests.New_backup_manifest_and_schema_gate_read_completed_feature_data`
- `CropQc.Api.Tests.TruckReceiptAdoptionTests.All_15_adopt_without_inventory_changes_then_match_and_reconcile_exactly`
- `CropQc.Api.Tests.LegacyGrowerLotReconciliationTests.PostgreSql_stale_status_reconciliation_commits_once_or_rolls_back_every_write`
- `CropQc.Api.Tests.TruckReceiptSchemaContractTests.Exact_feature_schema_passes_both_gates_in_read_only_transaction`
- `CropQc.Api.Tests.ReceiptDateBaselineTests.PostgreSql_date_edit_preserves_accounting`
- `CropQc.Api.Tests.TruckReceiptPostgresTests.Independent_context_rejects_stale_row_version`
