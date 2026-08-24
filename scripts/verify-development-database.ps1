param(
    [string] $BaseUrl = 'http://127.0.0.1:5091'
)

$ErrorActionPreference = 'Stop'
$connection = $env:ConnectionStrings__UnicoreCRM
if ([string]::IsNullOrWhiteSpace($connection)) {
    throw 'ConnectionStrings__UnicoreCRM must be supplied through local environment configuration.'
}
$hasExpectedServer = $connection -match '(?i)(?:Server|Data Source)=DESKTOP-ICH225R(?:;|$)'
$hasExpectedDatabase = $connection -match '(?i)(?:Database|Initial Catalog)=UnicoreCRM_Development(?:;|$)'
$hasEncryption = $connection -match '(?i)Encrypt=True(?:;|$)'
$trustsServerCertificate = $connection -match '(?i)TrustServerCertificate=True(?:;|$)'
if (-not $hasExpectedServer -or -not $hasExpectedDatabase -or -not $hasEncryption -or -not $trustsServerCertificate) {
    throw 'The configured connection must target encrypted DESKTOP-ICH225R/UnicoreCRM_Development with TrustServerCertificate enabled.'
}
$hasSqlUser = $connection -match '(?i)(?:User Id|UID)=([^;]+)'
$hasSqlPassword = $connection -match '(?i)(?:Password|Pwd)=([^;]+)'
if (-not $hasSqlUser -or -not $hasSqlPassword) {
    throw 'The Development database verifier requires externally supplied SQL Server authentication.'
}

$sqlUserMatch = [regex]::Match($connection, '(?i)(?:User Id|UID)=([^;]+)')
$sqlPasswordMatch = [regex]::Match($connection, '(?i)(?:Password|Pwd)=([^;]+)')
$sqlUser = $sqlUserMatch.Groups[1].Value
$sqlPassword = $sqlPasswordMatch.Groups[1].Value
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$email = "persistent.database.$suffix@example.test"
$applicationPassword = 'LocalDev-' + [Guid]::NewGuid().ToString('N')
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$refreshPepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$webhookSecret = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$memberWorkspaceKey = 'persistent-development-main'
$nonMemberWorkspaceKey = 'persistent-development-foreign'
$integrationId = "int_persistent_$suffix"
$secretReference = "persistent_development_$suffix"
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$logDirectory = [IO.Path]::GetFullPath([IO.Path]::Combine($temporaryRoot, 'unicore-development-database-' + [Guid]::NewGuid().ToString('N')))
if (-not $logDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The generated verification-log directory escaped the temporary root.'
}
New-Item -ItemType Directory -Path $logDirectory | Out-Null

$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(20)
$checks = [System.Collections.Generic.List[string]]::new()
$capabilities = @(
    'access.read', 'workspace.context.resolve',
    'tasks.read', 'tasks.create', 'tasks.update', 'tasks.assign', 'tasks.complete',
    'leads.read', 'leads.create', 'leads.update', 'leads.qualify',
    'deals.read', 'deals.create', 'deals.update', 'deals.assign', 'deals.close', 'deals.delete', 'deals.bulk'
)

function Set-HostEnvironment(
    [bool] $enableIntegration,
    [string] $workspaceId = '',
    [string] $memberId = '') {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $BaseUrl
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $refreshPepper
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $email
    $env:IdentityAuth__DevelopmentBootstrap__Password = $applicationPassword
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Persistent Development Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'true'
    $env:Workspace__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Workspace__DevelopmentBootstrap__IdentityEmail = $email
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Key = $memberWorkspaceKey
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Name = 'Persistent Development Main'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__LogoText = 'PD'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__TimeZone = 'Asia/Saigon'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__0 = 'leads'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__1 = 'deals'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__2 = 'tasks'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Key = $nonMemberWorkspaceKey
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Name = 'Persistent Development Foreign'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__LogoText = 'PF'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__TimeZone = 'Asia/Saigon'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'true'
    $env:AccessControl__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:AccessControl__DevelopmentBootstrap__IdentityEmail = $email
    $env:AccessControl__DevelopmentBootstrap__WorkspaceKey = $memberWorkspaceKey
    $env:AccessControl__DevelopmentBootstrap__RoleName = 'Persistent Development Owner'
    for ($index = 0; $index -lt $capabilities.Count; $index++) {
        [Environment]::SetEnvironmentVariable(
            "AccessControl__DevelopmentBootstrap__Capabilities__$index",
            $capabilities[$index],
            'Process')
    }
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'
    $env:AI__Provider__DevelopmentMode = 'Normal'
    $env:AI__Provider__TimeoutSeconds = '10'
    $env:Integrations__DevelopmentBootstrap__Enabled = $enableIntegration.ToString().ToLowerInvariant()
    $env:Integrations__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Integrations__DevelopmentBootstrap__IntegrationId = $integrationId
    $env:Integrations__DevelopmentBootstrap__ProviderCode = 'generic-signed-json'
    $env:Integrations__DevelopmentBootstrap__WorkspaceId = $workspaceId
    $env:Integrations__DevelopmentBootstrap__DelegatedMemberId = $memberId
    $env:Integrations__DevelopmentBootstrap__SecretReference = $secretReference
    $env:Integrations__DevelopmentBootstrap__BindingEnabled = 'true'
    [Environment]::SetEnvironmentVariable("Integrations__Secrets__$secretReference", $webhookSecret, 'Process')
}

