param(
    [string]$PackageSource,
    [string]$PackagesPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ILLinkVersion
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/aot'
New-Item -ItemType Directory -Force $output | Out-Null
$properties = @('-p:EnableTrimAnalyzer=true', '-p:EnableAotAnalyzer=true', '-p:NuGetAudit=false')
if ($ILLinkVersion) {
    # Optional offline patch-version override; it never changes the checked-in SDK/package versions.
    $hook = Join-Path $output 'analyzer-version.targets'
    @"
<Project>
  <Target Name="UseSelectedILLink" BeforeTargets="ProcessFrameworkReferences">
    <ItemGroup>
      <KnownILLinkPack Update="Microsoft.NET.ILLink.Tasks" Condition="'%(KnownILLinkPack.TargetFramework)' == 'net10.0'">
        <ILLinkPackVersion>$ILLinkVersion</ILLinkPackVersion>
      </KnownILLinkPack>
    </ItemGroup>
  </Target>
</Project>
"@ | Set-Content $hook
    $properties += "-p:CustomAfterMicrosoftCommonTargets=$hook"
}
$restoreOptions = @()
if ($PackageSource) { $restoreOptions += @('--source', $PackageSource) }
if ($PackagesPath) { $restoreOptions += @('--packages', $PackagesPath) }
$projects = @('src', 'MS.Microservice.AI/src', 'MS.Microservice.Messaging/src', 'MS.Microservice.Persistence/src') |
    ForEach-Object { Get-ChildItem (Join-Path $root $_) -Filter '*.csproj' -Recurse } |
    Sort-Object FullName
$results = @()
foreach ($project in $projects) {
    $name = $project.BaseName
    $restoreLog = Join-Path $output "$name.restore.log"
    & dotnet restore $project.FullName @restoreOptions @properties --verbosity quiet *> $restoreLog
    if ($LASTEXITCODE -ne 0) { Get-Content $restoreLog; throw "Restore failed: $name" }
    $buildLog = Join-Path $output "$name.build.log"
    & dotnet build $project.FullName --no-restore -t:Rebuild @properties --verbosity normal *> $buildLog
    if ($LASTEXITCODE -ne 0) { Get-Content $buildLog -Tail 60; throw "Analyzer build failed: $name" }
    $log = Get-Content $buildLog -Raw
    if ($log -notmatch '/analyzer:[^\r\n]*ILLink\.RoslynAnalyzer\.dll') {
        throw "ILLink analyzer was not passed to the compiler: $name"
    }
    if ($log -match '\b(?:warning|error) IL\d+:') {
        throw "Unresolved ILLink diagnostics in $buildLog"
    }
    $results += [ordered]@{ project = $name; analyzerLoaded = $true; diagnostics = 0 }
    Write-Output "$name : passed"
}
[ordered]@{
    sdk = (& dotnet --version)
    ilLinkVersionOverride = $ILLinkVersion
    validation = 'Roslyn trim/AOT analysis only; no publish or native executable'
    projects = $results
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'results.json')
