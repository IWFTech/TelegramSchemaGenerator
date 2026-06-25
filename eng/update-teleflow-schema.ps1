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

function Get-GeneratedHeaderMetadata {
    param([string] $HeaderPath)

    $contents = Get-Content -Raw -LiteralPath $HeaderPath
    $version = [regex]::Match($contents, "Telegram Bot API version:\s*(?<value>[^\r\n]+)").Groups["value"].Value.Trim()

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read Telegram Bot API version from '$HeaderPath'."
    }

    return [ordered]@{
        Version = $version
    }
}

function Update-TelegramBotApiBadge {
    param([string] $SchemaOutput)

    $updateTypePath = Join-Path $SchemaOutput "Types\Update.g.cs"
    if (-not (Test-Path -LiteralPath $updateTypePath)) {
        throw "Could not find generated Update.g.cs at '$updateTypePath'."
    }

    $metadata = Get-GeneratedHeaderMetadata $updateTypePath
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
