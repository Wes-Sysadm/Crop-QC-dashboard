#requires -Version 5.1
<#
.SYNOPSIS
Compare this clone with GitHub main; optionally fast-forward a clean local main.
.DESCRIPTION
Exit 0: synchronized; 2: manual action/report-only update needed; 1: verification/fetch failure.
No reset, stash, rebase, checkout, conflict resolution, credential or production operations.
#>
[CmdletBinding()]
param(
    [string]$RepositoryPath = (Join-Path $PSScriptRoot '..'),
    [switch]$Update,
    # Test harness only: local bare repository with an explicit fixture marker.
    [string]$OfflineTestRemote
)
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string[]]$Arguments)
    # Buffer errors: never echo a configured URL that might contain credentials.
    $savedPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 represents normal Git stderr progress as errors.
        $ErrorActionPreference = 'Continue'
        $result = & git -C $script:repoRoot @Arguments 2>&1
        $gitExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($gitExit -ne 0) { throw "Git command failed ($($Arguments[0])); check repository/access locally. No branch reset was attempted." }
    return @($result | ForEach-Object { "$_" })
}
function Get-OperationState {
    foreach ($name in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply', 'sequencer', 'index.lock')) {
        $p = @(Invoke-Git -Arguments @('rev-parse', '--git-path', $name))[0]
        if (-not [IO.Path]::IsPathRooted($p)) { $p = Join-Path $script:repoRoot $p }
        if (Test-Path -LiteralPath $p) { return $name }
    }
    return $null
}
try {
    $script:repoRoot = (Resolve-Path -LiteralPath $RepositoryPath).Path
    $script:repoRoot = @(Invoke-Git -Arguments @('rev-parse', '--show-toplevel'))[0]
    $urls = @(Invoke-Git -Arguments @('remote', 'get-url', '--all', 'origin'))
    $allowed = '^(https://github\.com/Wes-Sysadm/Crop-QC-dashboard(?:\.git)?|git@github\.com:Wes-Sysadm/Crop-QC-dashboard(?:\.git)?|ssh://git@github\.com/Wes-Sysadm/Crop-QC-dashboard(?:\.git)?)$'
    if ($OfflineTestRemote) {
        $fixture = (Resolve-Path -LiteralPath $OfflineTestRemote).Path
        if ($urls.Count -ne 1 -or [IO.Path]::GetFullPath($urls[0]) -ne $fixture -or
            -not (Test-Path -LiteralPath (Join-Path $fixture 'cropqc-sync-fixture')) -or
            (& git -C $fixture rev-parse --is-bare-repository) -ne 'true') {
            throw 'Offline tests require an explicitly marked local bare remote; network URLs are forbidden.'
        }
        Write-Output 'OFFLINE DISPOSABLE TEST MODE; this does not verify GitHub freshness.'
    } elseif ($urls.Count -ne 1 -or $urls[0] -cnotmatch $allowed) {
        throw 'origin must be the credential-free Wes-Sysadm/Crop-QC-dashboard GitHub URL (HTTPS or SSH). Remote was not contacted.'
    }
    $branch = @(Invoke-Git -Arguments @('rev-parse', '--abbrev-ref', 'HEAD'))[0]
    $head = @(Invoke-Git -Arguments @('rev-parse', 'HEAD'))[0]
    Write-Output "Repository: $script:repoRoot"
    Write-Output "Origin verified for selected mode; branch: $branch; HEAD: $head"
    # Explicit refspec handles clones configured to fetch only a feature branch.
    Invoke-Git -Arguments @('fetch', '--no-tags', 'origin', '+refs/heads/main:refs/remotes/origin/main') | Out-Null
    $remote = @(Invoke-Git -Arguments @('rev-parse', 'refs/remotes/origin/main'))[0]
    $counts = (@(Invoke-Git -Arguments @('rev-list', '--left-right', '--count', 'HEAD...refs/remotes/origin/main'))[0] -split '\s+')
    $ahead = [int]$counts[0]; $behind = [int]$counts[1]
    Write-Output "Fetched main: $remote; local-only commits: $ahead; incoming commits: $behind"
    $paths = @('AGENTS.md', 'AGENTS.override.md', ':(glob)**/AGENTS.md', ':(glob)**/AGENTS.override.md',
        'docs', '.github', 'scripts/governance', 'scripts/Sync-CropQcKnowledge.ps1')
    $differences = @(Invoke-Git -Arguments (@('diff', '--name-status', 'HEAD', $remote, '--') + $paths))
    if ($differences.Count) {
        Write-Output 'Knowledge/tooling differences (HEAD -> fetched main; includes local proposals):'
        $differences | Write-Output
    } else { Write-Output 'Tracked knowledge matches fetched main.' }
    $status = @(Invoke-Git -Arguments @('status', '--porcelain=v1', '--untracked-files=all'))
    $localKnowledge = @(Invoke-Git -Arguments (@('status', '--porcelain=v1', '--untracked-files=all', '--') + $paths))
    if ($localKnowledge.Count) { Write-Output 'Locally edited/untracked knowledge (preserved):'; $localKnowledge | Write-Output }
    $governancePath = 'docs/governance/CROP_QC_BUSINESS_RULES.md'
    $remoteFiles = @(Invoke-Git -Arguments @('ls-tree', '-r', '--name-only', $remote, '--', 'AGENTS.md', $governancePath))
    if ($remoteFiles -notcontains 'AGENTS.md' -or $remoteFiles -notcontains $governancePath) {
        Write-Output 'MANUAL: fetched main has not adopted the canonical governance files. A draft is not approved main.'
        exit 2
    }
    $version = (Invoke-Git -Arguments @('show', "${remote}:$governancePath") | Select-String '^Specification version:').Line
    $knowledgeCommit = @(Invoke-Git -Arguments @('log', '-1', '--format=%H', $remote, '--', 'AGENTS.md', 'docs/governance'))[0]
    Write-Output "Remote $version; governance commit: $knowledgeCommit"
    $operation = Get-OperationState
    if ($operation) { Write-Output "MANUAL: unfinished Git operation/lock ($operation). Resolve or abort it yourself; nothing overwritten."; exit 2 }
    if ($status.Count) { Write-Output 'MANUAL: uncommitted or untracked work exists. Commit or preserve it yourself before integrating; no stash or overwrite performed.'; exit 2 }
    if ($branch -eq 'HEAD') { Write-Output 'MANUAL: detached HEAD; select the intended branch yourself.'; exit 2 }
    if ($ahead -gt 0 -and $behind -gt 0) {
        Write-Output 'MANUAL: histories diverged. Review git log --left-right HEAD...origin/main. Integrate on your feature branch after preserving work; resolve conflicts explicitly. Never reset main to discard commits.'
        exit 2
    }
    if ($branch -ne 'main') {
        if ($behind -gt 0 -or $differences.Count -gt 0) {
            Write-Output 'MANUAL: feature branch preserved. Read current rules with git show origin/main:docs/governance/CROP_QC_BUSINESS_RULES.md and the decision register. Once clean, integrate origin/main using git merge origin/main, or git rebase origin/main only for an unpublished branch. Resolve conflicts manually and rerun checks. Publish shared updates through a PR.'
            exit 2
        }
        Write-Output 'SUCCESS: feature branch knowledge matches fetched main; branch unchanged.'; exit 0
    }
    if ($ahead -gt 0) { Write-Output 'MANUAL: main contains local-only commits. Preserve them on a feature branch and submit a PR; no automatic rewrite.'; exit 2 }
    if ($behind -gt 0) {
        if (-not $Update) { Write-Output 'MANUAL: clean main can fast-forward. Rerun with -Update to update the entire checkout through Git.'; exit 2 }
        # Recheck immediately before the only working-tree mutation.
        if (@(Invoke-Git -Arguments @('rev-parse', 'HEAD'))[0] -ne $head -or
            @(Invoke-Git -Arguments @('rev-parse', '--abbrev-ref', 'HEAD'))[0] -ne 'main' -or
            @(Invoke-Git -Arguments @('status', '--porcelain=v1', '--untracked-files=all')).Count -gt 0 -or (Get-OperationState)) {
            throw 'Repository changed during synchronization; rerun after other Git work finishes.'
        }
        # Disable merge hooks for this invocation; no persistent config changes.
        Invoke-Git -Arguments @('-c', 'core.hooksPath=/dev/null', 'merge', '--ff-only', '--no-edit', $remote) | Out-Null
    }
    if (@(Invoke-Git -Arguments @('rev-parse', 'HEAD'))[0] -ne $remote) { throw 'Final HEAD does not match fetched main.' }
    Write-Output "SUCCESS: main and tracked knowledge synchronized at $remote. Start a new Codex session or explicitly reload AGENTS.md and applicable rules."
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
