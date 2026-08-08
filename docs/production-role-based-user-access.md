# Production role-based user access

This release changes application authorization from per-user `UserPageAccesses` rows to one active role and a complete `RolePageAccesses` matrix. Legacy rows remain intact as rollback and audit evidence, but the converted application no longer reads or writes them for authorization.

## Fresh-database roles

Fresh databases contain only the five standard active system roles. Production-specific imported roles are created only by the reviewed compatibility package.

| Role | Default matrix |
| --- | --- |
| Viewer | Read-only operational overview and inventory pages |
| QC Tech | Viewer access plus QC and receipt data entry |
| QC Admin | QC/master-data administration plus receipt creation |
| Manager | Broad operational and master-data administration |
| Admin | Admin for all 40 application areas |

Viewer, QC Tech, QC Admin, and Manager matrices are editable after creation. Admin is a read-only, full-access system role. `wes@fruitandland.com` remains the owner break-glass identity and resolves to Admin independently of stored matrix rows.

## Authoritative production baseline

Production and `main` both resolved to `cc9f979463da3fb55993f14472d3d2bec4186ccc` when this analysis was refreshed. That deployment includes End-of-Day Fill, its antiforgery/Data Protection fix, the reviewed 42-WP/27-EBS room configuration, and PR #180's formatting-only email change.

Fresh predeployment backup run 54 is the authoritative database baseline:

- started: `2026-08-08T22:13:17.3239852+00:00` (15:13:17 Pacific)
- upload/read-back verified: `2026-08-08T22:16:28+00:00` (15:16:28 Pacific)
- package: `cropqc-production-predeployment-20260808-221317.zip`
- exact size: `1,713,680` bytes
- SHA-256: `24d22e96b0fe8328111eb6d552e0423a3598d9dfa3e55106cf569e054bc4893e`
- deployed commit captured: `cc9f979463da3fb55993f14472d3d2bec4186ccc`
- storage: restricted Google Drive package `1lYD4A364cecyAG8AXGrANoHRyVUTjxFA`

The exact Drive package was independently downloaded. Its ZIP is readable, the PostgreSQL dump is readable and nonempty, the configuration/schema/photo exports parse as JSON, and every component size and SHA-256 agrees with `backup-manifest.json`. The persisted backup record reports `Succeeded`, upload read-back verification, retention completion, and lease release. It was restored into localhost-only PostgreSQL 18 databases for untouched preflight, clean conversion rehearsal, repeat/idempotency verification, and negative fingerprint-guard testing.

The restored database contains 12 active users, four legacy roles, 17 legacy `UserRoles` relationships, and 480 preserved `UserPageAccesses` rows. Five users have a redundant second Viewer relationship. There are no production custom roles before conversion.

## Reviewed legacy effective access

The compatibility scripts reproduce the old runtime rules, including direct `UserPageAccess`, legacy-area fallback, Master Data Admin elevation, legacy Data Cleanup/Crop Year Review email gates, owner break-glass, and active state. Every reviewed profile contains exactly 40 application areas.

| Users | Admin | Create | View | None | MD5 fingerprint |
| --- | ---: | ---: | ---: | ---: | --- |
| Ada Hernandez | 1 | 15 | 1 | 23 | `38d291003ef3287eaf098fc161c4496d` |
| Alexis Ledezma, Dan Kezele, James Foreman, Jorge Ledezma, Maurya Dunning, Robert Fulgham | 38 | 0 | 0 | 2 | `4c094069afe868d0f2be67fd41965528` |
| Harvest Log | 0 | 3 | 15 | 22 | `d9184fc3c77b3ceff3c9ff6f08f603c6` |
| Kyle Hendrickson | 0 | 16 | 11 | 13 | `7bb78e3a6bc3c6d779804438458849eb` |
| Maria Ledezma | 1 | 16 | 0 | 23 | `5fa520cf854f0158aea6eb14f2e7b967` |
| Shianne Allen | 0 | 3 | 2 | 35 | `38d476024d63b43fba825b0f450751be` |
| Wes Cusick | 40 | 0 | 0 | 0 | `85f8d6a96a71434b59e3dfdc5ef6318e` |

An exact user identity or fingerprint mismatch aborts before persistent DDL. A negative rehearsal changed Harvest Log's disposable legacy matrix; the package rejected it with the reviewed-fingerprint error, and `RolePageAccesses` remained absent with the original four roles, 17 role relationships, and all 480 legacy rows intact.

## Deterministic production bridge

The reviewed conversion does not force unmatched users into a closest built-in role. It creates five active, editable, non-system roles that exactly reproduce the five distinct unresolved matrices. Their description identifies them as migration roles and recommends later review in User Administration.

