param(
    [Parameter(Mandatory = $true)]
    [string] $TeleFlowRoot,

    [string] $SourceUrl = "https://core.telegram.org/bots/api",
    [string] $Configuration = "Release",
    [string] $OutputPath = ""
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$generatorProject = Join-Path $repositoryRoot "src\TeleFlow.Telegram.SchemaGenerator\TeleFlow.Telegram.SchemaGenerator.csproj"

function Invoke-CheckedDotNet {
    param([string[]] $Arguments)

    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

function Get-GeneratedMetadata {
    param([string] $Root)

    if ([string]::IsNullOrWhiteSpace($Root)) {
        return $null
    }

    $candidateManifestPaths = @(
        "src\TeleFlow.Telegram.Schema\telegram-bot-api.manifest.json",
        "TeleFlow.Telegram.Schema\telegram-bot-api.manifest.json")

    foreach ($relativePath in $candidateManifestPaths) {
        $path = Join-Path $Root $relativePath
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $manifest = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        $version = [string] $manifest.telegramBotApi.version
        $release = [string] $manifest.telegramBotApi.releasedAt
        $changelog = [string] $manifest.telegramBotApi.changelogUrl

        if ([string]::IsNullOrWhiteSpace($version)) {
            throw "Could not read Telegram Bot API version from '$path'."
        }

        return [ordered]@{
            Version = $version
            ReleaseDate = $release
            ChangelogUrl = $changelog
            SourcePath = $path
        }
    }

    $candidatePaths = @(
        "src\TeleFlow.Telegram.Schema\Types\Update.g.cs",
        "TeleFlow.Telegram.Schema\Types\Update.g.cs")

    foreach ($relativePath in $candidatePaths) {
        $path = Join-Path $Root $relativePath
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $contents = Get-Content -Raw -LiteralPath $path
        $version = [regex]::Match($contents, "Telegram Bot API version:\s*(?<value>[^\r\n]+)").Groups["value"].Value.Trim()
        $release = [regex]::Match($contents, "Telegram Bot API release:\s*(?<value>[^\r\n]+)").Groups["value"].Value.Trim()
        $changelog = [regex]::Match($contents, "Telegram Bot API changelog:\s*(?<value>[^\r\n]+)").Groups["value"].Value.Trim()

        if ([string]::IsNullOrWhiteSpace($version)) {
            throw "Could not read Telegram Bot API version from '$path'."
        }

        return [ordered]@{
            Version = $version
            ReleaseDate = $release
            ChangelogUrl = $changelog
            SourcePath = $path
        }
    }

    throw "Could not find generated Telegram Bot API manifest or Update.g.cs under '$Root'."
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("teleflow-schema-check-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

try {
    $rawPath = Join-Path $tempDirectory "telegram-bot-api.raw.json"
    Invoke-CheckedDotNet @(
        "run",
        "--project",
        $generatorProject,
        "-c",
        $Configuration,
        "--",
        "parse-docs",
        "--url",
        $SourceUrl,
        "--output",
        $rawPath)

    $raw = Get-Content -Raw -LiteralPath $rawPath | ConvertFrom-Json
    $latest = [ordered]@{
        Version = [string] $raw.Metadata.TelegramBotApiVersion
        ReleaseDate = [string] $raw.Metadata.TelegramBotApiReleasedAt
        ChangelogAnchor = [string] $raw.Metadata.TelegramBotApiChangelogAnchor
        SourceUrl = [string] $raw.Metadata.SourceUrl
        SourceSha256 = [string] $raw.Metadata.SourceSha256
    }

    $current = Get-GeneratedMetadata $TeleFlowRoot
    $hasUpdate = $current.Version -ne $latest.Version

    $result = [ordered]@{
        HasUpdate = $hasUpdate
        Current = $current
        Latest = $latest
    }

    $json = $result | ConvertTo-Json -Depth 8
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        Write-Output $json
    }
    else {
        $outputDirectory = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        }

        Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        "has_update=$($hasUpdate.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "current_version=$($current.Version)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "latest_version=$($latest.Version)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "latest_release_date=$($latest.ReleaseDate)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "latest_changelog_anchor=$($latest.ChangelogAnchor)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
