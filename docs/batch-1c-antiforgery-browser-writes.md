# Batch 1C — Browser antiforgery and station heartbeat

## Status and scope

Development only, based on main `90c129f4bca77b6b6df8b259532d2974e79a1e0d`.
No migration, production requests, business-data repair, merge, or deployment.
**Blocked: the deleted-projection GET audit and shared canonical-grower seeding on read paths below require a scope decision.**

The affected shared dependency is MVC unsafe-method filtering. Tests therefore cover
HTTP workflows across browser controllers, rather than unrelated inventory arithmetic.
Existing history, quantities, identity, authorization policies and storage semantics remain unchanged.
Only station heartbeat telemetry moves to a dedicated write endpoint.

## Enforcement architecture

`AutoValidateAntiforgeryTokenAttribute` is registered globally in MVC options.
Missing/invalid evidence on unsafe browser methods returns normal HTTP 400.
Existing action-level validators remain valid. No cookie SameSite/Secure changes.
Authorization remains independent, including in-action Master Data checks.
A browser login POST is also protected (login CSRF); it is included in category A
even though the initiating visitor may be anonymous.

The architectural test inspects actual MVC descriptors, verifies global enforcement,
and requires the exact two-action exemption set. A second test enumerates all explicit
unsafe browser routes and sends missing/invalid tokens with real authentication cookies,
requiring 400 and no SaveChanges calls. New mapped browser writes are automatically tested.
The conventional Home Index/Error actions allow unrestricted verbs but are read-only;
they inherit the same global unsafe-method filter.

## Complete unsafe endpoint inventory

Counts distinguish verb/route pairs (aliases count separately) from controller methods:
**161 explicit unsafe routes: 157 browser routes and 4 machine routes.**
Additionally, two conventional actions (Home.Index and Home.Error) permit unsafe
methods without explicit verb attributes; both inherit global validation.
No Razor Pages, unsafe minimal APIs, extra application parts, or other hosted API
controllers were found in CropQc.Web. Five minimal health endpoints are GET-only.

The separate CropQc.Api executable is not referenced/discovered by CropQc.Web.
Its legacy security model is explicitly outside Batch 1C (Batch 1D); it was not
blanket-exempted or represented as a secured browser host.

A = browser/form or browser AJAX, normal user-cookie authorization (or protected login).
B = explicit independent station-code/API-key authentication. No unresolved unsafe
authentication classifications remain. JSON responses are not considered machine auth.

