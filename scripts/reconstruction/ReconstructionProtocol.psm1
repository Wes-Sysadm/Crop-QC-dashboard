#requires -Version 7.4
Set-StrictMode -Version Latest

function Read-ProtocolJson {
    param([Parameter(Mandatory)][string]$Path)
    # Decode strictly. Never let PowerShell infer DateTime, doubles, or property types.
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $text = $utf8.GetString($bytes)
    if ($text.StartsWith([string][char]0xFEFF, [StringComparison]::Ordinal)) { throw 'Protocol JSON must be UTF-8 without a BOM.' }
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 128
    $document = [System.Text.Json.JsonDocument]::Parse($text, $options)
    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'Protocol JSON must be an object.'
        }
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'Duplicate protocol property.' }
        }
        return [pscustomobject]@{ Text = $text; Document = $document }
    } catch { $document.Dispose(); throw }
}

function Write-ReconstructionEnvelope {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Approve', 'Execute')][string]$Mode,
        [Parameter(Mandatory)][string]$PreviewFile,
        [Parameter(Mandatory)][string]$MetadataFile,
        [Parameter(Mandatory)][string]$OutputFile
    )
    $preview = Read-ProtocolJson $PreviewFile
    $metadata = $null
    try {
        $metadata = Read-ProtocolJson $MetadataFile
        $allowed = if ($Mode -eq 'Approve') {
            @('approvalKey', 'approverId', 'approvalReference', 'reason', 'explicitlyApprove',
                'verifiedBackupRunId', 'verifiedBackupSha256', 'independentBackupVerificationReference')
        } else { @('operationKey', 'approvalAuditId', 'operatorId', 'explicitlyExecute') }
        foreach ($property in $metadata.Document.RootElement.EnumerateObject()) {
            if ($property.Name -cnotin $allowed) { throw 'Unexpected envelope metadata property; preview must come only from PreviewFile.' }
        }
        # Only metadata's closing brace is removed. The entire application output,
        # including whitespace, escapes, property order and numeric lexemes, is copied.
        $envelope = $metadata.Document.RootElement.GetRawText()
        $separator = if (@($metadata.Document.RootElement.EnumerateObject()).Count) { ',' } else { '' }
        $text = $envelope.Substring(0, $envelope.Length - 1) + $separator + '"preview":' + $preview.Text + '}'
        $encoded = [Text.UTF8Encoding]::new($false, $true).GetBytes($text)
        $stream = [IO.File]::Open([IO.Path]::GetFullPath($OutputFile), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try { $stream.Write($encoded, 0, $encoded.Length) } finally { $stream.Dispose() }
    } finally {
        $preview.Document.Dispose()
        if ($null -ne $metadata) { $metadata.Document.Dispose() }
    }
}

Export-ModuleMember -Function Write-ReconstructionEnvelope
