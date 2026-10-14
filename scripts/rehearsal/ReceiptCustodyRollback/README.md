# Receipt custody application rollback rehearsal

Current evidence: [immutable transit treatment repair](../../../docs/investigations/pr271-immutable-transit-treatment.md).
Forward, rollback, authenticated HTTP and independent verification phases passed.
**NO-GO:** subsequent strict release readiness failed at the legacy partial-custody
topology predicate. It was not changed or waived. Keep the PR draft.

This driver is not part of either application and must never run against production.
It accepts only `localhost / pr271_treatment_rollback_test`, requires an explicit
`isolated-only.marker` in its evidence directory, and runs on an internal Docker
network without production endpoints or supplied external credentials. Start with
a fresh verified backup restore; never restore a backup during application handover.

Build against a frozen application's extracted DLL directory:

```sh
dotnet build ReceiptCustodyRollback.csproj -c Release \
  -p:FrozenApplicationPath=/proof/forward-app -o /proof/driver
```

Run only the driver DLL beside the frozen application's unmodified binaries, using
that application's own deps/runtimeconfig. Never copy rebuilt Data/Shared DLLs over it:

```sh
cp /proof/driver/ReceiptCustodyRollback.dll /app/ReceiptCustodyRollback.dll
cd /app
dotnet exec --depsfile CropQc.Web.deps.json \
  --runtimeconfig CropQc.Web.runtimeconfig.json \
  ReceiptCustodyRollback.dll forward /proof
```

Sequence, stopping at every failure:

1. `forward` in the frozen forward image. Requires CohortKey/pre-custody schema,
   applies only the three missing bounded custody ranges and hashes all original
   columns/rows. Records origins, receipt treatment, later untreated stock,
   dispatch, partial acknowledgement, placement, compensation and corrected origins.
2. `rollback` in the frozen compensation-aware image, on the same database. Checks
   the entire row-state handover, cross-version replay, independent-session race,
   compensation, mixed partial movement, historical correction and continued writes.
3. `http` in that same rollback image while its Web host runs on localhost:8080
   in the same isolated network. Configure host DataProtection keys to `/proof/keys`,
   canonical commands and TruckReceipt reconciliation on, automatic seeding/schema
   mutation off, external email provider None. The driver creates a disposable-host-only
   protected cookie using the restored active owner's account; no production login
   or session is used, and cookie bytes are never reported. Real authenticated forms
   prove placement, untouched reversal, antiforgery and replay. Stop the host afterward.
4. `verify` in the frozen forward image. Verifies rollback/HTTP state independently,
   without mutations. Every result records the actually loaded application hashes.
5. Run the frozen application's strict `--verify-release-readiness` read-only
   command. Its current topology failure is a mandatory stop, not a PASS.

`state.json` is generated only by preceding successful phases; do not fabricate it
or skip dependencies. The driver writes incremental command results before asserting
success, so a stopped gate retains its exact boundary.

The older `DispatchProbe` remains a **failure diagnostic** for the two original
packages and the original `pr271_rollback_test` database. It asserts the old
Blocked outcome and unchanged operational rows. It is not a success criterion
for the corrected packages. The original record is preserved in
[the prior report](../../../docs/investigations/pr271-compensation-rollback.md).

No production capability override, readiness exception or synthetic approval is
provided. Driver/source archives and exact app images are separate artifacts.
