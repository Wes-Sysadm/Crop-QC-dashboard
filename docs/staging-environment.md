# Staging Environment

This runbook defines the safe staging path for authenticated workflow validation without weakening production authentication or sharing production data.

## Architecture

- Hosting: Render Docker web service using the same `Dockerfile` as production.
- Service: `crop-qc-dashboard-staging`.
- Database: separate Render PostgreSQL database `crop-qc-dashboard-staging-db`.
- Authentication: existing Google OAuth plus application cookie authentication.
- Authorization: existing user and page-access matrix.
- File storage: local staging disk by default at `/var/data/cropqc-staging-files`.
- Email: disabled with `Email__Provider=None`.
- Diagnostics: existing request performance diagnostics, visible only to Configuration Admin users in staging.

The staging service runs with `ASPNETCORE_ENVIRONMENT=Production` so it behaves like hosted production, and uses `AppEnvironment__Kind=Staging` to activate staging-only safety checks and the visible banner.

## Required Secrets

Set these in Render. Do not commit their values.

- `ConnectionStrings__CropQc`, injected from `crop-qc-dashboard-staging-db`
- `Authentication__Google__ClientId`
- `Authentication__Google__ClientSecret`
- `Authentication__BootstrapAdminEmails`
- `Staging__AllowedTestUserEmails`

## Startup Safety Checks

When `AppEnvironment__Kind=Staging`, startup fails if:

- the database provider is not PostgreSQL
- the staging database connection string is missing
- the connection string contains configured production database markers
- Google OAuth client ID or secret is missing
- no allowed Google domains are configured
- no explicit staging test-user allowlist is configured
- a staging test user is outside the allowed Google domains
- `Email__Provider` is not `None`
- Google Drive storage is enabled without explicit isolation confirmation
- Google Drive folder IDs match configured production folder IDs
- performance diagnostics are disabled or have no bounded retention
- `QcStation__ApiBaseUrl` points at the production Render service

The checks log no secret values.

## Google OAuth

Create a separate Google OAuth web client for staging when practical.

- Authorized redirect URI: `https://crop-qc-dashboard-staging.onrender.com/signin-google`
- Authorized JavaScript origin: `https://crop-qc-dashboard-staging.onrender.com` if required
- Allowed Workspace domains: `Authentication__AllowedGoogleDomains`
- Explicit test accounts: `Staging__AllowedTestUserEmails`

Staging login still uses the normal Google OAuth flow. After Google validates the account, the app rejects users who are not on the staging allowlist.

## Test Users

Create or allowlist separate Google Workspace test users for each role:

- Field Sample administrator: view/edit/admin access after PR #122 is rebased onto this staging work.
- Field Sample editor: view/edit access, no admin access.
- Field Sample viewer: view access only.
- Unauthorized user: authenticated Google account with no Field Sample permissions.

Grant permissions through the existing `/Admin/Users` access matrix. Do not hard-code roles, impersonation, or test-login cookies.

## Email Isolation

Staging is configured with:

```text
Email__Provider=None
Email__QcDefaultRecipients=
```

Operational Gmail sends are disabled. A future mail-sink mode must be reviewed separately and must not silently use production recipients.

## Google Drive Isolation

Staging defaults to local disk storage:

```text
FileStorage__Provider=Local
FileStorage__LocalRootPath=/var/data/cropqc-staging-files
```

This prevents staging photo uploads from writing to the production Google Shared Drive. If staging is later switched to Google Drive, use a separate staging shared drive or folder and set `Staging__GoogleDriveIsolationConfirmed=true` only after confirming the IDs do not match production.

## Database Setup And Reset

Create staging with the Render database defined in `render.yaml`, or create a separate PostgreSQL database manually with unique credentials.

Apply migrations with the existing migration smoke-test tooling:

```powershell
.\scripts\Invoke-MigrationSmokeTest.ps1 -Provider PostgreSql -ConnectionString "<staging connection string>"
```

To reset staging:

1. Confirm the target database name is `cropqc_staging` or another approved staging-only database.
2. Back up any staging data worth keeping.
3. Drop and recreate only the staging database.
4. Reapply migrations.
5. Recreate or seed test users and permissions.

Never reset or seed the production database.

## Test Data

Use deterministic, fictional data only. Examples:

- Test Orchard North
- Test Orchard South
- Block 12
- River Bottom
- Phoenix Test Block
- Test Grower A

Seed data must be opt-in and blocked in production. PR #122 adds Field Sample tables and should be rebased onto latest `main` after this staging work merges; Field Sample-specific test data can then be created through the authenticated UI or a staging-only seed command in that PR.

## Diagnostics

Enable staging diagnostics with:

```text
PerformanceDiagnostics__Enabled=true
PerformanceDiagnostics__RecentRequestLimit=250
PerformanceDiagnostics__IncludeUserIdentifier=false
```

Configuration Admin users can open:

```text
/Admin/Diagnostics/Requests
```

The page shows aggregate route, status, elapsed time, EF command count, cumulative EF time, response bytes, external-call counts, warnings, and trace IDs. It does not show SQL text, SQL parameters, request bodies, response bodies, cookies, OAuth tokens, photo URLs, or credentials.

The diagnostics page returns 404 outside staging.

## Deployment Workflow

1. Merge the staging infrastructure branch to `main`.
2. Create or sync the Render staging service from `render.yaml`.
3. Set all required staging secrets in Render.
4. Deploy `main` to the staging service.
5. Apply migrations to the staging database.
6. Sign in with the bootstrap staging admin.
7. Assign role-specific test-user permissions in `/Admin/Users`.
8. Rebase validation PRs, such as PR #122, onto latest `main`.
9. Deploy the validation branch to staging manually.
10. Use `/Admin/Diagnostics/Requests` while capturing manual timings and screenshots.

Avoid automatic deployment of every unreviewed PR unless isolated OAuth redirect URIs, databases, email, and file storage are already configured.

## Troubleshooting

- Login redirects to HTTP: confirm `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
- Login says Google OAuth is not configured: set staging OAuth client ID and secret.
- Login succeeds in Google but app rejects the account: add the email to `Staging__AllowedTestUserEmails` and ensure its domain is allowed.
- Startup fails with staging configuration errors: fix the listed setting; no secret values are printed.
- Diagnostics page 404s: confirm `AppEnvironment__Kind=Staging`.
- Diagnostics page 403s: grant Configuration Admin access through `/Admin/Users`.
- Photo upload writes to unexpected storage: verify `FileStorage__Provider=Local` or use a separate staging Drive folder with isolation confirmed.

## Security Boundaries

Staging must not:

- use production database credentials
- copy production data automatically
- use production Google Drive folders
- send Gmail messages to operational recipients
- use production OAuth tokens
- weaken Google OAuth or cookie authentication
- expose diagnostics to normal users
