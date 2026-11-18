# Six treatment-lineage findings: exact evidence annex

Production snapshot: 2026-10-08T03:11:31.590739+00:00; PostgreSQL transaction_read_only=on. All times UTC. Ledger order below uses recorded CreatedAt then Id; the separate effective clock is retained. Negative entries are authoritative consumption/corrections, not negative physical bins.

## Finding 1: DH-15 (warehouse 2, DH) / lot 2350 DANJ

| Evidence | Verified value |
|---|---|
| Room / GrowerLot / profile | 47 / 495 / 18 |
| Exact canonical identity | `2026&#124;495&#124;18&#124;2350&#124;2350&#124;DANJ&#124;CONVENTIONAL&#124;False&#124;` |
| Authority / explicit / excess | 202 / 598 / 396 |
| Initial recorded increment | Ledger 2316; current quantity is the full signed replay below, not that first increment |
| Direct receipt evidence | 1338 (TR508945), 1358 (TR508958), 1373 (TR508944), 1389 (TR508975), 1403 (TR508980), 1539 (TR509011), 1555 (TR509056), 1568 (TR509071), 1569 (TR509072) |
| Readiness code | TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY |
| Canonical treatment / exact receipt confidence | Unknown / Unknown |
| Room applications / segment application links | 0 / 0 |
| Recorded signatures / states | All positive segments and related movement snapshots: `u` / Untreated |
| Audit IDs | 105595, 122214, 123203, 123204 |
| Physical presence | Database custody supported; onsite count and exact surviving receipt allocation unverified |

### Stored segments (all Current; no application links)

| Segment | Receipt | Bins | Version | Status | Created | Last updated |
|---|---|---:|---:|---|---|---|
| 407 | shared | 396 | 4 | blank | 2026-09-03T18:46:18.428215+00:00 | 2026-09-09T23:31:49.201204+00:00 |
| 557 | shared | 202 | 3 | Conventional | 2026-09-10T20:15:05.804867+00:00 | 2026-09-10T20:15:05.899942+00:00 |

### Signed inventory chronology

| Ledger | Recorded UTC | Effective UTC | Type | Delta | Balance | Parent evidence |
|---|---|---|---|---:|---:|---|
| 2316 | 2026-09-01T22:14:57.259707+00:00 | 2026-09-01T21:57:00+00:00 | ReceiptAdd | +64 | 64 | ReceiptId=1338 |
| 2364 | 2026-09-02T17:37:27.272133+00:00 | 2026-09-01T17:19:00+00:00 | ReceiptAdd | +63 | 127 | ReceiptId=1358 |
| 2383 | 2026-09-02T18:16:42.329044+00:00 | 2026-08-31T18:15:00+00:00 | ReceiptAdd | +66 | 193 | ReceiptId=1373 |
| 2399 | 2026-09-02T20:20:39.347628+00:00 | 2026-09-02T20:20:00+00:00 | ReceiptAdd | +60 | 253 | ReceiptId=1389 |
| 2413 | 2026-09-02T22:30:05.596319+00:00 | 2026-09-02T22:09:00+00:00 | ReceiptAdd | +60 | 313 | ReceiptId=1403 |
| 2460 | 2026-09-03T18:46:18.465121+00:00 | 2026-09-03T18:45:00+00:00 | TransferIn | +66 | 379 | RoomTransferId=335 |
| 2790 | 2026-09-06T01:34:56.754001+00:00 | 2026-09-04T01:34:00+00:00 | ReceiptAdd | +23 | 402 | ReceiptId=1539 |
| 2806 | 2026-09-06T19:26:56.0207+00:00 | 2026-09-06T19:24:00+00:00 | ReceiptAdd | +66 | 468 | ReceiptId=1555 |
| 2821 | 2026-09-06T23:22:18.493729+00:00 | 2026-09-06T23:20:00+00:00 | ReceiptAdd | +56 | 524 | ReceiptId=1568 |
| 2822 | 2026-09-06T23:50:27.206959+00:00 | 2026-09-06T23:49:00+00:00 | ReceiptAdd | +64 | 588 | ReceiptId=1569 |
| 2972 | 2026-09-09T23:31:49.216256+00:00 | 2026-09-09T23:25:00+00:00 | TransferOut | -192 | 396 | RoomTransferId=369 |
| 3000 | 2026-09-10T20:13:10.541341+00:00 | 2026-09-10T20:13:10.541341+00:00 | ReceiptAdminOverride | -66 | 330 | ReceiptId=1373, ReceiptInventoryOverrideId=0f977af1-1376-4fa7-91a4-a56bbf512fb0 |
| 3001 | 2026-09-10T20:15:06.042995+00:00 | 2026-09-10T20:14:00+00:00 | TransferOut | -128 | 202 | RoomTransferId=370 |

