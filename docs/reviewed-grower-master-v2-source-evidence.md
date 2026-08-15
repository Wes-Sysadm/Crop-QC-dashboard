# Reviewed grower master v2 source evidence

This deterministic evidence was generated from the two independently inspected reviewed workbooks. The application reads only the checked-in CSV at runtime; it does not read XLSX files.

## Source identity

- Previous workbook: `pool.xlsx`; 31,013 bytes; SHA-256 `dc34005faca9dc241977c4680d9d52b7dc6682efff5246591ff43ff303fd4e6b`; 405 rows (389 active, 16 inactive).
- Current workbook: `pool(2).xlsx`; `Sheet1!A1:C670`; headers `#`, `Grower`, `POOL Starts`; 40,009 bytes; SHA-256 `13fa493a1dae9573a693cb9e43baeaa04fd51e583abedab1e0338144566ef409`; 669 rows (643 active, 26 inactive).
- Current deterministic CSV SHA-256: `39d89f8a07aa60a0b23b3f54012345818687b3f20f9152898476f1ef78fd7ff9`.
- Normalized duplicate grower numbers: 0.

## Structural comparison

- Common numbers: 405
- New numbers: 264
- Removed numbers: 0
- Raw workbook Grower changes: 400
- Raw workbook Grower values unchanged: 5 (3010, 3070, 6666, 8888, 8999)
- Active to inactive: 10 (1800, 1900, 2100, 2300, 2500, 3340, 3500, 3540, 3570, 6000)
- Inactive to active: 0
- POOL/POOL Starts differences: 1; `3805` is `S2` -> `2S`, informational only.

## Important mappings

| Grower number | New reviewed current name |
|---|---|
| 1050 | MFR - FUJI BLK E ORG |
| 1080 | WP ORCHARD ORG CHIL |
| 1082 | EAST POINT ORG |
| 1084 | WP ORCHARD CONV |
| 1085 | WP ORCHARD ORG |
| 1530 | Baldwin Pears ORG |
| 1531 | Baldwin Pears ORG CHIL |
| 1532 | Baldwin Pears CONV |
| 9392 | MFR - HOOKER PL CONV |

## Review artifacts

The companion `reviewed-grower-master-v2-comparison.csv` contains every one of the 669 numbers with previous/current names, status, pools, category, alias disposition, and the pool-write safeguard. Production-specific operational evidence is captured separately by the guarded sync dry run against a verified restored backup.
