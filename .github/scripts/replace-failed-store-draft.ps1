<#
.SYNOPSIS
Removes one exact failed submission before publishing its corrected replacement.
.DESCRIPTION
The Store API refuses updates after CommitFailed and requires a new submission.
Checks the current pending ID, failed state, and exact upload before deletion.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z0-9]+$')][string]$AppId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$SubmissionId,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.-]+\.msixupload$')][string]$UploadFileName
)

$ErrorActionPreference = 'Stop'
$api = "https://manage.devcenter.microsoft.com/v1.0/my/applications/$AppId"
$token = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$env:AZURE_AD_TENANT_ID/oauth2/token" -Body @{
    grant_type = 'client_credentials'
    client_id = $env:AZURE_AD_APPLICATION_CLIENT_ID
    client_secret = $env:AZURE_AD_APPLICATION_SECRET
    resource = 'https://manage.devcenter.microsoft.com'
}
$headers = @{ Authorization = "Bearer $($token.access_token)" }
$app = Invoke-RestMethod -Uri $api -Headers $headers
if ([string]$app.pendingApplicationSubmission.id -ne $SubmissionId) {
    throw 'The selected failed submission is not the current pending submission.'
}

$uri = "$api/submissions/$SubmissionId"
$submission = Invoke-RestMethod -Uri $uri -Headers $headers
if ($submission.status -ne 'CommitFailed') { throw 'The selected submission has not failed validation.' }
$uploads = @($submission.applicationPackages | Where-Object fileStatus -EQ 'PendingUpload')
if ($uploads.Count -ne 1 -or $uploads[0].fileName -cne $UploadFileName) {
    throw 'The failed submission does not contain the exact expected upload.'
}

# Only this failed draft is removed; the published submission is never addressed.
$null = Invoke-RestMethod -Method Delete -Uri $uri -Headers $headers
$after = Invoke-RestMethod -Uri $api -Headers $headers
if ($null -ne $after.pendingApplicationSubmission) {
    throw 'A submission is still pending after deletion. Do not publish over it.'
}
Write-Output "Removed failed submission $SubmissionId containing $UploadFileName; the published app is unchanged."
