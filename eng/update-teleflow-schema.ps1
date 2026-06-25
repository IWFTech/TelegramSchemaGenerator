param(
    [Parameter(Mandatory = $true)]
    [string] $TeleFlowRoot,

    [string] $SourceUrl = "https://core.telegram.org/bots/api",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$generatorProject = Join-Path $repositoryRoot "src\TeleFlow.Telegram.SchemaGenerator\TeleFlow.Telegram.SchemaGenerator.csproj"
$teleflowFullPath = (Resolve-Path -LiteralPath $TeleFlowRoot).Path

function Invoke-CheckedDotNet {
    param([string[]] $Arguments)

    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

function Resolve-TeleFlowPath {
    param(
        [string[]] $Candidates,
        [string] $Description
    )

    foreach ($relativePath in $Candidates) {
        $path = Join-Path $teleflowFullPath $relativePath
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    throw "Could not resolve $Description under '$teleflowFullPath'."
}

$rawOutput = Join-Path $teleflowFullPath "schema\telegram-bot-api\raw\telegram-bot-api.raw.json"
$normalizedOutput = Join-Path $teleflowFullPath "schema\telegram-bot-api\normalized\telegram-bot-api.normalized.json"
$schemaOutput = Resolve-TeleFlowPath @(
    "src\TeleFlow.Telegram.Schema",
    "TeleFlow.Telegram.Schema") "TeleFlow.Telegram.Schema output"
$telegramOutput = Resolve-TeleFlowPath @(
    "src\TeleFlow.Telegram.Client",
    "TeleFlow.Telegram.Client") "TeleFlow.Telegram.Client output"

New-Item -ItemType Directory -Path (Split-Path -Parent $rawOutput) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $normalizedOutput) -Force | Out-Null

Invoke-CheckedDotNet @(
    "run",
    "--project",
    $generatorProject,
    "-c",
    $Configuration,
    "--",
    "all",
    "--url",
    $SourceUrl,
    "--raw-output",
    $rawOutput,
    "--normalized-output",
    $normalizedOutput,
    "--generated-output",
    $schemaOutput,
    "--telegram-output",
    $telegramOutput)
