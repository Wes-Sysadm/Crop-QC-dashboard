# Repository enforcement settings

Read-only inspection on 2026-10-08 found this public, personal-account repository
at main `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`, `protected: false`, with no
rulesets. The collaborators response lists only `Wes-Sysadm` (admin), also the
author of #276 and sole CODEOWNER. CODEOWNERS validation reports no errors, but
that does not supply an independent reviewer. No settings or access were changed.

## Reviewer prerequisite

Historical version 1.0.1 proposed an independent collaborator with write access,
one approving review, CODEOWNERS review and latest-push approval. No such settings
were active at the 2026-10-09 read-only recheck (classic protection: 404 Branch not
protected; effective rules: []). This section is retained as the transition anchor.

After effective ADR-007 adoption, explicit owner task authorization is sufficient
for the normal approval workflow. CODEOWNERS routes review and identifies ownership;
it does not require another owner action. Independent human review is optional
unless specifically required. Do not invite a collaborator solely to duplicate the
owner's approval. Existing live review requirements still bind until separately
authorized configuration changes are applied and verified. Never use a bypass.
See [authority and transition](RELEASE_AUTHORIZATION.md).

## Exact main protection to activate after authorization

After policy adoption and explicit settings authorization, use one classic branch protection rule matching exactly `main` (Settings >
Branches > Add classic branch protection rule). Inspect existing rules first;
update an applicable rule instead of creating overlapping protection.

| Setting | Required value |
|---|---|
| Require a pull request before merging | Enabled |
| Required approving reviews | 0; owner authorization is verified from the original instruction |
| Dismiss stale approvals when new commits are pushed | Enabled for optional reviews; always recheck authorization scope/head |
| Require review from Code Owners | Disabled; retain CODEOWNERS for routing |
| Require approval of the most recent reviewable push | Disabled; retain reviewed-head evidence |
| Require status checks to pass | Enabled |
| Require branches to be up to date before merging | Enabled (strict) |
| Required checks | `governance`, `contracts`, `windows-sync (pwsh)`, `windows-sync (powershell)` |
| Expected source for each check | GitHub Actions; observed app slug `github-actions`, app ID `15368` |
| Require conversation resolution | Enabled |
| Do not allow bypassing the above settings | Enabled, including administrators |
| Allow specified actors to bypass required pull requests | No actors |
| Allow force pushes / branch deletion | Both disabled |

Do not enable merge queue, lock the branch, change deployment settings or impose
unrelated merge policies here. The workflow supports `merge_group` for a separately
approved future queue. Personal repositories do not use organization-only push
restrictions. Exact contexts above were observed on #276. GitHub may accept a
skipped/neutral check, so the workflow has no path filters and the runner rejects
missing/skipped mandatory tests. See
[branch protection behavior](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches).

After saving, read `GET /repos/Wes-Sysadm/Crop-QC-dashboard/branches/main/protection`.
Require this response subset, not just a successful settings save:

```json
{
  "required_status_checks": {
    "strict": true,
    "checks": [
      {"context": "governance", "app_id": 15368},
      {"context": "contracts", "app_id": 15368},
      {"context": "windows-sync (pwsh)", "app_id": 15368},
      {"context": "windows-sync (powershell)", "app_id": 15368}
    ]
  },
  "enforce_admins": {"enabled": true},
  "required_pull_request_reviews": {
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": false,
    "required_approving_review_count": 0,
    "require_last_push_approval": false
  },
  "required_conversation_resolution": {"enabled": true},
  "allow_force_pushes": {"enabled": false},
  "allow_deletions": {"enabled": false}
}
```

This is an expected response subset, not a PUT payload. Also confirm no PR bypass
allowances or conflicting ruleset/bypass. Use the
[branch-protection API](https://docs.github.com/en/rest/branches/branch-protection)
and [post-merge validation](POST_MERGE_ACTIVATION.md). Do not claim enforcement
active until the intentionally noncompliant PR is visibly blocked.

## Limits of repository checks

Metadata verifies rule IDs, version/decision evolution, links, mappings and
disclosure. It cannot authenticate approval or prove arbitrary business semantics.
A PR can propose weakening its own checks; substantive review of the workflow,
checker, contracts and CODEOWNERS remains essential. The operator verifies original
owner authority outside CI; no review-count setting authenticates a task message. Administrators can later change settings;
periodic owner inspection remains an operational responsibility. Merge-group
events run contracts but have no PR body to validate. A separately controlled
required-workflow source needs additional organization setup; none is claimed.

CI uses read-only permissions, disposable PostgreSQL and synthetic Git fixtures,
with no production secrets or privileged `pull_request_target` execution. Green
CI never authorizes merging, deployment, migrations, repairs or backups. Under
effective ADR-007, one explicit owner release instruction supplies scope authority;
all technical gates remain mandatory. This document changes no live settings.
