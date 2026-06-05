# Gmail User-Delegated Email Sending

MVP 1 sends QC Summary emails through the logged-in user's Google/Gmail account when `Email__Provider=GmailUser` is configured. A message sent by `wes@fruitandland.com` is sent by Gmail as `wes@fruitandland.com` and should appear in that user's Gmail Sent folder.

## Provider

The production provider is:

```text
Email__Provider=GmailUser
Google__Gmail__SendScope=https://www.googleapis.com/auth/gmail.send
```

Local development can keep `Email__Provider=None` to disable real sending. Shared SMTP is not the normal sending identity.

## Sender And Recipients

- Sender: the logged-in Google user.
- Recipients: configured `Email__QcDefaultRecipients`.
- Current testing recipients: `rob@earlbrownandsons.com,wes@fruitandland.com`.
- Reply-To: the user who took the sample, when available.

Do not use `QC@fruitandland.com` or broader distribution groups during the current email test phase unless the recipients are manually changed later. Update `Email__QcDefaultRecipients` before production rollout if the recipient list changes.

## Expected Send Rules

Normal QC Summary sending should require:

- At least one completed fruit row.
- Pressure 1, Pressure 2, weight, grade, and starch for every completed row.
- Required photos based on sample type:
  - Receiving: Truck photo (`BinTruck`), Top of truck (`TopOfTruck`), Hectre (`Hectre`), Whole sample (`SampleBeforeCutting`), Cut apples (`CutFruit`), and Starch apples (`FruitAfterStarch`).
  - Transfer: Truck photo (`BinTruck`), Top of truck (`TopOfTruck`), Whole sample (`SampleBeforeCutting`), and Cut apples (`CutFruit`). Hectre can be attached when applicable, but it does not block normal send by default.
  - Door or Room: Whole sample (`SampleBeforeCutting`) and Cut apples (`CutFruit`).
  - Line: Whole sample (`SampleBeforeCutting`) and Cut apples (`CutFruit`). Hectre can be attached when applicable, but it does not block normal send by default.

Normal ready-sample sending is available to Admin, Manager, and QC User roles. Manager/Admin override send can send even when required data is missing, with an override reason. Viewer users cannot send.

## Email Body Format

QC Summary emails are body-first HTML emails with a plain text fallback. The normal path does not generate a summary PDF, Excel file, or other data attachment.

The HTML body includes:

- Header context: receipt, sample type, warehouse, room, grower, lot, variety, sample date/time, and inspector when available.
- Summary table at the top with completed fruit count, average pressure, average starch, average weight, grade summary, defect summary, size/status summary, and notes.
- Line-by-line fruit overview with row number, pressures, average pressure, weight, grade, starch, size/category, defects, and notes.
- Photo sections grouped by friendly photo requirement name.
- Photo sections appear in this order when present: Truck photo, Top of truck, Hectre, Whole sample, Cut apples, and Starch apples.

Required and present photos are embedded inline with MIME `Content-ID` values and referenced from the HTML body using `cid:` image URLs. If the configured storage provider cannot return image bytes, the email falls back to a safe photo link when one is available. Tokens and storage credentials are never included in the email or logs.

## OAuth And Reconnect

Google OAuth requests `https://www.googleapis.com/auth/gmail.send` and offline access. Users may need to log out and sign back in after the scope is added so Google can show the new consent prompt.

If Gmail permission or a refresh token is missing, the UI shows:

```text
Gmail permission is required. Please reconnect Google/Gmail.
```

Reconnect by signing out and signing back in with the Gmail send permission.

## Token Security

Google access and refresh tokens are stored in `UserGoogleCredentials` encrypted with ASP.NET Core Data Protection. The app reuses the persisted Data Protection key setup used for login cookies on Render.

Tokens are not logged, displayed in the UI, or stored in auth cookies after login processing. Deactivating a user blocks dashboard access and stops future sends.

## Metadata And Audit

Each send or resend should create a `QcSummaryEmailLog` row with:

- From, To, Reply-To, subject, status, and message ID.
- User who sent the message.
- Sent timestamp.
- Resend or override reason when applicable.
- Gmail message ID when returned.
- Safe success/failure status and safe failure reason.

Email send, resend, and override actions are audit logged. Tokens and secrets are never written to email logs or audit logs.
