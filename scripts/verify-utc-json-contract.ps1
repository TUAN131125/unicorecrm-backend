param(
    [string] $BaseUrl = 'http://127.0.0.1:5094'
)

# Transport-level regression check for the canonical UtcDateTime wire contract.
# Every canonical UTC date-time emitted by the HTTP boundary must be an ISO-8601
# instant ending in the literal Z designator. This check is deliberately owner
# agnostic: it walks whole response documents and inspects every string value
# that has an ISO-8601 date-time shape, so a new owner or DTO is covered without
# adding a per-module test.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$connection = $env:ConnectionStrings__UnicoreCRM
if ([string]::IsNullOrWhiteSpace($connection)) {
    throw 'ConnectionStrings__UnicoreCRM must be supplied through local environment configuration.'
}

$seedEmail = $env:UNICORE_DEV_SEED_EMAIL
$seedPassword = $env:UNICORE_DEV_SEED_PASSWORD
if ([string]::IsNullOrWhiteSpace($seedEmail) -or [string]::IsNullOrWhiteSpace($seedPassword)) {
    throw 'The UTC JSON contract verifier requires UNICORE_DEV_SEED_EMAIL and UNICORE_DEV_SEED_PASSWORD from local environment state.'
}

$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$logDirectory = [IO.Path]::GetFullPath([IO.Path]::Combine($temporaryRoot, 'unicore-utc-json-' + [Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $logDirectory | Out-Null

$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(20)
$checks = [System.Collections.Generic.List[string]]::new()
$violations = [System.Collections.Generic.List[string]]::new()

# An ISO-8601 date-time shaped string. A canonical UTC instant must end in Z;
# anything carrying a numeric offset or a bare local instant is a violation.
$dateTimeShape = '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}'

function Test-UtcJsonNode([object] $node, [string] $path, [string] $operation) {
    if ($null -eq $node) { return }
    if ($node -is [string]) {
        if ($node -match $dateTimeShape -and $node -notmatch 'Z$') {
            $violations.Add("$operation $path -> '$node'")
        }
        return
    }
    if ($node -is [System.Collections.IEnumerable] -and -not ($node -is [string])) {
        $index = 0
        foreach ($item in $node) {
            Test-UtcJsonNode $item "$path[$index]" $operation
            $index++
        }
        return
    }
    if ($node -is [pscustomobject]) {
        foreach ($property in $node.PSObject.Properties) {
            Test-UtcJsonNode $property.Value "$path.$($property.Name)" $operation
        }
    }
}

function Assert-UtcContract([string] $operation, [string] $json) {
    Test-UtcJsonNode ($json | ConvertFrom-Json) '$' $operation
    $checks.Add("$operation=SCANNED")
}

function Set-HostEnvironment {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $BaseUrl
    if ([string]::IsNullOrWhiteSpace($env:IdentityAuth__Jwt__SigningKey)) {
        $env:IdentityAuth__Jwt__SigningKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    }
    if ([string]::IsNullOrWhiteSpace($env:IdentityAuth__RefreshTokenPepper)) {
        $env:IdentityAuth__RefreshTokenPepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    }
    $env:UNICORE_DEV_SEED_ENABLED = 'true'
}

function Start-ApiHost {
    Set-HostEnvironment
    $standardOutput = Join-Path $logDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $logDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOutput -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($process.HasExited) {
            $text = (Get-Content -LiteralPath $standardError -Raw).Replace($connection, 'ConnectionString=***')
            throw "ApiHost exited during startup: $text"
        }
        try {
            $probe = $client.GetAsync("$BaseUrl/auth/session").GetAwaiter().GetResult()
            if ([int] $probe.StatusCode -eq 401) { return $process }
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

function Send-Json([string] $method, [string] $path, $body, [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$BaseUrl$path")
    if (-not [string]::IsNullOrEmpty($body)) {
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

function New-Headers([string] $token, [string] $workspaceId) {
    $headers = @{
        'X-Request-Id' = 'req-' + [Guid]::NewGuid().ToString('N')
        'X-Correlation-Id' = 'corr-' + [Guid]::NewGuid().ToString('N')
    }
    if (-not [string]::IsNullOrWhiteSpace($token)) { $headers['Authorization'] = "Bearer $token" }
    if (-not [string]::IsNullOrWhiteSpace($workspaceId)) { $headers['X-Workspace-Id'] = $workspaceId }
    return $headers
}

$hostProcess = $null
try {
    $hostProcess = Start-ApiHost

    $signInHeaders = New-Headers '' ''
    $signInHeaders['Idempotency-Key'] = "idem-utc-signin-$suffix"
    $signIn = Send-Json 'POST' '/auth/sessions' (@{
        email = $seedEmail
        password = $seedPassword
        deviceLabel = 'UTC Contract Verifier'
    } | ConvertTo-Json -Compress) $signInHeaders
    if ($signIn.Status -ne 200) { throw "Sign-in expected HTTP 200 but got $($signIn.Status)." }
    Assert-UtcContract 'signIn' $signIn.Body
    $token = ($signIn.Body | ConvertFrom-Json).accessToken

    $session = Send-Json 'GET' '/auth/session' $null (New-Headers $token '')
    Assert-UtcContract 'getCurrentSession' $session.Body

    $workspaces = Send-Json 'GET' '/workspaces' $null (New-Headers $token '')
    Assert-UtcContract 'listMyWorkspaces' $workspaces.Body
    $workspaceId = (($workspaces.Body | ConvertFrom-Json).items | Select-Object -First 1).workspaceId
    if ([string]::IsNullOrWhiteSpace($workspaceId)) { throw 'No workspace membership is available for the UTC contract verifier.' }

    $bootstrap = Send-Json 'GET' "/workspaces/$workspaceId/bootstrap" $null (New-Headers $token $workspaceId)
    Assert-UtcContract 'getWorkspaceBootstrap' $bootstrap.Body
    $memberId = ($session.Body | ConvertFrom-Json).principal.memberId

    Assert-UtcContract 'getCurrentAuthorizationContext' (Send-Json 'GET' '/access/context' $null (New-Headers $token $workspaceId)).Body

    $taskHeaders = New-Headers $token $workspaceId
    $taskHeaders['Idempotency-Key'] = "idem-utc-task-$suffix"
    $task = Send-Json 'POST' '/tasks' (@{
        title = "UTC Contract Task $suffix"
        assigneeId = $memberId
        dueAt = [DateTimeOffset]::UtcNow.AddDays(7).ToString('yyyy-MM-ddTHH:mm:ssZ')
        priority = 'NORMAL'
    } | ConvertTo-Json -Compress) $taskHeaders
    if ($task.Status -ne 201) { throw "Task create expected HTTP 201 but got $($task.Status): $($task.Body)" }
    Assert-UtcContract 'createTask' $task.Body
    $taskId = ($task.Body | ConvertFrom-Json).aggregateId
    Assert-UtcContract 'getTask' (Send-Json 'GET' "/tasks/$taskId" $null (New-Headers $token $workspaceId)).Body

    $leadHeaders = New-Headers $token $workspaceId
    $leadHeaders['Idempotency-Key'] = "idem-utc-lead-$suffix"
    $lead = Send-Json 'POST' '/leads' (@{
        displayName = "UTC Contract Lead $suffix"
        source = 'Direct'
        ownerId = $memberId
        estimatedValue = @{ amount = '1250.00'; currency = 'USD' }
        email = "utc-$suffix@example.test"
    } | ConvertTo-Json -Compress -Depth 5) $leadHeaders
    if ($lead.Status -ne 201) { throw "Lead create expected HTTP 201 but got $($lead.Status): $($lead.Body)" }
    Assert-UtcContract 'createLead' $lead.Body
    $leadId = ($lead.Body | ConvertFrom-Json).aggregateId
    Assert-UtcContract 'getLead' (Send-Json 'GET' "/leads/$leadId" $null (New-Headers $token $workspaceId)).Body

    $dealHeaders = New-Headers $token $workspaceId
    $dealHeaders['Idempotency-Key'] = "idem-utc-deal-$suffix"
    $deal = Send-Json 'POST' '/deals' (@{
        name = "UTC Contract Deal $suffix"
        buyerRef = @{ type = 'CONTACT'; id = "contact_utc_$suffix" }
        stageCode = 'DISCOVERY'
        amount = @{ amount = '5000.00'; currency = 'USD' }
        opportunityScore = '25'
        ownerId = $memberId
        expectedCloseDate = [DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd')
        interestedProductIds = @()
        lineItems = @()
    } | ConvertTo-Json -Compress -Depth 6) $dealHeaders
    if ($deal.Status -ne 201) { throw "Deal create expected HTTP 201 but got $($deal.Status): $($deal.Body)" }
    Assert-UtcContract 'createDeal' $deal.Body
    $dealId = ($deal.Body | ConvertFrom-Json).aggregateId
    Assert-UtcContract 'getDeal' (Send-Json 'GET' "/deals/$dealId" $null (New-Headers $token $workspaceId)).Body

    $advisory = Send-Json 'POST' '/ai/advisories' (@{
        question = 'Summarize this context and suggest the next action.'
        locale = 'en'
        contextReferences = @{ leadId = $leadId; dealId = $dealId; taskId = $taskId }
    } | ConvertTo-Json -Compress -Depth 5) (New-Headers $token $workspaceId)
    if ($advisory.Status -ne 200) { throw "AI advisory expected HTTP 200 but got $($advisory.Status): $($advisory.Body)" }
    Assert-UtcContract 'requestAiAdvisory' $advisory.Body

    if ($violations.Count -gt 0) {
        throw "Canonical UtcDateTime contract violations detected:`n" + ($violations -join "`n")
    }

    [pscustomobject] @{
        Status = 'PASS'
        Contract = 'UtcDateTime must be an ISO-8601 instant ending in Z'
        Violations = 0
        Checks = $checks
    } | ConvertTo-Json -Depth 5
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
    if (Test-Path -LiteralPath $logDirectory) {
        Remove-Item -LiteralPath $logDirectory -Recurse -Force
    }
}