### Immutable lineage movements

| Movement | Recorded UTC | Effective UTC | Type / bins | Segment flow | Parent |
|---|---|---|---|---|---|
| 342 | 2026-09-03T18:46:18.428215+00:00 | 2026-09-03T18:45:00+00:00 | Transfer / 66 | 406 → 407 | RoomTransferId=335 |
| 506 | 2026-09-09T23:31:49.201204+00:00 | 2026-09-09T23:25:00+00:00 | Transfer / 192 | 407 → 552 | RoomTransferId=369 |
| 516 | 2026-09-10T20:15:05.899942+00:00 | 2026-09-10T20:14:00+00:00 | Transfer / 128 | 557 → 553 | RoomTransferId=370 |

## Finding 2: Evans-5 (warehouse 1, EBS) / lot 3152 GALA

| Evidence | Verified value |
|---|---|
| Room / GrowerLot / profile | 15 / 511 / 2 |
| Exact canonical identity | `2026&#124;511&#124;2&#124;3152&#124;3152&#124;GALA&#124;CONVENTIONAL&#124;False&#124;` |
| Authority / explicit / excess | 61 / 162 / 101 |
| Initial recorded increment | Ledger 2491; current quantity is the full signed replay below, not that first increment |
| Direct receipt evidence | None assigned by destination movements; source ancestry follows below |
| Readiness code | TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY |
| Canonical treatment / exact receipt confidence | Proven / Unknown |
| Room applications / segment application links | 0 / 0 |
| Recorded signatures / states | All positive segments and related movement snapshots: `u` / Untreated |
| Audit IDs | 106635, 106644, 106648 |
| Physical presence | Database custody supported; onsite count and exact surviving receipt allocation unverified |

### Stored segments (all Current; no application links)

| Segment | Receipt | Bins | Version | Status | Created | Last updated |
|---|---|---:|---:|---|---|---|
| 414 | shared | 101 | 3 | blank | 2026-09-04T14:57:43.99909+00:00 | 2026-09-04T15:07:31.459524+00:00 |
| 426 | shared | 61 | 3 | Conventional | 2026-09-04T15:10:39.464615+00:00 | 2026-09-04T15:10:39.473749+00:00 |

### Signed inventory chronology

| Ledger | Recorded UTC | Effective UTC | Type | Delta | Balance | Parent evidence |
|---|---|---|---|---:|---:|---|
| 2491 | 2026-09-04T14:57:44.012635+00:00 | 2026-09-04T14:54:00+00:00 | TransferIn | +25 | 25 | RoomTransferId=337 |
| 2509 | 2026-09-04T15:07:31.468144+00:00 | 2026-09-04T15:07:00+00:00 | TransferIn | +76 | 101 | RoomTransferId=346 |
| 2516 | 2026-09-04T15:10:39.485913+00:00 | 2026-09-04T15:10:00+00:00 | TransferOut | -40 | 61 | RoomTransferId=350 |

### Immutable lineage movements

| Movement | Recorded UTC | Effective UTC | Type / bins | Segment flow | Parent |
|---|---|---|---|---|---|
| 352 | 2026-09-04T14:57:43.99909+00:00 | 2026-09-04T14:54:00+00:00 | Transfer / 25 | 50 → 414 | RoomTransferId=337 |
| 361 | 2026-09-04T15:07:31.459524+00:00 | 2026-09-04T15:07:00+00:00 | Transfer / 76 | 38 → 414 | RoomTransferId=346 |
| 365 | 2026-09-04T15:10:39.473749+00:00 | 2026-09-04T15:10:00+00:00 | Transfer / 40 | 426 → 427 | RoomTransferId=350 |

## Finding 3: Evans-5 (warehouse 1, EBS) / lot 9682 GALA

| Evidence | Verified value |
|---|---|
| Room / GrowerLot / profile | 15 / 642 / 2 |
| Exact canonical identity | `2026&#124;642&#124;2&#124;9682&#124;9682&#124;GALA&#124;CONVENTIONAL&#124;False&#124;` |
| Authority / explicit / excess | 252 / 536 / 284 |
| Initial recorded increment | Ledger 2499; current quantity is the full signed replay below, not that first increment |
| Direct receipt evidence | None assigned by destination movements; source ancestry follows below |
| Readiness code | TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY |
| Canonical treatment / exact receipt confidence | Proven / Unknown |
| Room applications / segment application links | 0 / 0 |
| Recorded signatures / states | All positive segments and related movement snapshots: `u` / Untreated |
| Audit IDs | 106639, 106647, 106649 |
| Physical presence | Database custody supported; onsite count and exact surviving receipt allocation unverified |

### Stored segments (all Current; no application links)

