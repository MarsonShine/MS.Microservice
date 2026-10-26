param(
    [string[]]$Modules,
    [string]$ReportPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/validation/module-consumption.json')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'module-tools.ps1')
$root = Split-Path $PSScriptRoot -Parent
if (-not $Modules) { $Modules = @(Get-SharedProjects $root | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }) }
$projects = @(Get-ModuleProjects $root $Modules)
$projectDirectories = @($projects | ForEach-Object { [IO.Path]::GetRelativePath($root, (Split-Path $_ -Parent)).Replace('\', '/') })
$untracked = @(& git -C $root -c core.quotepath=false ls-files --others --exclude-standard -- @projectDirectories)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate untracked component files.' }
if ($untracked.Count -gt 0) {
    $inputs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($project in $projects) {
        [void]$inputs.Add([IO.Path]::GetRelativePath($root, $project).Replace('\', '/'))
        $evaluation = & dotnet msbuild $project -nologo -property:Configuration=Release -getItem:Compile,EmbeddedResource,AdditionalFiles,Content,None
        if ($LASTEXITCODE -ne 0) { throw "Cannot inspect build inputs: $project" }
        $items = ($evaluation -join [Environment]::NewLine | ConvertFrom-Json).Items
        foreach ($kind in @('Compile', 'EmbeddedResource', 'AdditionalFiles', 'Content', 'None')) {
            foreach ($item in $items.$kind) {
                if ($kind -eq 'None' -and -not ($item.PSObject.Properties['Pack'] -and $item.Pack -eq 'true') -and
                    -not $item.PSObject.Properties['CopyToOutputDirectory'] -and -not $item.PSObject.Properties['CopyToPublishDirectory']) { continue }
                [void]$inputs.Add([IO.Path]::GetRelativePath($root, $item.FullPath).Replace('\', '/'))
            }
        }
    }
    $missing = @($untracked | Where-Object { $inputs.Contains($_) -or [IO.Path]::GetExtension($_) -in @('.props', '.targets') } | Sort-Object)
    if ($missing.Count -gt 0) { throw ("Tracked-source export would omit untracked build inputs. Review and track these files before validation:`n" + ($missing -join "`n")) }
}
$working = Join-Path ([IO.Path]::GetTempPath()) ('ms-module-consumption-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $working 'source'
$packages = Join-Path $working 'packages'
$consumer = Join-Path $working 'consumer'
New-Item -ItemType Directory $working | Out-Null
$version = '1.0.0-validation.' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
& (Join-Path $PSScriptRoot 'export-modules.ps1') -Modules $Modules -OutputDirectory $source
Push-Location $source
try {
    Invoke-CheckedDotnet restore modules.slnx --configfile nuget.config --verbosity quiet
    Invoke-CheckedDotnet build modules.slnx -c Release --no-restore --verbosity quiet
    $manifest = Get-Content module-manifest.json -Raw | ConvertFrom-Json
    New-Item -ItemType Directory $packages | Out-Null
    foreach ($project in $manifest.projects) {
        Invoke-CheckedDotnet pack $project -c Release --no-build --no-restore --output $packages "-p:PackageVersion=$version" --verbosity quiet
    }
}
finally { Pop-Location }
New-Item -ItemType Directory $consumer | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $consumer
[xml]$buildProperties = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$framework = $buildProperties.SelectSingleNode('//TargetFramework').InnerText
$references = @($Modules | ForEach-Object { '<PackageReference Include="' + $_ + '" Version="' + $version + '" />' })
$closureNames = @($manifest.projects | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
$defines = @()
if ($closureNames -contains 'MS.Microservice.Threading') { $defines += 'VERIFY_THREADING' }
if ($closureNames -contains 'MS.Microservice.Caching') { $defines += 'VERIFY_CACHING' }
if ($closureNames -contains 'MS.Microservice.Http.Resilience') { $defines += 'VERIFY_HTTP' }
if ($defines.Count -gt 0) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'consumers/PlatformConsumer.cs') -Destination $consumer }
$verifyMessaging = @($manifest.projects | Where-Object { $_ -match '/MS.Microservice.Messaging.SelfManaged.EFCore/' }).Count -gt 0
$hostDependencies = @()
if ($verifyMessaging) {
    [xml]$packageProperties = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
    $sqliteVersion = $packageProperties.SelectSingleNode('//PackageVersion[@Include="Microsoft.EntityFrameworkCore.Sqlite"]').Version
    $sqliteBundleVersion = $packageProperties.SelectSingleNode('//PackageVersion[@Include="SQLitePCLRaw.bundle_e_sqlite3"]').Version
    $references += '<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="' + $sqliteVersion + '" />'
    $references += '<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="' + $sqliteBundleVersion + '" />'
    $hostDependencies += @(
        [ordered]@{ package = 'Microsoft.EntityFrameworkCore.Sqlite'; version = $sqliteVersion },
        [ordered]@{ package = 'SQLitePCLRaw.bundle_e_sqlite3'; version = $sqliteBundleVersion }
    )
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'consumers/MessagingConsumer.cs') -Destination $consumer
}
$projectText = @('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>',
    ('<TargetFramework>' + $framework + '</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><DefineConstants>' + ($defines -join ';') + '</DefineConstants></PropertyGroup><ItemGroup>')) +
    $references + @('</ItemGroup></Project>')
[IO.File]::WriteAllLines((Join-Path $consumer 'Consumer.csproj'), $projectText)
$escapedFeed = [Security.SecurityElement]::Escape($packages)
$localMappings = ($closureNames | ForEach-Object { '<package pattern="' + [Security.SecurityElement]::Escape($_) + '"/>' }) -join ''
'<configuration><packageSources><clear/><add key="local" value="' + $escapedFeed +
    '"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>' +
    '<packageSourceMapping><packageSource key="local">' + $localMappings +
    '</packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>' |
    Set-Content (Join-Path $consumer 'nuget.config')
$assemblyNames = ($Modules | ForEach-Object { '"' + $_ + '"' }) -join ','
$program = 'string[] modules = [' + $assemblyNames + '];' + [Environment]::NewLine +
    'foreach (var module in modules) { var assembly = System.Reflection.Assembly.Load(module); Console.WriteLine(assembly.GetName().Name + " exported types: " + assembly.GetExportedTypes().Length); }' + [Environment]::NewLine +
    'var checks = new List<string>();'
if ($defines.Count -gt 0) { $program += [Environment]::NewLine + 'checks.AddRange(await PlatformConsumer.VerifyAsync());' }
if ($verifyMessaging) { $program += [Environment]::NewLine + 'await MessagingConsumer.VerifyAsync(); checks.AddRange(["messaging.first-context.commit-rollback-serialization-deduplication", "messaging.second-context.commit-rollback-serialization-deduplication"]);' }
$program += [Environment]::NewLine + 'await File.WriteAllTextAsync("consumer-results.json", System.Text.Json.JsonSerializer.Serialize(checks));'
[IO.File]::WriteAllText((Join-Path $consumer 'Program.cs'), $program)
Push-Location $consumer
try {
    Invoke-CheckedDotnet restore Consumer.csproj --configfile nuget.config --verbosity quiet
    Invoke-CheckedDotnet run --project Consumer.csproj -c Release --no-restore --verbosity quiet
}
finally { Pop-Location }
New-Item -ItemType Directory -Force (Split-Path ([IO.Path]::GetFullPath($ReportPath)) -Parent) | Out-Null
[ordered]@{
    revision = $manifest.revision; sdk = (& dotnet --version); version = $version
    modules = $Modules; sourceProjectCount = $manifest.projects.Count
    sourceBuild = 'passed'; packageConsumer = 'passed'; messagingContextsVerified = $(if ($verifyMessaging) { 2 } else { 0 }); validatedAtUtc = [DateTimeOffset]::UtcNow
    consumerChecks = @(Get-Content -LiteralPath (Join-Path $consumer 'consumer-results.json') -Raw | ConvertFrom-Json)
    packages = @(Get-ChildItem -LiteralPath $packages -Filter '*.nupkg' | Sort-Object Name | ForEach-Object {
        [ordered]@{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    localPackageIds = $closureNames
    hostDependencies = $hostDependencies
    validationScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    consumerConfigSha256 = (Get-FileHash -LiteralPath (Join-Path $consumer 'nuget.config') -Algorithm SHA256).Hash
    workingDirectory = $working
} | ConvertTo-Json -Depth 5 | Set-Content $ReportPath
Write-Output "Source and package consumption passed. Report: $ReportPath"
