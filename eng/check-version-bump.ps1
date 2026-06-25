param(
    [string] $BaseRef = "main",
    [string] $Remote = "origin"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$versionFile = "src/TeleFlow.Telegram.SchemaGenerator/SchemaPipelineVersions.cs"

function Invoke-Git {
    param([string[]] $Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed with exit code ${LASTEXITCODE}: git $($Arguments -join ' ')"
    }
}

function Invoke-GitOutput {
    param([string[]] $Arguments)

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed with exit code ${LASTEXITCODE}: git $($Arguments -join ' ')"
    }

    return $output
}

function Get-VersionValues {
    param(
        [string] $Content,
        [string] $Source
    )

    $schemaMatch = [regex]::Match($Content, "public\s+const\s+int\s+SchemaVersion\s*=\s*(?<value>\d+)\s*;")
    $generatorMatch = [regex]::Match($Content, "public\s+const\s+int\s+GeneratorVersion\s*=\s*(?<value>\d+)\s*;")

    if (-not $schemaMatch.Success) {
        throw "Could not read SchemaVersion from $Source."
    }

    if (-not $generatorMatch.Success) {
        throw "Could not read GeneratorVersion from $Source."
    }

    return [ordered]@{
        SchemaVersion = [int] $schemaMatch.Groups["value"].Value
        GeneratorVersion = [int] $generatorMatch.Groups["value"].Value
    }
}

function Test-AnyPathMatches {
    param(
        [string[]] $Paths,
        [string[]] $Patterns
    )

    foreach ($path in $Paths) {
        foreach ($pattern in $Patterns) {
            if ($path -like $pattern) {
                return $true
            }
        }
    }

    return $false
}

function Format-MatchedPaths {
    param(
        [string[]] $Paths,
        [string[]] $Patterns
    )

    return $Paths |
        Where-Object {
            $path = $_
            $Patterns | Where-Object { $path -like $_ }
        } |
        Sort-Object -Unique
}

Push-Location $repositoryRoot
try {
    $baseBranch = "$Remote/$BaseRef"
    Invoke-Git @("fetch", "--quiet", $Remote, "+refs/heads/$BaseRef`:refs/remotes/$Remote/$BaseRef")

    $mergeBase = (Invoke-GitOutput @("merge-base", "HEAD", $baseBranch) | Select-Object -First 1).Trim()
    if ([string]::IsNullOrWhiteSpace($mergeBase)) {
        throw "Could not resolve merge base between HEAD and $baseBranch."
    }

    $changedFiles = @(Invoke-GitOutput @("diff", "--name-only", $mergeBase, "HEAD", "--") |
        ForEach-Object { $_.Replace("\", "/", [System.StringComparison]::Ordinal).Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($changedFiles.Count -eq 0) {
        Write-Host "No changed files against $baseBranch. Version bump check skipped."
        return
    }

    $schemaVersionPatterns = @(
        "src/TeleFlow.Telegram.SchemaGenerator/Extraction/*",
        "src/TeleFlow.Telegram.SchemaGenerator/Input/*",
        "src/TeleFlow.Telegram.SchemaGenerator/Models/*",
        "src/TeleFlow.Telegram.SchemaGenerator/Normalization/*",
        "src/TeleFlow.Telegram.SchemaGenerator/Parsing/*",
        "src/TeleFlow.Telegram.SchemaGenerator/Validation/*",
        "src/TeleFlow.Telegram.SchemaGenerator/Writers/*"
    )

    $generatorVersionPatterns = @(
        "src/TeleFlow.Telegram.SchemaGenerator/Generation/*"
    )

    $requiresSchemaVersionBump = Test-AnyPathMatches $changedFiles $schemaVersionPatterns
    $requiresGeneratorVersionBump = Test-AnyPathMatches $changedFiles $generatorVersionPatterns

    if (-not $requiresSchemaVersionBump -and -not $requiresGeneratorVersionBump) {
        Write-Host "No schema pipeline or generated output contract files changed. Version bump check passed."
        return
    }

    $baseVersionContent = Invoke-GitOutput @("show", "${mergeBase}:$versionFile") | Out-String
    $headVersionContent = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot $versionFile)
    $baseVersions = Get-VersionValues $baseVersionContent "${mergeBase}:$versionFile"
    $headVersions = Get-VersionValues $headVersionContent $versionFile

    $schemaVersionBumped = $headVersions.SchemaVersion -gt $baseVersions.SchemaVersion
    $generatorVersionBumped = $headVersions.GeneratorVersion -gt $baseVersions.GeneratorVersion

    if ($requiresSchemaVersionBump) {
        Write-Host "Schema pipeline files changed:"
        Format-MatchedPaths $changedFiles $schemaVersionPatterns | ForEach-Object { Write-Host "  $_" }
    }

    if ($requiresGeneratorVersionBump) {
        Write-Host "Generated output contract files changed:"
        Format-MatchedPaths $changedFiles $generatorVersionPatterns | ForEach-Object { Write-Host "  $_" }
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    if ($requiresSchemaVersionBump -and -not $schemaVersionBumped) {
        $errors.Add(
            "Schema pipeline changed, but SchemaVersion was not bumped. " +
            "Update SchemaPipelineVersions.SchemaVersion.")
    }

    if ($requiresGeneratorVersionBump -and -not $generatorVersionBumped) {
        $errors.Add(
            "Generated output contract changed, but GeneratorVersion was not bumped. " +
            "Update SchemaPipelineVersions.GeneratorVersion.")
    }

    if ($errors.Count -gt 0) {
        throw ($errors -join [Environment]::NewLine)
    }

    Write-Host "Version bump check passed."
    Write-Host "Base versions: SchemaVersion=$($baseVersions.SchemaVersion), GeneratorVersion=$($baseVersions.GeneratorVersion)"
    Write-Host "Head versions: SchemaVersion=$($headVersions.SchemaVersion), GeneratorVersion=$($headVersions.GeneratorVersion)"
}
finally {
    Pop-Location
}