| Segment | Receipt | Bins | Version | Status | Created | Last updated |
|---|---|---:|---:|---|---|---|
| 419 | shared | 284 | 3 | blank | 2026-09-04T15:00:55.020622+00:00 | 2026-09-04T15:08:26.048562+00:00 |
| 428 | shared | 252 | 3 | Conventional | 2026-09-04T15:12:12.603259+00:00 | 2026-09-04T15:12:12.615589+00:00 |

### Signed inventory chronology

| Ledger | Recorded UTC | Effective UTC | Type | Delta | Balance | Parent evidence |
|---|---|---|---|---:|---:|---|
| 2499 | 2026-09-04T15:00:55.033207+00:00 | 2026-09-04T15:00:00+00:00 | TransferIn | +104 | 104 | RoomTransferId=341 |
| 2515 | 2026-09-04T15:08:26.057295+00:00 | 2026-09-04T15:08:00+00:00 | TransferIn | +180 | 284 | RoomTransferId=349 |
| 2518 | 2026-09-04T15:12:12.628742+00:00 | 2026-09-04T15:11:00+00:00 | TransferOut | -32 | 252 | RoomTransferId=351 |

### Immutable lineage movements

| Movement | Recorded UTC | Effective UTC | Type / bins | Segment flow | Parent |
|---|---|---|---|---|---|
| 356 | 2026-09-04T15:00:55.020622+00:00 | 2026-09-04T15:00:00+00:00 | Transfer / 104 | 56 → 419 | RoomTransferId=341 |
| 364 | 2026-09-04T15:08:26.048562+00:00 | 2026-09-04T15:08:00+00:00 | Transfer / 180 | 40 → 419 | RoomTransferId=349 |
| 366 | 2026-09-04T15:12:12.615589+00:00 | 2026-09-04T15:11:00+00:00 | Transfer / 32 | 428 → 429 | RoomTransferId=351 |

## Finding 4: WP-5 (warehouse 4, WP) / lot 1084 GALA

| Evidence | Verified value |
|---|---|
| Room / GrowerLot / profile | 2 / 398 / 2 |
| Exact canonical identity | `2026&#124;398&#124;2&#124;1084&#124;1084&#124;GALA&#124;CONVENTIONAL&#124;False&#124;` |
| Authority / explicit / excess | 10 / 130 / 120 |
| Initial recorded increment | Ledger 130; current quantity is the full signed replay below, not that first increment |
| Direct receipt evidence | 1069 (TR508772), 132 (TR508186), 1339 (TR508908), 169 (TR508223), 189 (TR508232), 201 (TR508244), 206 (TR508249), 318 (TR508306), 378 (TR508330), 444 (TR508367), 633 (TR508489), 709 (TR508534), 767 (TR508570) |
| Readiness code | TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY |
| Canonical treatment / exact receipt confidence | Unknown / Unknown |
| Room applications / segment application links | 0 / 0 |
| Recorded signatures / states | All positive segments and related movement snapshots: `u` / Untreated |
| Audit IDs | 58640, 99709, 120803, 126455 |
| Physical presence | Database custody supported; onsite count and exact surviving receipt allocation unverified |

### Stored segments (all Current; no application links)

| Segment | Receipt | Bins | Version | Status | Created | Last updated |
|---|---|---:|---:|---|---|---|
| 65 | shared | 120 | 5 | blank | 2026-08-20T17:24:21.31066+00:00 | 2026-09-01T21:02:40.777355+00:00 |
| 597 | shared | 10 | 3 | Conventional | 2026-09-14T14:54:49.530913+00:00 | 2026-09-14T14:54:49.543054+00:00 |

### Signed inventory chronology

