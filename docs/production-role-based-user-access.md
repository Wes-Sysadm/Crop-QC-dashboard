# Production role-based user access

This release changes application authorization from per-user `UserPageAccesses` rows to one active role and a complete `RolePageAccesses` matrix. The legacy table remains intact as rollback and migration evidence, but the application no longer reads or writes it for authorization.

## Fresh-database matrices

Every built-in role receives one row for each of the 40 centralized `ApplicationAreas`. Areas not named below are explicitly `None`.

| Role | Admin | Create | View |
| --- | --- | --- | --- |
| Viewer | none | none | Dashboard; Receipt QC; Field Samples; QC Reports; Receipts; Current Lots; Rooms; Inventory; Grower Lots |
| QC Tech | none | Receipt QC; Field Samples; Receipts | Dashboard; QC Reports; Current Lots; Rooms; Inventory; Grower Lots |
| QC Admin | Receipt QC; Field Samples; QC Reports; QC Stations; Varieties; Grades; Defects; Size Configuration; Variety Colors; Orchard Recipients; Orchard Managers | Receipts | Dashboard; Current Lots; Rooms; Inventory; Grower Lots; Master Data |
| Manager | Receipt QC; Field Samples; QC Reports; Receipts; Current Lots; Bins Run; Rooms; Room Transactions; Grower Lots; Projection Planner; Planning Projection Reports; Actual Runs; Packout Results; Transfers; True Up; Inventory; Master Data; QC Stations; Orchard Recipients; Orchard Managers; Facilities; Varieties; Grades; Defects; Size Configuration; Variety Colors; Import Tools; Export Tools | none | Dashboard; Downloads; Audit History |
| Admin | all 40 areas | none | none |

Viewer, QC Tech, QC Admin, and Manager matrices are editable after creation. Admin is a read-only full-access system role. `wes@fruitandland.com` remains the clearly labeled owner break-glass identity and resolves to Admin independently of its stored matrix.

## Newest verified-backup analysis

Production and `main` were checked immediately before this analysis and both resolve to `138ca27415c6368a360d847ae367596b08af3ff2`. This includes the merged End-of-Day Fill release, its antiforgery/Data Protection hotfix, and the reviewed 42-WP/27-EBS Room scope. The restored baseline contains the complete End-of-Day Fill schema and configuration and is the authoritative source for the preservation checks below.

Backup run 52 started at `2026-08-08 19:58:47.0390263+00` (12:58:47 Pacific) and completed at `2026-08-08 20:02:02+00` (13:02:02 Pacific) with status `Succeeded`. Its package is `cropqc-production-predeployment-20260808-195847.zip`, 1,693,275 bytes, SHA-256 `d57d30118a73d4a87f524eb8640cb2d5c0b1b4335935e823717593e826fd6eee`. The persisted run records Google Drive read-back verification, retention completion, and lease release. The exact Drive package was independently downloaded; its ZIP is readable, all four component sizes and SHA-256 values agree with `backup-manifest.json`, the configuration, schema, and photo manifests are readable, and the PostgreSQL dump expands to a readable 50,138,176-byte SQL stream. The package was restored to the new localhost-only PostgreSQL 18 database `cropqc_run52_role_refresh`.

The restored database contains 12 active users, four legacy role rows, 17 `UserRoles` assignments, no zero-role user, five multiple-role users, and 480 preserved `UserPageAccesses` rows:

| User | Employment facility | Current role rows | Gmail credential present |
| --- | --- | --- | --- |
| Ada Hernandez | Unassigned | QC User, Viewer | yes |
| Alexis Ledezma | WP | Viewer | yes |
| Dan Kezele | Unassigned | Manager, Viewer | yes |
| Harvest Log | Unassigned | Viewer | yes |
| James Foreman | Unassigned | Viewer | yes |
| Jorge Ledezma | Unassigned | Viewer | yes |
| Kyle Hendrickson | Unassigned | Viewer | yes |
| Maria Ledezma | Unassigned | Viewer | yes |
| Maurya Dunning | Unassigned | Manager, Viewer | yes |
| Robert Fulgham | EBS | Admin, Viewer | yes |
| Shianne Allen | Unassigned | Viewer | yes |
| Wes Cusick | Unassigned | Admin, Viewer | yes |

