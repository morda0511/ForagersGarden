param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PackRoot,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [Parameter(Mandatory = $false)][string]$PackageName = "StoreAndCraft"
)

$ErrorActionPreference = "Stop"

$manifestPath = Join-Path $PackRoot "manifest.json"
$iconPath = Join-Path $PackRoot "icon.png"
$readmePath = Join-Path $PackRoot "README.md"
$changelogPath = Join-Path $PackRoot "CHANGELOG.md"
$pluginDll = Join-Path $PackRoot "plugins\$PackageName\$PackageName.dll"

foreach ($required in @($manifestPath, $iconPath, $readmePath, $pluginDll)) {
    if (-not (Test-Path $required)) {
        throw "Missing required package file: $required"
    }
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.version_number = $Version
$json = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($manifestPath, $json)

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-ModZip {
    param(
        [string]$ZipPath,
        [string[]]$EntryPaths,
        [string[]]$EntryNames
    )
    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }
    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        for ($i = 0; $i -lt $EntryPaths.Count; $i++) {
            if (-not (Test-Path $EntryPaths[$i])) { continue }
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $EntryPaths[$i], $EntryNames[$i])
        }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host "Pack created: $ZipPath"
}

$dllEntry = "plugins/$PackageName/$PackageName.dll"

# Thunderstore (manifest + icon)
$tsZip = Join-Path $OutputDir "$PackageName-$Version.zip"
New-ModZip -ZipPath $tsZip `
    -EntryPaths @($iconPath, $manifestPath, $readmePath, $changelogPath, $pluginDll) `
    -EntryNames @("icon.png", "manifest.json", "README.md", "CHANGELOG.md", $dllEntry)

# Hexium (Thunderstore-compatible layout)
$hxZip = Join-Path $OutputDir "$PackageName-$Version-Hexium.zip"
Copy-Item $tsZip $hxZip -Force
Write-Host "Pack created: $hxZip"

# Nexus / Vortex (no Thunderstore manifest/icon)
$nxZip = Join-Path $OutputDir "$PackageName-$Version-Nexus.zip"
New-ModZip -ZipPath $nxZip `
    -EntryPaths @($readmePath, $changelogPath, $pluginDll) `
    -EntryNames @("README.md", "CHANGELOG.md", $dllEntry)