| Ledger | Recorded UTC | Effective UTC | Type | Delta | Balance | Parent evidence |
|---|---|---|---|---:|---:|---|
| 130 | 2026-08-01T22:03:10.768961+00:00 | 2026-08-01T21:56:00+00:00 | ReceiptAdd | +8 | 8 | ReceiptId=132 |
| 135 | 2026-08-04T00:08:11.01819+00:00 | 2026-08-03T00:07:00+00:00 | BinsRun | -8 | 0 | ActualRunId=7, ActualRunRevisionId=7 |
| 196 | 2026-08-06T23:56:04.741576+00:00 | 2026-08-06T23:55:00+00:00 | ReceiptAdd | +50 | 50 | ReceiptId=169 |
| 221 | 2026-08-07T23:59:01.566453+00:00 | 2026-08-07T23:57:00+00:00 | BinsRun | -34 | 16 | ActualRunId=14, ActualRunRevisionId=14 |
| 223 | 2026-08-08T00:51:30.323093+00:00 | 2026-08-08T00:50:00+00:00 | ReceiptAdd | +66 | 82 | ReceiptId=189 |
| 235 | 2026-08-08T20:02:37.662402+00:00 | 2026-08-08T20:01:00+00:00 | ReceiptAdd | +59 | 141 | ReceiptId=201 |
| 240 | 2026-08-09T20:47:29.760328+00:00 | 2026-08-09T20:40:00+00:00 | ReceiptAdd | +8 | 149 | ReceiptId=206 |
| 410 | 2026-08-13T22:04:25.826698+00:00 | 2026-08-13T22:02:00+00:00 | ReceiptAdd | +39 | 188 | ReceiptId=318 |
| 501 | 2026-08-14T21:27:37.812816+00:00 | 2026-08-14T21:26:00+00:00 | ReceiptAdd | +33 | 221 | ReceiptId=378 |
| 651 | 2026-08-15T21:40:54.534909+00:00 | 2026-08-15T21:40:00+00:00 | ReceiptAdd | +53 | 274 | ReceiptId=444 |
| 1015 | 2026-08-18T23:04:21.10297+00:00 | 2026-08-18T23:03:00+00:00 | ReceiptAdd | +60 | 334 | ReceiptId=633 |
| 1148 | 2026-08-19T23:46:53.116518+00:00 | 2026-08-19T23:46:00+00:00 | ReceiptAdd | +76 | 410 | ReceiptId=709 |
| 1205 | 2026-08-20T17:24:21.295966+00:00 | 2026-08-19T17:23:00+00:00 | BinsRun | -215 | 195 | ActualRunId=38, ActualRunRevisionId=43 |
| 1303 | 2026-08-21T00:33:52.20462+00:00 | 2026-08-21T00:32:00+00:00 | ReceiptAdd | +65 | 260 | ReceiptId=767 |
| 1870 | 2026-08-27T23:05:29.976871+00:00 | 2026-08-27T23:04:00+00:00 | ReceiptAdd | +48 | 308 | ReceiptId=1069 |
| 2300 | 2026-09-01T21:02:40.756502+00:00 | 2026-08-31T18:58:00+00:00 | BinsRun | -188 | 120 | ActualRunId=67, ActualRunRevisionId=73 |
| 2317 | 2026-09-01T22:15:07.076058+00:00 | 2026-08-28T22:13:00+00:00 | ReceiptAdd | +6 | 126 | ReceiptId=1339 |
| 2939 | 2026-09-09T17:38:04.998215+00:00 | 2026-09-09T17:38:04.998215+00:00 | ReceiptAdminOverride | -6 | 120 | ReceiptId=1339, ReceiptInventoryOverrideId=61ff4cac-48ce-4331-8306-8d2b0bdd21a8 |
| 3132 | 2026-09-14T14:54:49.516694+00:00 | 2026-09-11T14:50:00+00:00 | BinsRun | -110 | 10 | ActualRunId=102, ActualRunRevisionId=125 |

### Immutable lineage movements

| Movement | Recorded UTC | Effective UTC | Type / bins | Segment flow | Parent |
|---|---|---|---|---|---|
| 31 | 2026-08-20T17:24:21.320782+00:00 | 2026-08-19T17:23:00+00:00 | BinsRun / 215 | 65 → None | BinsRunEntryId=125 |
| 299 | 2026-09-01T21:02:40.777355+00:00 | 2026-08-31T18:58:00+00:00 | BinsRun / 188 | 65 → None | BinsRunEntryId=221 |
| 573 | 2026-09-14T14:54:49.543054+00:00 | 2026-09-11T14:50:00+00:00 | BinsRun / 110 | 597 → None | BinsRunEntryId=357 |

## Finding 5: WP-7 (warehouse 4, WP) / lot 1372 BART

| Evidence | Verified value |
|---|---|
| Room / GrowerLot / profile | 4 / 448 / 17 |
| Exact canonical identity | `2026&#124;448&#124;17&#124;1372&#124;1372&#124;BART&#124;CONVENTIONAL&#124;False&#124;` |
| Authority / explicit / excess | 1122 / 1568 / 446 |
| Initial recorded increment | Ledger 3225; current quantity is the full signed replay below, not that first increment |
| Direct receipt evidence | 626 (TR508482), 660 (TR508515), 720 (TR508546), 774 (TR508580) |
| Readiness code | TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY |
| Canonical treatment / exact receipt confidence | Proven / Ambiguous |
| Room applications / segment application links | 0 / 0 |
| Recorded signatures / states | All positive segments and related movement snapshots: `u` / Untreated |
| Audit IDs | 127762, 127763, 127764, 127765, 127766, 127767, 127768, 129559, 129562, 129563, 129564, 129565, 129567, 132520, 137045, 145038, 148094, 148095 |
| Physical presence | Database custody supported; onsite count and exact surviving receipt allocation unverified |

