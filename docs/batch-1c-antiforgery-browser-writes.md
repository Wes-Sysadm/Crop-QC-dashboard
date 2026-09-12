# Batch 1C — Browser antiforgery and station heartbeat

## Status and scope

Development only, based on main `90c129f4bca77b6b6df8b259532d2974e79a1e0d`.
No migration, production requests, business-data repair, merge, or deployment.
Problem: cookie-authenticated browser writes lacked uniform antiforgery enforcement,
and the accompanying audit found hidden durable side effects in ordinary GET paths.
The user explicitly deferred exactly three existing compatibility/bootstrap read-side
initialization paths on 2026-09-12. Their implementation remains unchanged in this
continuation. They are not additional antiforgery exemptions and are not being described
as safe HTTP design. The four ordinary GET-mutation corrections remain in place.

**These three exceptions are not considered good final architecture. Their relocation/removal
is deferred to the compatibility/startup-boundary hardening work because changing
schema/bootstrap ownership is outside Batch 1C.**

The affected shared dependency is MVC unsafe-method filtering. Tests therefore cover
HTTP workflows across browser controllers, rather than unrelated inventory arithmetic.
Existing history, quantities, identity, authorization policies and storage semantics remain unchanged.
Station heartbeat telemetry and explicit deleted-projection inspection now use write endpoints.

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
**162 explicit unsafe routes: 158 browser routes and 4 machine routes.**
These resolve to **158 unsafe controller methods: 156 browser and 2 machine**.
The single new route in this continuation is deleted-projection inspection.
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
| BinsRun | POST | `/BinsRun/Projections/{id:long}/InspectDeleted` | A |
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

## Deleted projection GET — corrected

`GetPlannerAsync` previously created `InspectDeleted` only when `projectionId`
explicitly equaled the selected deleted record and the user had Planner Admin access.
There are no consumers of this action implementing an access gate. Critically, the
same method already selected/displayed the first deleted record when no ID was supplied,
without that audit. Thus this was informational explicit-inspection telemetry, not
an every-display compliance/access gate. The Outcome route actually excludes deleted
records; it is not an alternative deleted-record access path.

Both planner-card and recent-activity deleted-record controls now submit
`POST /BinsRun/Projections/{id:long}/InspectDeleted` with exactly one form token.
The controller requires the existing ProjectionPlannerAdmin policy and antiforgery;
the service independently requires Planner Admin, queries the exact deleted record,
adds the same `InspectDeleted` entity key, user, source, timestamp, facility/deletion
evidence and `Result=Viewed`, then redirects to the read-only planner view. Active or
missing records return 404 without an audit. No projection/source/history is modified.
Existing authorized direct GETs and implicit first-record display remain read-only.
No query-string authorization token or new access policy was invented.

HTTP tests prove active/deleted GET zero SaveChanges, exact POST audit, token/permission
rejection, correct redirect, read-back with no second audit, and unchanged projection
serialization. The service deletion-history regression retains the audit assertion
after explicitly calling inspection rather than expecting a GET mutation.

## Canonical grower resolution — corrected

The cache factory and uncached `LoadResolutionSetAsync` both called lazy seeding.
`AdminManagementService.CanonicalGrowersPage` also called it directly. All such calls
and the now-unnecessary `EnsureSeedMappingsAsync` interface/implementation are removed.
Reads now query AsNoTracking and build only the existing in-memory resolution/cache.
They do not rename display names, create aliases or IDs, change timestamps, or save.

No replacement automatic seed endpoint/startup job was added. The original canonical
grower migration already owns initial seed rows. Later durable changes remain at
the existing explicit `/MasterData/canonical-growers/Save` and `/Map` POST boundaries
in AdminManagementService, with their existing Create-level Master Data permission,
normalization, uniqueness, audit and reviewed-active-master restrictions. No permission
was broadened. An administrator must explicitly create missing durable mappings;
simply viewing a page no longer restores or renames them.

Read consumers (Dashboard/Receiving/Crop Year Review, Bins Run display, inventory import,
reconciliation and losses) use normalized/display resolution. Mapping forms enumerate
persisted canonical IDs. Legacy reconciliation explicitly fails closed if the resolved
canonical ID is null or does not match the reviewed active target; it must not obtain
an ID by triggering a read-side seed. Reviewed Grower sync semantics remain unchanged.

