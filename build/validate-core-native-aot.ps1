param(
    [ValidateSet('win-x64', 'linux-x64')][string]$RuntimeIdentifier,
    [ValidateSet('All', 'Analysis', 'Consumer')][string]$Mode = 'All',
    [ValidateSet('Core', 'Reference')][string]$Variant = 'Core'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$hostRid = if ($IsWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } else { throw 'Use a Windows or Linux x64 host.' }
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'Native validation requires an x64 host.'
}
if (-not $RuntimeIdentifier) { $RuntimeIdentifier = $hostRid }
if ($RuntimeIdentifier -ne $hostRid) { throw "Publish and run on the matching host: $RuntimeIdentifier (current: $hostRid)." }

$project = Join-Path $root 'test/MS.Microservice.Core.NativeAot.Smoke/MS.Microservice.Core.NativeAot.Smoke.csproj'
# Each mode starts with fresh intermediates and output so roots and old binaries cannot mask failures.
$suiteName = if ($Variant -eq 'Core') { 'Core.Native' } else { 'Reference.Native' }
$runPath = Join-Path $root ('artifacts/aot/' + $suiteName + '/' + $RuntimeIdentifier + '/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $runPath | Out-Null
Write-Output "Native validation logs: $runPath"
$modes = if ($Mode -eq 'All') { @('Analysis', 'Consumer') } else { @($Mode) }
$results = @()
foreach ($currentMode in $modes) {
    $modePath = Join-Path $runPath $currentMode
    New-Item -ItemType Directory -Force $modePath | Out-Null
    $publishPath = Join-Path $modePath 'publish'
    $publishLog = Join-Path $modePath 'publish.log'
    & dotnet publish $project -c Release -r $RuntimeIdentifier --artifacts-path (Join-Path $modePath 'build') -o $publishPath `
        "-p:NativeAotMode=$currentMode" "-p:SmokeVariant=$Variant" '-p:NuGetAudit=false' --verbosity normal *> $publishLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $publishLog -Tail 60
        throw "Native publish failed ($currentMode). See $publishLog"
    }
    if ((Get-Content -LiteralPath $publishLog -Raw) -match '\bIL\d+\s*:') {
        throw "Unresolved trim/AOT diagnostics in $publishLog"
    }
    $executableName = 'MS.Microservice.Core.NativeAot.Smoke' + $(if ($IsWindows) { '.exe' } else { '' })
    $executable = Join-Path $publishPath $executableName
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing native executable: $executable" }
    # An apphost needs managed sidecars. Run only the published binary in a fresh directory.
    $standalonePath = Join-Path $modePath 'standalone'
    New-Item -ItemType Directory $standalonePath | Out-Null
    Copy-Item -LiteralPath $executable -Destination $standalonePath
    $runLog = Join-Path $modePath 'run.log'
    & (Join-Path $standalonePath $executableName) *> $runLog
    $runExitCode = $LASTEXITCODE
    Get-Content -LiteralPath $runLog
    if ($runExitCode -ne 0) { throw "Native smoke failed ($currentMode), exit code $runExitCode. See $runLog" }
    $results += [ordered]@{ mode = $currentMode; publish = 'passed'; run = 'passed'; exitCode = $runExitCode }
}
[ordered]@{
    sdk = (& dotnet --version)
    variant = $Variant
    runtimeIdentifier = $RuntimeIdentifier
    modes = $results
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runPath 'results.json')
Write-Output "Native AOT validation passed: $RuntimeIdentifier ($($modes -join ', '))."