Credential presence was checked without reading or emitting tokens. There are no existing custom roles. `QC Tech` does not exist, so `QC User` can be renamed in place without an identity collision. `QC Admin` is new.

### Current effective 40-area profiles

The deployed `DataCleanup__AllowedEmails` and `CropYearReview__AllowedEmails` environment values are unset, so the deployed application uses its `wes@fruitandland.com` fallback for both gates. Every profile below contains exactly 40 areas. The old hidden Master Data Admin elevation explains the unexpectedly broad profiles.

| Users | Admin | Create | View | None |
| --- | ---: | ---: | ---: | ---: |
| Ada | 1 | 15 | 1 | 23 |
| Alexis, Dan, James, Jorge, Maurya, Robert | 38 | 0 | 0 | 2 |
| Harvest Log | 0 | 3 | 15 | 22 |
| Kyle | 0 | 16 | 11 | 13 |
| Maria | 1 | 16 | 0 | 23 |
| Shianne | 0 | 3 | 2 | 35 |
| Wes | 40 | 0 | 0 | 0 |

Exact area membership:

- Ada: Admin = Receipts. View = Grower Lots. Create = Actual Runs, Bins Run, Current Lots, Receipt QC, Dashboard, Field Samples, Inventory, Packout Results, Planning Projection Reports, Projection Planner, QC Reports, Rooms, Room Transactions, Transfers, True Up. All other areas are None.
- Alexis, Dan, James, Jorge, Maurya, and Robert: Admin for every area except Crop Year Review and Data Cleanup, which are None.
- Harvest Log: Create = Email Configuration, Orchard Managers, Orchard Recipients. View = Actual Runs, Bins Run, Current Lots, Receipt QC, Dashboard, Field Samples, Grower Lots, Inventory, Packout Results, Planning Projection Reports, Projection Planner, QC Reports, Receipts, Rooms, Transfers. All other areas are None.
- Kyle: Create = Actual Runs, Bins Run, Current Lots, Receipt QC, Dashboard, Grower Lots, Inventory, Packout Results, Planning Projection Reports, Projection Planner, QC Reports, Receipts, Rooms, Room Transactions, Transfers, True Up. View = Audit History, Defects, Downloads, Export Tools, Facilities, Field Samples, Grades, Import Tools, Master Data, Size Configuration, Varieties. All other areas are None.
- Maria: Admin = Receipts. Create = Actual Runs, Bins Run, Current Lots, Receipt QC, Dashboard, Field Samples, Grower Lots, Inventory, Packout Results, Planning Projection Reports, Projection Planner, QC Reports, Rooms, Room Transactions, Transfers, True Up. All other areas are None.
- Shianne: Create = Receipt QC, QC Reports, Receipts. View = Dashboard, Field Samples. All other areas are None.
- Wes: Admin for all 40 areas through the owner break-glass rule.

The current Viewer assignees conflict in 38 of 40 areas. A single catch-all Viewer conversion cannot preserve their access.

### Approved redundant-role cleanup

The reviewed cleanup removes only the redundant Viewer assignment from Ada, Dan, Maurya, Robert, and Wes. Ada retains role ID 3, whose identity is renamed from `QC User` to `QC Tech`; Dan and Maurya retain Manager; Robert and Wes retain Admin. The cleanup was rehearsed only on a disposable clone and was not applied to production.

Admin is intentionally full access. Robert's exact permission delta is therefore:

- Crop Year Review: None -> Admin.
- Data Cleanup: None -> Admin.
- The other 38 areas are unchanged at Admin.

### Role decisions required

The table compares each unresolved current profile with the compiled built-in defaults. `+` is the number of additional grants and `-` the number of reductions. No exact built-in match exists for any unresolved user.