All five Vantage/Stayman aliases retain their existing in-memory fallback. Missing rows
yield a null canonical ID, never a fabricated ID. Existing complete or incomplete
rows retain their actual ID and stored display casing. Tests exercise both uncached
loads and cache hits, actual GETs with missing/incomplete rows, and exact before/after
serialized grower/alias/number/audit fingerprints plus zero SaveChanges calls.
The existing explicit Save HTTP test proves token and permission rejection, audited
creation of a historical mapping, normalized uniqueness and duplicate rejection.
Existing mapping-service tests preserve active source/number mapping behavior.

## Additional narrow audit correction — Variety Colors

Admin Variety Colors and Fruit Profile Master Data reads called both runtime DDL and
`ConsolidateAliasConfigurationsAsync` (rename/delete configuration rows and audit).
Read methods now use the existing AsNoTracking preference/normalization resolver only.
The same canonical winner and display color are retained without rewriting aliases.
Consolidation and its existing audit remain at the existing protected Save/Reset actions.
No new endpoint, color algorithm, schema helper implementation or startup change.
HTTP and service tests prove conflicting aliases remain untouched on GET, the winner
is unchanged, and consolidation occurs only after an authorized token-bearing Save.

## Centralized temporary compatibility/bootstrap exception list

Runtime MVC descriptors enumerate **85 explicit GET verb/route pairs** (aliases count
separately), plus **5 minimal GET health endpoints** and **2 conventional actions**.
The architectural test prints this inventory; it enumerates HttpMethodActionConstraint
and AttributeRouteInfo on actual ControllerActionDescriptors. The unsafe table above
uses the same methodology, not file-string estimates. All 85 GET entries were included
in the source/call-path audit. Exactly three existing durable initialization mechanisms
are temporarily deferred by explicit user decision, affecting two browser destinations:

1. `/Admin/Configuration` -> `GetConfigurationAsync` -> `EnsureConfigurationTableAsync`
   and `EnsureConfigurationDefaultsAsync`: runtime DDL and missing configuration-row
   inserts. `ApprovedCompatibilityException_ConfigurationGetOnlyCreatesMissingDefaults`
   deliberately reproduces inserts after removing defaults in a disposable DB, checks
   that no other entity type is written, and fingerprints a repeat GET to prove existing
   configuration values are unchanged and no further durable changes occur.
   Configuration editing currently addresses persisted IDs, so replacing seed-on-read
   requires an explicit initialization/default-editing contract, not invented IDs.
2. `/Admin/QcStations` -> `GetStationsAsync` -> `EnsureQcStationColumnsAsync`: provider
   SQL contains ALTER TABLE and UPDATE of blank StationName from Name. An InMemory
   SaveChanges observer cannot certify this raw relational SQL as read-only.
3. `/Admin/Configuration` -> email status -> `GoogleCredentialStore.GetDiagnosticAsync`
   -> `EnsureSchemaAsync`: conditional provider DDL. Diagnostic reads do not otherwise
   update credential use/refresh timestamps. Token refresh/use writes are in sending
   and background-mailbox paths, not this diagnostic.

These are specific temporary GET-side-effect exceptions, NOT unsafe-method antiforgery
exemptions. They are explicitly accepted for deferral, not silently grandfathered.
No startup/bootstrap behavior was altered. Four read-mutation families are corrected
in this PR (station telemetry, projection audit, canonical grower seeds, variety colors);
three initialization mechanisms remain. No fourth exception is allowed or was found by
the resumed source/call-path audit. No unsafe authentication classification is ambiguous.

The audit also traced controller/service reads for inventory diagnostics, backups,
configuration, report previews, EOD history/preview, projection detail/export, photos,
and sample refresh. OAuth callbacks are authentication-protocol endpoints handled by
middleware state/correlation, not ordinary MVC writes. HarvestWatch Connect initiates
that protocol without saving rows itself. Photo GETs authorize and stream existing
content; delete GETs show confirmations. Process-local caches and structured logging
are not counted as durable writes. No claim of universal GET-read-only coverage is made.

