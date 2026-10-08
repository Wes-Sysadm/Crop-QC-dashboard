# Required GitHub repository settings — human approval pending

Read-only inspection for this task: repository token has admin permission; `main` protection returns 404 (Branch not protected); repository rulesets list is empty. No setting was changed. Workflow existence and CODEOWNERS are **not** branch protection.

Proposed settings for owner approval:

- Protect main with a pull-request requirement, no direct/force pushes or deletion, administrators included/no unrestricted bypass.
- Require the GitHub Actions check contexts **governance** and **contracts** (workflow **Governance**) on the current candidate; require branch up-to-date (or a merge queue with equivalent checks). These exact check-run names were observed on draft PR #274.
- Require code-owner approval and at least one independent human approving review, dismiss stale approvals on new commits, require latest push approval and resolve conversations.
- Restrict governance workflow/registry/test changes to reviewed PRs. CODEOWNERS proposes @Wes-Sysadm; confirm this account can receive required independent review. GitHub does not let an author approve their own PR; if owner is the sole contributor, designate another authorized reviewer rather than claiming self-review satisfies this.
- Enable GitHub Actions for pull requests. Workflow token permissions stay contents:read, no production secrets, no pull_request_target, no deploy step.
- If merge queue is adopted, enable/require the existing merge_group workflow event. Select these observed GitHub Actions contexts in repository settings; do not prepend the workflow name to the required context string.

Without these settings, checks report failures but cannot prevent an administrator from merging. This task proposes rather than installs protections, even though admin permission is available.

The PR workflow runs on opened/synchronize/reopened/ready_for_review/edited and merge_group, without path filters. First PR execution must be observed and reported. Fork workflow approval or account billing restrictions may require manual configuration; do not label them a CI pass.
