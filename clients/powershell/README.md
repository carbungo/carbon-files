# CarbonFiles

PowerShell client for the [CarbonFiles](https://github.com/CarbonNeuron/carbon-files) file-sharing API. Requires PowerShell 7.0+.

## Installation

```powershell
Install-Module -Name CarbonFiles
```

## Quick Start

```powershell
# Connect to server
Connect-CfServer -Uri "https://files.example.com" -Token "cf4_your_api_key"

# Create a bucket
$bucket = New-CfBucket -Name "my-bucket" -Description "Project assets" -ExpiresIn "30d"

# Upload a file
Send-CfFile -BucketId $bucket.Id -FilePath ./report.pdf

# List files
Get-CfFile -BucketId $bucket.Id
```

## Common Operations

### Buckets

```powershell
# Create a bucket
New-CfBucket -Name "my-bucket" -Description "Shared files" -ExpiresIn "7d"

# List all buckets
Get-CfBucket

# Get a specific bucket
Get-CfBucket -Id "bucket-id"

# Update a bucket
Update-CfBucket -Id "bucket-id" -Name "new-name" -ExpiresIn "30d"

# Delete a bucket
Remove-CfBucket -Id "bucket-id"

# Get bucket summary (plaintext)
Get-CfBucketSummary -Id "bucket-id"

# Download bucket as ZIP
Save-CfBucketZip -Id "bucket-id" -OutFile ./bucket.zip
```

### Files

```powershell
# Upload a file
Send-CfFile -BucketId "bucket-id" -FilePath ./photo.jpg

# Upload with custom destination path
Send-CfFile -BucketId "bucket-id" -FilePath ./photo.jpg -DestinationPath "images/photo.jpg"

# List files in a bucket
Get-CfFile -BucketId "bucket-id"

# Get file metadata
Get-CfFile -BucketId "bucket-id" -FilePath "photo.jpg"

# Download file content
Save-CfFileContent -BucketId "bucket-id" -FilePath "photo.jpg" -OutFile ./downloaded.jpg

# Delete a file
Remove-CfFile -BucketId "bucket-id" -FilePath "photo.jpg"
```

### API Keys

```powershell
# Create an API key
$key = New-CfApiKey -Name "ci-pipeline"

# List API keys
Get-CfApiKey

# Get usage stats
Get-CfApiKeyUsage -Prefix "cf4_ab"

# Revoke an API key
Remove-CfApiKey -Prefix "cf4_ab"
```

### Tokens

```powershell
# Create an upload token (scoped to one bucket)
New-CfUploadToken -BucketId "bucket-id" -ExpiresIn "1h" -MaxUploads 10

# Create a dashboard JWT
New-CfDashboardToken -ExpiresIn "1h"

# Validate current dashboard token
Test-CfDashboardToken
```

### Diagnostics

```powershell
# Health check
Test-CfHealth

# System statistics (admin only)
Get-CfStats
```

## Authentication

CarbonFiles supports four token types, all passed as Bearer tokens:

| Type | Format | Scope |
|------|--------|-------|
| Admin key | Any string | Full access |
| API key | `cf4_` prefix | Own buckets |
| Dashboard JWT | JWT token | Admin-level, 24h max |
| Upload token | `cfu_` prefix | Single bucket |

```powershell
# Connect with an API key
Connect-CfServer -Uri "https://files.example.com" -Token "cf4_your_api_key"

# Connect with an admin key
Connect-CfServer -Uri "https://files.example.com" -Token "your_admin_key"

# Disconnect
Disconnect-CfServer
```

Upload tokens can be passed directly to `Send-CfFile`:

```powershell
Send-CfFile -BucketId "bucket-id" -FilePath ./file.txt -Token "cfu_upload_token"
```

## Pipeline Support

Many cmdlets accept pipeline input:

```powershell
# Delete all files in a bucket
Get-CfFile -BucketId "bucket-id" | Remove-CfFile

# List files across multiple buckets
Get-CfBucket | ForEach-Object { Get-CfFile -BucketId $_.Id }
```

## All Cmdlets

| Cmdlet | Description |
|--------|-------------|
| `Connect-CfServer` | Connect to a CarbonFiles server |
| `Disconnect-CfServer` | Clear connection state |
| `New-CfBucket` | Create a bucket |
| `Get-CfBucket` | List or get buckets |
| `Update-CfBucket` | Update a bucket |
| `Remove-CfBucket` | Delete a bucket |
| `Get-CfBucketSummary` | Get bucket summary |
| `Save-CfBucketZip` | Download bucket as ZIP |
| `Get-CfFile` | List or get files |
| `Send-CfFile` | Upload a file |
| `Sync-CfDeploy` | Verify a complete path/SHA-256 manifest and prune other bucket files |
| `Save-CfFileContent` | Download file content |
| `Remove-CfFile` | Delete a file |
| `New-CfApiKey` | Create an API key |
| `Get-CfApiKey` | List API keys |
| `Remove-CfApiKey` | Revoke an API key |
| `Get-CfApiKeyUsage` | API key usage stats |
| `New-CfUploadToken` | Create an upload token |
| `New-CfDashboardToken` | Create a dashboard token |
| `Test-CfDashboardToken` | Validate dashboard token |
| `Get-CfStats` | System statistics |
| `Test-CfHealth` | Health check |

## Links

- [CarbonFiles repository](https://github.com/CarbonNeuron/carbon-files)
- [PowerShell Gallery](https://www.powershellgallery.com/packages/CarbonFiles)