Ordinary application GET paths are read-only after Batch 1C except for three documented
legacy compatibility/bootstrap initialization paths: Configuration initialization,
QC Station schema/backfill initialization, and Google credential schema initialization.

## GET architecture guard

`GetMutationArchitectureTests` walks compiled application call paths from MVC GETs and
the two conventional Home actions. It follows async/iterator state machines, referenced
delegates, helper calls and application-interface implementations across Web/Data/Shared.
EF saves, raw SQL mutation entry points, file mutation APIs and process execution are
tripwires. In-memory collections/caches, metrics and structured logging are not writes.
The five minimal health delegates remain source-reviewed reads.

The only permitted edges are the exact four helper calls implementing the three named
read-path exceptions above (Configuration has two helpers). Each has a documented reason.
Tests require all expected edges to be reached and exactly three exception callers;
there is no controller, `/Admin/*`, diagnostic, API, or schema-helper wildcard.
The exceptional helper source bodies are frozen by normalized SHA-256 checks, so adding
business writes inside an approved helper also fails review. Their current DDL/backfill
targets were inspected from source; no PostgreSQL execution claim is made for those checks.

Self-tests prove indirect, async, interface-dispatched database writes and file writes
are detected; normal process-local state is permitted. This is a regression tripwire,
not a formal whole-program verifier of reflection/dynamic code or third-party internals.
Existing disposable real-cookie HTTP tests provide runtime evidence for corrected reads,
allowed Configuration bootstrap effects, forms, permissions and protected writes.

## Future work — Compatibility/bootstrap request-time mutation cleanup

This is the deferred request-time compatibility DDL/startup-ownership concern, not new
Batch 1C implementation scope. Review Configuration schema/default creation, QC Station
schema/name backfill, and Google credential schema creation together. Identify any other
request-time compatibility DDL encountered in that later review. Deliberately assign
initialization ownership, validate old-deployment compatibility, preserve persisted
configuration IDs/default semantics and station/credential history, and remove the
corresponding narrowly frozen exceptions only after that work is verified.

Goal: **Request handlers do not own database schema evolution or hidden durable bootstrap
synchronization.** No new migration, startup job, middleware initializer, admin page,
architecture PR, or production change is introduced here.

## Forms and JavaScript

85 existing explicit-action POST forms plus 2 new inspection forms across 31 views use the framework FormTagHelper's
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
- Final change-scoped HTTP/antiforgery/station/Fruit Profile guard/projection/canonical
  grower/variety-color/GET-architecture suite: **332/332 PASS**, zero skips.
- Dedicated BrowserAntiforgeryTests: **31/31 PASS**, including the explicitly approved
  Configuration bootstrap exception test. Corrected projection, grower and color
  tests require zero read-side writes; the exception test permits only its documented
  initialization purpose and requires repeat-read fingerprints unchanged.
- GET mutation architecture and frozen-exception guard: **7/7 PASS**.
- Batch 1B Fruit Profile identity guard: **31/31 PASS**; HTTP valid-token identity
  rejection and tokenless pre-business rejection also remain covered.
- QC Station regressions: **105/105 PASS**, including **5/5** shared client tests.
- Run Projection: **63/63**, canonical grower: **19/19**, variety alias/color: **27/27**,
  variety navigation: **3/3**, all PASS and included in the 332.
- Upload-feedback and camera-control JavaScript: **22/22 PASS**.
- EF pending-model check: clean; no migration executed.
- `dotnet format CropQc.sln --no-restore --verify-no-changes`: PASS.
- `git diff --check`: PASS.
- MSI: existing `scripts/build-qcstation-installer.ps1`, default version **1.0.0**,
  `artifacts/installers/CropQcStationSetup.msi`, **872,448 bytes**, build PASS.
  Rebuilt 2026-09-12 18:19:16 UTC; unsigned, not uploaded/installed/distributed.
  The WiX payload includes the rebuilt shared station client with the heartbeat call.
  No hardware test was performed. The normal build script removes its generated
  development settings file from the local installer payload; no user configuration
  or production installer was removed.

The affected suite command was:

