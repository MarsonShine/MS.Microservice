param(
    [string]$PackageSource
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'MS.Microservice.Idempotency/src/MS.Microservice.Idempotency.EFCore/MS.Microservice.Idempotency.EFCore.csproj'
$working = Join-Path $root ('artifacts/validation/idempotency-compiled-model/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $working -Force | Out-Null

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

$projectReference = [Security.SecurityElement]::Escape($project)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$projectReference" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $working 'Consumer.csproj')

@'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MS.Microservice.Idempotency.EFCore;

public sealed class ConsumerContext(DbContextOptions<ConsumerContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.AddHttpIdempotency();
}

public sealed class ConsumerContextFactory : IDesignTimeDbContextFactory<ConsumerContext>
{
    public ConsumerContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ConsumerContext>().UseSqlite("Data Source=probe.db").Options);
}
'@ | Set-Content -LiteralPath (Join-Path $working 'ConsumerContext.cs')

$consumerProject = Join-Path $working 'Consumer.csproj'
Push-Location $root
try {
    $toolArgs = @('tool', 'restore', '--ignore-failed-sources', '--verbosity', 'quiet')
    if ($PackageSource) { $toolArgs += @('--add-source', $PackageSource) }
    Invoke-Dotnet -Arguments $toolArgs

    $restoreArgs = @('restore', $consumerProject, '-p:NuGetAudit=false', '--verbosity', 'quiet')
    if ($PackageSource) { $restoreArgs += @('--source', $PackageSource) }
    Invoke-Dotnet -Arguments $restoreArgs
    Invoke-Dotnet -Arguments @('build', $consumerProject, '--no-restore', '-p:NuGetAudit=false', '--verbosity', 'quiet')
    Invoke-Dotnet -Arguments @('ef', 'dbcontext', 'optimize', '--nativeaot', '--no-build',
        '--project', $consumerProject, '--startup-project', $consumerProject,
        '--context', 'ConsumerContext', '--output-dir', 'Generated')
    Invoke-Dotnet -Arguments @('build', $consumerProject, '--no-restore', '-p:NuGetAudit=false', '--verbosity', 'quiet')
    Write-Output "Idempotency EF compiled-model consumer passed: $working"
}
finally { Pop-Location }