### Stored segments (all Current; no application links)

| Segment | Receipt | Bins | Version | Status | Created | Last updated |
|---|---|---:|---:|---|---|---|
| 617 | 774 | 134 | 4 | blank | 2026-09-17T16:29:01.533122+00:00 | 2026-09-17T16:30:28.491918+00:00 |
| 618 | shared | 604 | 3 | blank | 2026-09-17T16:29:01.546926+00:00 | 2026-09-24T16:52:50.420452+00:00 |
| 619 | 660 | 250 | 5 | blank | 2026-09-17T16:30:52.825979+00:00 | 2026-09-18T19:13:00.487129+00:00 |
| 630 | shared | 324 | 3 | Conventional | 2026-09-21T04:33:20.682528+00:00 | 2026-09-21T04:33:20.693509+00:00 |
| 669 | 626 | 72 | 2 | blank | 2026-09-24T16:52:50.39704+00:00 | 2026-09-24T16:52:50.403441+00:00 |
| 670 | 720 | 184 | 2 | blank | 2026-09-24T16:52:50.408119+00:00 | 2026-09-24T16:52:50.41405+00:00 |

### Signed inventory chronology

| Ledger | Recorded UTC | Effective UTC | Type | Delta | Balance | Parent evidence |
|---|---|---|---|---:|---:|---|
| 3225 | 2026-09-17T23:28:00+00:00 | 2026-09-17T23:28:00+00:00 | InterCrewTransferReceive | +66 | 66 | ReceiptId=774, InterCrewTransferId=22 |
| 3227 | 2026-09-17T23:29:00+00:00 | 2026-09-17T23:29:00+00:00 | InterCrewTransferReceive | +64 | 130 | ReceiptId=774, InterCrewTransferId=21 |
| 3228 | 2026-09-17T23:30:00+00:00 | 2026-09-17T23:30:00+00:00 | InterCrewTransferReceive | +66 | 196 | ReceiptId=774, InterCrewTransferId=20 |
| 3229 | 2026-09-17T23:30:00+00:00 | 2026-09-17T23:30:00+00:00 | InterCrewTransferReceive | +66 | 262 | InterCrewTransferId=19 |
| 3230 | 2026-09-17T23:31:00+00:00 | 2026-09-17T23:31:00+00:00 | InterCrewTransferReceive | +64 | 326 | InterCrewTransferId=18 |
| 3231 | 2026-09-17T23:31:00+00:00 | 2026-09-17T23:31:00+00:00 | InterCrewTransferReceive | +62 | 388 | InterCrewTransferId=17 |
| 3287 | 2026-09-19T02:12:00+00:00 | 2026-09-19T02:12:00+00:00 | InterCrewTransferReceive | +58 | 446 | InterCrewTransferId=16 |
| 3375 | 2026-09-21T04:33:20.66777+00:00 | 2026-09-17T04:31:00+00:00 | BinsRun | -122 | 324 | ActualRunId=111, ActualRunRevisionId=134 |
| 3624 | 2026-09-24T23:52:00+00:00 | 2026-09-24T23:52:00+00:00 | InterCrewTransferReceive | +798 | 1122 | InterCrewTransferId=34 |

### Immutable lineage movements

| Movement | Recorded UTC | Effective UTC | Type / bins | Segment flow | Parent |
|---|---|---|---|---|---|
| 601 | 2026-09-17T16:29:01.541312+00:00 | 2026-09-17T23:28:00+00:00 | InterCrewReceive / 4 | 159 → 617 | InterCrewTransferId=22, ReceiptId=774 |
| 602 | 2026-09-17T16:29:01.552812+00:00 | 2026-09-17T23:28:00+00:00 | InterCrewReceive / 62 | 160 → 618 | InterCrewTransferId=22 |
| 603 | 2026-09-17T16:30:04.768088+00:00 | 2026-09-17T23:29:00+00:00 | InterCrewReceive / 64 | 159 → 617 | InterCrewTransferId=21, ReceiptId=774 |
| 604 | 2026-09-17T16:30:28.49193+00:00 | 2026-09-17T23:30:00+00:00 | InterCrewReceive / 66 | 159 → 617 | InterCrewTransferId=20, ReceiptId=774 |
| 605 | 2026-09-17T16:30:52.835749+00:00 | 2026-09-17T23:30:00+00:00 | InterCrewReceive / 66 | 156 → 619 | InterCrewTransferId=19, ReceiptId=660 |
| 606 | 2026-09-17T16:31:16.336638+00:00 | 2026-09-17T23:31:00+00:00 | InterCrewReceive / 64 | 156 → 619 | InterCrewTransferId=18, ReceiptId=660 |
| 607 | 2026-09-17T16:31:40.284331+00:00 | 2026-09-17T23:31:00+00:00 | InterCrewReceive / 62 | 156 → 619 | InterCrewTransferId=17, ReceiptId=660 |
| 615 | 2026-09-18T19:13:00.487158+00:00 | 2026-09-19T02:12:00+00:00 | InterCrewReceive / 58 | 156 → 619 | InterCrewTransferId=16, ReceiptId=660 |
| 623 | 2026-09-21T04:33:20.693509+00:00 | 2026-09-17T04:31:00+00:00 | BinsRun / 122 | 630 → None | BinsRunEntryId=377 |
| 703 | 2026-09-24T16:52:50.403445+00:00 | 2026-09-24T23:52:00+00:00 | InterCrewReceive / 72 | 154 → 669 | InterCrewTransferId=34, ReceiptId=626 |
| 704 | 2026-09-24T16:52:50.414053+00:00 | 2026-09-24T23:52:00+00:00 | InterCrewReceive / 184 | 148 → 670 | InterCrewTransferId=34, ReceiptId=720 |
| 705 | 2026-09-24T16:52:50.420454+00:00 | 2026-09-24T23:52:00+00:00 | InterCrewReceive / 542 | 160 → 618 | InterCrewTransferId=34 |

