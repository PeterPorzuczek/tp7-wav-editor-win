<#
.SYNOPSIS
Creates dist/TP7-Wav-Editor-Source.zip without building the application.
.DESCRIPTION
Packages the source, tests, scripts, documentation, and licenses from an
explicit manifest. Works without Git, including from an extracted source ZIP.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workspacePrefix = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$outputDirectory = Join-Path $repositoryRoot 'dist'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$archivePath = Join-Path $outputDirectory 'TP7-Wav-Editor-Source.zip'

$rootFiles = @(
    'Directory.Build.props', 'global.json', '.editorconfig', '.gitattributes',
    '.gitignore', 'TP7-Wav-Editor.slnx', 'README.md', 'LICENSE', 'CONTRIBUTING.md',
    'THIRD-PARTY-NOTICES.txt'
)
$sourceFiles = @($rootFiles | ForEach-Object { Get-Item -LiteralPath (Join-Path $repositoryRoot $_) })
foreach ($directory in @('src', 'tests', 'scripts', 'docs', 'licenses', '.github')) {
    $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $directory) -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}

$zipStream = [IO.File]::Create($archivePath)
$zip = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($file in ($sourceFiles | Sort-Object FullName)) {
        if (-not $file.FullName.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Source file is outside the project directory.'
        }
        $entryName = $file.FullName.Substring($workspacePrefix.Length).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
    $zipStream.Dispose()
}
Write-Host "Source package: $archivePath"
