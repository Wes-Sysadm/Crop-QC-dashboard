# Gmail Email Plan

MVP 1 currently creates QC Summary email log placeholder records only. This document defines the target Gmail direction. It does not implement real email sending yet.

## Target Provider Options

Use one of these Google Workspace paths:

- Gmail API with OAuth/service-account domain delegation where appropriate.
- Google Workspace SMTP relay if the organization prefers relay-based sending.

The email boundary should remain provider-based so Gmail can be swapped or tested without changing QC workflow code.

## Reserved Addresses

- Sender: `HL@fruitandland.com`
- Recipient: `QC@fruitandland.com`
- Reply-To: the user who took the sample, when available.

## Expected Send Rules

Normal QC Summary sending should require:

- At least one completed fruit row.
- Pressure 1, Pressure 2, weight, grade, and starch for every completed row.
- Required photos: BinTruck, SampleBeforeCutting, CutFruit, and FruitAfterStarch.

Override send remains a Manager/Admin workflow placeholder until authentication/authorization is implemented.

## Metadata And Audit

Each send or resend should create a `QcSummaryEmailLog` row with:

- From, To, Reply-To, subject, status, and message ID.
- User who sent the message.
- Sent timestamp.
- Resend or override reason when applicable.
- Report/body snapshot or durable report reference when practical.

Email send, resend, and override actions must be audit logged.
