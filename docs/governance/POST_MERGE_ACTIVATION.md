# Post-merge activation and safe enforcement validation

Status: prepared, **not executed**. This procedure does not authorize merge,
settings changes, invitations or closing #274. Obtain the owner's explicit
approval for those actions separately. No changes to pending inventory PRs,
production configuration, data, backups or deployments are required.

## 1. Adopt and provision review

1. Have a human review the consolidated #276, its final commit and all four CI
   results. Merge only after explicit authorization. Do not merge both governance
   drafts. The owner decides separately what to do with #274.
2. Complete the [reviewer prerequisite](REPOSITORY_SETTINGS.md#reviewer-prerequisite):
   accepted write access for an independent human and their real login on every
   protected CODEOWNERS pattern in merged main. Review that access/configuration
   change separately. Do not turn on code-owner approval with only the PR author
   available, and do not solve the deadlock by granting routine bypass.
3. Record merged main SHA, specification version, reviewer identity/access and
   CODEOWNERS validation. Wait for successful workflow runs to make all four exact
   contexts selectable. Check that no competing rule or ruleset has appeared.

## 2. Activate and read back protection

After explicit settings authorization, an administrator applies the exact
[main protection settings](REPOSITORY_SETTINGS.md#exact-main-protection-to-activate-after-authorization).
Then capture read-only evidence outside the repository or in the review record:

```powershell
gh api repos/Wes-Sysadm/Crop-QC-dashboard/branches/main/protection
gh api repos/Wes-Sysadm/Crop-QC-dashboard/rulesets
gh api 'repos/Wes-Sysadm/Crop-QC-dashboard/codeowners/errors?ref=main'
gh api repos/Wes-Sysadm/Crop-QC-dashboard/collaborators --paginate --jq '.[] | {login,role_name,permissions}'
```

Verify every expected response value, all four contexts with GitHub Actions as
source, strict branch currency, review/conversation gates and no bypass. A save
or green historical run is insufficient proof that noncompliant changes are blocked.

## 3. Open a disposable negative probe after activation

The prepared [patch](fixtures/enforcement-probe.patch) adds only one explicitly
disposable documentation file containing a broken local reference. The prepared
[PR body](fixtures/enforcement-probe-body.txt) satisfies disclosure so the failure
tests the reference gate. It changes no policy, workflow, application code or test
implementation. First confirm its file/branch names do not already exist. Use a
new dedicated clone in an unused local directory; never reuse or clean a dirty
working copy. The following commands are for the authorized operator, not an
instruction to execute this future step during #276 finalization:

```powershell
git clone https://github.com/Wes-Sysadm/Crop-QC-dashboard.git Crop-QC-enforcement-probe
Set-Location Crop-QC-enforcement-probe
git fetch origin main
git switch -c codex/governance-enforcement-probe origin/main
git rev-parse origin/main
git apply --check docs/governance/fixtures/enforcement-probe.patch
git apply docs/governance/fixtures/enforcement-probe.patch
node scripts/governance/check.mjs
# EXPECT nonzero: Missing local reference intentionally-missing-enforcement-proof.md
git add -- docs/governance/ENFORCEMENT_PROBE.md
git commit -m 'test: disposable governance enforcement probe DO NOT MERGE'
git push -u origin codex/governance-enforcement-probe
gh pr create --base main --head codex/governance-enforcement-probe --title 'DO NOT MERGE: governance enforcement probe' --body-file docs/governance/fixtures/enforcement-probe-body.txt
```

Record the returned PR number and use it below as `$probeNumber`. Open it ready
for review, not draft: draft status must not be mistaken for protection working.
Never click Merge, enable auto-merge, invoke a merge API or attempt a direct push
to main. The experiment requires observing the server's blocked merge state,
not attempting a production branch mutation.

```powershell
gh pr checks $probeNumber --required
gh pr view $probeNumber --json headRefOid,baseRefOid,mergeStateStatus,reviewDecision,statusCheckRollup,url
gh pr view $probeNumber --web
```

Wait for all four jobs to finish. Expected: `governance` fails on the deliberate
broken reference; `contracts` and both Windows jobs pass; the UI identifies that
failed check as **required** and merging is blocked. Confirm the required list
contains all four names, and main's SHA is unchanged. Save timestamp, head SHA,
job links, protection readback and UI evidence. `BLOCKED` alone is ambiguous;
capture the explicit failed-required-check reason, separate from missing review.
If checks are optional or the UI offers a normal merge while a required check
fails, activation is **NO-GO**: investigate configuration, do not weaken it.

## 4. Separate the human-review gate from the check gate

Fix only the probe's broken link, as a new commit, without reset/rebase:

```powershell
$probeFile = 'docs/governance/ENFORCEMENT_PROBE.md'
$probeText = Get-Content -LiteralPath $probeFile -Raw
$probeText.Replace('intentionally-missing-enforcement-proof.md', 'README.md') | Set-Content -LiteralPath $probeFile -Encoding utf8
git add -- docs/governance/ENFORCEMENT_PROBE.md
git commit -m 'test: repair probe reference while retaining DO NOT MERGE notice'
git push
```

Wait for four green jobs on the new head, but leave the independent review absent:
GitHub must still require an eligible code-owner approval and show merging blocked.
If a prior approval existed it must be dismissed. The reviewer can confirm their
eligibility without approving this throwaway change. Record the explicit missing
review reason. Owner-approved closure of this probe without merging ends the test;
keep the evidence, not its broken file, as the validation record. Do not close
#274 as part of this step. Branch cleanup is optional and must not discard any
other work. No intentionally failing probe belongs on main.

This proves a noncompliant change is blocked by a mandatory check and a compliant
head is blocked without review. Required-check readback verifies all four contexts;
the probe deliberately fails only governance. It does not simulate every runtime
defect or prove protection against an administrator changing settings later.

## 5. Activate each Windows computer

Follow [Windows setup](WINDOWS_SETUP.md) in each actual Git root. Authenticate
locally, fetch, inspect status, and use the helper's report-only mode first.
Only clean main may use `-Update`; feature branches, local commits and dirty work
require explicit manual integration. Compare fetched main, specification version
and governance SHA between computers, then perform the manual new-session
discovery check on **each** actual Windows Codex installation. Record results and
local overrides. Do not copy credentials, sessions, production data or a parallel
rule catalog between machines. The optional global bootstrap is local guidance.

Activation is complete only when protection readback, negative-check/review proof
and per-computer discovery records all pass. Until then, #276 can be technically
ready for human review while enforced multi-computer operation remains NO-GO.