| User | Employment | End-of-Day Fill | Closest built-in | Matches / differs | Viewer | QC Tech | QC Admin | Manager | Shared-profile candidate |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Shianne Allen | Unassigned | none | QC Tech | 34 / 6 | 33/7, +4/-3 | 34/6, +5/-1 | 24/16, +16/-0 | 10/30, +30/-0 | none |
| Harvest Log | Unassigned | none | Viewer | 31 / 9 | 31/9, +0/-9 | 28/12, +3/-9 | 20/20, +13/-7 | 9/31, +30/-1 | none |
| Alexis Ledezma | WP | none | Manager | 30 / 10 | 2/38, +0/-38 | 2/38, +0/-38 | 13/27, +0/-27 | 30/10, +0/-10 | Custom profile A |
| James Foreman | Unassigned | none | Manager | 30 / 10 | 2/38, +0/-38 | 2/38, +0/-38 | 13/27, +0/-27 | 30/10, +0/-10 | Custom profile A |
| Jorge Ledezma | Unassigned | WP | Manager | 30 / 10 | 2/38, +0/-38 | 2/38, +0/-38 | 13/27, +0/-27 | 30/10, +0/-10 | Custom profile A |
| Maria Ledezma | Unassigned | none | QC Tech | 25 / 15 | 23/17, +0/-17 | 25/15, +0/-15 | 14/26, +12/-14 | 10/30, +29/-1 | none |
| Kyle Hendrickson | Unassigned | none | QC Tech | 15 / 25 | 14/26, +0/-26 | 15/25, +1/-24 | 11/29, +11/-18 | 11/29, +28/-1 | none |

The least-delta candidates above are analysis aids, not assignments. In particular, a smaller numerical delta can still remove important operational access.

Human-reviewable deltas for the plausible candidates:

- Alexis, James, or Jorge -> Manager: no grants. Would reduce Audit History Admin -> View, Backup History Admin -> None, Backups Admin -> None, Configuration Admin -> None, Dashboard Admin -> View, Downloads Admin -> View, EBS Historical Cleanup Admin -> None, Email Configuration Admin -> None, Permission Matrix Admin -> None, and Users Admin -> None. Thirty areas are unchanged.
- Alexis, James, or Jorge -> QC Admin: no grants. Would reduce Actual Runs, Audit History, Backup History, Backups, Bins Run, Configuration, Downloads, EBS Historical Cleanup, Email Configuration, Export Tools, Facilities, Import Tools, Packout Results, Permission Matrix, Planning Projection Reports, Projection Planner, Room Transactions, Transfers, True Up, and Users from Admin to None; Current Lots, Dashboard, Grower Lots, Inventory, Master Data, and Rooms from Admin to View; and Receipts from Admin to Create. Thirteen areas are unchanged.
- Harvest Log -> Viewer: no grants. Would remove Actual Runs View, Bins Run View, Email Configuration Create, Orchard Managers Create, Orchard Recipients Create, Packout Results View, Planning Projection Reports View, Projection Planner View, and Transfers View. Thirty-one areas are unchanged.
- Harvest Log -> QC Tech: would grant Field Samples, Receipt QC, and Receipts from View to Create; it would remove the same nine areas listed for Viewer. Twenty-eight areas are unchanged.
- Kyle -> QC Tech: would grant Field Samples View -> Create. It would remove Actual Runs, Bins Run, Packout Results, Planning Projection Reports, Projection Planner, Room Transactions, Transfers, and True Up from Create to None; reduce Current Lots, Dashboard, Grower Lots, Inventory, QC Reports, and Rooms from Create to View; and remove Audit History, Defects, Downloads, Export Tools, Facilities, Grades, Import Tools, Master Data, Size Configuration, and Varieties from View to None. Fifteen areas are unchanged.
- Kyle -> Viewer: no grants. It has the same reductions as QC Tech plus Receipt QC and Receipts Create -> View and Field Samples View remains unchanged. Fourteen areas are unchanged.
- Maria -> QC Tech: no grants. Would remove Actual Runs, Bins Run, Packout Results, Planning Projection Reports, Projection Planner, Room Transactions, Transfers, and True Up from Create to None; reduce Current Lots, Dashboard, Grower Lots, Inventory, QC Reports, and Rooms from Create to View; and reduce Receipts Admin -> Create. Twenty-five areas are unchanged.
- Maria -> Viewer: no grants. It has the QC Tech reductions, plus Field Samples, Receipt QC, and Receipts are reduced to View. Twenty-three areas are unchanged.
- Shianne -> QC Tech: would grant Current Lots, Grower Lots, Inventory, and Rooms None -> View plus Field Samples View -> Create; it would reduce QC Reports Create -> View. Thirty-four areas are unchanged.
- Shianne -> Viewer: would grant Current Lots, Grower Lots, Inventory, and Rooms None -> View; it would reduce QC Reports, Receipt QC, and Receipts from Create to View. Thirty-three areas are unchanged.

