param(
    [string]$ReportPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/validation/api-compatibility-self-test.json')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$working = Join-Path ([IO.Path]::GetTempPath()) ('ms-api-self-test-' + [Guid]::NewGuid().ToString('N'))
$results = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ status = 'running'; sdk = (& dotnet --version); cases = $results; workingDirectory = $working }
try {
    New-Item -ItemType Directory $working | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $working
    Copy-Item -LiteralPath (Join-Path $root 'nuget.config') -Destination $working
    '<Project />' | Set-Content -LiteralPath (Join-Path $working 'Directory.Build.props')
    [xml]$properties = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
    $framework = $properties.SelectSingleNode('//TargetFramework').InnerText
    $projectText = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>' + $framework +
        '</TargetFramework><PackageId>ApiCompatibilityFixture</PackageId><Version>1.0.0</Version></PropertyGroup></Project>'
    $cases = [ordered]@{
        baseline = 'public class Contract { public void Existing() { } }'
        unchanged = 'public class Contract { public void Existing() { } }'
        additive = 'public class Contract { public void Existing() { } public void Added() { } }'
        removed = 'public class Contract { }'
    }
    Push-Location $working
    try {
        foreach ($case in $cases.GetEnumerator()) {
            $directory = Join-Path $working $case.Key
            New-Item -ItemType Directory $directory | Out-Null
            $project = Join-Path $directory 'ApiCompatibilityFixture.csproj'
            $projectText | Set-Content -LiteralPath $project
            $case.Value | Set-Content -LiteralPath (Join-Path $directory 'Contract.cs')
            $arguments = @('pack', $project, '-c', 'Release', '--artifacts-path', (Join-Path $directory 'artifacts'),
                '--output', (Join-Path $directory 'packages'), '--verbosity', 'quiet')
            if ($case.Key -ne 'baseline') {
                $contract = Join-Path $working 'baseline/packages/ApiCompatibilityFixture.1.0.0.nupkg'
                $arguments += @('-p:EnablePackageValidation=true', '-p:RunApiCompat=true',
                    "-p:PackageValidationBaselinePath=$contract", '-p:EnableStrictModeForBaselineValidation=false',
                    '-p:ApiCompatEnableRuleCannotChangeParameterName=true')
            }
            $output = @(& dotnet @arguments 2>&1 | ForEach-Object { $_.ToString() })
            $exitCode = $LASTEXITCODE
            $log = Join-Path $directory 'pack.log'
            $output | Set-Content -LiteralPath $log
            if ($case.Key -eq 'removed') {
                if ($exitCode -eq 0 -or ($output -join "`n") -notmatch '\bCP0002\b') {
                    throw "Removing a public member must fail with CP0002; exit=$exitCode. Inspect $log"
                }
            }
            elseif ($exitCode -ne 0) { throw "Fixture '$($case.Key)' failed unexpectedly; exit=$exitCode. Inspect $log" }
            $results.Add([ordered]@{ case = $case.Key; exitCode = $exitCode; result = 'passed'; log = $log })
        }
    }
    finally { Pop-Location }
    $report.status = 'passed'
    Write-Output "API validation self-test passed: unchanged/additive packages accepted; removed member rejected with CP0002. Report: $ReportPath"
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
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFile
}
