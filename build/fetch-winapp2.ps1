<#
.SYNOPSIS
    Updates rules\winapp2\Winapp2.ini and License.md from the Winapp2 project (developer machine, not the app).

.DESCRIPTION
    CLEANit ships the Non-CCleaner variant of Winapp2.ini unmodified (CC BY-SA 4.0). The app itself never downloads it.
    Run this before a release, review the diff, update the version in docs\DEPENDENCIES.md and the About page.

.EXAMPLE
    pwsh -File build\fetch-winapp2.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'rules\winapp2'
$base = 'https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master'

New-Item -ItemType Directory -Path $target -Force | Out-Null
Invoke-WebRequest -Uri "$base/Non-CCleaner/Winapp2.ini" -OutFile (Join-Path $target 'Winapp2.ini') -UseBasicParsing
Invoke-WebRequest -Uri "$base/License.md" -OutFile (Join-Path $target 'License.md') -UseBasicParsing

$version = (Get-Content (Join-Path $target 'Winapp2.ini') -TotalCount 1) -replace '^;\s*Version:\s*', ''
Write-Host "Winapp2.ini version $version saved to rules\winapp2 (unmodified)." -ForegroundColor Green