Alexis, James, and Jorge have byte-for-byte identical 40-area fingerprints and are therefore a candidate shared custom role, labeled `Custom profile A` until their actual function is confirmed. Harvest, Kyle, Maria, and Shianne each have a unique matrix. No role name based on a guessed job function is proposed.

The compatibility package currently preserves the agreed legacy matrix for an in-use non-Admin role when its assignees agree, while Admin is normalized to full access. Consequently, the disposable QC Tech matrix preserved Ada's legacy profile and the Manager matrix preserved Dan and Maurya's shared 38-Admin/2-None profile. A future production mapping must explicitly decide whether unresolved users join one of those preserved production matrices, adopt the compiled built-in defaults, or use a reviewed custom role; this analysis does not silently choose among those outcomes.

### Disposable resolved-state rehearsal

The run-52 clone used the five approved redundant-role removals and the following explicitly labeled test fixture only: Alexis, James, and Jorge -> `TEST FIXTURE ONLY - Custom profile A`; Harvest -> B; Kyle -> C; Maria -> D; Shianne -> E. These fixture roles preserve the five distinct unresolved fingerprints and are not production recommendations.

The first compatibility apply and verification passed, every active user ended with exactly one role, all 480 `UserPageAccesses` rows remained intact, and every active role received a complete 40-cell authoritative matrix. Admin had 40 Admin cells; the owner break-glass account remained Admin. The only before/after access differences were Robert's two documented Admin normalizations. A second apply changed no role assignment, matrix, or protected operational fingerprint, and verification passed again.

Protected hashes were identical before the first apply, after the first apply, and after the second apply for Users (12), User employment history (2), Google credentials (12), Receipts (195), QC Samples (216), Grower Lots (398), Room Inventory Adjustments (234), Bins Run entries (53), Actual Runs (14), Actual Run revisions (14), and Room Transfers (0). This separately confirms that employment facility, Google identity, active status, last-login data, Gmail credentials, and operational records did not change.

The compatibility package now captures and compares exact in-transaction fingerprints for all End-of-Day Fill groups (2), recipients (3), user/group assignments (4), Room assignments and capacities (69), sends (0), and reservations (0). Both the first and second applies reproduced the same fingerprints. Verification retained Jorge -> WP, Rob -> EBS, Wes -> EBS + WP, 42 WP Rooms, 27 EBS Rooms, MCD-01 and WP-4 through WP-8 in WP, all three reviewed recipients, and unchanged Room capacities. Role conversion remains completely separate from user-specific End-of-Day Fill assignment.

## Package use

The package is bounded to role authorization objects and does not alter `__EFMigrationsHistory`:

1. Run `preflight-role-based-user-access.sql` read-only, supplying the actual deployed legacy email lists when they differ from the repository defaults:

   `psql ... -v data_cleanup_allowed_emails='...' -v crop_year_review_allowed_emails='...' -f scripts/postgresql/preflight-role-based-user-access.sql`

2. Resolve every reported zero/multiple-role user and every same-role matrix conflict through a separately reviewed mapping. Re-run preflight until clean.
3. Apply `apply-role-based-user-access-schema.sql` transactionally with the same two variables.
4. Run `verify-role-based-user-access.sql` with the same variables. Review every before/after row; only the documented Admin full-access normalization is expected.
5. Run apply and verification again to prove idempotency before deployment.

On the untouched run-52 restore the apply stops before DDL with `Every active user must have exactly one reviewed role before conversion`; `RolePageAccesses` remains absent. The disposable mapping above demonstrates the package mechanics only. Production remains blocked until the seven unresolved role decisions and the intended treatment of compiled built-in defaults versus preserved current matrices are explicitly reviewed.

## Runtime authorization

Authorization resolves normalized email to an active User, requires exactly one active Role, then reads that role's matrix. Missing roles, multiple roles, inactive roles, invalid levels, and missing area rows fail closed. The only role-name-specific behavior is Admin full access; the only email-specific behavior is the owner break-glass override. Role claims remain in the authentication ticket only to display the signed-in role label and are not used by application authorization policies.

`UserPageAccesses` remains modeled solely so existing records can be retained and inspected. Normal sign-in, User Administration, role edits, and authorization do not create, update, or consult those rows.
