. (Join-Path $PSScriptRoot 'module-tools.ps1')

function Get-LocalSettings {
    $root = Split-Path $PSScriptRoot -Parent
    $directory = Join-Path $root 'artifacts/local'
    $file = Join-Path $directory 'settings.json'
    New-Item -ItemType Directory -Force $directory | Out-Null
    $keys = @('POSTGRES_PASSWORD','RABBITMQ_PASSWORD','KEYCLOAK_ADMIN_PASSWORD','MS_REFERENCE_OPS_SECRET','MS_REFERENCE_READER_SECRET','LAB_SIGNING_KEY')
    if (-not (Test-Path -LiteralPath $file)) {
        $values = [ordered]@{}
        foreach ($key in $keys) { $values[$key] = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant() }
        $values | ConvertTo-Json | Set-Content $file
    }
    $settings = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in $keys) {
        if (-not $settings.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($settings[$key])) {
            throw 'Local settings are incomplete. Restore the settings or explicitly reset the dedicated development environment.'
        }
    }
    # Add credentials for newly introduced Lab exercises without rotating existing service secrets.
    $added = $false
    foreach ($key in @('LAB_OPERATOR_PASSWORD','LAB_READER_PASSWORD')) {
        if (-not $settings.ContainsKey($key)) {
            $settings[$key] = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
            $added = $true
        }
    }
    if ($added) { $settings | ConvertTo-Json | Set-Content $file }
    $keys | ForEach-Object { $_ + '=' + $settings[$_] } | Set-Content (Join-Path $directory '.env')
    return $settings
}

function Invoke-LocalCompose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $root = Split-Path $PSScriptRoot -Parent
    & docker compose --env-file (Join-Path $root 'artifacts/local/.env') -f (Join-Path $PSScriptRoot 'local/compose.yaml') @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Local compose operation failed: $($Arguments[0])" }
}

function Get-ReferenceEnvironment {
    param([hashtable]$Settings, [ValidateSet('SelfManaged','Wolverine')][string]$Provider)
    return @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = 'http://127.0.0.1:5200'
        ConnectionStrings__ReferenceDatabase = "Host=localhost;Port=55432;Database=ms_reference_$($Provider.ToLowerInvariant());Username=ms_reference;Password=$($Settings.POSTGRES_PASSWORD)"
        Messaging__Provider = $Provider
        Messaging__RabbitMQ__ConnectionString = "amqp://ms_reference:$($Settings.RABBITMQ_PASSWORD)@localhost:55672/ms-reference"
        Messaging__RabbitMQ__Exchange = "ms.reference.$($Provider.ToLowerInvariant())"
        Messaging__RabbitMQ__QueuePrefix = "ms.reference.$($Provider.ToLowerInvariant())"
        Authentication__Authority = 'http://localhost:58080/realms/ms-reference'
        Authentication__Audience = 'ms-reference'
        OpenTelemetry__ConsoleExporterEnabled = 'false'
        OpenTelemetry__OtlpExporterEnabled = 'false'
    }
}

function Wait-LocalHttp {
    param([string]$Url, [int]$TimeoutSeconds = 180, [Diagnostics.Process]$Process)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if ($Process -and $Process.HasExited) { throw "The host exited with code $($Process.ExitCode); inspect artifacts/local logs." }
        try {
            $response = Invoke-WebRequest -Uri $Url -TimeoutSec 3 -SkipHttpErrorCheck
            if ($response.StatusCode -eq 200) { return }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "HTTP readiness deadline exceeded: $Url"
}

function Get-DevelopmentToken {
    param([hashtable]$Settings, [switch]$Reader)
    $client = if ($Reader) { 'ms-reference-reader' } else { 'ms-reference-ops' }
    $secret = if ($Reader) { $Settings.MS_REFERENCE_READER_SECRET } else { $Settings.MS_REFERENCE_OPS_SECRET }
    $result = Invoke-RestMethod -TimeoutSec 15 -Method Post -Uri 'http://localhost:58080/realms/ms-reference/protocol/openid-connect/token' -ContentType 'application/x-www-form-urlencoded' -Body @{
        grant_type = 'client_credentials'; client_id = $client; client_secret = $secret
    }
    return $result.access_token
}

function Invoke-ReferenceExercise {
    param([hashtable]$Settings, [string]$BaseUrl = 'http://127.0.0.1:5200')
    $token = Get-DevelopmentToken $Settings
    $headers = @{ Authorization = "Bearer $token" }
    $part = $token.Split('.')[1].Replace('-', '+').Replace('_', '/')
    $part = $part.PadRight([int]([Math]::Ceiling($part.Length / 4.0) * 4), '=')
    $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($part)) | ConvertFrom-Json
    $existing = Invoke-WebRequest -TimeoutSec 15 -Uri "$BaseUrl/api/v1/me" -Headers $headers -SkipHttpErrorCheck
    $name = 'Reference ' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    if ($existing.StatusCode -eq 404) {
        $body = @{ issuer = $claims.iss; subject = $claims.sub; displayName = $name; roles = @('reader') } | ConvertTo-Json
        $profile = Invoke-RestMethod -TimeoutSec 60 -Method Post -Uri "$BaseUrl/api/v1/profiles" -Headers $headers -ContentType 'application/json' -Body $body
    }
    elseif ($existing.StatusCode -eq 200) {
        $current = $existing.Content | ConvertFrom-Json
        $body = @{ displayName = $name; roles = @('reader'); expectedVersion = $current.version } | ConvertTo-Json
        $profile = Invoke-RestMethod -TimeoutSec 60 -Method Patch -Uri "$BaseUrl/api/v1/profiles/$($current.id)" -Headers $headers -ContentType 'application/json' -Body $body
    }
    else { throw "Profile lookup failed with HTTP $($existing.StatusCode)." }
    return @{ Profile = $profile; Headers = $headers }
}

