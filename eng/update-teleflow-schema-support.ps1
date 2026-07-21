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

function Test-TeleFlowGeneratedOutputChanged {
    param(
        [string] $TeleFlowRoot,
        [string] $SchemaOutput,
        [string] $TelegramOutput
    )

    $schemaRelativePath = [System.IO.Path]::GetRelativePath($TeleFlowRoot, $SchemaOutput).Replace("\", "/")
    $telegramRelativePath = [System.IO.Path]::GetRelativePath($TeleFlowRoot, $TelegramOutput).Replace("\", "/")
    $changes = @(& git -C $TeleFlowRoot status --porcelain -- $schemaRelativePath $telegramRelativePath)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect generated TeleFlow output changes."
    }

    return $changes.Count -gt 0
}

function Update-TelegramBotApiBadge {
    param(
        [string] $TeleFlowRoot,
        [string] $SchemaOutput
    )

    $metadata = Get-GeneratedManifestMetadata $SchemaOutput
    $badgePath = Join-Path $TeleFlowRoot "docs\badges\telegram-bot-api.json"
    New-Item -ItemType Directory -Path (Split-Path -Parent $badgePath) -Force | Out-Null

    $badge = [ordered]@{
        schemaVersion = 1
        label = "Telegram Bot API"
        message = $metadata.Version
        color = "26A5E4"
        namedLogo = "telegram"
    }

    $badgeJson = (($badge | ConvertTo-Json -Depth 4) -replace "`r`n", "`n" -replace "`r", "`n") + "`n"
    [System.IO.File]::WriteAllText($badgePath, $badgeJson, [System.Text.UTF8Encoding]::new($false))
}

function Update-TelegramBotApiChangelog {
    param(
        [string] $TeleFlowRoot,
        [string] $SchemaOutput,
        [string] $PreviousVersion,
        [bool] $GeneratedOutputChanged
    )

    $metadata = Get-GeneratedManifestMetadata $SchemaOutput
    $isVersionUpdate = -not [string]::Equals(
        $PreviousVersion,
        $metadata.Version,
        [System.StringComparison]::Ordinal)

    if (-not $isVersionUpdate -and -not $GeneratedOutputChanged) {
        return
    }

    $changelogPath = Join-Path $TeleFlowRoot "CHANGELOG.md"
    if (-not (Test-Path -LiteralPath $changelogPath)) {
        throw "Could not find TeleFlow changelog at '$changelogPath'."
    }

    $entry = if ($isVersionUpdate) {
        "- Updated the generated Telegram schema and client surface to Telegram Bot API $($metadata.Version)."
    }
    else {
        "- Refreshed the generated Telegram schema and client surface for Telegram Bot API $($metadata.Version)."
    }

    $content = [System.IO.File]::ReadAllText($changelogPath)
    $lineEnding = if ($content.Contains("`r`n", [System.StringComparison]::Ordinal)) { "`r`n" } else { "`n" }
    $lines = [System.Collections.Generic.List[string]]::new(
        [string[]](($content -replace "`r`n", "`n") -split "`n"))
    $unreleasedIndex = $lines.FindIndex([System.Predicate[string]]{
            param($line)
            $line -eq "## Unreleased"
        })

    if ($unreleasedIndex -lt 0) {
        throw "TeleFlow changelog must contain a '## Unreleased' section."
    }

    $nextReleaseIndex = $lines.FindIndex(
        $unreleasedIndex + 1,
        [System.Predicate[string]]{
            param($line)
            $line.StartsWith("## ", [System.StringComparison]::Ordinal)
        })
    if ($nextReleaseIndex -lt 0) {
        $nextReleaseIndex = $lines.Count
    }

    for ($index = $unreleasedIndex + 1; $index -lt $nextReleaseIndex; $index++) {
        if ([string]::Equals($lines[$index], $entry, [System.StringComparison]::Ordinal)) {
            return
        }
    }

    $changedIndex = -1
    for ($index = $unreleasedIndex + 1; $index -lt $nextReleaseIndex; $index++) {
        if ($lines[$index] -eq "### Changed") {
            $changedIndex = $index
            break
        }
    }

    if ($changedIndex -ge 0) {
        $entryIndex = $changedIndex + 1
        if ($entryIndex -lt $lines.Count -and [string]::IsNullOrWhiteSpace($lines[$entryIndex])) {
            $entryIndex++
        }
        else {
            $lines.Insert($entryIndex, "")
            $entryIndex++
        }

        $lines.Insert($entryIndex, $entry)
    }
    else {
        $insertIndex = $nextReleaseIndex
        while ($insertIndex -gt $unreleasedIndex + 1 -and [string]::IsNullOrWhiteSpace($lines[$insertIndex - 1])) {
            $insertIndex--
        }

        $lines.Insert($insertIndex, "")
        $lines.Insert($insertIndex + 1, "### Changed")
        $lines.Insert($insertIndex + 2, "")
        $lines.Insert($insertIndex + 3, $entry)
    }

    $updatedContent = [string]::Join($lineEnding, $lines)
    if (-not $updatedContent.EndsWith($lineEnding, [System.StringComparison]::Ordinal)) {
        $updatedContent += $lineEnding
    }

    [System.IO.File]::WriteAllText($changelogPath, $updatedContent, [System.Text.UTF8Encoding]::new($false))
}
