## Problem and change

Explain the defect/request, root cause, before/after behavior and affected paths.

## Rule compliance assessment

- Applicable rule IDs:
- Implementation paths and authoritative records versus projections:
- Proposed behavior and potential conflicts:
- Authoritative inventory changes (yes/no and why):
- Treatment lineage changes:
- Historical records changed/preserved:
- New origins or writers (registry review):
- Exact tests/provider/results proving compliance:
- Unverified rules, skipped bodies and unresolved assumptions:
- Production-data implications and authorization:
- Business-policy approval required/reference:

## Governance change

If governance, tests, writer registry or CI is changed, complete each line below.
Otherwise replace this section's contents with Not applicable.
Do not claim automated checks authenticate human approval.

Previous:
New:
Impact:
Tests/history:
Approval:

## Release authorization

Status: Not requested

Use `Owner authorized` only when an original explicit owner instruction exists.
Record it here without asking the owner to repeat it in GitHub. Required fields:
Owner, Reference (original message/link and instruction), Date (YYYY-MM-DD),
Head (full reviewed PR SHA), Scope (`Merge only` or `Merge and production release`),
and Exclusions. Use `Revoked` after revocation. Policy approval or green CI alone
is not release authority. Metadata checks do not authenticate the owner, clear
technical safeguards, or bypass current repository/explicit review requirements.

## Verification and release limits

Restore/build, focused tests, EF/schema, formatting/diff; justify any broader suite.
Installer/hardware impact; compatible rollback where applicable.
Nothing merged/deployed and no production mutation unless separately authorized.
