@{
    RootModule        = 'CarbonFiles.psm1'
    ModuleVersion     = '0.0.0'
    GUID              = '8b7e572a-3410-4ff6-987a-51aa53614f99'
    Author            = 'CarbonFiles'
    Description       = 'PowerShell client for the CarbonFiles API'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Connect-CfServer'
        'Disconnect-CfServer'
        'New-CfBucket'
        'Get-CfBucket'
        'Update-CfBucket'
        'Remove-CfBucket'
        'Get-CfBucketSummary'
        'Save-CfBucketZip'
        'Get-CfFile'
        'Remove-CfFile'
        'Send-CfFile'
        'Sync-CfDeploy'
        'Save-CfFileContent'
        'New-CfApiKey'
        'Get-CfApiKey'
        'Remove-CfApiKey'
        'Get-CfApiKeyUsage'
        'New-CfUploadToken'
        'New-CfDashboardToken'
        'Test-CfDashboardToken'
        'Get-CfStats'
        'Test-CfHealth'
    )
    CmdletsToExport   = @()
    VariablesToExport  = @()
    AliasesToExport    = @()
    PrivateData       = @{
        PSData = @{
            Tags         = @('CarbonFiles', 'API', 'FileSharing', 'REST')
            LicenseUri   = 'https://github.com/CarbonNeuron/carbon-files/blob/main/LICENSE'
            ProjectUri   = 'https://github.com/CarbonNeuron/carbon-files'
        }
    }
}
