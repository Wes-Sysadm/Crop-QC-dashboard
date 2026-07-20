# Staging Test Checklist

Staging/Test is for fake data only. It must be visually labeled and isolated from production.

- Deploy the PR or branch to the staging Render service.
- Confirm the page banner says `STAGING - Non-production data`.
- Confirm `AppEnvironment__Kind=Staging`.
- Confirm `Staging__AllowedTestUserEmails` lists only approved staging test users.
- Confirm staging uses a separate staging Postgres database.
- Confirm staging uses separate Google Drive photo and backup folders.
- Confirm staging OAuth redirect URI is separate from production.
- Confirm staging email recipients are test-only.
- Create fake receipts and fake QC samples only.
- Verify sample creation and editing.
- Verify partial row saves.
- Verify QC email preview and send only to test recipients.
- Verify QC Station fake/test config does not use production station keys.
- Verify Google Drive upload writes to staging/test folders.
- Verify Data Cleanup can purge/reset test data only for allowed cleanup emails.
- Confirm no production database, production Drive folder, production backup folder, or production QC Station config is used.
