#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Preview', 'Approve', 'Execute', 'Verify', 'Readiness')][string]$Mode,
    [Parameter(Mandatory)][string]$BinaryPath,
    [string]$RequestFile,
    [Parameter(Mandatory)][string]$OutputFile,
    [Parameter(Mandatory)][string]$ErrorFile,
    [ValidateSet('Production', 'DisposableRestore')][string]$Confirmation
)
$ErrorActionPreference = 'Stop'
$binary = (Resolve-Path -LiteralPath $BinaryPath).Path
$mutating = $Mode -in @('Approve', 'Execute')
if ($mutating -and -not $Confirmation) { throw 'Explicit production or disposable-restore confirmation is required.' }
if ($Mode -ne 'Readiness') {
    if (-not $RequestFile) { throw 'An existing exact request file is required.' }
    $request = (Resolve-Path -LiteralPath $RequestFile).Path
}
if ([IO.Path]::GetFullPath($OutputFile) -eq [IO.Path]::GetFullPath($ErrorFile)) { throw 'Output and error files must differ.' }
if ((Test-Path -LiteralPath $OutputFile) -or (Test-Path -LiteralPath $ErrorFile)) { throw 'Evidence output already exists; inspect rather than overwrite.' }
# Connection/configuration and release authorization belong to the caller. The
# existing CLI owns every production guard, including the local-only restore gate.
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.WorkingDirectory = [IO.Path]::GetDirectoryName($binary)
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($binary)
if ($Mode -eq 'Readiness') { $start.ArgumentList.Add('--verify-release-readiness') }
else {
    $start.ArgumentList.Add('--reconstruct-historical-projection')
    $start.ArgumentList.Add('--mode=' + $Mode.ToLowerInvariant())
    $start.ArgumentList.Add('--request-file=' + $request)
}
if ($mutating) {
    $start.ArgumentList.Add($(if ($Confirmation -eq 'Production') { '--confirm-production' } else { '--confirm-disposable-restore' }))
}
# Logging goes to its own evidence file; stdout is the exact UTF-8 CLI JSON.
$start.Environment['Logging__Console__LogToStandardErrorThreshold'] = 'Trace'
$stdout = [IO.File]::Open([IO.Path]::GetFullPath($OutputFile), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
$stderr = $null
$process = [Diagnostics.Process]::new()
try {
    $stderr = [IO.File]::Open([IO.Path]::GetFullPath($ErrorFile), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'Maintenance process did not start.' }
    # Copy both byte streams concurrently; no line decoding, DateTime conversion,
    # newline rewriting, stdout/stderr mixing, or PowerShell scalar splatting.
    $outTask = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $errTask = $process.StandardError.BaseStream.CopyToAsync($stderr)
    $process.WaitForExit()
    $outTask.GetAwaiter().GetResult()
    $errTask.GetAwaiter().GetResult()
    $result = $process.ExitCode
} finally {
    $stdout.Dispose()
    if ($null -ne $stderr) { $stderr.Dispose() }
    $process.Dispose()
}
exit $result
