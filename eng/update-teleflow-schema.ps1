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

function Get-GeneratedManifestMetadata {
    param([string] $SchemaOutput)

    $manifestPath = Join-Path $SchemaOutput "telegram-bot-api.manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Could not find generated Telegram Bot API manifest at '$manifestPath'."
    }

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $version = [string] $manifest.telegramBotApi.version

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read Telegram Bot API version from '$manifestPath'."
    }

    return [ordered]@{
        Version = $version
        SourcePath = $manifestPath
    }
}

function Update-TelegramBotApiBadge {
    param([string] $SchemaOutput)

    $metadata = Get-GeneratedManifestMetadata $SchemaOutput
    $badgePath = Join-Path $teleflowFullPath "docs\badges\telegram-bot-api.json"
    New-Item -ItemType Directory -Path (Split-Path -Parent $badgePath) -Force | Out-Null

    $badge = [ordered]@{
        schemaVersion = 1
        label = "Telegram Bot API"
        message = $metadata.Version
        color = "26A5E4"
        namedLogo = "telegram"
    }

    $badge | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $badgePath -Encoding UTF8
}

$schemaOutput = Resolve-TeleFlowPath @(
    "src\TeleFlow.Telegram.Schema",
    "TeleFlow.Telegram.Schema") "TeleFlow.Telegram.Schema output"
$telegramOutput = Resolve-TeleFlowPath @(
    "src\TeleFlow.Telegram.Client",
    "TeleFlow.Telegram.Client") "TeleFlow.Telegram.Client output"

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

    Update-TelegramBotApiBadge $schemaOutput
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