## Finding 6: WP-8 (warehouse 4, WP) / lot 2350 DANJ

| Evidence | Verified value |
|---|---|
| Room / GrowerLot / profile | 5 / 495 / 18 |
| Exact canonical identity | `2026&#124;495&#124;18&#124;2350&#124;2350&#124;DANJ&#124;CONVENTIONAL&#124;False&#124;` |
| Authority / explicit / excess | 170 / 362 / 192 |
| Initial recorded increment | Ledger 2677; current quantity is the full signed replay below, not that first increment |
| Direct receipt evidence | 1532 (TR509046), 1572 (TR509079), 1587 (TR509078), 1594 (TR509078), 1626 (TR509101), 1754 (TR509156) |
| Readiness code | TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY |
| Canonical treatment / exact receipt confidence | Unknown / Unknown |
| Room applications / segment application links | 0 / 0 |
| Recorded signatures / states | All positive segments and related movement snapshots: `u` / Untreated |
| Audit IDs | 122214, 122217, 123204, 126585, 137043, 137053 |
| Physical presence | Database custody supported; onsite count and exact surviving receipt allocation unverified |

### Stored segments (all Current; no application links)

| Segment | Receipt | Bins | Version | Status | Created | Last updated |
|---|---|---:|---:|---|---|---|
| 552 | shared | 192 | 2 | blank | 2026-09-09T23:31:49.201204+00:00 | 2026-09-09T23:31:49.201204+00:00 |
| 553 | shared | 170 | 6 | Conventional | 2026-09-09T23:45:30.21799+00:00 | 2026-09-21T04:42:41.170619+00:00 |

### Signed inventory chronology

| Ledger | Recorded UTC | Effective UTC | Type | Delta | Balance | Parent evidence |
|---|---|---|---|---:|---:|---|
| 2677 | 2026-09-05T23:45:41.262826+00:00 | 2026-09-05T23:22:00+00:00 | ReceiptAdd | +60 | 60 | ReceiptId=1532 |
| 2825 | 2026-09-07T15:37:45.142663+00:00 | 2026-09-06T15:36:00+00:00 | ReceiptAdd | +64 | 124 | ReceiptId=1572 |
| 2843 | 2026-09-07T19:50:57.91652+00:00 | 2026-09-06T19:50:00+00:00 | ReceiptAdd | +60 | 184 | ReceiptId=1587 |
| 2857 | 2026-09-07T21:36:14.672598+00:00 | 2026-09-06T21:26:00+00:00 | ReceiptAdd | +60 | 244 | ReceiptId=1594 |
| 2901 | 2026-09-08T20:58:35.496301+00:00 | 2026-09-07T20:57:00+00:00 | ReceiptAdd | +6 | 250 | ReceiptId=1626 |
| 2973 | 2026-09-09T23:31:49.216418+00:00 | 2026-09-09T23:25:00+00:00 | TransferIn | +192 | 442 | RoomTransferId=369 |
| 2975 | 2026-09-09T23:45:30.203292+00:00 | 2026-09-09T23:43:00+00:00 | BinsRun | -237 | 205 | ActualRunId=91, ActualRunRevisionId=110 |
| 3002 | 2026-09-10T20:15:06.04326+00:00 | 2026-09-10T20:14:00+00:00 | TransferIn | +128 | 333 | RoomTransferId=370 |
| 3138 | 2026-09-14T16:39:57.507951+00:00 | 2026-09-10T16:39:00+00:00 | ReceiptAdd | +64 | 397 | ReceiptId=1754 |
| 3142 | 2026-09-14T17:13:10.346474+00:00 | 2026-09-14T17:13:10.346474+00:00 | ReceiptAdminOverride | -64 | 333 | ReceiptId=1754, ReceiptInventoryOverrideId=95eeef85-d8d8-4da4-a2b2-dfbd68e27656 |
| 3374 | 2026-09-21T04:30:03.624568+00:00 | 2026-09-14T04:29:00+00:00 | BinsRun | -23 | 310 | ActualRunId=110, ActualRunRevisionId=133 |
| 3380 | 2026-09-21T04:42:41.155551+00:00 | 2026-09-20T04:41:00+00:00 | BinsRun | -140 | 170 | ActualRunId=115, ActualRunRevisionId=138 |

