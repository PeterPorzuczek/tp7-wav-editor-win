<#
.SYNOPSIS
Builds the portable Windows application and distributable ZIP archives.
.DESCRIPTION
Restores locked dependencies and runs regression tests before publishing.
Use -SkipTests or -NoRestore only after those steps have already succeeded.
All output is written to the repository's dist directory.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    if (-not $NoRestore) {
        dotnet restore TP7-Wav-Editor.slnx --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    }

    if (-not $SkipTests) {
        & (Join-Path $PSScriptRoot 'test.ps1') -NoRestore
    }

    $publishDirectory = Join-Path $repositoryRoot 'dist/TP7-Wav-Editor-Windows'
    dotnet publish src/TP7.Windows/TP7.Windows.csproj -c Release --no-restore -p:DebugType=none -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Application publishing failed.' }

    foreach ($name in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', 'CONTRIBUTING.md')) {
        Copy-Item -LiteralPath $name -Destination $publishDirectory -Force
    }
    foreach ($name in @('licenses', 'docs')) {
        $destination = Join-Path $publishDirectory $name
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath $name | Copy-Item -Destination $destination -Recurse -Force
    }
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath dist/TP7-Wav-Editor-Windows-x64.zip -Force
    & (Join-Path $PSScriptRoot 'package-source.ps1')
    Get-FileHash -LiteralPath (Join-Path $publishDirectory 'TP7-Wav-Editor.exe') -Algorithm SHA256 | Format-List
    Write-Host 'Ready: dist/TP7-Wav-Editor-Windows/TP7-Wav-Editor.exe'
}
finally {
    Pop-Location
}
