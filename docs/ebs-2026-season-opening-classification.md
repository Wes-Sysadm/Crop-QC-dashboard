# EBS 2026 season-opening inventory classification

This report is a read-only classification of every permanent EBS room-ledger row outside Evans 7 observed in production on 2026-07-31. It is evidence for a separately authorized operational correction; checking in this report does not authorize or apply that correction.

## Verified season boundary

The first legitimate 2026 EBS receipt was derived from persisted data, not from a hard-coded date:

- Receipt ID: `99`
- Receipt number: `108833`
- Received: `2026-07-28 08:36:00` Pacific (`2026-07-28 15:36:00+00` UTC)
- Facility / room: EBS / Evans Street 7 (persisted room ID `17`)
- Crop year / variety: 2026 / Gala
- Grower / lot: DL & JJ FARMS - TRUMBULL / 9290
- Bins: 44
- Test or deleted: no

All 79 classified adjustments outside Evans 7 precede this boundary. The row-level evidence is in `ebs-2026-season-opening-production-evidence.csv`; the reproducible classification is in `ebs-2026-season-opening-classification.csv`.

The checked-in preflight was run read-only against production after implementation and exited successfully. Its fingerprint agrees with the earlier PR #163 cleanup findings: 79 rows outside Evans 7, a net 583-bin carry, 12 Evans 7 receipts, 2 Evans 7 Grower Lots, 10 Evans 7 ledger rows, and an unchanged 388-bin Evans 7 balance.

## Classification totals

| Category | Rows | Treatment |
| --- | ---: | --- |
| Prior-season carry requiring explicit season-opening neutralization | 1 | Neutralize adjustment 1 only; preserve receipt 26. |
| Already-neutralized historical row or net-zero pair | 69 | Preserve unchanged. |
| Likely duplicate or unsupported carryover | 1 | Neutralize adjustment 8 only; preserve receipt 28 and Bins Run history. |
| Real historical run evidence that must be preserved | 8 | Correct four zero-valued source rows and retain four linked Bins Run deductions. |
| Unclear | 0 | The apply package fails closed if the fingerprint changes. |

## Current room balances observed

| EBS room outside Evans 7 | Ledger rows | Current bins | Reviewed result |
| --- | ---: | ---: | ---: |
| Bluemountain 4 | 1 | 34 | 0 |
| BM-1 | 6 | 0 | 0 |
| BM-6 | 10 | 0 | 0 |
| Evans-01 | 9 | 1,039 | 0 |
| Evans-12 | 18 | 0 | 0 |
| Evans-5 | 18 | 0 | 0 |
| Lamb-17 | 6 | 0 | 0 |
| Lamb Street 14 | 11 | -490 | 0 |

## Root causes and reviewed treatment

### Bluemountain 4: 34 bins

Adjustment 1 is a valid pre-season `ReceiptAdd` for receipt 26, but the confirmed business rule is that all pre-boundary EBS inventory had been run. No matching persisted Bins Run exists. The correction changes this adjustment's current-ledger impact from `+34` to `0` while leaving the receipt and historical evidence intact.

### Evans Street 1: 1,039 bins

Adjustment 8 is a `+1,039` ReceiptAdd for soft-deleted receipt 28. The later opening-baseline adjustment 44 (`+1,039`) and linked Bins Run adjustment 62 (`-1,039`) already net to zero. Three other baseline/Bins Run pairs also net to zero. Adjustment 8 is the sole duplicate carry. The correction changes adjustment 8 from `+1,039` to `0`; it does not delete receipt 28 or alter any Bins Run record.

### Lamb Street 14: -490 bins

The negative balance is not corrected by adding a generic `+490`. Four receipt-edit source rows—22, 23, 25, and 26—have persisted old and new bin evidence of 144, 144, 101, and 101 bins but a zero ledger quantity. Existing Bins Run entries 23–26 link those source rows to deductions 76–79 of `-144`, `-144`, `-101`, and `-101`. The correction restores the four missing positive source quantities, leaving every linked deduction and Bins Run unchanged.

## Expected balance effect

The six direct row corrections have a net effect of `-583` bins:

- BM-4: `34` to `0`
- Evans-01: `1,039` to `0`
- Lamb Street 14: `-490` to `0`
- Existing zero-net rooms stay at `0`
- Evans 7 stays at `388`
- WP and every other facility stay unchanged

No receipt, Bins Run, Actual Run, room transfer, or new inventory adjustment is created. A single idempotency/audit marker is created only when the separately authorized apply script commits.
