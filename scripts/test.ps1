<#
.SYNOPSIS
Runs the regression suite and writes TRX results to artifacts/test-results.
.DESCRIPTION
Restores locked dependencies and builds in Release by default.
Use -NoBuild and -NoRestore to reuse completed steps, for example in CI.
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testArguments = @(
    'test', 'TP7-Wav-Editor.slnx', '-c', 'Release',
    '-p:RestoreLockedMode=true', '--logger', 'trx',
    '--results-directory', 'artifacts/test-results'
)
if ($NoBuild) { $testArguments += '--no-build' }
if ($NoRestore) { $testArguments += '--no-restore' }

Push-Location $repositoryRoot
try {
    dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
}
finally {
    Pop-Location
}