| Final role | Assigned active users | Matrix fingerprint |
| --- | --- | --- |
| QC Tech | Ada Hernandez | `38d291003ef3287eaf098fc161c4496d` |
| Manager | Dan Kezele; Maurya Dunning | `4c094069afe868d0f2be67fd41965528` |
| Admin | Robert Fulgham; Wes Cusick | `85f8d6a96a71434b59e3dfdc5ef6318e` |
| Imported Access A | Alexis Ledezma; James Foreman; Jorge Ledezma | `4c094069afe868d0f2be67fd41965528` |
| Imported Access B | Harvest Log | `d9184fc3c77b3ceff3c9ff6f08f603c6` |
| Imported Access C | Kyle Hendrickson | `7bb78e3a6bc3c6d779804438458849eb` |
| Imported Access D | Maria Ledezma | `5fa520cf854f0158aea6eb14f2e7b967` |
| Imported Access E | Shianne Allen | `38d476024d63b43fba825b0f450751be` |

The shared A role is based on a byte-for-byte identical reviewed matrix, not a guessed job function. A-E naming is fixed by the reviewed user mapping and does not depend on role IDs or runtime discovery. Fresh databases do not receive these roles.

The conversion renames the existing QC User identity to QC Tech and removes redundant Viewer relationships from Ada, Dan, Maurya, Robert, and Wes. Every active user ends with exactly one role.

Admin intentionally means full application administration. Robert's only reviewed before/after differences are:

- Data Cleanup: `None` -> `Admin`
- Crop Year Review: `None` -> `Admin`

All other users have zero effective-access changes in all 40 areas. All 480 legacy `UserPageAccesses` rows remain unchanged and become read-only migration/audit evidence.

## End-of-Day Fill preservation

Role assignment remains separate from user-specific End-of-Day Fill assignment. Run 54 contains:

- WP rooms: 42
- EBS rooms: 27
- total assigned rooms: 69
- MCD-01 and WP-4 through WP-8 included in WP
- recipients: `wes@fruitandland.com`, `jorge@wp-packing.com`, `rob@earlbrownandsons.com`
- Jorge -> WP; Rob -> EBS; Wes -> WP + EBS
- successful send history rows: 2
- active reservations: 0

First apply, first verify, repeat apply, and repeat verify reproduced the same protected fingerprints:

| Protected state | Rows | MD5 fingerprint |
| --- | ---: | --- |
| End-of-Day Fill groups | 2 | `4ed24ac0ab6c6ce1525799c5b427ad0d` |
| Recipients | 3 | `25450543a5e2af47d5a3642fbd33983c` |
| User/group assignments | 4 | `74c7bf40bfd8e94da81f6756d4c55de1` |
| Room assignments and capacities | 69 | `a168b825cd73c3104b6648bff75a138c` |
| Send history | 2 | `7c63ed8bee27486fefc73ad9da734a93` |
| Reservations | 0 | `d41d8cd98f00b204e9800998ecf8427e` |
| Legacy UserPageAccess evidence | 480 | `f2b8fb86c5411fb70a9ce4b7ba0900ac` |

The role package does not update End-of-Day Fill groups, recipients, assignments, room membership, room capacities, history, reservations, or report snapshots. The web-preview formatting correction uses the same display semantics as email rendering and does not alter raw `ProductionType`, `IsOrganic`, report grouping, quantities, or snapshot hashing.

## Disposable PostgreSQL 18 rehearsal

Against an untouched run-54 restore:

1. read-only preflight passed all 12 exact identity/fingerprint guards;
2. first apply created 10 roles total, 400 complete matrix rows, and reduced role relationships from 17 to 12;
3. verification found exactly Robert's two reviewed Admin normalizations and no other access differences;
4. all protected End-of-Day Fill and legacy-access fingerprints remained identical;
5. second apply reported the schema already present and performed verification without rewriting configuration;
6. second verification passed with the same assignments, matrices, differences, and protected fingerprints.

The package remains transactional, audited, fail-closed, and idempotent. It does not change employment, Gmail credentials, Receiving, QC, inventory, Bins Run, Actual Runs, transfers, true-ups, Grower Lots, projections, or packout data. It does not insert or repair `__EFMigrationsHistory`.

## Package use

1. Run `preflight-role-based-user-access.sql` read-only against the current database, using the actual legacy email-list configuration when it differs from repository defaults.
2. Require all 12 reviewed identities and fingerprints to match exactly.
3. Apply `apply-role-based-user-access-schema.sql` transactionally.
4. Run `verify-role-based-user-access.sql` and review the user-by-user/area-by-area output. Only Robert's two documented Admin grants may differ.
5. Run apply and verification again to prove idempotency.
6. Run the application object-state gate, inventory readiness, and normal release validation before any separately authorized deployment.

Production role data is not changed by this draft PR work. The package must be regenerated and reviewed if any guarded identity, legacy effective matrix, or protected End-of-Day Fill fingerprint changes before a future production window.

## Runtime authorization and administration

Authorization resolves normalized email to an active user, requires exactly one active role, then reads that role's complete matrix. Missing roles, multiple roles, inactive roles, invalid levels, and missing matrix rows fail closed. Admin is always full access, and owner break-glass remains available.

User Administration shows a compact user list with role dropdown, employment details, End-of-Day Fill assignments, and last login. Employment and report assignments remain user-specific. Roles & Permissions renders one selected matrix, assigned users, imported-role warnings, and a differences-only comparison against another role with gain/loss/unchanged counts. Imported roles can be renamed, edited, and reassigned; a role cannot be deactivated while active users remain assigned.
