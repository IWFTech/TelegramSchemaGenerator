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
. (Join-Path $PSScriptRoot "update-teleflow-schema-support.ps1")

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

$schemaOutput = Resolve-TeleFlowPath @(
    "src\TeleFlow.Telegram.Schema",
    "TeleFlow.Telegram.Schema") "TeleFlow.Telegram.Schema output"
$telegramOutput = Resolve-TeleFlowPath @(
    "src\TeleFlow.Telegram.Client",
    "TeleFlow.Telegram.Client") "TeleFlow.Telegram.Client output"
if (Test-TeleFlowGeneratedOutputChanged `
        -TeleFlowRoot $teleflowFullPath `
        -SchemaOutput $schemaOutput `
        -TelegramOutput $telegramOutput) {
    throw "TeleFlow generated output paths must be clean before regeneration."
}

$previousMetadata = Get-GeneratedManifestMetadata $schemaOutput

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("teleflow-schema-update-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

try {
    $rawOutput = Join-Path $tempDirectory "telegram-bot-api.raw.json"
    $normalizedOutput = Join-Path $tempDirectory "telegram-bot-api.normalized.json"

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

    $generatedOutputChanged = Test-TeleFlowGeneratedOutputChanged `
        -TeleFlowRoot $teleflowFullPath `
        -SchemaOutput $schemaOutput `
        -TelegramOutput $telegramOutput

    Update-TelegramBotApiBadge `
        -TeleFlowRoot $teleflowFullPath `
        -SchemaOutput $schemaOutput
    Update-TelegramBotApiChangelog `
        -TeleFlowRoot $teleflowFullPath `
        -SchemaOutput $schemaOutput `
        -PreviousVersion $previousMetadata.Version `
        -GeneratedOutputChanged $generatedOutputChanged
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
