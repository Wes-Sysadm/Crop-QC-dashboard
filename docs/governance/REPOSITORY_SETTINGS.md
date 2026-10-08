# Repository enforcement settings

Read-only inspection on 2026-10-08 found this public, personal-account repository
at main `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`, `protected: false`, with no
rulesets. The collaborators response lists only `Wes-Sysadm` (admin), also the
author of #276 and sole CODEOWNER. CODEOWNERS validation reports no errors, but
that does not supply an independent reviewer. No settings or access were changed.

## Reviewer prerequisite

Before requiring CODEOWNERS approval, the owner must choose and invite an
independent human collaborator, have them accept **write permission**, and add
their actual GitHub login beside the existing owner on every protected pattern
in `.github/CODEOWNERS` through a reviewed PR. Verify access and CODEOWNERS errors
against merged main. Do not insert a placeholder login. The author cannot approve
their own PR; latest-push approval must come from someone other than that pusher.
One matching code owner suffices: listing two does not require both. Explicit
business-owner policy approval remains a separate documented requirement. This
personal-account repository has no organization team to use instead. See
[GitHub CODEOWNERS rules](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners).

## Exact main protection to activate after authorization

Use one classic branch protection rule matching exactly `main` (Settings >
Branches > Add classic branch protection rule). Inspect existing rules first;
update an applicable rule instead of creating overlapping protection.

| Setting | Required value |
|---|---|
| Require a pull request before merging | Enabled |
| Required approving reviews | 1 independent human |
| Dismiss stale approvals when new commits are pushed | Enabled |
| Require review from Code Owners | Enabled, only after the prerequisite above |
| Require approval of the most recent reviewable push | Enabled |
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
    "require_code_owner_reviews": true,
    "required_approving_review_count": 1,
    "require_last_push_approval": true
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
A PR can propose weakening its own checks; independent review of the workflow,
checker, contracts and CODEOWNERS is essential. Review-count settings cannot
themselves prove the reviewer is human. Administrators can later change settings;
periodic owner inspection remains an operational responsibility. Merge-group
events run contracts but have no PR body to validate. A separately controlled
required-workflow source needs additional organization setup; none is claimed.

CI uses read-only permissions, disposable PostgreSQL and synthetic Git fixtures,
with no production secrets or privileged `pull_request_target` execution. Green
CI never authorizes merging, deployment, migrations, repairs or backups.
