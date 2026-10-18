function Sync-CfDeploy {
    <#
    .SYNOPSIS
        Verifies a complete deploy manifest and prunes other bucket files.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string]$BucketId,
        [Parameter(Mandatory)] [object[]]$Files,
        [Parameter()] [string]$Token
    )

    if ($PSCmdlet.ShouldProcess($BucketId, 'Sync deploy and prune stale files')) {
        $query = if ($Token) { @{ token = $Token } } else { @{} }
        Invoke-CfApiRequest -Method Post -Path "/api/buckets/$BucketId/deploy/sync" -Query $query -Body @{ files = $Files }
    }
}
