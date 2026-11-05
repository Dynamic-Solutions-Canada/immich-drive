$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../repair-store-draft.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('store-repair-test-' + [guid]::NewGuid().ToString('N'))

# Exercise draft recovery without network access or real Store packages.
function Reset-RepairFixture([string]$Status) {
    $global:repairFixture = @{
        status = $Status
        pricing = @{ priceId = 'Tier1012'; marketSpecificPricings = @{ CA = 'keep' } }
        targetPublishMode = 'Immediate'
        listings = @{ 'en-us' = @{ baseListing = @{ title = 'Drive for Immich' } } }
        applicationPackages = @(@{ fileName = 'old.msixupload'; fileStatus = 'PendingUpload' })
    }
    $global:repairPuts = 0
    $global:repairUploads = 0
    $global:repairPendingId = '123'
}
function global:Invoke-RestMethod {
    param($Method, $Uri, $Headers, $Body, $ContentType)
    if ($Uri -like '*oauth2/token') { return @{ access_token = 'test-only' } }
    if ($Uri -match '/submissions/123$') {
        if ($Method -eq 'Put') {
            $global:repairPuts++
            $global:repairFixture = $Body | ConvertFrom-Json
            return @{ fileUploadUrl = 'https://fixture.invalid/upload' }
        }
        return $global:repairFixture
    }
    return @{ pendingApplicationSubmission = @{ id = $global:repairPendingId } }
}
function global:Invoke-WebRequest {
    param($Method, $Uri, $InFile, $ContentType, $Headers)
    if ($Method -ne 'Put' -or $Uri -ne 'https://fixture.invalid/upload') { throw 'Unexpected upload call.' }
    $global:repairUploads++
}
function Check-Repair($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}

try {
    $null = New-Item -ItemType Directory -Path "$temporary/source" -Force
    [IO.File]::WriteAllText("$temporary/source/test.msixbundle", 'fixture')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $upload = "$temporary/test.msixupload"
    [IO.Compression.ZipFile]::CreateFromDirectory("$temporary/source", $upload)

    foreach ($status in @('PendingCommit')) {
        Reset-RepairFixture $status
        & $scriptPath -AppId 9MWC6165N7DH -SubmissionId 123 -PriceId Tier1012 -UploadPath $upload
        Check-Repair ($global:repairPuts -eq 1 -and $global:repairUploads -eq 1) "$status replaces the selected draft's package once"
        Check-Repair ($global:repairFixture.applicationPackages[0].fileStatus -eq 'PendingDelete') 'old package is marked for removal'
        Check-Repair ($global:repairFixture.listings.'en-us'.baseListing.title -eq 'Drive for Immich') 'listing is preserved'
        Check-Repair ($global:repairFixture.pricing.marketSpecificPricings.CA -eq 'keep' -and $global:repairFixture.targetPublishMode -eq 'Immediate') 'regional pricing and publishing mode are preserved'
    }

    foreach ($status in @('CommitFailed', 'Certification', 'Published')) {
        Reset-RepairFixture $status
        try {
            & $scriptPath -AppId 9MWC6165N7DH -SubmissionId 123 -PriceId Tier1012 -UploadPath $upload
            throw 'Expected rejection.'
        } catch { Check-Repair ($_.Exception.Message -like '*not editable*') "$status is rejected" }
        Check-Repair ($global:repairPuts -eq 0 -and $global:repairUploads -eq 0) "$status is not modified"
    }

    Reset-RepairFixture 'CommitFailed'
    $global:repairPendingId = '999'
    try {
        & $scriptPath -AppId 9MWC6165N7DH -SubmissionId 123 -PriceId Tier1012 -UploadPath $upload
        throw 'Expected rejection.'
    } catch { Check-Repair ($_.Exception.Message -like '*not the current pending submission*') 'different pending submission is rejected' }
    Check-Repair ($global:repairPuts -eq 0 -and $global:repairUploads -eq 0) 'unrelated submission is not modified'
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
