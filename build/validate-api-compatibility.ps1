param(
    [string[]]$Modules,
    [string]$ReportPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/validation/api-compatibility.json')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'module-tools.ps1')
$root = Split-Path $PSScriptRoot -Parent
$baseline = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'api-baseline.json') -Raw | ConvertFrom-Json
if ($baseline.formatVersion -ne 1 -or $baseline.commit -notmatch '^[a-f0-9]{40}$') {
    throw 'API baseline requires formatVersion 1 and a full immutable commit SHA.'
}
if (-not $Modules) { $Modules = @($baseline.modules) }
foreach ($module in $Modules) {
    if ($module -notin $baseline.modules) { throw "Module is not configured for API protection: $module" }
}
$resolvedBaseline = & git -C $root rev-parse --verify "$($baseline.commit)^{commit}"
if ($LASTEXITCODE -ne 0 -or $resolvedBaseline -ne $baseline.commit) {
    throw "Baseline commit $($baseline.commit) is unavailable. Fetch the recorded history; do not replace the baseline automatically."
}
$currentHead = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the current source revision.' }
$workingTree = @(& git -C $root status --porcelain=v1 --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the current working-tree state.' }
$working = Join-Path ([IO.Path]::GetTempPath()) ('ms-api-compatibility-' + [Guid]::NewGuid().ToString('N'))
$baselineRoot = Join-Path $working 'baseline-source'
$baselinePackages = Join-Path $working 'baseline-packages'
$currentPackages = Join-Path $working 'current-packages'
$baselineVersion = '1.0.0-api-baseline'
$currentVersion = '1.0.0-api-current.' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$results = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    formatVersion = 1; status = 'running'; baselineCommit = $baseline.commit; currentHead = $currentHead
    currentSource = 'working-tree'; dirty = $workingTree.Count -gt 0; workingTreeStatus = $workingTree
    sdk = (& dotnet --version); modules = $Modules; packages = $results; workingDirectory = $working
}
try {
    New-Item -ItemType Directory $working, $baselineRoot, $baselinePackages, $currentPackages | Out-Null
    $archive = Join-Path $working 'baseline.zip'
    $moduleDirectories = @(& git -C $root ls-tree --name-only -d $baseline.commit |
        Where-Object { $_.StartsWith('MS.Microservice.', [StringComparison]::Ordinal) })
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate baseline module directories.' }
    $archivePaths = @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'nuget.config', 'src') + $moduleDirectories
    & git -C $root archive --format=zip "--output=$archive" $baseline.commit -- @archivePaths
    if ($LASTEXITCODE -ne 0) { throw 'Cannot export the recorded baseline source.' }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $baselineRoot)

    $baselineProjects = @(Get-ModuleProjects $baselineRoot $Modules)
    $currentProjects = @(Get-ModuleProjects $root $Modules)
    # Keep protecting former dependencies too when an entry module removes a project reference.
    $protectedModules = @(@($baselineProjects + $currentProjects) |
        ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) } | Sort-Object -Unique)
    $baselineProjects = @(Get-ModuleProjects $baselineRoot $protectedModules)
    $currentProjects = @(Get-ModuleProjects $root $protectedModules)
    $sourceDirectories = @($currentProjects | ForEach-Object { [IO.Path]::GetRelativePath($root, (Split-Path $_ -Parent)) })
    $report['untrackedSourceFiles'] = @(& git -C $root -c core.quotepath=false ls-files --others --exclude-standard -- @sourceDirectories)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate untracked source inputs.' }

    Push-Location $baselineRoot
    try {
        $report['baselineSdk'] = & dotnet --version
        foreach ($project in $baselineProjects) {
            Invoke-CheckedDotnet pack $project -c Release --artifacts-path (Join-Path $working 'baseline-artifacts') `
                --output $baselinePackages "-p:PackageVersion=$baselineVersion" --verbosity quiet
        }
    }
    finally { Pop-Location }

    Push-Location $root
    try {
        foreach ($project in $currentProjects) {
            $module = [IO.Path]::GetFileNameWithoutExtension($project)
            $contract = Join-Path $baselinePackages "$module.$baselineVersion.nupkg"
            if (-not (Test-Path -LiteralPath $contract -PathType Leaf)) { throw "Missing baseline package: $module" }
            Write-Output "Validating API compatibility: $module"
            # Fresh per-run artifacts include obj and the validation semaphore. Build the working tree,
            # including new Compile items, instead of reusing binaries or the tracked-only export.
            Invoke-CheckedDotnet pack $project -c Release --artifacts-path (Join-Path $working 'current-artifacts') `
                --output $currentPackages "-p:PackageVersion=$currentVersion" `
                '-p:EnablePackageValidation=true' '-p:RunApiCompat=true' `
                "-p:PackageValidationBaselinePath=$contract" '-p:EnableStrictModeForBaselineValidation=false' `
                '-p:ApiCompatEnableRuleCannotChangeParameterName=true' --verbosity quiet
            $candidate = Join-Path $currentPackages "$module.$currentVersion.nupkg"
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Missing current package: $module" }
            $results.Add([ordered]@{
                module = $module; result = 'passed'
                baselinePackage = $contract; baselineSha256 = (Get-FileHash -LiteralPath $contract -Algorithm SHA256).Hash
                currentPackage = $candidate; currentSha256 = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
            })
        }
    }
    finally { Pop-Location }
    $report.status = 'passed'
    Write-Output "API compatibility passed for $($results.Count) packages. Report: $ReportPath"
}
catch {
    $report.status = 'failed'
    $report['failure'] = $_.Exception.Message
    throw
}
finally {
    $report['validatedAtUtc'] = [DateTimeOffset]::UtcNow
    $reportFile = [IO.Path]::GetFullPath($ReportPath)
    New-Item -ItemType Directory -Force (Split-Path $reportFile -Parent) | Out-Null
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile
}
