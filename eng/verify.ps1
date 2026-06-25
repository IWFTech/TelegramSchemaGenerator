param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "TeleFlow.Telegram.SchemaGenerator.sln"

function Invoke-CheckedDotNet {
    param([string[]] $Arguments)

    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

Invoke-CheckedDotNet @("restore", $solutionPath)
Invoke-CheckedDotNet @(
    "format",
    "whitespace",
    $solutionPath,
    "--verify-no-changes",
    "--no-restore",
    "--verbosity",
    "minimal")
Invoke-CheckedDotNet @(
    "format",
    "style",
    $solutionPath,
    "--verify-no-changes",
    "--no-restore",
    "--verbosity",
    "minimal")
Invoke-CheckedDotNet @(
    "build",
    $solutionPath,
    "-c",
    $Configuration,
    "--no-restore",
    "/nodeReuse:false")
Invoke-CheckedDotNet @(
    "test",
    $solutionPath,
    "-c",
    $Configuration,
    "--no-build",
    "--no-restore",
    "/nodeReuse:false",
    "--logger",
    "console;verbosity=minimal")
