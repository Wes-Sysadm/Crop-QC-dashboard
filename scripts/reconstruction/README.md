# Reconstruction maintenance JSON transport

PowerShell 7.4+ and the existing .NET maintenance binary are required. These scripts
replace the JSON transport in the untracked PR #275 release wrapper; they do not
authorize a release, select a database, fetch credentials, relax a server guard,
or replace the existing production configuration/backup/readiness procedure.

The failed wrapper used `Get-Content -Raw | ConvertFrom-Json` on a preview and then
included the resulting PowerShell object in `ConvertTo-Json`. PowerShell inferred
`System.DateTime` values and re-emitted UTC timestamps with the local offset.
The application's `SamePreview` compares its complete serialized typed preview,
excluding only `SnapshotCapturedAt`. Equivalent instants are not interchangeable
serialized approval evidence. The rejection protected production correctly.

## Supported transport

`Invoke-ReconstructionMaintenance.ps1` runs only the existing preview, approve,
execute, verify or full-readiness command. It uses `ProcessStartInfo.ArgumentList`
and copies stdout/stderr byte streams separately. Standard console diagnostics
are routed to stderr through the child-only logging setting. It preserves the
real exit status and refuses to overwrite existing evidence files. Review errors
and stop on any nonzero exit; never salvage a JSON-looking suffix from mixed logs.

`Write-ReconstructionRequest.ps1` validates strict UTF-8 object JSON, then copies
the **entire original preview text** into an envelope. JSON DOM inspection never
converts strings into dates or numbers into floating point values. The composer
serializes no preview property, never sorts an array/property, and never replaces
escapes or numeric spellings. Whitespace inside and outside the preview object
is also preserved. The output is UTF-8 without a BOM and is created once.

Metadata must contain only the named fields in the existing approval or execution
record. It cannot contain `preview`, unexpected names, duplicate names or case
variants. These syntax checks do not grant eligibility: the unchanged application
remains responsible for actors, backup, explicit approval, exact intent, stale
state, locks, protected history, treatment and custody.

## Separate invocation steps

Use a reviewed directory of immutable operation artifacts, the frozen binary and
the existing approved environment. The following is an invocation pattern, not a
production execution instruction. For local rehearsal the environment must name
an isolated localhost `pr271_release_*` database and canonical mode must be on.
Only that environment may use `DisposableRestore`; the application enforces it.

```powershell
$tools = './scripts/reconstruction'
# BinaryPath, target.json and operator-owned metadata come from the release plan.
& "$tools/Invoke-ReconstructionMaintenance.ps1" -Mode Preview -BinaryPath $binary `
    -RequestFile "$evidence/target.json" -OutputFile "$evidence/preview.json" `
    -ErrorFile "$evidence/preview.stderr"
if ($LASTEXITCODE -ne 0) { throw 'Preview rejected; stop.' }

& "$tools/Write-ReconstructionRequest.ps1" -Mode Approve `
    -PreviewFile "$evidence/preview.json" -MetadataFile "$evidence/approval-metadata.json" `
    -OutputFile "$evidence/approval.json"
& "$tools/Invoke-ReconstructionMaintenance.ps1" -Mode Approve -BinaryPath $binary `
    -RequestFile "$evidence/approval.json" -OutputFile "$evidence/approval-result.json" `
    -ErrorFile "$evidence/approval.stderr" -Confirmation DisposableRestore
if ($LASTEXITCODE -ne 0) { throw 'Approval rejected; stop.' }

# execution-metadata.json binds the returned approvalAuditId and approved operator/key.
& "$tools/Write-ReconstructionRequest.ps1" -Mode Execute `
    -PreviewFile "$evidence/preview.json" -MetadataFile "$evidence/execution-metadata.json" `
    -OutputFile "$evidence/execution.json"
& "$tools/Invoke-ReconstructionMaintenance.ps1" -Mode Execute -BinaryPath $binary `
    -RequestFile "$evidence/execution.json" -OutputFile "$evidence/result.json" `
    -ErrorFile "$evidence/execution.stderr" -Confirmation DisposableRestore
if ($LASTEXITCODE -ne 0) { throw 'Execution rejected or uncertain; stop and inspect persisted intent.' }
```

Keep the same preview **file** for approval and execution, not a deserialized copy.
Only independently owned envelope metadata may be assembled with `ConvertTo-Json`.
Extract an audit ID with `JsonDocument` / `GetInt64()` if needed; do not transport a
whole application object through PowerShell type inference. Verification uses
`-Mode Verify` and the existing execution request. An authorized replay uses the
identical execution file/key with distinct output paths. Never regenerate its
preview, invent a new key to retry an uncertain result, or edit a bound request.

After each committed pool, the next approval needs a fresh preview: protected
fingerprints can change as approved reconstruction journals/projections accumulate.
Do not reuse the stopped production request artifacts or the old lossy wrapper.
Integrate this reviewed transport into the approved release configuration wrapper
while retaining its workspace/SHA/maintenance/backup checks. This tooling change
does not resume that release or make those deployment checks optional.

## Focused validation

```powershell
node --test scripts/reconstruction/protocol.test.mjs
python scripts/reconstruction/test-restored-protocol.py --binary $binary `
    --database pr271_release_example --pg-bin $pgBin --port $localPort `
    --output $newEvidenceDirectory --manifest $frozenBinaryManifest
```

The first command is offline and database-free. It compares the actual PowerShell
envelopes byte-for-byte for UTC/offset/fractional dates, numeric lexemes, nulls,
strings, arrays, ordering, Unicode and escaping, plus malformed/ambiguous input
and overwrite rejection. CI runs it on Windows PowerShell **7**, not 5.1.

The second is an explicit, opt-in **backup #189 fixture**. It refuses non-local
database naming, validates all frozen assembly hashes against the #275 manifest,
and has no Render/credential discovery. Its six historical cases are test fixtures,
not special cases in production transport. It exercises the actual PowerShell
scripts and exact merged CLI for every approval/execution/verification/replay.
It also rejects modified and stale previews and an incompatible lock held by a
second PostgreSQL session. It asserts 26 protected group hashes, six approval and
six reconstruction audits, 1,817 conserved authoritative bins, and passing full
readiness. Use a clean established restore; it does not reset or repair an already
used database. Evidence output must be a new directory.

Raw restored previews/requests can contain private business data. Keep them in
restricted local release evidence, not Git or public CI artifacts. Commit only
the summarized investigation/results approved for repository documentation.
