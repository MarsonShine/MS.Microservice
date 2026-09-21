param(
    [string]$PackageSource,
    [string]$PackagesPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ILLinkVersion,
    [string]$Project
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/aot'
if ($Project) {
    $projectPath = if ([IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $root $Project }
    $selectedProject = Get-Item -LiteralPath $projectPath
    if ($selectedProject.Extension -ne '.csproj') { throw 'Project must be a .csproj file.' }
    $output = Join-Path $output $selectedProject.BaseName
}
New-Item -ItemType Directory -Force $output | Out-Null
# A failed rerun must not leave a previous success report in place.
$resultPath = Join-Path $output 'results.json'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
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
$projects = if ($Project) { @($selectedProject) } else {
    @('src', 'MS.Microservice.Excel/src', 'MS.Microservice.AI/src', 'MS.Microservice.Messaging/src', 'MS.Microservice.Persistence/src') |
    ForEach-Object { Get-ChildItem (Join-Path $root $_) -Filter '*.csproj' -Recurse } |
    Where-Object {
        [xml]$definition = Get-Content -LiteralPath $_.FullName -Raw
        $component = $definition.SelectSingleNode('//IsRoslynComponent')
        $compatibility = $definition.SelectSingleNode('//IsAotCompatible')
        ($null -eq $component -or $component.InnerText -ne 'true') -and ($null -eq $compatibility -or $compatibility.InnerText -ne 'false')
    } | Sort-Object FullName
}
$results = @()
foreach ($candidate in $projects) {
    $name = $candidate.BaseName
    $projectProperties = @($properties)
    $restoreLog = Join-Path $output "$name.restore.log"
    & dotnet restore $candidate.FullName @restoreOptions @projectProperties --verbosity quiet *> $restoreLog
    if ($LASTEXITCODE -ne 0) { Get-Content $restoreLog; throw "Restore failed: $name" }
    $buildLog = Join-Path $output "$name.build.log"
    & dotnet build $candidate.FullName --no-restore '-t:Rebuild' '-p:UseSharedCompilation=false' @projectProperties --verbosity normal *> $buildLog
    if ($LASTEXITCODE -ne 0) { Get-Content $buildLog -Tail 60; throw "Analyzer build failed: $name" }
    $log = Get-Content $buildLog -Raw
    # A referenced project loading the analyzer does not prove this project loaded it.
    $assemblyName = [regex]::Escape($name)
    $outputPattern = '(?:^|\s)/out:(?:"[^"\r\n]*[/\\]|[^\s"]*[/\\])' + $assemblyName + '\.dll"?(?:\s|$)'
    $compilerLines = $log -split '\r?\n' | Where-Object {
        $_ -match $outputPattern
    }
    if (-not ($compilerLines | Where-Object { $_ -match '/analyzer:[^\r\n]*ILLink\.RoslynAnalyzer\.dll' })) {
        throw "ILLink analyzer was not passed to the compiler: $name"
    }
    if ($log -match '\bIL\d+\s*:') {
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
} | ConvertTo-Json -Depth 5 | Set-Content $resultPath
