$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $root ('artifacts/static-aot-script-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $fixture 'build') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'validate-static-aot.ps1') -Destination (Join-Path $fixture 'build')
foreach ($directory in @('src/Core', 'src/Other', 'MS.Microservice.Excel/src', 'MS.Microservice.AI/src', 'MS.Microservice.Messaging/src', 'MS.Microservice.Persistence/src')) {
    New-Item -ItemType Directory -Force (Join-Path $fixture $directory) | Out-Null
}
'<Project />' | Set-Content (Join-Path $fixture 'src/Core/Core.csproj')
'<Project><PropertyGroup><IsAotCompatible>false</IsAotCompatible></PropertyGroup></Project>' |
    Set-Content (Join-Path $fixture 'src/Other/Other.csproj')

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

# Intercept only this fixture's dotnet calls; no SDK or network is needed for failure-path tests.
$state = @{ Calls = [Collections.Generic.List[object]]::new(); Scenario = 'success' }
Set-Item Function:dotnet -Value {
    $state.Calls.Add(@($args))
    $global:LASTEXITCODE = 0
    if ($args[0] -eq '--version') { return '10.0.401' }
    if ($args[0] -eq 'restore') {
        if ($state.Scenario -eq 'restore-failure') { $global:LASTEXITCODE = 1 }
        return
    }
    if ($state.Scenario -eq 'build-failure') { $global:LASTEXITCODE = 1; return }
    $analyzer = '/analyzer:/packages/ILLink.RoslynAnalyzer.dll'
    switch ($state.Scenario) {
        'missing-analyzer' { 'csc /out:obj/Debug/net10.0/Core.dll'; return }
        'dependency-only' {
            "csc /out:obj/Debug/net10.0/Dependency.dll $analyzer"
            'csc /out:obj/Debug/net10.0/Core.dll'
            return
        }
        'quoted-paths' { 'csc /out:"obj/build with spaces/Core.dll" /analyzer:"/packages with spaces/ILLink.RoslynAnalyzer.dll"' }
        'windows-paths' { "csc /out:obj\Debug\net10.0\Core.dll $analyzer" }
        default { "csc /out:obj/Debug/net10.0/Core.dll $analyzer" }
    }
    if ($state.Scenario -eq 'english-diagnostic') { 'Code.cs(1,1): warning IL2026: Requires trimming support.' }
    if ($state.Scenario -eq 'localized-diagnostic') { 'Code.cs(1,1): 警告 IL3050: 需要动态代码。' }
}.GetNewClosure()

$validator = Join-Path $fixture 'build/validate-static-aot.ps1'
$report = Join-Path $fixture 'artifacts/aot/Core/results.json'
$cases = @(
    @{ Name = 'success'; Error = $null },
    @{ Name = 'quoted-paths'; Error = $null },
    @{ Name = 'windows-paths'; Error = $null },
    @{ Name = 'missing-analyzer'; Error = 'ILLink analyzer was not passed' },
    @{ Name = 'dependency-only'; Error = 'ILLink analyzer was not passed' },
    @{ Name = 'english-diagnostic'; Error = 'Unresolved ILLink diagnostics' },
    @{ Name = 'localized-diagnostic'; Error = 'Unresolved ILLink diagnostics' },
    @{ Name = 'restore-failure'; Error = 'Restore failed' },
    @{ Name = 'build-failure'; Error = 'Analyzer build failed' }
)
foreach ($case in $cases) {
    $state.Scenario = $case.Name
    $state.Calls = [Collections.Generic.List[object]]::new()
    New-Item -ItemType Directory -Force (Split-Path $report -Parent) | Out-Null
    '{"stale":true}' | Set-Content $report
    $failure = $null
    try { & $validator -Project 'src/Core/Core.csproj' | Out-Null }
    catch { $failure = $_.Exception.Message }
    if ($case.Error) {
        Assert-True ($failure -like "*$($case.Error)*") "$($case.Name): unexpected failure: $failure"
        Assert-True (-not (Test-Path -LiteralPath $report)) "$($case.Name): stale success report survived"
    } else {
        Assert-True ($null -eq $failure) "$($case.Name): $failure"
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        Assert-True ($result.projects.Count -eq 1 -and $result.projects[0].project -eq 'Core') 'Wrong project reported'
        Assert-True ($result.projects[0].analyzerLoaded -and $result.projects[0].diagnostics -eq 0) 'Incorrect analysis result'
    }
    foreach ($call in $state.Calls | Where-Object { $_[0] -in @('restore', 'build') }) {
        Assert-True ($call[1] -eq (Join-Path $fixture 'src/Core/Core.csproj')) 'Another project was selected'
        Assert-True ($call -contains '-p:EnableTrimAnalyzer=true' -and $call -contains '-p:EnableAotAnalyzer=true') 'Restore/build analysis properties differ'
        if ($call[0] -eq 'build') {
            Assert-True ($call -contains '-t:Rebuild' -and $call -contains '--no-restore') 'Build may reuse an unchecked compilation'
        }
    }
    if ($case.Name -eq 'restore-failure') { Assert-True ($state.Calls.Count -eq 1) 'Build ran after restore failure' }
    Write-Output "$($case.Name): passed"
}

# Preserve the existing discovery mode and exclusion of explicitly incompatible projects.
$state.Scenario = 'success'
$state.Calls = [Collections.Generic.List[object]]::new()
& $validator | Out-Null
$discovered = Get-Content (Join-Path $fixture 'artifacts/aot/results.json') -Raw | ConvertFrom-Json
Assert-True ($discovered.projects.Count -eq 1 -and $discovered.projects[0].project -eq 'Core') 'Default project discovery changed'
Write-Output 'default-discovery: passed'

$state.Calls.Clear()
$failure = $null
try { & $validator -Project 'src/Missing.csproj' | Out-Null }
catch { $failure = $_.Exception.Message }
Assert-True ($null -ne $failure -and $state.Calls.Count -eq 0) 'Missing project invoked dotnet'
Write-Output 'missing-project: passed'
