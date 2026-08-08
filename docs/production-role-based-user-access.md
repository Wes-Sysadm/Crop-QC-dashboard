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

Analysis used backup run 45, `cropqc-production-predeployment-20260807-200524.zip`, generated from deployed commit `d8a4a904b4f0778ac3a528e1b86f00ad97860458`. The persisted record and independently downloaded package agree on 1,529,335 bytes and SHA-256 `907c30e0a3db18ad08ab7c1dd17ad9ca4be268fca14e1b95af8d9d674839b562`. The ZIP is readable, the four component hashes agree with `backup-manifest.json`, and the PostgreSQL dump is a readable 46,859,328-byte SQL stream. The exact package was restored to a new localhost-only PostgreSQL 18 database.

The restored database contains 12 active users, four legacy roles, 17 role assignments, and 480 `UserPageAccesses` rows:

| User | Current role rows |
| --- | --- |
| Ada Hernandez | QC User, Viewer |
| Alexis Ledezma | Viewer |
| Dan Kezele | Manager, Viewer |
| Harvest Log | Viewer |
| James Foreman | Viewer |
| Jorge Ledezma | Viewer |
| Kyle Hendrickson | Viewer |
| Maria Ledezma | Viewer |
| Maurya Dunning | Manager, Viewer |
| Robert Fulgham | Admin, Viewer |
| Shianne Allen | Viewer |
| Wes Cusick | Admin, Viewer |

There are no existing custom roles. `QC Tech` does not exist, so `QC User` can be renamed in place without an identity collision. `QC Admin` is new. Robert and Wes are the current Admin assignees; Wes is already assigned Admin as well as the redundant Viewer row.

### Blocking decisions

The production apply is intentionally blocked. Ada, Dan, Maurya, Robert, and Wes each have two active role rows, violating the required one-role cardinality. In addition, the current Viewer role is a catch-all assignment whose users have 38 conflicting effective-access areas. After disregarding redundant Viewer rows on users already assigned QC User, Manager, or Admin, the remaining Viewer users still have different effective matrices and cannot safely share one role without changing access.

No production mapping is proposed for Alexis, Harvest Log, James, Jorge, Kyle, Maria, or Shianne. Review must decide which standard role or deliberately named custom role represents each distinct access profile. The compatibility script does not create arbitrary legacy roles or select a winner.

The old configured-email gates default to only `wes@fruitandland.com` for Data Cleanup and Crop Year Review. The preflight accepts the deployed values as explicit psql variables so external Render configuration can be included in the old-effective calculation. Robert therefore currently resolves to `None` for those two areas even though his old Master Data value is Admin. Under the required Admin semantics he would become Admin for both. That is the only difference in a disposable resolved-state rehearsal and must be explicitly accepted as the intentional Admin full-access normalization.

The old Master Data Admin elevation is responsible for broad hidden access across many users. It is included in the old-effective preflight calculation but is removed from the new runtime: each visible role-matrix cell is now authoritative.

## Package use

The package is bounded to role authorization objects and does not alter `__EFMigrationsHistory`:

1. Run `preflight-role-based-user-access.sql` read-only, supplying the actual deployed legacy email lists when they differ from the repository defaults:

   `psql ... -v data_cleanup_allowed_emails='...' -v crop_year_review_allowed_emails='...' -f scripts/postgresql/preflight-role-based-user-access.sql`

2. Resolve every reported zero/multiple-role user and every same-role matrix conflict through a separately reviewed mapping. Re-run preflight until clean.
3. Apply `apply-role-based-user-access-schema.sql` transactionally with the same two variables.
4. Run `verify-role-based-user-access.sql` with the same variables. Review every before/after row; only the documented Admin full-access normalization is expected.
5. Run apply and verification again to prove idempotency before deployment.

On an untouched run-45 restore the apply stops before DDL with `Every active user must have exactly one reviewed role before conversion`; `RolePageAccesses` remains absent. On a disposable clone with an explicitly test-only conflict fixture, apply/verify/repeat succeeds, preserves all 480 legacy rows, and creates complete 40-area matrices. That fixture is mechanical test data, not a production access recommendation.

Protected operational hashes were identical before and after the disposable rehearsal for Receipts (177), QC Samples (198), Grower Lots (398), Room Inventory Adjustments (212), Bins Run entries (49), Actual Runs (12), Actual Run revisions (12), Room Transfers (0), employment history (2), and Google credentials (12).

## Runtime authorization

Authorization resolves normalized email to an active User, requires exactly one active Role, then reads that role's matrix. Missing roles, multiple roles, inactive roles, invalid levels, and missing area rows fail closed. The only role-name-specific behavior is Admin full access; the only email-specific behavior is the owner break-glass override. Role claims remain in the authentication ticket only to display the signed-in role label and are not used by application authorization policies.

`UserPageAccesses` remains modeled solely so existing records can be retained and inspected. Normal sign-in, User Administration, role edits, and authorization do not create, update, or consult those rows.
