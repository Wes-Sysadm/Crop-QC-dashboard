# Requirements

## Product

Crop QC Dashboard supports receiving quality control workflows. MVP 1 is limited to Receiving/QC.

## MVP 1 Functional Scope

- Email/password login.
- Roles: Admin, Manager, QC User, Viewer.
- Admin-configurable roles and permissions.
- Password policy configurable from the dashboard:
  - Yearly reset.
  - Minimum 8 characters.
  - At least 1 uppercase letter.
  - At least 1 lowercase letter.
  - At least 1 number.
  - At least 1 symbol.
- Warehouses: EBS, DH, McDougall, WP.
- Admin-editable warehouses and rooms.
- Fruit profile and variety code table.
- Grade list: W1, W2, W3, W4, WF, US1, US2, USF.
- Admin-editable defect list.
- Starch scale list, admin-editable by fruit profile.
- Apple and pear size conversion tables.
- Receipt entry.
- Receiving sample entry.
- 25-row QC grid.
- Actual sample size can be fewer than 25.
- Completed fruit rows require Pressure 1, Pressure 2, weight in grams, and grade.
- Pressure is recorded in lbs.
- Weight is recorded in grams.
- Starch is per fruit and can be added later.
- Starch is required before QC Summary email can be sent.
- Multiple defects per fruit.
- Two USB cameras per QC station:
  - Bin/truck camera.
  - Sample/starch camera.
- Manual photo upload fallback.
- Required photos before sending QC Summary:
  - At least one bin/truck photo.
  - Sample before cutting photo.
  - Cut fruit photo.
  - Fruit after starch photo.
- QC Summary preview before send.
- One email per receipt.
- No batching.
- QC Summary email sends from the logged-in Google Workspace user through Gmail API when `Email__Provider=GmailUser` is configured. Allowed company domains are `fruitandland.com`, `earlbrownandsons.com`, and `wp-packingllc.com`.
- Reply-To is the user who took the sample.
- Managers and Admins can resend with a reason.
- Daily QC dashboard showing received samples and sent/not-sent/ready/missing status.
- Offline capture in Windows QC Station app.
- Sync to the Render/Postgres backend and Google Drive storage when internet returns.
- Photos and attachments stored in Google Shared Drive.
- Database stores metadata and structured data.
- Everything is audit logged.

## Out of Scope for MVP 1

- Storage inventory.
- Room controller imports.
- Mexico qualification.
- Packout imports.
- Pool closing imports.
- Long-term performance analytics.