### Immutable lineage movements

| Movement | Recorded UTC | Effective UTC | Type / bins | Segment flow | Parent |
|---|---|---|---|---|---|
| 506 | 2026-09-09T23:31:49.201204+00:00 | 2026-09-09T23:25:00+00:00 | Transfer / 192 | 407 → 552 | RoomTransferId=369 |
| 507 | 2026-09-09T23:45:30.229327+00:00 | 2026-09-09T23:43:00+00:00 | BinsRun / 237 | 553 → None | BinsRunEntryId=313 |
| 516 | 2026-09-10T20:15:05.899942+00:00 | 2026-09-10T20:14:00+00:00 | Transfer / 128 | 557 → 553 | RoomTransferId=370 |
| 622 | 2026-09-21T04:30:03.642615+00:00 | 2026-09-14T04:29:00+00:00 | BinsRun / 23 | 553 → None | BinsRunEntryId=376 |
| 628 | 2026-09-21T04:42:41.170619+00:00 | 2026-09-20T04:41:00+00:00 | BinsRun / 140 | 553 → None | BinsRunEntryId=382 |

## Additional positive positions requiring canonical reconciliation

These 35 positions total 10,265 bins in the read-only restored diagnostic. They exactly match the prior live canonical readiness count. All pre-existing operational rows were compared to the current production snapshot: unchanged. The only additions are receipts 2510–2514 (230 bins), ledger 4187–4191, segments 806–810 and movements 870–874; their three positions are outside this list. Application/link/transfer/correction rows are unchanged. This validates these 35 findings against current production without claiming that backup 186 contains the five newer receipts.

| Room / warehouse | GrowerLot / profile | Lot / variety | Authority | Projection | Canonical blocker |
|---|---|---|---:|---:|---|
| 2 / 4 (WP-5) | 398 / 2 | 1084 / GALA | 10 | 130 | UnsupportedHistoricalEvidence |
| 5 / 4 (WP-8) | 495 / 18 | 2350 / DANJ | 170 | 362 | UnsupportedHistoricalEvidence |
| 7 / 1 (LAMB-14) | 574 / 9 | 9332 / ORGS | 155 | 0 | UnknownTreatment |
| 9 / 1 (LAMB-16) | 623 / 10 | 9541 / ORHC | 228 | 30 | UnknownTreatment |
| 10 / 1 (Lamb-17) | 628 / 14 | 9562 / RED | 972 | 933 | UnknownTreatment |
| 11 / 1 (Evans-01) | 132 / 2 | 1565 / GALA | 140 | 0 | UnknownTreatment |
| 11 / 1 (Evans-01) | 331 / 2 | 9380 / GALA | 147 | 0 | UnknownTreatment |
| 12 / 1 (EVANS-2) | 399 / 7 | 1011 / ORGA | 1153 | 1049 | UnknownTreatment |
| 12 / 1 (EVANS-2) | 574 / 7 | 9332 / ORGA | 180 | 0 | UnknownTreatment |
| 15 / 1 (Evans-5) | 399 / 7 | 1011 / ORGA | 350 | 189 | UnknownTreatment |
| 15 / 1 (Evans-5) | 579 / 2 | 9342 / GALA | 77 | 0 | UnknownTreatment |
| 17 / 1 (EVANS-7) | 505 / 2 | 3032 / GALA | 113 | 105 | UnknownTreatment |
| 18 / 1 (EVANS-8) | 505 / 2 | 3032 / GALA | 55 | 11 | UnknownTreatment |
| 18 / 1 (EVANS-8) | 336 / 2 | 3050 / GALA | 275 | 275 | MixedTreatmentAmbiguity |
| 21 / 1 (EVANS-11) | 520 / 10 | 3241 / ORHC | 146 | 0 | UnknownTreatment |
| 21 / 1 (EVANS-11) | 623 / 10 | 9541 / ORHC | 378 | 129 | UnknownTreatment |
| 22 / 1 (Evans-12) | 649 / 9 | 9751 / ORGS | 510 | 52 | UnknownTreatment |
| 28 / 1 (BM-2) | 511 / 14 | 3152 / RED | 120 | 0 | UnknownTreatment |
| 28 / 1 (BM-2) | 517 / 14 | 3192 / RED | 214 | 54 | UnknownTreatment |
| 28 / 1 (BM-2) | 600 / 14 | 9432 / RED | 350 | 75 | UnknownTreatment |
| 30 / 1 (BM-4) | 97 / 14 | 9285 / RED | 34 | 0 | UnknownTreatment |
| 47 / 2 (DH-15) | 495 / 18 | 2350 / DANJ | 202 | 598 | UnsupportedHistoricalEvidence |
| 49 / 2 (DH-17) | 396 / 9 | 1082 / ORGS | 271 | 0 | UnknownTreatment |
| 51 / 2 (DH-19) | 468 / 7 | 1451 / ORGA | 253 | 140 | UnknownTreatment |
| 54 / 2 (DH-22) | 395 / 9 | 1081 / ORGS | 403 | 2 | UnknownTreatment |
| 57 / 3 (MCD-05) | 57 / 7 | 2200 / ORGA | 280 | 0 | UnknownTreatment |
| 57 / 3 (MCD-05) | 186 / 7 | 2710 / ORGA | 323 | 0 | UnknownTreatment |
| 57 / 3 (MCD-05) | 184 / 7 | 2910 / ORGA | 69 | 42 | UnknownTreatment |
| 58 / 3 (MCD-06) | 14 / 21 | 2825 / ORDA | 537 | 340 | UnknownTreatment |
| 63 / 3 (MCD-11) | 186 / 7 | 2710 / ORGA | 426 | 159 | UnknownTreatment |
| 67 / 3 (MCD-15) | 126 / 19 | 1110 / ORBA | 286 | 0 | UnknownTreatment |
| 67 / 3 (MCD-15) | 272 / 19 | 1290 / ORBA | 5 | 0 | UnknownTreatment |
| 67 / 3 (MCD-15) | 14 / 19 | 2825 / ORBA | 524 | 0 | UnknownTreatment |
| 73 / 3 (MCD-02) | 430 / 14 | 1242 / RED | 652 | 442 | MixedTreatmentAmbiguity |
| 73 / 3 (MCD-02) | 187 / 14 | 2750 / RED | 257 | 183 | MixedTreatmentAmbiguity |

