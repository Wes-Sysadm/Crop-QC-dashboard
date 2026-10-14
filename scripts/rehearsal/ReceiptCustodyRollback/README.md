# Receipt custody application rollback rehearsal

**NO-GO:** the forward rehearsal currently blocks at mixed-cohort dispatch.
See [the stopped-gate report](../../../docs/investigations/pr271-compensation-rollback.md).
Neither the prepared rollback phase nor final verification has passed.

This driver is not part of either application and must never run against production.
It accepts only `localhost / pr271_rollback_test`, requires an explicit
`isolated-only.marker` in its evidence directory, and uses an isolated Docker
network with no production secrets. Start from a new verified backup restore.
`forward` expects the post-CohortKey, pre-custody schema and stops on unexpected state.
It applies the three exact missing additive migration ranges, verifies original
columns/rows, then executes canonical commands on disposable new receipt origins.

Build against a frozen application's extracted DLL directory:

```sh
dotnet build ReceiptCustodyRollback.csproj -c Release \
  -p:FrozenApplicationPath=/proof/forward-app -o /proof/driver
```

Run **only the driver DLL** inside the exact application image, alongside its
unmodified application DLLs, with its own `CropQc.Web.deps.json` and runtimeconfig:

```sh
cp /proof/driver/ReceiptCustodyRollback.dll /app/ReceiptCustodyRollback.dll
cd /app
dotnet exec --depsfile CropQc.Web.deps.json \
  --runtimeconfig CropQc.Web.runtimeconfig.json \
  ReceiptCustodyRollback.dll forward /proof
```

The currently failed prerequisite prevents generation of a handover `state.json`.
Do not fabricate that file or skip to the rollback phase. `rollback` and `verify`
are prepared sequences only; they require completed preceding phases and exact
whole-database handover hashes. No old database backup is restored during handover.
Application bytes are independently identified in results.

After the failed dispatch leaves the original synthetic source intact, the
`DispatchProbe` child project diagnoses that exact failure without changing the
application. Its existing test observer captures transaction-local evidence at
`PersistedSource2`; the unchanged engine then rejects and rolls back. Build with
the same `FrozenApplicationPath` and execute its DLL beside the frozen app using
the same deps/runtimeconfig, passing `forward` or `rollback` to label evidence.
It asserts Blocked and unchanged operational tables **as a diagnostic of a defect**,
not as a successful release rehearsal or desired future business behavior.

The diagnostic records the Data/Shared hashes actually loaded, source inputs,
30-bin verified custody, precise treatment blocker and atomic rollback. No
production capability, guard override, fixture exclusion or synthetic approval
is provided by these tools. The complete prior 160-case evidence is not rerun or
replaced by this narrower release-specific experiment.
