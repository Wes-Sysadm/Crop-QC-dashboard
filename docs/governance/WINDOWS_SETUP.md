# Windows setup and safe synchronization

## New computer

1. Install Git for Windows and PowerShell (Windows PowerShell 5.1 or PowerShell 7).
   Install/open Codex using the organization's approved Windows distribution;
   see the [official Windows app guide](https://learn.chatgpt.com/docs/windows/windows-app).
   Sign in locally; credentials and production configuration never go in this repository.
2. Clone and verify the exact project, not a parent workspace containing many checkouts:

   ```powershell
   git clone https://github.com/Wes-Sysadm/Crop-QC-dashboard.git
   Set-Location Crop-QC-dashboard
   git remote get-url origin
   git rev-parse --show-toplevel
   git status --short --branch
   ./scripts/Sync-CropQcKnowledge.ps1 -Update
   ```

   HTTPS above, `git@github.com:Wes-Sysadm/Crop-QC-dashboard.git`, and its
   `ssh://git@github.com/...` form are accepted. Embedded credentials, unexpected
   remotes and multiple fetch URLs are rejected. Authenticate through your existing
   Git setup; the helper does not install or change credentials. Until this PR is
   merged, main will not contain the helper/catalog; review the draft separately.
3. Add/open this Git root as the Codex project and start a new task there. For an
   already installed CLI, `codex` from this directory starts a session in that root.
4. Verify discovery with this first prompt:

   > Read the applicable AGENTS.md. Report the Git root, branch, HEAD, fetched
   > origin/main, governance commit, canonical specification version and relevant
   > decision IDs. Summarize INV-001, INV-004, INV-006, TRT-001 and REC-002. Report
   > any stale or overriding instructions before changing files.

   Expected: the repository's AGENTS.md and governance paths, current version
   **1.0.0** initially, and the fetched main SHA. The helper prints the remote
   specification and last governance commit. Also inspect local content with
   `Get-Content docs/governance/CROP_QC_BUSINESS_RULES.md -TotalCount 3` and
   `git log -1 --format=%H -- AGENTS.md docs/governance`.

Codex discovers instructions at session start along the Git-root-to-working-directory
path. `AGENTS.override.md`, nested instructions, global configuration and the
instruction size limit can affect discovery. Links require explicit reading, which
the root instructions require. If the prompt reports the wrong files, fix the
project directory or conflicting overrides and start again; do not claim compliance
from file presence alone. See [official AGENTS.md discovery](https://learn.chatgpt.com/docs/agent-configuration/agents-md).

## Daily operation and after merges

Run `./scripts/Sync-CropQcKnowledge.ps1` before designing a task. It fetches main
explicitly even in a clone whose fetch refspec tracks only one feature branch.
It compares all tracked documentation, root/nested agent instructions, governance
tooling and GitHub configuration, and reports local knowledge edits separately.
It never copies a documentation subset over a different code version.

| State | Behavior / next step |
|---|---|
| Clean main behind GitHub | Report-only exits 2. `-Update` fast-forwards the entire checkout with `--ff-only`. |
| Clean main current | Exit 0; repeating is harmless. |
| Dirty or untracked work | Exit 2; preserve/commit work yourself. No auto-stash or overwrite. |
| Feature branch | Never moved by the helper, even with `-Update`. Incoming or different knowledge exits 2. Read current fetched rules and integrate before dependent work. |
| Divergence / local-only main commits | Exit 2. Preserve commits, review both sides, and integrate through a feature branch/PR. No reset or force push. |
| Merge/rebase/cherry-pick conflict, detached HEAD or index lock | Exit 2. Finish/abort the operation yourself, then rerun. |
| Wrong remote, failed fetch, unverifiable state | Exit 1. No claim of current knowledge; fix access/remote deliberately. |
| Governance not yet on main | Exit 2. A local proposal is not the adopted source of truth. |

After synchronizing clean main, start a task branch with `git switch -c codex/<task>`.
On an existing clean feature branch, review `git log --left-right HEAD...origin/main`
and `git diff HEAD origin/main -- AGENTS.md docs/governance`. Prefer
`git merge origin/main` for a shared branch. `git rebase origin/main` is an option
only for an unpublished branch; it rewrites commits. Resolve conflicts manually,
retaining approved rules, then run the affected checks. Never use force reset to
make the instructions appear current. Re-read the rules/decisions after integration
and start a new session or explicitly reload them in an existing conversation.

Exit 0 on a feature branch means tracked knowledge matches fetched main, not that
all local feature code has merged. No helper can prevent someone from running an
obsolete copy or ignoring instructions; session verification and protected review
are separate controls. Avoid simultaneous Git mutations during synchronization.
Fetched freshness is as of the printed SHA, not a guarantee main cannot advance.

The helper changes only Git fetch metadata and, with `-Update` on clean main,
the normal versioned checkout. No production services, secret copying, credential
configuration, background jobs or conflict resolution are included. Execution
policy is not modified; use your organization's approved script-signing/policy setup.
`-OfflineTestRemote` is reserved for disposable fixture tests; it requires an
explicitly marked local bare remote and prints that GitHub freshness is unverified.

## Optional minimal global bootstrap

On each computer, manually add the following to your existing `~/.codex/AGENTS.md`
(or the configured CODEX_HOME equivalent) only if useful. Do not overwrite other
global preferences or duplicate this repository's business rules.

```markdown
For Crop-QC-dashboard, open the actual Git root and read its AGENTS.md.
Use the repository's current version-controlled governance and synchronization
procedure. Report stale/local overrides. Prior chats and global preferences do
not replace the project's approved business rules.
```

Global files are local to each computer and do not synchronize through this repo.
Do not synchronize `.codex` credentials, sessions, production secrets or databases.
