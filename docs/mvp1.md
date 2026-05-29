# MVP 1 - Receiving/QC

MVP 1 is Receiving/QC only. It does not include storage inventory, room controller imports, Mexico qualification, packout imports, pool closing imports, or long-term performance analytics.

## Users and Security

- Email/password login.
- Roles: Admin, Manager, QC User, Viewer.
- Roles and permissions are admin-configurable.
- Password policy is configurable from the dashboard:
  - Yearly reset.
  - Minimum 8 characters.
  - At least 1 uppercase letter.
  - At least 1 lowercase letter.
  - At least 1 number.
  - At least 1 symbol.

## Master Data

- Warehouses: EBS, DH, McDougall, WP.
- Warehouses and rooms are admin-editable.
- Fruit profile and variety code table.
- Grade list: W1, W2, W3, W4, WF, US1, US2, USF.
- Defect list is admin-editable.
- Starch scale list is admin-editable by fruit profile.
- Apple and pear size conversion tables.

## Receiving and QC

- Receipt entry.
- Receiving sample entry.
- 25-row QC grid.
- Actual sample size can be fewer than 25.
- Completed fruit rows require:
  - Pressure 1.
  - Pressure 2.
  - Weight in grams.
  - Grade.
- Pressure is lbs.
- Weight is grams.
- Starch is per fruit and can be added later.
- Starch is required before QC Summary email can be sent.
- Multiple defects can be recorded per fruit.

## Photos

Each QC station supports two USB cameras:

- Bin/truck camera.
- Sample/starch camera.

Manual photo upload is required as a fallback.

Required photos before sending QC Summary:

- At least one bin/truck photo.
- Sample before cutting photo.
- Cut fruit photo.
- Fruit after starch photo.

Photos and attachments will be stored in Google Shared Drive. The database stores metadata and structured data only.

## QC Summary Email

- QC Summary preview is required before send.
- One email per receipt.
- No batching.
- Email sends from `HL@fruitandland.com` to `QC@fruitandland.com`.
- Reply-To is the user who took the sample.
- Managers and Admins can resend with a reason.

## Dashboard

The daily QC dashboard shows received samples and whether each sample is sent, not sent, ready, or missing required information.

## Offline QC Station

- Offline capture is required in the Windows QC Station app.
- Sync to the Render/Postgres backend and Google Drive storage when internet returns.
- SQLite local cache will be used for offline QC Station data.
- Design with sync boundaries in mind.

## Audit

Everything is audit logged, including create, edit, delete, send, import, and export actions.

## Starting Variety Codes

| Variety | Code | Fruit |
| --- | --- | --- |
| Fuji | FUJI | Apple |
| Gala | GALA | Apple |
| Golden Delicious | GOLD | Apple |
| Granny Smith | GSMT | Apple |
| Honey Crisp | HONY | Apple |
| Organic Fuji | ORFU | Apple |
| Organic Gala | ORGA | Apple |
| Organic Golden Delicious | ORGD | Apple |
| Organic Granny Smith | ORGS | Apple |
| Organic Honey Crisp | ORHC | Apple |
| Organic Pink Lady | ORPL | Apple |
| Organic Red Delicious | ORRD | Apple |
| Pink Lady | PINK | Apple |
| Red Delicious | RED | Apple |
| Mardi Gras | MDGS | Pear |
| Bosc | BOSC | Pear |
| Bartlett | BART | Pear |
| D'Anjou | DANJ | Pear |
| Organic Bartlett | ORBA | Pear |
| Organic Bosc | ORBO | Pear |
| Organic D'anjou | ORDA | Pear |
| Autumn Glory | ATGL | Apple |

## Size Conversion Rules

Weights are minimum thresholds, not closest-match values. Fruit should be assigned the largest size category it qualifies for. If the fruit weight is below the smallest threshold, mark it as Undersized.

### Apple

| Size | Minimum Weight Grams |
| --- | ---: |
| 48 | 405.0000 |
| 56 | 354.0000 |
| 64 | 298.0000 |
| 72 | 264.0000 |
| 80 | 238.0000 |
| 88 | 215.0000 |
| 100 | 190.0000 |
| 113 | 167.0000 |
| 125 | 153.0000 |
| 138 | 136.0000 |
| 150 | 128.0000 |
| 163 | 116.0000 |
| 175 | 108.0000 |
| 198 | 96.0000 |
| 216 | 88.0000 |

### Pear

| Size | Minimum Weight Grams |
| --- | ---: |
| 50 | 360.0000 |
| 60 | 303.0000 |
| 70 | 260.0000 |
| 80 | 227.0000 |
| 90 | 203.0000 |
| 100 | 182.0000 |
| 110 | 165.0000 |
| 120 | 151.0000 |
| 135 | 135.0000 |
| 150 | 121.0000 |
| 165 | 110.0000 |
| 180 | 101.0000 |
| 193 | 94.0000 |
| 210 | 87.0000 |
| 225 | 81.0000 |