function Get-SafeHostLog([string] $standardOutput, [string] $standardError) {
    $text = (Get-Content -LiteralPath $standardError -Raw) + (Get-Content -LiteralPath $standardOutput -Raw)
    return $text.Replace($connection, 'ConnectionString=***').Replace($sqlPassword, '***')
}

function Start-ApiHost(
    [bool] $enableIntegration,
    [string] $workspaceId = '',
    [string] $memberId = '') {
    Set-HostEnvironment $enableIntegration $workspaceId $memberId
    $standardOutput = Join-Path $logDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $logDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOutput -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($process.HasExited) {
            throw "ApiHost exited during startup: $(Get-SafeHostLog $standardOutput $standardError)"
        }
        try {
            $probe = $client.GetAsync("$BaseUrl/auth/session").GetAwaiter().GetResult()
            if ([int] $probe.StatusCode -eq 401) {
                $checks.Add('Kestrel startup=PASS')
                return $process
            }
        }
        catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'ApiHost did not listen within the startup timeout.'
}

function Stop-ApiHost($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(5000) | Out-Null
    }
}

function Send-Json(
    [string] $method,
    [string] $path,
    [string] $body,
    [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$BaseUrl$path")
    if ($null -ne $body) {
        $message.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    }
    foreach ($entry in $headers.GetEnumerator()) {
        $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
    }
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $status = [int] $response.StatusCode
    $response.Dispose()
    $message.Dispose()
    return [pscustomobject] @{ Status = $status; Body = $text }
}

function Assert-Status($response, [int] $expected, [string] $name) {
    if ($response.Status -ne $expected) {
        throw "$name expected HTTP $expected but got $($response.Status): $($response.Body)"
    }
    $checks.Add("$name=PASS")
}

function Invoke-SqlScalar([string] $query) {
    $env:SQLCMDPASSWORD = $sqlPassword
    $value = & sqlcmd -S 'DESKTOP-ICH225R' -U $sqlUser -d 'UnicoreCRM_Development' -N -C -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) {
        throw 'A SQL verification query failed.'
    }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Sign-In {
    $attemptId = [Guid]::NewGuid().ToString('N')
    $response = Send-Json 'POST' '/auth/sessions' (@{
        email = $email
        password = $applicationPassword
        deviceLabel = 'Persistent Development Smoke'
    } | ConvertTo-Json -Compress) @{
        'X-Request-Id' = "req-persistent-$attemptId"
        'X-Correlation-Id' = "corr-persistent-$attemptId"
        'Idempotency-Key' = "idem-persistent-$attemptId"
    }
    Assert-Status $response 200 'B01 sign-in'
    return ($response.Body | ConvertFrom-Json).accessToken
}

function New-WorkspaceHeaders([string] $token, [string] $workspaceId) {
    return @{
        Authorization = "Bearer $token"
        'X-Workspace-Id' = $workspaceId
        'X-Request-Id' = 'req-' + [Guid]::NewGuid().ToString('N')
        'X-Correlation-Id' = 'corr-' + [Guid]::NewGuid().ToString('N')
    }
}

function New-WebhookSignature([string] $timestamp, [string] $deliveryId, [string] $body) {
    $prefix = [Text.Encoding]::UTF8.GetBytes($timestamp + [char] 10 + $deliveryId + [char] 10)
    $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
    $material = [byte[]]::new($prefix.Length + $bodyBytes.Length)
    [Array]::Copy($prefix, 0, $material, 0, $prefix.Length)
    [Array]::Copy($bodyBytes, 0, $material, $prefix.Length, $bodyBytes.Length)
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($webhookSecret))
    try {
        return 'sha256=' + ([Convert]::ToHexString($hmac.ComputeHash($material))).ToLowerInvariant()
    }
    finally {
        $hmac.Dispose()
    }
}

