param(
    [Parameter(Mandatory = $true)]
    [string] $TeleFlowRoot,

    [string] $SourceUrl = "https://core.telegram.org/bots/api",
    [string] $Configuration = "Release",
    [string] $OutputPath = "",
    [string] $GeneratorConfiguration = ".tg-schema-generator/config.yml",
    [string] $FailureOutputPath = ""
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$generatorProject = Join-Path $repositoryRoot "src\TeleFlow.Telegram.SchemaGenerator\TeleFlow.Telegram.SchemaGenerator.csproj"

function Invoke-CheckedDotNet {
    param([string[]] $Arguments)

    Write-Host "dotnet $($Arguments -join ' ')"
    $output = @(& dotnet @Arguments 2>&1)
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        $details = [string]::Join([Environment]::NewLine, $output)
        throw "dotnet command failed with exit code $LASTEXITCODE.`n$details"
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
        $semanticFingerprint = [string] $manifest.source.semanticFingerprint

        if ([string]::IsNullOrWhiteSpace($version)) {
            throw "Could not read Telegram Bot API version from '$path'."
        }

        return [ordered]@{
            Version = $version
            ReleaseDate = $release
            ChangelogUrl = $changelog
            SemanticFingerprint = $semanticFingerprint
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
            SemanticFingerprint = ""
            SourcePath = $path
        }
    }

    throw "Could not find generated Telegram Bot API manifest or Update.g.cs under '$Root'."
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("teleflow-schema-check-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

try {
    $rawPath = Join-Path $tempDirectory "telegram-bot-api.raw.json"
    $normalizedPath = Join-Path $tempDirectory "telegram-bot-api.normalized.json"
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
        $rawPath,
        "--configuration",
        $GeneratorConfiguration)

    $raw = Get-Content -Raw -LiteralPath $rawPath | ConvertFrom-Json
    Invoke-CheckedDotNet @(
        "run",
        "--project",
        $generatorProject,
        "-c",
        $Configuration,
        "--",
        "normalize",
        "--input",
        $rawPath,
        "--output",
        $normalizedPath,
        "--configuration",
        $GeneratorConfiguration)

    $normalized = Get-Content -Raw -LiteralPath $normalizedPath | ConvertFrom-Json
    $latest = [ordered]@{
        Version = [string] $raw.Metadata.TelegramBotApiVersion
        ReleaseDate = [string] $raw.Metadata.TelegramBotApiReleasedAt
        ChangelogAnchor = [string] $raw.Metadata.TelegramBotApiChangelogAnchor
        SourceUrl = [string] $raw.Metadata.SourceUrl
        SourceSha256 = [string] $raw.Metadata.SourceSha256
        SemanticFingerprint = [string] $normalized.Metadata.SemanticFingerprint
    }

    $current = Get-GeneratedMetadata $TeleFlowRoot
    $hasVersionUpdate = $current.Version -ne $latest.Version
    $hasSemanticUpdate = $current.SemanticFingerprint -ne $latest.SemanticFingerprint
    $hasUpdate = $hasVersionUpdate -or $hasSemanticUpdate

    $result = [ordered]@{
        HasUpdate = $hasUpdate
        HasVersionUpdate = $hasVersionUpdate
        HasSemanticUpdate = $hasSemanticUpdate
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
        "schema_error=false" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "has_update=$($hasUpdate.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "has_version_update=$($hasVersionUpdate.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "has_semantic_update=$($hasSemanticUpdate.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "current_version=$($current.Version)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "latest_version=$($latest.Version)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "latest_release_date=$($latest.ReleaseDate)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "latest_changelog_anchor=$($latest.ChangelogAnchor)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
}
catch {
    if ($_.Exception.Message -notmatch "SCHEMA_CONFIGURATION_REQUIRED") {
        throw
    }

    $failureDirectory = if ([string]::IsNullOrWhiteSpace($FailureOutputPath)) {
        $tempDirectory
    }
    else {
        $FailureOutputPath
    }

    New-Item -ItemType Directory -Path $failureDirectory -Force | Out-Null
    $diagnosticsPath = Join-Path $failureDirectory "schema-diagnostics.json"
    $diagnostics = [ordered]@{
        Status = "configuration-required"
        Message = $_.Exception.Message
        ExceptionType = $_.Exception.GetType().FullName
        SourceUrl = $SourceUrl
        GeneratorConfiguration = $GeneratorConfiguration
        GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    $diagnostics | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $diagnosticsPath -Encoding UTF8

    if (Test-Path -LiteralPath $rawPath) {
        Copy-Item -LiteralPath $rawPath -Destination (Join-Path $failureDirectory "telegram-bot-api.raw.json") -Force
    }

    $failureResult = [ordered]@{
        Status = "configuration-required"
        HasUpdate = $false
        DiagnosticsPath = $diagnosticsPath
        Diagnostics = $diagnostics
    }
    $failureJson = $failureResult | ConvertTo-Json -Depth 8
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        Write-Output $failureJson
    }
    else {
        $outputDirectory = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        }

        Set-Content -LiteralPath $OutputPath -Value $failureJson -Encoding UTF8
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        "schema_error=true" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "has_update=false" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "has_version_update=false" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "has_semantic_update=false" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }

    Write-Warning "Telegram Bot API schema generation requires a configuration decision: $($_.Exception.Message)"
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
