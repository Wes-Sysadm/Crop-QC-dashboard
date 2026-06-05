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
- Recipient: configured `Email__ToAddress`, default `QC@fruitandland.com`.
- Reply-To: the user who took the sample, when available.

## Expected Send Rules

Normal QC Summary sending should require:

- At least one completed fruit row.
- Pressure 1, Pressure 2, weight, grade, and starch for every completed row.
- Required photos: BinTruck, SampleBeforeCutting, CutFruit, and FruitAfterStarch.

Normal ready-sample sending is available to Admin, Manager, and QC User roles. Manager/Admin override send can send even when required data is missing, with an override reason. Viewer users cannot send.

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