| Controller | Method | Route | Category |
| --- | --- | --- | --- |
| Admin | POST | `/Admin/VarietyColors/Save` | A |
| Admin | POST | `/Admin/VarietyColors/Reset` | A |
| Admin | POST | `/Admin/DataCleanup/Execute` | A |
| Admin | POST | `/Admin/QcStations/Create` | A |
| Admin | POST | `/Admin/QcStations/Update` | A |
| Admin | POST | `/Admin/QcStations/Deactivate` | A |
| Admin | POST | `/Admin/QcStations/Reactivate` | A |
| Admin | POST | `/Admin/QcStations/RotateKey` | A |
| Admin | POST | `/Admin/QcStations/DownloadConfig` | A |
| Admin | POST | `/Admin/Users/Add` | A |
| Admin | POST | `/Admin/Users/Update` | A |
| Admin | POST | `/Admin/Users/Employment` | A |
| Admin | POST | `/Admin/Users/Roles/Create` | A |
| Admin | POST | `/Admin/Users/Roles/Update` | A |
| Admin | POST | `/Admin/Users/Roles/Delete` | A |
| Admin | POST | `/Admin/Users/Roles/Matrix` | A |
| Auth | POST | `/Login/Google` | A |
| Auth | POST | `/Logout` | A |
| Backups | POST | `/Admin/Backups/RunNow` | A |
| Backups | POST | `/Admin/Backups/Settings` | A |
| Backups | POST | `/Admin/Backups/TestAccess` | A |
| Backups | POST | `/Admin/Backups/Notifications/{id:long}/Retry` | A |
| BinsRun | POST | `/BinsRun/Projections` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Header` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Sources` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Sources/{sourceId:long}` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Packout/ApplyAll` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/PackPlan/Preview` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/PackPlan/Apply` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Sources/{sourceId:long}/Refresh` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Sources/{sourceId:long}/Remove` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Ready` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Cancel` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Duplicate` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/CreateInventory` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Delete` | A |
| BinsRun | POST | `/BinsRun/Projections/{id:long}/Packout` | A |
| BinsRun | POST | `/BinsRun/ActualRuns/{id:long}/Packout` | A |
| BinsRun | POST | `/BinsRun/Packout/{id:long}/Line` | A |
| BinsRun | POST | `/BinsRun/Packout/{id:long}/SecondaryOutputs` | A |
| BinsRun | POST | `/BinsRun/Packout/{id:long}/Finalize` | A |
| BinsRun | POST | `/BinsRun/Packout/{id:long}/Reopen` | A |
| BinsRun | POST | `/BinsRun/Packout/{id:long}/Delete` | A |
| BinsRun | POST | `/BinsRun/Packout/PackCodes` | A |
| BinsRun | POST | `/BinsRun/Packout/Configuration` | A |
| BinsRun | POST | `/BinsRun/Projection` | A |
| BinsRun | POST | `/BinsRun/Create` | A |
| BinsRun | POST | `/BinsRun/ActualRuns` | A |
| BinsRun | POST | `/BinsRun/ActualRuns/{id:long}` | A |
| BinsRun | POST | `/BinsRun/ActualRuns/{id:long}/Cancel` | A |
| BinsRun | POST | `/BinsRun/ActualRuns/{id:long}/SalesDesk` | A |
| BinsRun | POST | `/BinsRun/ActualRuns/{id:long}/Details` | A |
| BinsRun | POST | `/BinsRun/ActualRunOverrides/{id:long}/Approve` | A |
| BinsRun | POST | `/BinsRun/{id:long}/Edit` | A |
| BinsRun | POST | `/BinsRun/{id:long}/Reverse` | A |
| BinsRun | POST | `/BinsRun/Transfer` | A |
| BinsRun | POST | `/BinsRun/Transfer/{id:long}/Reverse` | A |
| BinsRun | POST | `/BinsRun/OutsideTransfers` | A |
| BinsRun | POST | `/BinsRun/OutsideTransfers/{id:long}/Reverse` | A |
| BinsRun | POST | `/BinsRun/InterCrewTransfers` | A |
| BinsRun | POST | `/BinsRun/InterCrewTransfers/{id:long}/Receive` | A |
| BinsRun | POST | `/BinsRun/InterCrewTransfers/{id:long}/Review` | A |
| BinsRun | POST | `/BinsRun/InterCrewTransfers/{id:long}/Reverse` | A |
| BinsRun | POST | `/BinsRun/TrueUp` | A |
| CommercialPacks | POST | `/Admin/CommercialPacks/Plans/Save` | A |
| CommercialPacks | POST | `/Admin/CommercialPacks/Packs/Save` | A |
| CommercialPacks | POST | `/Admin/CommercialPacks/PlanItems/Save` | A |
| CommercialPacks | POST | `/Admin/CommercialPacks/PlanItems/Remove` | A |
| CommercialPacks | POST | `/Admin/CommercialPacks/Plans/{id:int}/Deactivate` | A |
| CommercialPacks | POST | `/Admin/CommercialPacks/Packs/{id:int}/Deactivate` | A |
| Configuration | POST | `/Admin/Configuration/Save` | A |
| Configuration | POST | `/Admin/Configuration/EbsDailyBins/SendNow` | A |
| Configuration | POST | `/Admin/Configuration/EbsDailyBins/Test` | A |
| EndOfDayFillAdmin | POST | `/MasterData/end-of-day-fill-groups/group` | A |
| EndOfDayFillAdmin | POST | `/MasterData/end-of-day-fill-groups/recipient` | A |
| EndOfDayFillAdmin | POST | `/Admin/Users/EndOfDayFillGroups` | A |
| EndOfDayFillAdmin | POST | `/MasterData/end-of-day-fill-groups/recovery` | A |
| EndOfDayFill | POST | `/EndOfDayFill/Send` | A |
| FieldSamples | POST | `/[controller]/Create` | A |
| FieldSamples | POST | `/[controller]/{id:long}/Delete` | A |
| FieldSamples | POST | `/[controller]/{id:long}/autosave` | A |
| FieldSamples | POST | `/[controller]/{id:long}/metadata` | A |
| FieldSamples | POST | `/[controller]/{id:long}/rows` | A |
| FieldSamples | POST | `/[controller]/{id:long}/complete` | A |
| FieldSamples | POST | `/[controller]/{id:long}/report/send` | A |
| FieldSamples | POST | `/[controller]/{id:long}/photos` | A |
| FieldSamples | POST | `/[controller]/{id:long}/photos/{photoId:long}/reclassify` | A |
| FieldSamples | POST | `/[controller]/{id:long}/photos/{photoId:long}/rotate` | A |
| FieldSamples | POST | `/[controller]/{id:long}/photos/{photoId:long}/remove` | A |
| HarvestWatch | POST | `/Rooms/{roomId:int}/HarvestWatch/Deploy` | A |
| HarvestWatch | POST | `/Rooms/{roomId:int}/HarvestWatch/{deploymentId:long}/Retire` | A |
| Home | POST | `/Dashboard/Rooms/{roomId:int}/Projection` | A |
| Home | POST | `/Rooms/{roomId:int}/Projection` | A |
| Home | POST | `/Dashboard/Rooms/{roomId:int}/Deplete` | A |
| Home | POST | `/Dashboard/Rooms/{roomId:int}/Depletions/{depletionId:long}/Void` | A |
| Home | POST | `/Dashboard/Rooms/{roomId:int}/InventoryTrueUp` | A |
| Home | POST | `/Dashboard/Rooms/{roomId:int}/Transfer` | A |
| Home | POST | `/Rooms/{roomId:int}/DroppedBins` | A |
| Home | POST | `/Rooms/{roomId:int}/DroppedBins/{lossId:long}/Reverse` | A |
| MasterData | POST | `/MasterData/{type}/Save` | A |
| MasterData | POST | `/MasterData/{type}/Deactivate/{id:int}` | A |
| MasterData | POST | `/MasterData/canonical-growers/Map` | A |
| MasterData | POST | `/MasterData/grower-lots/ImportPreview` | A |
| MasterData | POST | `/MasterData/grower-lots/ImportApply` | A |
| OrchardRecipientImports | POST | `/Admin/OrchardRecipientImports/Preview` | A |
| OrchardRecipientImports | POST | `/Admin/OrchardRecipientImports/ExportDryRun` | A |
| OrchardRecipientImports | POST | `/Admin/OrchardRecipientImports/Stage` | A |
| OrchardRecipientImports | POST | `/Admin/OrchardRecipientImports/{id:long}/Review` | A |
| OrchardRecipientImports | POST | `/Admin/OrchardRecipientImports/{id:long}/Apply` | A |
| OrchardRecipients | POST | `/Admin/OrchardRecipients/GrowerNumbers/Save` | A |
| OrchardRecipients | POST | `/Admin/OrchardRecipients/GrowerNumbers/{id:int}/Enabled` | A |
| OrchardRecipients | POST | `/Admin/OrchardRecipients/GrowerNumbers/{id:int}/Delete` | A |
| OrchardRecipients | POST | `/Admin/OrchardRecipients/Save` | A |
| OrchardRecipients | POST | `/Admin/OrchardRecipients/{id:int}/Enabled` | A |
| OrchardRecipients | POST | `/Admin/OrchardRecipients/{id:int}/Delete` | A |
| ProcessorShipments | POST | `/ProcessorShipments/Review` | A |
| ProcessorShipments | POST | `/ProcessorShipments` | A |
| ProcessorShipments | POST | `/ProcessorShipments/{id:long}/PriceCorrection` | A |
| ProcessorShipments | POST | `/ProcessorShipments/{id:long}/Reverse` | A |
| QcStation | PUT | `/api/qc-station/samples/{sampleId:long}/pressures` | B |
| QcStation | PUT | `/api/qc-station/samples/{sampleId:long}/pressure` | B |
| QcStation | POST | `/api/qc-station/samples/{sampleId:long}/pressure` | B |
| QcStation | POST | `/api/qc-station/heartbeat` | B |
| Receipts | POST | `/[controller]/Varieties/QuickAdd` | A |
| Receipts | POST | `/[controller]/Create` | A |
| Receipts | POST | `/[controller]/{id:long}/Treatments/Review` | A |
| Receipts | POST | `/[controller]/{id:long}/Treatments` | A |
| Receipts | POST | `/[controller]/{id:long}/Treatments/{applicationId:long}/Reports` | A |
| Receipts | POST | `/[controller]/{id:long}/Treatments/{applicationId:long}/Reports/{attachmentId:long}/Remove` | A |
| Receipts | POST | `/[controller]/{id:long}/Treatments/{applicationId:long}/Reverse` | A |
| Receipts | POST | `/[controller]/{id:long}/Edit` | A |
| Receipts | POST | `/[controller]/{id:long}/AdminInventoryOverride` | A |
| Receipts | POST | `/[controller]/{id:long}/Delete` | A |
| Receipts | POST | `/[controller]/{id:long}/receiving/open` | A |
| Receipts | POST | `/[controller]/{id:long}/samples` | A |
| Receipts | POST | `/[controller]/{id:long}/photos` | A |
| Receipts | POST | `/[controller]/{id:long}/photos/{photoId:long}/rotate` | A |
| Receipts | POST | `/[controller]/{id:long}/photos/{photoId:long}/remove` | A |
| RoomInventory | POST | `/Admin/RoomInventory/Diagnostics/Dismiss` | A |
| RoomInventory | POST | `/Admin/RoomInventory/Diagnostics/Restore` | A |
| RoomInventory | POST | `/Admin/RoomInventory/Preview` | A |
| RoomInventory | POST | `/Admin/RoomInventory/ImportEbsStartingInventory` | A |
| RoomInventory | POST | `/Admin/RoomInventory/Apply` | A |
| RoomSealing | POST | `/Rooms/{roomId:int}/Seal` | A |
| RoomTreatments | POST | `/Rooms/{roomId:int}/Treatments/Review` | A |
| RoomTreatments | POST | `/Rooms/{roomId:int}/Treatments` | A |
| RoomTreatments | POST | `/Rooms/{roomId:int}/Treatments/{applicationId:long}/Reports` | A |
| RoomTreatments | POST | `/Rooms/{roomId:int}/Treatments/{applicationId:long}/Reports/{attachmentId:long}/Remove` | A |
| RoomTreatments | POST | `/Rooms/{roomId:int}/Treatments/{applicationId:long}/Reverse` | A |
| Samples | POST | `/[controller]/{id:long}/Delete` | A |
| Samples | POST | `/[controller]/{id:long}/rows` | A |
| Samples | POST | `/[controller]/{id:long}/autosave` | A |
| Samples | POST | `/[controller]/{id:long}/sample-type` | A |
| Samples | POST | `/[controller]/{id:long}/Starch` | A |
| Samples | POST | `/[controller]/{id:long}/Starch/photos` | A |
| Samples | POST | `/[controller]/{id:long}/Send` | A |
| Samples | POST | `/[controller]/{id:long}/OverrideSend` | A |
| Samples | POST | `/[controller]/{id:long}/photos` | A |
| Samples | POST | `/[controller]/{id:long}/photos/{photoId:long}/reclassify` | A |
| Samples | POST | `/[controller]/{id:long}/photos/{photoId:long}/rotate` | A |
| Samples | POST | `/[controller]/{id:long}/photos/{photoId:long}/remove` | A |

## Exact machine exemptions

Only `QcStationController.UpdatePressures` and `QcStationController.Heartbeat`
declare `IgnoreAntiforgeryToken`:

- PUT `/api/qc-station/samples/{sampleId}/pressures`
- PUT `/api/qc-station/samples/{sampleId}/pressure`
- POST `/api/qc-station/samples/{sampleId}/pressure`
- POST `/api/qc-station/heartbeat`

All authenticate through `X-QC-STATION-CODE` and `X-QC-STATION-API-KEY`,
looking up an active station and verifying its hashed key. Browser cookies alone
cannot authorize these actions. Missing/bad keys return 401; inactive stations return
403. There is no controller-wide or route-prefix exemption. No secrets are included here.

## QC Station read-only contract and client

Authentication now only verifies credentials. The list GET and both detail GET aliases
do not update LastSeenAt/LastSeenIp or call SaveChanges.
Heartbeat POST writes only those two existing properties and returns 204.

The shared `QcStationApiClient` now sends one authenticated heartbeat before each
existing list/detail/save request. These are existing user-driven queue refresh,
sample selection/deep-link and save operations in WinForms MainForm, plus the
ConfigImportForm connection test. No background/high-frequency timer was added.
All supported WinForms paths use this shared client; FTA polling/protocols are unchanged.
Authentication/heartbeat failure is surfaced by the existing station exception handling.

A future release must put the web heartbeat endpoint live before rolling out the new
station client. Older clients can still read/save but reads no longer refresh presence.
The local installer uses the existing build script/default version 1.0.0, not a new
packaging process; the generated MSI is unsigned, uninstalled and unuploaded.
Signing and onsite hardware verification remain release/rollout prerequisites.

## GET mutation findings

1. QC Station authentication persisted LastSeenAt/LastSeenIp on GET. Fixed as authorized.
2. **Unresolved:** GET `/BinsRun?Section=Planner&ProjectionId=<deleted id>`
   calls `RunProjectionService.GetPlannerAsync`, which adds an `InspectDeleted`
   AuditLog and saves when an administrator explicitly selects a deleted projection.
   This is durable application data, not ordinary diagnostic logging. The existing
   test in RunProjectionTests requires this audit. Recommended narrow correction:
   move inspection recording to an authorized, token-protected POST Inspect action,
   retain the original audit evidence, redirect to a read-only planner GET, and update
   the page-local inspection control/tests. Approval was requested; this behavior has
   not been silently removed or exempted.
3. **Unresolved systemic read mutation:** GET `/Admin/RoomInventory` calls
   `RoomInventoryImportService.GetCurrentLotsAsync`, which calls
   `CanonicalGrowerService.LoadResolutionSetAsync`. On cache miss that invokes
   `EnsureSeedMappingsAsync`: it creates missing CanonicalGrowers/aliases, can rename
   an existing canonical grower, and calls SaveChanges. The same shared resolver is
   used by Receiving, receipt details, reconciliation and loss views. Local fixtures
   reproduced two growers/five aliases being created during view loading. Moving or
   removing this initialization requires a separate identity/bootstrap decision;
   changing resolver semantics or startup work is explicitly outside this batch.
   It is not an acceptable CSRF exemption. A dedicated diagnostic test reproduces
   this durable GET mutation. Valid-token Room Inventory success-path fixtures
   establish resolver mappings explicitly before measuring request writes. Those
   success tests do not imply that cold-start GET seeding is acceptable.
4. OAuth callbacks are authentication-protocol endpoints handled by Google middleware,
   not ordinary MVC writes. Their state/correlation validation remains intact.
   The HarvestWatch Connect GET initiates that protocol and does not itself save a row.
5. Photo content GETs authorize then stream existing storage content; no rotation,
   derivative creation or storage write was added. Delete GETs in Receipts, Samples,
   Field Samples and Projections show confirmation pages; their mutation actions use POST.

The review traced controller read actions and service calls, including backup status,
configuration, inventory views, report previews, projection detail/export and photo
content. The deleted-projection audit and shared grower seeding remain outstanding;
this document does not claim universal read-only GET certification while it is open.

## Forms and JavaScript

85 explicit-action POST forms across 31 views now opt into the framework FormTagHelper's
`asp-antiforgery="true"`. Existing explicit tokens and automatically tokenized forms
were retained rather than adding duplicate hidden inputs.

Existing AJAX conventions remain appropriate:
- FormData(form): Receipts QuickAdd, sample manual rows, projection autosave,
  photo rotation/reclassification, upload-feedback XMLHttpRequest.
- JSON autosave: the existing RequestVerificationToken header from the rendered form.
- Device capture: existing dedicated hidden token is appended to manually built FormData.
- Other fetch calls are read-only searches/refreshes.

No global fetch monkey-patch or browser-endpoint exemption was added.
Rendered-form regressions require exactly one hidden token per POST form.

## Tests and validation

Executed 2026-09-12:

- Restore and solution build: PASS (existing nullable warnings; no build errors).
- Change-scoped HTTP/antiforgery/station/Fruit Profile guard suite: **207/207 PASS**, zero skips.
- Dedicated BrowserAntiforgeryTests: **25/25 PASS**, including the diagnostic that
  reproduces the unresolved canonical-grower GET write. Passing this diagnostic
  does not satisfy the read-only GET gate.
- Shared QC Station client regression: **5/5 PASS** (included in the 207).
- Upload-feedback and camera-control JavaScript: **22/22 PASS**.
- EF pending-model check: clean; no migration executed.
- `dotnet format CropQc.sln --no-restore --verify-no-changes`: PASS.
- `git diff --check`: PASS.
- MSI: existing `scripts/build-qcstation-installer.ps1`, default version **1.0.0**,
  `artifacts/installers/CropQcStationSetup.msi`, **872,448 bytes**, build PASS.
  Unsigned; not uploaded/installed. No hardware test was performed.

The affected suite command was:

```powershell
dotnet test tests/CropQc.Api.Tests/CropQc.Api.Tests.csproj --no-build --filter '(FullyQualifiedName~Http|FullyQualifiedName~BrowserAntiforgery|FullyQualifiedName~Antiforgery|FullyQualifiedName~QcStation|FullyQualifiedName~FruitProfileIdentityGuardTests)&FullyQualifiedName!~PostgreSql&FullyQualifiedName!~Restore'
node --test tests/js/upload-feedback.test.cjs tests/js/device-camera-controls.test.cjs
```

This expanded HTTP regression scope is justified by changing the global MVC filter.
The full application suite and production/restored-PostgreSQL certification were not run.
Dedicated BrowserAntiforgeryTests use an isolated InMemory database, real protected
browser cookies, ephemeral test-only Data Protection and a SaveChanges observer.
They cover all explicit browser unsafe routes, Master Data/Batch 1B, permission denial,
form rendering, header-token submissions, station GETs, heartbeat-only writes and
station pressure aliases. Existing HTTP regressions cover genuine operational/photo
success paths. No PostgreSQL claim is made for provider-independent filter work.

## Migration / production

Migration: None. Production changes: None. Draft development only.
