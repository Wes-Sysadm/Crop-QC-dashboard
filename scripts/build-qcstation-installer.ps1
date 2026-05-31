param(
    [string]$Configuration = "Release",
    [string]$ProductVersion = "1.0.0",
    [string]$PublishOutput = "artifacts/qcstation-winforms-x86",
    [string]$InstallerOutput = "artifacts/installers"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishPath = Join-Path $repoRoot $PublishOutput
$installerPath = Join-Path $repoRoot $InstallerOutput
$projectPath = Join-Path $repoRoot "src/CropQc.QcStation.WinForms/CropQc.QcStation.WinForms.csproj"
$installerProjectPath = Join-Path $repoRoot "installers/CropQc.QcStation.Installer/CropQc.QcStation.Installer.wixproj"
$msiPath = Join-Path $installerPath "CropQcStationSetup.msi"

Write-Host "Publishing Crop QC Station WinForms app..."
Write-Host "Output: $publishPath"
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
dotnet publish $projectPath `
    -c $Configuration `
    -r win-x86 `
    --self-contained false `
    -p:EnableWindowsTargeting=true `
    -p:Platform=x86 `
    -p:PlatformTarget=x86 `
    -p:RuntimeIdentifier=win-x86 `
    -p:PublishSingleFile=false `
    -o $publishPath

$exePath = Join-Path $publishPath "CropQc.QcStation.WinForms.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "CropQc.QcStation.WinForms.exe was not found after publish: $exePath"
}

$devSettingsPath = Join-Path $publishPath "qcstation.settings.json"
if (Test-Path -LiteralPath $devSettingsPath) {
    Remove-Item -LiteralPath $devSettingsPath -Force
    Write-Host "Removed development qcstation.settings.json from installer payload. Station config is installed separately."
}

$generatedWxsPath = Join-Path $repoRoot "installers/CropQc.QcStation.Installer/GeneratedFiles.wxs"
$wixFiles = Get-ChildItem -Path $publishPath -File -Recurse | Sort-Object FullName
$componentLines = New-Object System.Collections.Generic.List[string]
$componentRefLines = New-Object System.Collections.Generic.List[string]
$index = 0
foreach ($file in $wixFiles) {
    $index++
    $baseUri = [Uri]((Resolve-Path $publishPath).Path.TrimEnd('\') + '\')
    $fileUri = [Uri]$file.FullName
    $relative = [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
    $componentId = "PublishedFile$($index.ToString('000'))"
    $source = $file.FullName.Replace("&", "&amp;").Replace('"', "&quot;")
    $componentLines.Add("    <Component Id=`"$componentId`" Directory=`"INSTALLFOLDER`" Guid=`"*`">")
    $componentLines.Add("      <File Id=`"$componentId`_File`" Source=`"$source`" KeyPath=`"yes`" />")
    $componentLines.Add("    </Component>")
    $componentRefLines.Add("    <ComponentRef Id=`"$componentId`" />")
}

$generatedWxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
$($componentLines -join [Environment]::NewLine)
  </Fragment>
  <Fragment>
    <ComponentGroup Id="PublishedAppComponents">
$($componentRefLines -join [Environment]::NewLine)
    </ComponentGroup>
  </Fragment>
</Wix>
"@
Set-Content -LiteralPath $generatedWxsPath -Value $generatedWxs -Encoding UTF8
Write-Host "Generated WiX file list: $generatedWxsPath"

New-Item -ItemType Directory -Path $installerPath -Force | Out-Null
Write-Host "Building WiX MSI installer..."
Write-Host "Installer output: $installerPath"
dotnet build $installerProjectPath `
    -c $Configuration `
    -p:ProductVersion=$ProductVersion `
    -p:PublishDir=$publishPath `
    -p:OutputPath=$installerPath

$builtMsi = Get-ChildItem -Path $installerPath -Filter "CropQcStationSetup.msi" -Recurse | Select-Object -First 1
if ($null -eq $builtMsi) {
    throw "CropQcStationSetup.msi was not found under $installerPath"
}

if ($builtMsi.FullName -ne $msiPath) {
    Copy-Item -LiteralPath $builtMsi.FullName -Destination $msiPath -Force
}

function Invoke-SignTool {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $timestampUrl = if ([string]::IsNullOrWhiteSpace($env:SIGN_TIMESTAMP_URL)) { "http://timestamp.digicert.com" } else { $env:SIGN_TIMESTAMP_URL }
    $signTool = if ([string]::IsNullOrWhiteSpace($env:SIGNTOOL_PATH)) { "signtool.exe" } else { $env:SIGNTOOL_PATH }

    if ([string]::IsNullOrWhiteSpace($env:SIGN_CERT_PATH)) {
        throw "SIGN_CERT_PATH is required for SIGNING_MODE=SignTool."
    }

    $arguments = @(
        "sign",
        "/fd", "SHA256",
        "/tr", $timestampUrl,
        "/td", "SHA256",
        "/f", $env:SIGN_CERT_PATH
    )

    if (-not [string]::IsNullOrWhiteSpace($env:SIGN_CERT_PASSWORD)) {
        $arguments += @("/p", $env:SIGN_CERT_PASSWORD)
    }

    $arguments += $FilePath
    & $signTool @arguments
}

if ($env:SIGN_INSTALLER -eq "true") {
    $mode = if ([string]::IsNullOrWhiteSpace($env:SIGNING_MODE)) { "SignTool" } else { $env:SIGNING_MODE }
    if ($mode -eq "SignTool") {
        Write-Host "Signing WinForms executable and MSI with SignTool..."
        Invoke-SignTool -FilePath $exePath
        Invoke-SignTool -FilePath $msiPath
    }
    elseif ($mode -eq "AzureTrustedSigning") {
        Write-Warning "Azure Trusted Signing mode is documented but not automated by this script yet. Configure a compliant signing step before production rollout."
    }
    else {
        throw "Unsupported SIGNING_MODE '$mode'. Use SignTool or AzureTrustedSigning."
    }
}
else {
    Write-Warning "Installer is unsigned and may trigger SmartScreen/Defender. Configure code signing before production rollout."
}

Write-Host "Installer ready: $msiPath"
Write-Host "Upload this installer to Google Drive and set Downloads__QcStationInstallerUrl in Render."
