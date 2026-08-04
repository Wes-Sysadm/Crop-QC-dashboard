# Backup run 39 classification evidence

Source: independently verified production backup run 39, restored to localhost-only PostgreSQL 18 database `cropqc_prod_run39_restore_20260804`.

Facility credit is derived only from the exact recording employee identity. Crop year is derived from authoritative line/source/receipt metadata. Grower number uses only `Receipts.GrowerNumber`; `GrowerLots.LotNumber` is never used.

| Entry | Run | Recorder | Facility | Crop | Variety | Production | Grower | Bins | Result |
| ---: | ---: | ---: | --- | ---: | --- | --- | --- | ---: | --- |
| 28 | legacy | 8 | WP | 2026 | BART | Conventional | 1084 | 64 | Include |
| 29 | legacy | 8 | WP | 2026 | BART | Conventional | 1084 | 62 | Include |
| 30 | legacy | 8 | WP | 2026 | BART | Conventional | 1084 | 58 | Include |
| 31 | 1 | 8 | WP | 2026 | BART | Conventional | 1084 | 184 | Include |
| 32 | 2 | 8 | WP | 2026 | ORBA | Organic | 1080 | 155 | Include |
| 33 | 3 | 8 | WP | 2026 | ORBA | Organic | missing | 173 | Exclude; Needs Review |
| 34 | 4 | 8 | WP | 2026 | BART | Conventional | 1084 | 196 | Include |
| 35 | 5 | 2 | EBS | 2026 | GALA | Conventional | 9290 | 260 | Include |
| 36 | 5 | 2 | EBS | 2026 | GALA | Conventional | 1565 | 128 | Include |
| 37 | 6 | 8 | WP | 2026 | BART | Conventional | 1084 | 195 | Include |
| 38 | 7 | 8 | WP | 2026 | GALA | Conventional | 1565 | 178 | Include |
| 39 | 7 | 8 | WP | 2026 | GALA | Conventional | 1084 | 8 | Include |

All included lines are active and unreversed. Organic and conventional remain separate. Entry 33 has Grower Lot lot number `1080`, but no authoritative receipt grower number; it is intentionally not assigned a reporting snapshot.
