$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../replace-failed-store-draft.ps1'

# Model the exact submission binding and deletion without making network calls.
function Reset-FailedFixture {
    $global:failedDraft = @{
        status = 'CommitFailed'
        applicationPackages = @(@{ fileName = 'ImmichDrive-0.1.49.msixupload'; fileStatus = 'PendingUpload' })
    }
    $global:failedPendingId = '123'
    $global:failedDeletes = 0
}
function global:Invoke-RestMethod {
    param($Method, $Uri, $Headers, $Body, $ContentType)
    if ($Uri -like '*oauth2/token') { return @{ access_token = 'test-only' } }
    if ($Uri -match '/submissions/123$') {
        if ($Method -eq 'Delete') {
            $global:failedDeletes++
            $global:failedPendingId = $null
            return
        }
        return $global:failedDraft
    }
    if ($global:failedPendingId) { return @{ pendingApplicationSubmission = @{ id = $global:failedPendingId } } }
    return @{ pendingApplicationSubmission = $null }
}
function Check-Failed($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}
function Run-Failed {
    & $scriptPath -AppId 9MWC6165N7DH -SubmissionId 123 -UploadFileName ImmichDrive-0.1.49.msixupload
}

Reset-FailedFixture
Run-Failed
Check-Failed ($global:failedDeletes -eq 1) 'exact failed submission removed once'
foreach ($status in @('PendingCommit', 'Certification', 'Published', 'Canceled')) {
    Reset-FailedFixture
    $global:failedDraft.status = $status
    try { Run-Failed; throw 'Expected rejection.' } catch { Check-Failed ($_.Exception.Message -like '*has not failed validation*') "$status is rejected" }
    Check-Failed ($global:failedDeletes -eq 0) "$status is not deleted"
}
Reset-FailedFixture
$global:failedPendingId = '999'
try { Run-Failed; throw 'Expected rejection.' } catch { Check-Failed ($_.Exception.Message -like '*not the current pending submission*') 'different pending submission is rejected' }
Check-Failed ($global:failedDeletes -eq 0) 'unrelated submission is not deleted'
Reset-FailedFixture
$global:failedDraft.applicationPackages[0].fileName = 'other.msixupload'
try { Run-Failed; throw 'Expected rejection.' } catch { Check-Failed ($_.Exception.Message -like '*exact expected upload*') 'unexpected upload is rejected' }
Check-Failed ($global:failedDeletes -eq 0) 'unexpected upload is not deleted'