## Depleted authority with positive projection

13 positions, 644 projected bins. These are additional stale-projection candidates, not 644 bins of current physical stock. The existing positive-inventory excess gate does not report them. No automatic retirement is justified solely by zero authority.

| Room | GrowerLot / profile | Lot / variety | Authority | Positive projection | All associated segment IDs (including depleted) |
|---|---|---|---:|---:|---|
| 7 | 94 / 2 | 9040 / GALA | 0 | 228 | 123, 124, 125, 128, 142, 162 |
| 7 | 98 / 2 | 9100 / GALA | 0 | 6 | 145, 284 |
| 7 | 331 / 2 | 9380 / GALA | 0 | 3 | 351, 555 |
| 8 | 331 / 2 | 9380 / GALA | 0 | 40 | 177, 178, 179, 181 |
| 15 | 588 / 10 | 9401 / ORHC | 0 | 8 | 432, 433 |
| 16 | 643 / 10 | 9691 / ORHC | 0 | 1 | 441, 570 |
| 20 | 336 / 2 | 3050 / GALA | 0 | 48 | 79, 93 |
| 20 | 227 / 10 | 3290 / ORHC | 0 | 35 | 533, 560, 562 |
| 20 | 593 / 10 | 9414 / ORHC | 0 | 6 | 540, 605 |
| 20 | 623 / 10 | 9541 / ORHC | 0 | 4 | 532, 537 |
| 20 | 250 / 10 | 9820 / ORHC | 0 | 3 | 564, 608 |
| 21 | 559 / 2 | 9092 / GALA | 0 | 10 | 425, 556 |
| 60 | 292 / 21 | 1600 / ORDA | 0 | 252 | 335, 541 |

## Negative current canonical positions

Five positions total -127 bins. This is a ledger deficit requiring historical reconciliation; it is not proof of negative physical fruit or an authorization to true up. It differs from the deduction verifier’s 1,079 individual negative transactions (93 legacy and 986 newer-format), which had zero structural blockers.

| Room | GrowerLot / profile | Lot / variety | Net |
|---|---|---|---:|
| 60 | 141 / 21 | 1270 / ORDA | -40 |
| 60 | 477 / 29 | 1538 / RDAN | -18 |
| 60 | 502 / 18 | 2822 / DANJ | -5 |
| 61 | 476 / 18 | 1537 / DANJ | -20 |
| 61 | 478 / 18 | 1539 / DANJ | -44 |
