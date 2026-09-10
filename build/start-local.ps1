param(
    [ValidateSet('SelfManaged','Wolverine')][string]$Provider = 'SelfManaged',
    [switch]$Lab,
    [switch]$ConsoleTelemetry,
    [switch]$PrepareOnly,
    [switch]$Smoke
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'local-tools.ps1')
$root = Split-Path $PSScriptRoot -Parent
$settings = Get-LocalSettings
if ($PrepareOnly) {
    [void](Get-Content (Join-Path $PSScriptRoot 'local/ms-reference-realm.json') -Raw | ConvertFrom-Json)
    Write-Output 'Development settings and realm configuration prepared. No containers or hosts started.'
    return
}
$port = if ($Lab) { 5210 } else { 5200 }
$baseUrl = "http://127.0.0.1:$port"
$hostName = if ($Lab) { 'Lab' } else { 'Reference' }
$application = if ($Lab) { 'samples/Lab/MS.Microservice.Lab' } else { 'samples/Reference/MS.Microservice.Reference.Web' }
$assemblyName = if ($Lab) { 'MS.Microservice.Lab.dll' } else { 'MS.Microservice.Reference.Web.dll' }
$auditPath = if ($Lab) { '/lab/messaging/audit' } else { '/api/v1/audit' }
$portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
try { $portProbe.Start() } finally { $portProbe.Stop() }
Invoke-LocalCompose up -d --wait --wait-timeout 180
Wait-LocalHttp 'http://localhost:58080/realms/ms-reference/.well-known/openid-configuration'
$configuration = if ($Lab) { Get-LabEnvironment $settings $Provider } else { Get-ReferenceEnvironment $settings $Provider }
$configuration.OpenTelemetry__ConsoleExporterEnabled = $ConsoleTelemetry.IsPresent.ToString().ToLowerInvariant()
$previous = @{}
$process = $null
$brokerStopped = $false
try {
    foreach ($key in $configuration.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $configuration[$key])
    }
    Push-Location $root
    try {
        Invoke-CheckedDotnet build MS.Microservice.slnx -c Release --verbosity quiet
        $migrations = Join-Path $root "artifacts/migrations/$hostName/$Provider"
        Invoke-CheckedDotnet run --project samples/Reference/MS.Microservice.Reference.DatabaseMigrator -c Release --no-build -- --provider $Provider --output $migrations --apply --provision-broker
        if ($Lab) {
            Invoke-CheckedDotnet run --project samples/Lab/MS.Microservice.Lab.DatabaseMigrator -c Release --no-build -- --context all --seed-lab-users
        }
        $publish = Join-Path $root "artifacts/local/publish/$hostName/$Provider"
        Invoke-CheckedDotnet publish $application -c Release --no-build --output $publish --verbosity quiet
    }
    finally { Pop-Location }
    $assembly = Join-Path $publish $assemblyName
    $launch = @{
        FilePath = 'dotnet'; ArgumentList = @('"' + $assembly + '"'); WorkingDirectory = $publish; PassThru = $true
        RedirectStandardOutput = (Join-Path $root "artifacts/local/$hostName.stdout.log")
        RedirectStandardError = (Join-Path $root "artifacts/local/$hostName.stderr.log")
    }
    if ($IsWindows) { $launch.WindowStyle = 'Hidden' }
    $process = Start-Process @launch
    Wait-LocalHttp "$baseUrl/health/ready" -Process $process
    $operationPath = if ($Lab) { '/lab/messaging/failures' } else { '/api/operations/messages/failures' }
    $anonymous = Invoke-WebRequest "$baseUrl$operationPath" -TimeoutSec 15 -SkipHttpErrorCheck
    if ($anonymous.StatusCode -ne 401) { throw 'Anonymous management request was not challenged.' }
    $reader = if ($Lab) { Get-LabToken $settings -Reader -BaseUrl $baseUrl } else { Get-DevelopmentToken $settings -Reader }
    $denied = Invoke-WebRequest "$baseUrl$operationPath" -Headers @{Authorization="Bearer $reader"} -TimeoutSec 15 -SkipHttpErrorCheck
    if ($denied.StatusCode -ne 403) { throw 'A reader identity was allowed to operate messages.' }
    $exercise = if ($Lab) { Invoke-LabExercise $settings -BaseUrl $baseUrl } else { Invoke-ReferenceExercise $settings -BaseUrl $baseUrl }
    $allowed = Invoke-WebRequest "$baseUrl$operationPath" -Headers $exercise.Headers -TimeoutSec 15 -SkipHttpErrorCheck
    if ($allowed.StatusCode -ne 200) { throw 'The operator identity could not query failed messages.' }
    Wait-ReferenceAudit $exercise -BaseUrl $baseUrl -AuditPath $auditPath
    Invoke-LocalCompose stop rabbitmq
    $brokerStopped = $true
    $recovery = if ($Lab) { Invoke-LabExercise $settings -BaseUrl $baseUrl } else { Invoke-ReferenceExercise $settings -BaseUrl $baseUrl }
    if (-not $Lab) {
        $ready = Invoke-RestMethod "$baseUrl/health/ready" -TimeoutSec 15
        if ($ready.status -ne 'degraded') { throw 'Broker outage was not reported as degraded.' }
    }
    Invoke-LocalCompose up -d --wait --wait-timeout 180 rabbitmq
    $brokerStopped = $false
    Wait-ReferenceAudit $recovery -BaseUrl $baseUrl -AuditPath $auditPath
    Write-Output "$hostName $Provider passed identity, transaction, consumption and Broker recovery checks at $baseUrl."
    if (-not $Smoke) {
        Write-Output 'The host remains active. Press Ctrl+C to stop it; dependency volumes are retained.'
        Wait-Process -Id $process.Id
    }
}
finally {
    try {
        if ($brokerStopped) { Invoke-LocalCompose up -d --wait --wait-timeout 180 rabbitmq }
    }
    finally {
        try {
            if ($process -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        }
        finally {
            foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
        }
    }
}