$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path

# Resolve the real projects' runtime packs without restore, compilation, or Windows tools.
function Test-RuntimeSelection {
    param(
        [string]$Project,
        [string]$Platform,
        [string]$RuntimeVersion,
        [bool]$ShouldPass
    )

    $rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    $buildArgs = @(
        'msbuild', (Join-Path $repoRoot $Project),
        '-nologo', '-verbosity:quiet', '-maxCpuCount:1', '-nodeReuse:false',
        '-target:ProcessFrameworkReferences', '-getItem:RuntimePack', '-p:EnableWindowsTargeting=true',
        '-p:SelfContained=true', "-p:Platform=$Platform", "-p:RuntimeIdentifier=$rid"
    )
    if ($RuntimeVersion) {
        $buildArgs += "-p:RuntimeFrameworkVersion=$RuntimeVersion"
    }

    $output = & dotnet @buildArgs 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "Runtime resolution failed for $Project ($Platform, '$RuntimeVersion'): $output"
    }
    if (-not $ShouldPass -and ($exitCode -eq 0 -or $output -notlike '*CVE-2026-111497*')) {
        throw "Expected the runtime floor to reject $Project ($Platform, '$RuntimeVersion'): $output"
    }

    if ($ShouldPass) {
        # Windows SDK reference packs also appear here; only core runtime packs contain this decoder.
        $packs = @(($output | ConvertFrom-Json).Items.RuntimePack |
            Where-Object { $_.FrameworkName -eq 'Microsoft.NETCore.App' })
        if ($packs.Count -ne 1 -or $packs[0].RuntimeIdentifier -ne $rid -or
            [version]$packs[0].NuGetPackageVersion -lt [version]'10.0.14') {
            throw "Missing or invalid core runtime pack for $Project ($Platform): $output"
        }
        if ($RuntimeVersion -and $packs[0].NuGetPackageVersion -ne $RuntimeVersion) {
            throw "Runtime override was not selected for $Project ($Platform): $output"
        }
    }

    Write-Output "PASS: $Project $Platform runtime '$RuntimeVersion' (allowed: $ShouldPass)"
}

Push-Location $repoRoot
try {
    foreach ($project in @('ImmichDrive/ImmichDrive.csproj', 'ImmichDrive.ThumbnailProvider/ImmichDrive.ThumbnailProvider.csproj')) {
        foreach ($platform in @('x64', 'ARM64')) {
            Test-RuntimeSelection $project $platform '' $true
            Test-RuntimeSelection $project $platform '10.0.13' $false
            Test-RuntimeSelection $project $platform '10.0.14' $true
            # Only version selection is tested; this does not download a future runtime pack.
            Test-RuntimeSelection $project $platform '10.0.15' $true
        }
    }
} finally {
    Pop-Location
}
