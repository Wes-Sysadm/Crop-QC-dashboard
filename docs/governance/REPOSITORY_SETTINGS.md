# Repository enforcement settings

Read-only GitHub inspection on 2026-10-08 found main at
`e58d73a12c8bf0cc5f0f2333e442497e2b91b807`, `protected: false`, and no repository
rulesets (`[]`). This task changes no settings. The following checks are proposed,
not mandatory until an administrator enables and verifies enforcement.

After the workflow has run, configure a main ruleset/branch protection to require:

- Pull requests, at least one independent human approval, and CODEOWNERS review
  for governance, tests, writer registries, sync tooling and workflow changes.
- Dismiss stale approvals and require approval of the latest reviewable push.
- Required status contexts from the actual workflow run: `governance`, `contracts`,
  `windows-sync (pwsh)` and `windows-sync (powershell)`. Verify the exact context
  names shown by GitHub before saving settings. Require an up-to-date branch or
  a tested merge queue; the workflow handles `merge_group`.
- Block force pushes and deletion; do not grant routine automation bypass rights.
  Restrict emergency bypass to an explicitly controlled human procedure.

CODEOWNERS names the repository owner, but an author cannot approve their own PR.
Provision an independent eligible human reviewer/code owner where needed; do not
pretend a single-owner self-review meets this requirement. Branch protection and
workflow integrity are administrative controls, not something code can grant itself.

Metadata checks require protected-change disclosure and preserve stable rule IDs.
Changing policy or removing mandatory contracts requires a specification version
bump and a decision update. These are review gates, not semantic proof or an
authentication of approval text. A PR can propose weakening its own checks;
independent review and protected settings must reject unauthorized weakening.
For stronger organization enforcement, host required workflows in a separately
protected ruleset source. This is a recommendation, not a configuration made here.

CI uses read-only repository permissions, no production secrets, disposable local
PostgreSQL and synthetic Git clones. It does not use `pull_request_target` to run
untrusted code with privileged tokens. Keep real credentials out of fixtures.
Green CI does not authorize merging, deployment, migrations, repairs or backups.