$hostProcess = $null
try {
    $hostProcess = Start-ApiHost $false
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    $authority = Invoke-SqlScalar "SELECT TOP (1) m.WorkspaceId + '|' + m.MemberId FROM workspace.Memberships m INNER JOIN workspace.Workspaces w ON w.WorkspaceId=m.WorkspaceId INNER JOIN iam.Accounts a ON a.AccountId=m.AccountId WHERE w.[Key]='$memberWorkspaceKey' AND a.NormalizedEmail='$($email.ToUpperInvariant())';"
    $authorityParts = $authority.Split('|')
    if ($authorityParts.Count -ne 2) {
        throw 'Development Workspace authority was not bootstrapped.'
    }
    $workspaceId = $authorityParts[0]
    $memberId = $authorityParts[1]

    $hostProcess = Start-ApiHost $true $workspaceId $memberId
    $token = Sign-In
    Assert-Status (Send-Json 'GET' '/auth/session' $null @{
        Authorization = "Bearer $token"
        'X-Request-Id' = 'req-session-' + $suffix
        'X-Correlation-Id' = 'corr-session-' + $suffix
    }) 200 'B01 session'
    Assert-Status (Send-Json 'GET' '/workspaces' $null @{
        Authorization = "Bearer $token"
        'X-Request-Id' = 'req-workspaces-' + $suffix
        'X-Correlation-Id' = 'corr-workspaces-' + $suffix
    }) 200 'B02 workspace list'
    Assert-Status (Send-Json 'GET' "/workspaces/$workspaceId/bootstrap" $null (New-WorkspaceHeaders $token $workspaceId)) 200 'B02 trusted workspace'
    Assert-Status (Send-Json 'GET' '/access/context' $null (New-WorkspaceHeaders $token $workspaceId)) 200 'B03 authorization'

    $taskHeaders = New-WorkspaceHeaders $token $workspaceId
    $taskHeaders['Idempotency-Key'] = "idem-persistent-task-$suffix"
    $taskResponse = Send-Json 'POST' '/tasks' (@{
        title = "Persistent Development Task $suffix"
        assigneeId = $memberId
        dueAt = [DateTimeOffset]::UtcNow.AddDays(7).ToString('yyyy-MM-ddTHH:mm:ssZ')
        priority = 'NORMAL'
    } | ConvertTo-Json -Compress) $taskHeaders
    Assert-Status $taskResponse 201 'B04 task create'
    $taskId = ($taskResponse.Body | ConvertFrom-Json).aggregateId
    Assert-Status (Send-Json 'GET' "/tasks/$taskId" $null (New-WorkspaceHeaders $token $workspaceId)) 200 'B04 task get'

    $leadHeaders = New-WorkspaceHeaders $token $workspaceId
    $leadHeaders['Idempotency-Key'] = "idem-persistent-lead-$suffix"
    $leadResponse = Send-Json 'POST' '/leads' (@{
        displayName = "Persistent Development Lead $suffix"
        source = 'Direct'
        ownerId = $memberId
        estimatedValue = @{ amount = '1250.00'; currency = 'USD' }
        email = "persistent-$suffix@example.test"
    } | ConvertTo-Json -Compress -Depth 5) $leadHeaders
    Assert-Status $leadResponse 201 'B05 lead create'
    $leadId = ($leadResponse.Body | ConvertFrom-Json).aggregateId
    Assert-Status (Send-Json 'GET' "/leads/$leadId" $null (New-WorkspaceHeaders $token $workspaceId)) 200 'B05 lead get'

    $dealHeaders = New-WorkspaceHeaders $token $workspaceId
    $dealHeaders['Idempotency-Key'] = "idem-persistent-deal-$suffix"
    $dealResponse = Send-Json 'POST' '/deals' (@{
        name = "Persistent Development Deal $suffix"
        buyerRef = @{ type = 'CONTACT'; id = "contact_persistent_$suffix" }
        stageCode = 'DISCOVERY'
        amount = @{ amount = '5000.00'; currency = 'USD' }
        opportunityScore = '25'
        ownerId = $memberId
        expectedCloseDate = [DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd')
        interestedProductIds = @()
        lineItems = @()
    } | ConvertTo-Json -Compress -Depth 6) $dealHeaders
    Assert-Status $dealResponse 201 'B06 deal create'
    $dealId = ($dealResponse.Body | ConvertFrom-Json).aggregateId
    Assert-Status (Send-Json 'GET' "/deals/$dealId" $null (New-WorkspaceHeaders $token $workspaceId)) 200 'B06 deal get'

    $deliveryId = "delivery-persistent-$suffix"
    $webhookBody = @{
        displayName = "Persistent Webhook Lead $suffix"
        source = 'Development signed webhook'
        estimatedValue = @{ amount = '750.00'; currency = 'USD' }
        email = "webhook-$suffix@example.test"
        companyName = 'Persistent Development'
    } | ConvertTo-Json -Compress -Depth 5
    $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString([Globalization.CultureInfo]::InvariantCulture)
    $webhookResponse = Send-Json 'POST' "/integrations/inbound/leads/$integrationId" $webhookBody @{
        'X-Unicore-Delivery-Id' = $deliveryId
        'X-Unicore-Timestamp' = $timestamp
        'X-Unicore-Signature' = New-WebhookSignature $timestamp $deliveryId $webhookBody
        'X-Correlation-Id' = 'corr-webhook-' + $suffix
    }
    Assert-Status $webhookResponse 200 'B07 signed inbound webhook'
    $webhookLeadId = ($webhookResponse.Body | ConvertFrom-Json).leadId

    $aiResponse = Send-Json 'POST' '/ai/advisories' (@{
        question = 'Summarize this persistent Development CRM context and suggest the next action.'
        locale = 'en'
        contextReferences = @{ leadId = $leadId; dealId = $dealId; taskId = $taskId }
    } | ConvertTo-Json -Compress -Depth 5) (New-WorkspaceHeaders $token $workspaceId)
    Assert-Status $aiResponse 200 'B08 deterministic AI advisory'
    $advisory = $aiResponse.Body | ConvertFrom-Json
    $hasExpectedProvider = $advisory.provider.name -eq 'development-deterministic'
    $hasAdvisorySummary = -not [string]::IsNullOrWhiteSpace($advisory.summary)
    if (-not $hasExpectedProvider -or -not $hasAdvisorySummary) {
        throw 'B08 advisory response did not use the Development deterministic provider.'
    }

    Stop-ApiHost $hostProcess
    $hostProcess = $null
    $hostProcess = Start-ApiHost $true $workspaceId $memberId
    $restartToken = Sign-In
    Assert-Status (Send-Json 'GET' "/tasks/$taskId" $null (New-WorkspaceHeaders $restartToken $workspaceId)) 200 'Persistence restart task'
    Assert-Status (Send-Json 'GET' "/leads/$leadId" $null (New-WorkspaceHeaders $restartToken $workspaceId)) 200 'Persistence restart lead'
    Assert-Status (Send-Json 'GET' "/deals/$dealId" $null (New-WorkspaceHeaders $restartToken $workspaceId)) 200 'Persistence restart deal'
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$webhookLeadId' AND WorkspaceId='$workspaceId';") -ne '1') {
        throw 'Persistent webhook Lead was not retained after restart.'
    }
    $checks.Add('Persistence restart webhook lead=PASS')
    $resolvedTarget = Invoke-SqlScalar "SELECT DB_NAME() + '|' + CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));"
    if ($resolvedTarget -ne 'UnicoreCRM_Development|DESKTOP-ICH225R') {
        throw "Resolved SQL target mismatch: $resolvedTarget"
    }
    $checks.Add('Resolved SQL target=PASS')

    [pscustomobject] @{
        Status = 'PASS'
        Environment = 'Development'
        Server = 'DESKTOP-ICH225R'
        Database = 'UnicoreCRM_Development'
        WorkspaceId = $workspaceId
        TaskId = $taskId
        LeadId = $leadId
        DealId = $dealId
        WebhookLeadId = $webhookLeadId
        Checks = $checks
    } | ConvertTo-Json -Depth 5
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
    Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $logDirectory) {
        Remove-Item -LiteralPath $logDirectory -Recurse -Force
    }
}
