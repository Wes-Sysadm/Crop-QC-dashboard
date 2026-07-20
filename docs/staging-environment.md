# Staging Environment

This runbook describes the minimal non-production environment used for authenticated workflow validation. It exists so a human tester can sign in with the existing Google OAuth flow and validate PRs such as Field Samples without production data, production email, or production photo storage.

## What Staging Adds

- A separate Render web service: `crop-qc-dashboard-staging`
- A separate Render PostgreSQL database: `crop-qc-dashboard-staging-db`
- The existing Google OAuth configuration supplied through staging secrets
- Email disabled with `Email__Provider=None`
- Photo/file storage isolated from production by using local staging disk storage
- A visible banner: `STAGING - Non-production data`

Staging does not add a development login, role impersonation, alternate authentication scheme, new email provider, new storage provider, or new diagnostics system.

## Required Render Settings

Use the staging service in `render.yaml`. Configure these values in Render without committing secret values:

- `ASPNETCORE_ENVIRONMENT=Production`
- `AppEnvironment__Kind=Staging`
- `AppEnvironment__DisplayName=Crop QC Staging`
- `DATABASE_PROVIDER=PostgreSql`
- `ConnectionStrings__CropQc` from `crop-qc-dashboard-staging-db`
- `Authentication__AllowedGoogleDomains`
- `Authentication__BootstrapAdminEmails`
- `Authentication__Google__ClientId`
- `Authentication__Google__ClientSecret`
- `DataProtection__PersistKeysToFileSystem=true`
- `DataProtection__KeysPath=/var/data/dataprotection-keys`
- `DataProtection__ApplicationName=CropQcDashboardStaging`
- `FileStorage__Provider=Local`
- `FileStorage__LocalRootPath=/var/data/cropqc-staging-files`
- `Email__Provider=None`
- `QcStation__ApiBaseUrl=https://crop-qc-dashboard-staging.onrender.com`

Performance diagnostics from PR #120 can be enabled with the existing `PerformanceDiagnostics__*` settings when measurements are needed. Use normal logs/metrics; this PR does not add another diagnostics UI.

## Google OAuth Setup

Use the existing Google OAuth login flow.

Recommended staging OAuth settings:

- Authorized redirect URI: `https://crop-qc-dashboard-staging.onrender.com/signin-google`
- Authorized JavaScript origin: `https://crop-qc-dashboard-staging.onrender.com`, if required by Google Cloud
- Allowed domains limited through `Authentication__AllowedGoogleDomains`
- Bootstrap admin listed in `Authentication__BootstrapAdminEmails`

After the bootstrap admin signs in, assign test-user access from `/Admin/Users` using the existing permission matrix.

## Test User Setup

Use separate authorized Google Workspace accounts where practical:

- Field Sample administrator: view, edit, and administer Field Samples after PR #122 is deployed
- Field Sample editor: view and edit Field Samples, no Orchard Block administration
- Field Sample viewer: view only
- Unauthorized user: Google-authenticated account with no Field Sample access

Do not create hard-coded test users, password login, cookie fabrication, or role-switching controls.

## Database Setup And Reset

The staging database must be isolated from production. Do not copy production data automatically.

Typical setup:

1. Create or sync the Render staging service and staging PostgreSQL database from `render.yaml`.
2. Apply migrations to the staging database using the repository migration process or smoke-test tooling.
3. Sign in as the bootstrap admin.
4. Add fictional test data through the application or approved staging-only tooling.

Reset procedure:

1. Confirm the target is the staging database, not production.
2. Back up any staging data worth keeping.
3. Drop and recreate only the staging database.
4. Reapply migrations.
5. Recreate test users and permissions.

## Safe Test Data

Use fictional names and values only, for example:

- Test Orchard North
- Test Orchard South
- Block 12
- River Bottom
- Phoenix Test Block
- Test Grower A

PR #122 contains the Field Sample schema and behavior. After PR #124 merges, rebase PR #122 onto latest `main`, deploy that branch to staging, and create Field Sample test data there.

## Field Sample Human Test Checklist

Use this checklist after PR #122 is deployed to staging.

1. Confirm Field Samples navigation appears for an authorized user.
2. Confirm an unauthorized user cannot see or access Field Samples.
3. Create a Field Sample.
4. Confirm orchard/grower selection works.
5. Confirm block selection is clear.
6. Confirm variety selection works.
7. Confirm exactly 10 fruit rows are created.
8. Confirm no receipt, room, bin count, carrier, truck, or receiving-photo field is required.
9. Save one fruit weight only, reopen, and confirm it persisted.
10. Save one starch value only, reopen, and confirm it persisted.
11. Save one pressure side only, reopen, and confirm it persisted.
12. Save two pressure sides for one fruit, reopen, and confirm both persisted.
13. Save pressure without weight and confirm no missing value became zero.
14. Save weight without pressure and confirm no completion validation blocked the save.
15. Confirm exact block matches resolve automatically.
16. Confirm known aliases resolve automatically.
17. Confirm fuzzy block suggestions require explicit confirmation.
18. Confirm new block creation requires explicit confirmation.
19. Confirm numeric mismatches, directional mismatches, and same block names at other orchards are not silently combined.
20. Verify size trend.
21. Verify starch trend.
22. Verify average and peak weight trend.
23. Verify pressure trend.
24. Verify highlighted average pressure change.
25. Verify Orchard Block Master Data create, edit, aliases, and deactivate.
26. Confirm no full block merge/remap control is shown.
27. Confirm Field Samples do not affect Dashboard occupied rooms, current bins, Bins Run, room warnings, Ready-to-Email, receiving email, truck photo status, or inventory adjustments.
28. Capture screenshots for the PR #122 validation record.

## Using Staging To Finish PR #122

1. Merge PR #124 after review.
2. Deploy the staging service.
3. Confirm `/health` returns 200.
4. Confirm Google OAuth login works for the bootstrap admin.
5. Assign role-specific test users through `/Admin/Users`.
6. Rebase PR #122 onto latest `main`.
7. Deploy PR #122 to staging.
8. Have a human complete the Field Sample checklist and provide screenshots.
9. Fix any Field Sample issues on PR #122.
10. Rerun build and tests.
11. Update PR #122 and mark it ready only after workflow validation passes.
