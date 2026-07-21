$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "update-teleflow-schema-support.ps1")

function Assert-Condition {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Write-Manifest {
    param(
        [string] $SchemaOutput,
        [string] $Version
    )

    $manifest = [ordered]@{
        telegramBotApi = [ordered]@{
            version = $Version
        }
    }
    $json = (($manifest | ConvertTo-Json -Depth 4) -replace "`r`n", "`n") + "`n"
    [System.IO.File]::WriteAllText(
        (Join-Path $SchemaOutput "telegram-bot-api.manifest.json"),
        $json,
        [System.Text.UTF8Encoding]::new($false))
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("teleflow-schema-support-test-" + [System.Guid]::NewGuid().ToString("N"))
$schemaOutput = Join-Path $testRoot "src\TeleFlow.Telegram.Schema"
$telegramOutput = Join-Path $testRoot "src\TeleFlow.Telegram.Client"
$changelogPath = Join-Path $testRoot "CHANGELOG.md"

try {
    New-Item -ItemType Directory -Path $schemaOutput -Force | Out-Null
    New-Item -ItemType Directory -Path $telegramOutput -Force | Out-Null
    Write-Manifest -SchemaOutput $schemaOutput -Version "10.2"
    [System.IO.File]::WriteAllText(
        (Join-Path $telegramOutput "Generated.g.cs"),
        "// generated`n",
        [System.Text.UTF8Encoding]::new($false))

    $historicalEntry = "- Updated the generated Telegram schema and client surface to Telegram Bot API 10.2."
    $initialChangelog = @"
# Changelog

## Unreleased

### Added

- Existing change.

## 1.0.0

### Changed

$historicalEntry
"@
    [System.IO.File]::WriteAllText($changelogPath, $initialChangelog.Replace("`r`n", "`n") + "`n")

    Update-TelegramBotApiChangelog `
        -TeleFlowRoot $testRoot `
        -SchemaOutput $schemaOutput `
        -PreviousVersion "10.1" `
        -GeneratedOutputChanged $true

    $updatedChangelog = [System.IO.File]::ReadAllText($changelogPath).Replace("`r`n", "`n")
    Assert-Condition `
        -Condition $updatedChangelog.Contains(
            "### Changed`n`n$historicalEntry`n`n## 1.0.0",
            [System.StringComparison]::Ordinal) `
        -Message "A historical changelog entry suppressed the required Unreleased version update."

    Update-TelegramBotApiChangelog `
        -TeleFlowRoot $testRoot `
        -SchemaOutput $schemaOutput `
        -PreviousVersion "10.1" `
        -GeneratedOutputChanged $true
    $updateEntryCount = [System.Text.RegularExpressions.Regex]::Matches(
        [System.IO.File]::ReadAllText($changelogPath),
        [regex]::Escape($historicalEntry)).Count
    Assert-Condition `
        -Condition ($updateEntryCount -eq 2) `
        -Message "The Unreleased version update entry is not idempotent."

    $refreshBaseChangelog = @"
# Changelog

## Unreleased

### Changed

- Existing change.

## 1.0.0
"@
    [System.IO.File]::WriteAllText($changelogPath, $refreshBaseChangelog.Replace("`r`n", "`n") + "`n")
    Update-TelegramBotApiChangelog `
        -TeleFlowRoot $testRoot `
        -SchemaOutput $schemaOutput `
        -PreviousVersion "10.2" `
        -GeneratedOutputChanged $false
    Assert-Condition `
        -Condition ([System.IO.File]::ReadAllText($changelogPath) -eq ($refreshBaseChangelog.Replace("`r`n", "`n") + "`n")) `
        -Message "A no-op same-version refresh changed the changelog."

    $refreshEntry = "- Refreshed the generated Telegram schema and client surface for Telegram Bot API 10.2."
    Update-TelegramBotApiChangelog `
        -TeleFlowRoot $testRoot `
        -SchemaOutput $schemaOutput `
        -PreviousVersion "10.2" `
        -GeneratedOutputChanged $true
    Update-TelegramBotApiChangelog `
        -TeleFlowRoot $testRoot `
        -SchemaOutput $schemaOutput `
        -PreviousVersion "10.2" `
        -GeneratedOutputChanged $true
    $refreshEntryCount = [System.Text.RegularExpressions.Regex]::Matches(
        [System.IO.File]::ReadAllText($changelogPath),
        [regex]::Escape($refreshEntry)).Count
    Assert-Condition `
        -Condition ($refreshEntryCount -eq 1) `
        -Message "The same-version refresh changelog entry is missing or duplicated."

    Update-TelegramBotApiBadge -TeleFlowRoot $testRoot -SchemaOutput $schemaOutput
    $badge = Get-Content -Raw -LiteralPath (Join-Path $testRoot "docs\badges\telegram-bot-api.json") | ConvertFrom-Json
    Assert-Condition `
        -Condition ($badge.message -eq "10.2") `
        -Message "The Telegram Bot API badge does not use the generated manifest version."

    & git -C $testRoot init --initial-branch main --quiet
    & git -C $testRoot config user.name "Schema Support Verification"
    & git -C $testRoot config user.email "schema-support-verification@example.invalid"
    & git -C $testRoot config core.autocrlf false
    & git -C $testRoot add .
    & git -C $testRoot commit --quiet -m "test fixture"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create schema support verification fixture."
    }

    Assert-Condition `
        -Condition (-not (Test-TeleFlowGeneratedOutputChanged `
                -TeleFlowRoot $testRoot `
                -SchemaOutput $schemaOutput `
                -TelegramOutput $telegramOutput)) `
        -Message "A clean generated-output fixture was reported as changed."

    [System.IO.File]::AppendAllText(
        (Join-Path $telegramOutput "Generated.g.cs"),
        "// changed`n",
        [System.Text.UTF8Encoding]::new($false))
    Assert-Condition `
        -Condition (Test-TeleFlowGeneratedOutputChanged `
            -TeleFlowRoot $testRoot `
            -SchemaOutput $schemaOutput `
            -TelegramOutput $telegramOutput) `
        -Message "A modified generated-output fixture was reported as clean."

    Write-Host "TeleFlow schema update support verification passed."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
