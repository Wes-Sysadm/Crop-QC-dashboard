#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Approve', 'Execute')][string]$Mode,
    [Parameter(Mandatory)][string]$PreviewFile,
    [Parameter(Mandatory)][string]$MetadataFile,
    [Parameter(Mandatory)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReconstructionProtocol.psm1') -Force
Write-ReconstructionEnvelope @PSBoundParameters
