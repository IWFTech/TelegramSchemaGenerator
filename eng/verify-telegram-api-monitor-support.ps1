$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "telegram-api-monitor-support.ps1")

function Assert-Condition {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-PullRequest {
    param(
        [string] $RepositoryFullName,
        [string] $Branch,
        [string] $Url
    )

    return [pscustomobject]@{
        html_url = $Url
        head = [pscustomobject]@{
            ref = $Branch
            repo = [pscustomobject]@{
                full_name = $RepositoryFullName
            }
        }
    }
}

$repository = "IWFTech/TeleFlow"
$branchPrefix = "schema/update-telegram-bot-api-10.2"

$emptyResponse = [object[]]@()
$emptyResult = Find-OpenTeleFlowSchemaPullRequest `
    -PullRequests $emptyResponse `
    -RepositoryFullName $repository `
    -BranchPrefix $branchPrefix
Assert-Condition `
    -Condition ($null -eq $emptyResult) `
    -Message "An empty GitHub pull request response was treated as an open pull request."

$legacyPullRequest = New-PullRequest `
    -RepositoryFullName $repository `
    -Branch $branchPrefix `
    -Url "https://github.com/IWFTech/TeleFlow/pull/100"
$legacyResult = Find-OpenTeleFlowSchemaPullRequest `
    -PullRequests $legacyPullRequest `
    -RepositoryFullName $repository `
    -BranchPrefix $branchPrefix
Assert-Condition `
    -Condition ($legacyResult.html_url -eq $legacyPullRequest.html_url) `
    -Message "A legacy fixed-name update branch was not detected."

$suffixedPullRequest = New-PullRequest `
    -RepositoryFullName $repository `
    -Branch "$branchPrefix-29832629448-2" `
    -Url "https://github.com/IWFTech/TeleFlow/pull/101"
$suffixedResult = Find-OpenTeleFlowSchemaPullRequest `
    -PullRequests $suffixedPullRequest `
    -RepositoryFullName $repository `
    -BranchPrefix $branchPrefix
Assert-Condition `
    -Condition ($suffixedResult.html_url -eq $suffixedPullRequest.html_url) `
    -Message "A run-suffixed update branch was not detected."

$otherVersionPullRequest = New-PullRequest `
    -RepositoryFullName $repository `
    -Branch "schema/update-telegram-bot-api-10.20-1-1" `
    -Url "https://github.com/IWFTech/TeleFlow/pull/102"
$otherVersionResult = Find-OpenTeleFlowSchemaPullRequest `
    -PullRequests $otherVersionPullRequest `
    -RepositoryFullName $repository `
    -BranchPrefix $branchPrefix
Assert-Condition `
    -Condition ($null -eq $otherVersionResult) `
    -Message "A branch for another Bot API version matched the update prefix."

$forkPullRequest = New-PullRequest `
    -RepositoryFullName "external/TeleFlow" `
    -Branch "$branchPrefix-29832629448-3" `
    -Url "https://github.com/IWFTech/TeleFlow/pull/103"
$forkResult = Find-OpenTeleFlowSchemaPullRequest `
    -PullRequests $forkPullRequest `
    -RepositoryFullName $repository `
    -BranchPrefix $branchPrefix
Assert-Condition `
    -Condition ($null -eq $forkResult) `
    -Message "A pull request from another repository was treated as a managed schema update."

Write-Host "Telegram API monitor support verification passed."
