function Find-OpenTeleFlowSchemaPullRequest {
    param(
        [AllowNull()]
        [object] $PullRequests,

        [Parameter(Mandatory)]
        [string] $RepositoryFullName,

        [Parameter(Mandatory)]
        [string] $BranchPrefix
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryFullName)) {
        throw "RepositoryFullName must not be empty."
    }

    if ([string]::IsNullOrWhiteSpace($BranchPrefix)) {
        throw "BranchPrefix must not be empty."
    }

    foreach ($pullRequest in $PullRequests) {
        if ($null -eq $pullRequest) {
            continue
        }

        $headRepository = [string] $pullRequest.head.repo.full_name
        $headBranch = [string] $pullRequest.head.ref
        $matchesRepository = [string]::Equals(
            $headRepository,
            $RepositoryFullName,
            [System.StringComparison]::OrdinalIgnoreCase)
        $matchesBranch = [string]::Equals(
            $headBranch,
            $BranchPrefix,
            [System.StringComparison]::Ordinal) -or
            $headBranch.StartsWith(
                "$BranchPrefix-",
                [System.StringComparison]::Ordinal)

        if ($matchesRepository -and $matchesBranch) {
            return $pullRequest
        }
    }

    return $null
}
