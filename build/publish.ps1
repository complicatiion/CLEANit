<#
.SYNOPSIS
    Builds the CLEANit release: cleanit_v<version>_portable.zip (+ .sha256) and, if Inno Setup 6 is installed,
    cleanit_v<version>_setup.exe (+ .sha256).

.DESCRIPTION
    1. Runs the tests (skip with -SkipTests)
    2. Publishes the app self-contained (no .NET runtime needed on the target system)
    3. Checks that the license files and the unmodified Winapp2.ini are part of the output
    4. Packs the portable ZIP (with CLEANit.portable marker) and writes the SHA-256
    5. Compiles build\installer\cleanit.iss with ISCC (skip with -SkipInstaller)

.EXAMPLE
    pwsh -File build\publish.ps1
    pwsh -File build\publish.ps1 -SkipTests -SkipInstaller
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [switch] $SkipTests,
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
[xml] $props = Get-Content -Raw -Path (Join-Path $root 'Directory.Build.props')
$versionNode = $props.SelectSingleNode('//Version')
if ($null -eq $versionNode) { throw 'Version not found in Directory.Build.props.' }
$version = $versionNode.InnerText.Trim()

$name = "cleanit_v$version"
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish\CLEANit'
$portableRoot = Join-Path $artifacts "$($name)_portable"
$zip = Join-Path $artifacts "$($name)_portable.zip"

Write-Host "CLEANit $version · $Configuration · $Runtime" -ForegroundColor Magenta

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'CLEANit.slnx') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

foreach ($dir in $publishDir, $portableRoot) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish (Join-Path $root 'src\CLEANit.App\CLEANit.App.csproj') -c $Configuration -r $Runtime --self-contained true `
    -p:PublishReadyToRun=false -p:DebugType=none -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Publishing CLEANit failed.' }

# Shipped next to the app: licenses, README and Winapp2.ini (CC BY-SA 4.0, unmodified).
foreach ($required in 'CLEANit.exe', 'README.md', 'licenses\LICENSE.md', 'licenses\third-party\WINAPP2.md',
                      'licenses\third-party\LICENSE-CC-BY-SA-4.0.md', 'rules\winapp2\Winapp2.ini', 'rules\winapp2\License.md') {
    if (-not (Test-Path (Join-Path $publishDir $required))) { throw "Missing in output: $required" }
}

$source = Get-FileHash -Path (Join-Path $root 'rules\winapp2\Winapp2.ini') -Algorithm SHA256
$shipped = Get-FileHash -Path (Join-Path $publishDir 'rules\winapp2\Winapp2.ini') -Algorithm SHA256
if ($source.Hash -ne $shipped.Hash) { throw 'Winapp2.ini differs from the source file – it must ship unmodified.' }

# Portable ZIP: same files plus the marker (data in .\data).
$staging = Join-Path $portableRoot 'CLEANit'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $staging -Recurse
Set-Content -Path (Join-Path $staging 'CLEANit.portable') -Value 'Portable mode: settings, reports and backups are stored in .\data' -Encoding ascii

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path -Leaf $zip)" -Encoding ascii
Write-Host "Portable: $zip" -ForegroundColor Green
Write-Host "SHA-256:  $hash"

# Installer (Inno Setup 6).
if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if ($null -eq $iscc) {
        Write-Warning 'Inno Setup 6 (ISCC.exe) not found – installer skipped. https://jrsoftware.org/isinfo.php'
    }
    else {
        & $iscc "/DAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'installer\cleanit.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
        $setup = Join-Path $artifacts "$($name)_setup.exe"
        $setupHash = (Get-FileHash -Path $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -Path "$setup.sha256" -Value "$setupHash  $(Split-Path -Leaf $setup)" -Encoding ascii
        Write-Host "Setup:    $setup" -ForegroundColor Green
        Write-Host "SHA-256:  $setupHash"
    }
}