function Wait-ReferenceAudit {
    param([hashtable]$Exercise, [string]$BaseUrl = 'http://127.0.0.1:5200', [string]$AuditPath = '/api/v1/audit')
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        $audit = Invoke-RestMethod -TimeoutSec 15 -Uri "$BaseUrl$($AuditPath)?profileId=$($Exercise.Profile.id)" -Headers $Exercise.Headers
        if (@($audit | Where-Object { $_.profileVersion -eq $Exercise.Profile.version }).Count -eq 1) { return }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'The committed profile version did not produce one audit effect before the deadline.'
}

function Get-LabEnvironment {
    param([hashtable]$Settings, [ValidateSet('SelfManaged','Wolverine')][string]$Provider)
    $values = Get-ReferenceEnvironment $Settings $Provider
    $base = "Host=localhost;Port=55432;Username=ms_reference;Password=$($Settings.POSTGRES_PASSWORD)"
    $values.ASPNETCORE_URLS = 'http://127.0.0.1:5210'
    $values.ConnectionStrings__ReferenceDatabase = "$base;Database=ms_lab_messages_$($Provider.ToLowerInvariant())"
    $values['ConnectionStrings__LabMessagingDatabase'] = $values.ConnectionStrings__ReferenceDatabase
    $values['ConnectionStrings__ActivationConnection'] = "$base;Database=ms_lab_activation"
    $values['ConnectionStrings__ActivationReaderConnection'] = "$base;Database=ms_lab_activation"
    $values['ConnectionStrings__EventStoreConnection'] = "$base;Database=ms_lab_event_store"
    $values['ConnectionStrings__Default'] = "$base;Database=ms_lab_sqlsugar"
    $values['ShardingOptions__ConnectionStrings__0'] = "$base;Database=ms_lab_sqlsugar"
    $values['SqlSugarOptions__PrintLog'] = 'false'
    $values['ShardingOptions__PrintLog'] = 'false'
    $values['FzPlatformDbContextSettings__EnableSensitiveDataLogging'] = 'false'
    $values['LabMessaging__Enabled'] = 'true'
    $values['LabTokenIssuer__Issuer'] = 'http://localhost:5210'
    $values['LabTokenIssuer__Audience'] = 'ms-lab'
    $values['LabTokenIssuer__SigningKey'] = $Settings.LAB_SIGNING_KEY
    $values['LabBootstrap__OperatorPassword'] = $Settings.LAB_OPERATOR_PASSWORD
    $values['LabBootstrap__ReaderPassword'] = $Settings.LAB_READER_PASSWORD
    $values.Messaging__RabbitMQ__Exchange = "ms.lab.$($Provider.ToLowerInvariant())"
    $values.Messaging__RabbitMQ__QueuePrefix = "ms.lab.$($Provider.ToLowerInvariant())"
    return $values
}

function Get-LabToken {
    param([hashtable]$Settings, [switch]$Reader, [string]$BaseUrl = 'http://127.0.0.1:5210')
    $account = if ($Reader) { 'lab-reader' } else { 'lab-operator' }
    $password = if ($Reader) { $Settings.LAB_READER_PASSWORD } else { $Settings.LAB_OPERATOR_PASSWORD }
    $body = @{ account = $account; password = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($password)) } | ConvertTo-Json
    $result = Invoke-RestMethod -TimeoutSec 60 -Method Post -Uri "$BaseUrl/api/v1/account/login" -ContentType 'application/json' -Body $body
    return $result.data.token
}

function Invoke-LabExercise {
    param([hashtable]$Settings, [string]$BaseUrl = 'http://127.0.0.1:5210')
    $headers = @{ Authorization = 'Bearer ' + (Get-LabToken $Settings -BaseUrl $BaseUrl) }
    $body = @{
        issuer = 'http://localhost:5210'; subject = [Guid]::NewGuid().ToString()
        displayName = 'Lab profile'; roles = @('reader')
    } | ConvertTo-Json
    $profile = Invoke-RestMethod -TimeoutSec 60 -Method Post -Uri "$BaseUrl/lab/messaging/profiles" -Headers $headers -ContentType 'application/json' -Body $body
    return @{ Profile = $profile; Headers = $headers }
}