param(
    [string[]]$Modules,
    [string]$ReportPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/validation/module-consumption.json')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'module-tools.ps1')
$root = Split-Path $PSScriptRoot -Parent
if (-not $Modules) { $Modules = @(Get-SharedProjects $root | Where-Object { Test-ProjectPackable $_ } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }) }
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
        if (-not (Test-ProjectPackable $project)) { continue }
        Invoke-CheckedDotnet pack $project -c Release --no-build --no-restore --output $packages "-p:PackageVersion=$version" --verbosity quiet
    }
}
finally { Pop-Location }
New-Item -ItemType Directory $consumer | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $consumer
[xml]$buildProperties = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$framework = $buildProperties.SelectSingleNode('//TargetFramework').InnerText
$references = @($Modules | ForEach-Object { '<PackageReference Include="' + $_ + '" Version="' + $version + '" />' })
$verifyMessaging = @($manifest.projects | Where-Object { $_ -match '/MS.Microservice.Messaging.SelfManaged.EFCore/' }).Count -gt 0
if ($verifyMessaging) {
    [xml]$packageProperties = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
    $sqliteVersion = $packageProperties.SelectSingleNode('//PackageVersion[@Include="Microsoft.EntityFrameworkCore.Sqlite"]').Version
    $references += '<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="' + $sqliteVersion + '" />'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'consumers/MessagingConsumer.cs') -Destination $consumer
}
$projectText = @('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>',
    ('<TargetFramework>' + $framework + '</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup>')) +
    $references + @('</ItemGroup></Project>')
[IO.File]::WriteAllLines((Join-Path $consumer 'Consumer.csproj'), $projectText)
$escapedFeed = [Security.SecurityElement]::Escape($packages)
'<configuration><packageSources><clear/><add key="local" value="' + $escapedFeed +
    '"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>' |
    Set-Content (Join-Path $consumer 'nuget.config')
$assemblyNames = ($Modules | ForEach-Object { '"' + $_ + '"' }) -join ','
$program = 'string[] modules = [' + $assemblyNames + '];' + [Environment]::NewLine +
    'foreach (var module in modules) { var assembly = System.Reflection.Assembly.Load(module); Console.WriteLine(assembly.GetName().Name + " exported types: " + assembly.GetExportedTypes().Length); }'
if ($verifyMessaging) { $program += [Environment]::NewLine + "await MessagingConsumer.VerifyAsync();" }
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
    workingDirectory = $working
} | ConvertTo-Json -Depth 5 | Set-Content $ReportPath
Write-Output "Source and package consumption passed. Report: $ReportPath"