```powershell
dotnet test tests/CropQc.Api.Tests/CropQc.Api.Tests.csproj --no-build --filter '(FullyQualifiedName~Http|FullyQualifiedName~BrowserAntiforgery|FullyQualifiedName~GetMutationArchitectureTests|FullyQualifiedName~Antiforgery|FullyQualifiedName~QcStation|FullyQualifiedName~FruitProfileIdentityGuardTests|FullyQualifiedName~RunProjectionTests|FullyQualifiedName~CanonicalGrowerReviewTests|FullyQualifiedName~VarietyColorAliasTests|FullyQualifiedName~VarietyColorsNavigationTests)&FullyQualifiedName!~PostgreSql&FullyQualifiedName!~Restore'
node --test tests/js/upload-feedback.test.cjs tests/js/device-camera-controls.test.cjs
```

This expanded HTTP regression scope is justified by changing the global MVC filter;
the added projection/grower/color service tests cover the newly changed read dependencies.
The full application suite and production/restored-PostgreSQL certification were not run.
Dedicated BrowserAntiforgeryTests use an isolated InMemory database, real protected
browser cookies, ephemeral test-only Data Protection and a SaveChanges observer.
They cover all explicit browser unsafe routes, Master Data/Batch 1B, permission denial,
form rendering, header-token submissions, station GETs, heartbeat-only writes and
station pressure aliases. Existing HTTP regressions cover genuine operational/photo
success paths. No PostgreSQL claim is made for provider-independent filter work.

## Migration / production

Migration: None. Production changes: None. Draft development only.

## Previous continuation change manifest

Continued from `f18a82f7adc5555e5b314584d71dab8ea4236beb`, same branch and base.
No update from newer main was necessary. Twelve files changed in this continuation;
that revision changed 47 files relative to main:

- `src/CropQc.Web/Controllers/BinsRunController.cs`
- `src/CropQc.Web/Services/AdminManagementService.cs`
- `src/CropQc.Web/Services/CanonicalGrowerService.cs`
- `src/CropQc.Web/Services/RunProjectionService.cs`
- `src/CropQc.Web/Services/VarietyColorService.cs`
- `src/CropQc.Web/Views/BinsRun/Index.cshtml`
- `src/CropQc.Web/wwwroot/css/site.css`
- `tests/CropQc.Api.Tests/BrowserAntiforgeryTests.cs`
- `tests/CropQc.Api.Tests/CanonicalGrowerReviewTests.cs`
- `tests/CropQc.Api.Tests/RunProjectionTests.cs`
- `tests/CropQc.Api.Tests/VarietyColorAliasTests.cs`
- `docs/batch-1c-antiforgery-browser-writes.md`

The new deleted-inspection button retains the existing responsive card layout, adds
normal keyboard-submit behavior, full available width and inherited typography. HTTP
rendering proves both controls/token markup; a physical/mobile browser visual or onsite
station test was not performed. Runtime architecture enumeration and real-cookie HTTP
tests passed; raw SQL initialization findings remain source-confirmed rather than
misrepresented as PostgreSQL-tested read-only paths.

## Final completion continuation

Continued from `ee6374bcb1ec1a16132a0424844d477ee8bf85c9` on the same branch/base.
Only the implementation review, `BrowserAntiforgeryTests.cs`, and the new
`GetMutationArchitectureTests.cs` change in this continuation (3 files). The complete
PR changes 48 files versus main. All production application, compatibility-helper,
station-client and schema source remains byte-for-byte unchanged from the prior head.

Final inventory was recalculated from MVC descriptors: 162 explicit unsafe verb/route
pairs (158 browser, 4 machine), 158 distinct unsafe controller methods (156 browser,
2 machine), 2 conventional actions, and 85 explicit GET routes. Source confirms 5
minimal health endpoints. Recounting added token-enabled form markup in the diff from
main yields 87 forms across 31 Razor views. No new routes, permissions or antiforgery
exemptions were introduced by this completion continuation.

Batch 1C release-review blockers: none within the expressly approved scope. The three
temporary compatibility exceptions remain visible technical debt assigned to the
future work above. Actual deployment, production verification, signing/distribution
and onsite station/hardware validation remain later release/rollout work.

Status: **BATCH 1C READY FOR RELEASE REVIEW**. PR remains draft, unmerged and undeployed.
