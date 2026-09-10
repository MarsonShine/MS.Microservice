param(
    [string]$ImageName = "ms-microservice-web:smoke",
    [int]$StartupTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
$containerName = "ms-microservice-smoke-$([Guid]::NewGuid().ToString('N'))"
$containerStarted = $false
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    Invoke-Docker build --tag $ImageName $repositoryRoot

    Invoke-Docker run --detach --rm `
        --name $containerName `
        --publish "127.0.0.1::8080" `
        --env "ASPNETCORE_ENVIRONMENT=Production" `
        --env "Messaging__Provider=SelfManaged" `
        --env "Messaging__RabbitMQ__ConnectionString=amqp://127.0.0.1" `
        --env "ConnectionStrings__ReferenceDatabase=Host=127.0.0.1;Port=5432;Database=reference;Username=unused;Password=unused" `
        --env "Authentication__Authority=https://identity.example.invalid" `
        --env "Authentication__Audience=ms-reference" `
        --env "OpenTelemetry__Enabled=false" `
        $ImageName | Out-Null
    $containerStarted = $true

    $portMapping = (Invoke-Docker port $containerName "8080/tcp" | Select-Object -First 1).Trim()
    $hostPort = ($portMapping -split ":")[-1]
    $parsedHostPort = 0
    if (-not [int]::TryParse($hostPort, [ref]$parsedHostPort)) {
        throw "Unable to determine the published container port from '$portMapping'."
    }

    $livenessUrl = "http://127.0.0.1:$parsedHostPort/health/live"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $livenessUrl -TimeoutSec 2 -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                Write-Host "Container liveness check passed: $livenessUrl"
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    Invoke-Docker logs $containerName
    throw "Container did not become live within $StartupTimeoutSeconds seconds."
}
finally {
    if ($containerStarted) {
        & docker rm --force $containerName | Out-Null
    }
}
