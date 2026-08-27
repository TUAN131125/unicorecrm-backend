param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$baseUrl = 'http://127.0.0.1:5089'
$password = 'Initial-Provisioning-Smoke!234'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$accountA = 'provisioning.finish@example.test'
$accountB = 'provisioning.skip@example.test'
$accountC = 'provisioning.concurrent@example.test'
$accountD = 'provisioning.existing@example.test'
$accountE = 'provisioning.recovery@example.test'
$existingWorkspaceKey = 'provisioning-existing-member'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-initial-provisioning-' + [Guid]::NewGuid().ToString('N')))
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()

# Mirrors InitialWorkspaceAccessPolicy.Capabilities exactly. The four support.* entries were added
# to that policy by the Support Core task and this list was not updated with it, so the harness had
# drifted from the admitted policy and failed on a capability set that is correct.
$expectedInitialCapabilities = @(
    'deals.assign', 'deals.bulk', 'deals.close', 'deals.create', 'deals.delete', 'deals.read', 'deals.update',
    'leads.create', 'leads.qualify', 'leads.read', 'leads.update',
    'products.create', 'products.delete', 'products.edit', 'products.read',
    'support.assign', 'support.create', 'support.read', 'support.update',
    'tasks.assign', 'tasks.complete', 'tasks.create', 'tasks.read', 'tasks.update',
    'workspace.context.resolve'
)

function Invoke-SqlScalar([string] $query) {
    $value = & sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Initialize-Database {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated verification database.' }
    $env:ConnectionStrings__UnicoreCRM = $connection
    $contexts = @(
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'IdentityAuthDbContext' },
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'WorkspaceDbContext' },
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'AccessControlDbContext' },
        @{ Project = 'src/UnicoreCRM.Operations'; Context = 'TasksDbContext' },
        @{ Project = 'src/UnicoreCRM.Crm'; Context = 'LeadsDbContext' },
        @{ Project = 'src/UnicoreCRM.Crm'; Context = 'DealsDbContext' },
        @{ Project = 'src/UnicoreCRM.Integrations'; Context = 'IntegrationsDbContext' },
        @{ Project = 'src/UnicoreCRM.PlatformOperations'; Context = 'InboxDbContext' }
    )
    foreach ($entry in $contexts) {
        & dotnet ef database update --project (Join-Path $solutionRoot $entry.Project) --context $entry.Context --no-build | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not apply migrations for $($entry.Context)." }
    }
    $checks.Add('Isolated database migrated=PASS')
}

function Set-HostEnvironment([string] $identityEmail, [bool] $enableWorkspaceBootstrap, [bool] $failAccessAssignment, [bool] $resumeEnabled) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $identityEmail
    $env:IdentityAuth__DevelopmentBootstrap__Password = $password
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Initial Provisioning Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = $enableWorkspaceBootstrap.ToString().ToLowerInvariant()
    $env:Workspace__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Workspace__DevelopmentBootstrap__IdentityEmail = $identityEmail
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Key = $existingWorkspaceKey
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Name = 'Provisioning Existing Member Workspace'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__LogoText = 'PE'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__TimeZone = 'UTC'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Key = 'provisioning-existing-foreign'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Name = 'Provisioning Existing Foreign Workspace'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__LogoText = 'PF'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__TimeZone = 'UTC'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    # This harness owns the exact schema state under test, so the one-click Development
    # migration pass must stay off.
    $env:Development__ApplyMigrations = 'false'
    $env:Integrations__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = $resumeEnabled.ToString().ToLowerInvariant()
    $env:Workflows__InitialWorkspaceProvisioning__ResumeIntervalSeconds = '2'
    $env:Workflows__InitialWorkspaceProvisioning__DevelopmentFaultInjection__FailAccessAssignment = $failAccessAssignment.ToString().ToLowerInvariant()
}

function Start-ApiHost([string] $identityEmail, [bool] $enableWorkspaceBootstrap = $false, [bool] $failAccessAssignment = $false, [bool] $resumeEnabled = $true) {
    Set-HostEnvironment $identityEmail $enableWorkspaceBootstrap $failAccessAssignment $resumeEnabled
    $standardOut = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOut -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($process.HasExited) {
            throw "ApiHost exited during startup: $((Get-Content -LiteralPath $standardError -Raw)) $((Get-Content -LiteralPath $standardOut -Raw))"
        }
        try {
            $probe = $client.GetAsync("$baseUrl/auth/session").GetAwaiter().GetResult()
            if ([int] $probe.StatusCode -eq 401) { return $process }
        }
        catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'ApiHost did not listen within the smoke timeout.'
}

function Stop-ApiHost($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
}

function New-Message([string] $method, [string] $path, [string] $body, [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$baseUrl$path")
    if (-not [string]::IsNullOrEmpty($body)) {
        $message.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    }
    foreach ($entry in $headers.GetEnumerator()) {
        $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
    }
    return $message
}

function Send-Json([string] $method, [string] $path, [string] $body, [hashtable] $headers) {
    $message = New-Message $method $path $body $headers
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $setCookie = $null
    $cookieHeader = @($response.Headers | Where-Object { $_.Key -eq 'Set-Cookie' })
    if ($cookieHeader.Count -gt 0) {
        $setCookie = (@($cookieHeader[0].Value) -join '; ')
    }
    $message.Dispose()
    return [pscustomobject] @{ Status = [int] $response.StatusCode; Body = $text; SetCookie = $setCookie }
}

function Get-RefreshCookie([string] $setCookie) {
    if ([string]::IsNullOrEmpty($setCookie)) { return $null }
    $match = [regex]::Match($setCookie, '__Host-unicore-refresh=([^;]+)')
    if (-not $match.Success) { return $null }
    return '__Host-unicore-refresh=' + $match.Groups[1].Value
}

function Assert-Status($response, [int] $expected, [string] $name) {
    if ($response.Status -ne $expected) {
        throw "$name expected HTTP $expected but got $($response.Status): $($response.Body)"
    }
    $checks.Add("$name=$expected")
}

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name failed." }
    $checks.Add("$name=PASS")
}

function Get-Session([string] $email) {
    $attemptId = [Guid]::NewGuid().ToString('N')
    $response = Send-Json 'POST' '/auth/sessions' (@{
        email = $email
        password = $password
        deviceLabel = 'Initial provisioning smoke'
    } | ConvertTo-Json -Compress) @{
        'X-Request-Id' = "req-signin-$attemptId"
        'X-Correlation-Id' = "corr-signin-$attemptId"
        'Idempotency-Key' = "idem-signin-$attemptId"
    }
    Assert-Status $response 200 "Identity sign-in $email"
    $session = $response.Body | ConvertFrom-Json
    Add-Member -InputObject $session -NotePropertyName refreshCookie -NotePropertyValue (Get-RefreshCookie $response.SetCookie)
    return $session
}

function New-Headers([string] $token, [string] $workspaceId = $null, [string] $idempotencyKey = $null) {
    $headers = @{
        Authorization = "Bearer $token"
        'X-Request-Id' = 'req-' + [Guid]::NewGuid().ToString('N')
        'X-Correlation-Id' = 'corr-' + [Guid]::NewGuid().ToString('N')
    }
    if (-not [string]::IsNullOrEmpty($workspaceId)) { $headers['X-Workspace-Id'] = $workspaceId }
    if (-not [string]::IsNullOrEmpty($idempotencyKey)) { $headers['Idempotency-Key'] = $idempotencyKey }
    return $headers
}

function Get-Workspaces([string] $token) {
    $response = Send-Json 'GET' '/workspaces' $null (New-Headers $token)
    Assert-Status $response 200 'listMyWorkspaces'
    return ($response.Body | ConvertFrom-Json)
}

function Invoke-Provisioning([string] $token, [string] $idempotencyKey, $payload) {
    $body = if ($null -eq $payload) { '{}' } else { $payload | ConvertTo-Json -Compress }
    return Send-Json 'POST' '/workspaces/initial-provisioning' $body (New-Headers $token $null $idempotencyKey)
}

function Send-ChunkedJson([string] $path, [string] $body, [hashtable] $headers) {
    # Chunked framing omits Content-Length. The contract must still read the body, so a declared
    # length can never be used as the Skip signal.
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new('POST'), "$baseUrl$path")
    $message.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    $message.Content.Headers.ContentLength = $null
    $message.Headers.TransferEncodingChunked = $true
    foreach ($entry in $headers.GetEnumerator()) {
        $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
    }
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $message.Dispose()
    return [pscustomobject] @{ Status = [int] $response.StatusCode; Body = $text }
}

function Wait-ProvisioningState([string] $accountId, [string] $expectedState, [int] $timeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Invoke-SqlScalar "SELECT State FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountId';") -eq $expectedState) {
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Get-AccountId([string] $email) {
    return Invoke-SqlScalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())';"
}

function Assert-ProvisionedRuntime([string] $token, [string] $workspaceId, [string] $label) {
    $bootstrap = Send-Json 'GET' "/workspaces/$workspaceId/bootstrap" $null (New-Headers $token)
    Assert-Status $bootstrap 200 "$label getWorkspaceBootstrap"
    $document = $bootstrap.Body | ConvertFrom-Json
    $accessContext = Send-Json 'GET' '/access/context' $null (New-Headers $token $workspaceId)
    Assert-Status $accessContext 200 "$label getCurrentAuthorizationContext"
    $tasks = Send-Json 'GET' '/tasks' $null (New-Headers $token $workspaceId)
    Assert-Status $tasks 200 "$label workspace-required Tasks read"
    $leads = Send-Json 'GET' '/leads' $null (New-Headers $token $workspaceId)
    Assert-Status $leads 200 "$label workspace-required Leads read"
    $deals = Send-Json 'GET' '/deals' $null (New-Headers $token $workspaceId)
    Assert-Status $deals 200 "$label workspace-required Deals read"
    return $document
}

$hostProcess = $null
try {
    Initialize-Database

    # ---------------------------------------------------------------- Case A/B/C/E/G/I
    $hostProcess = Start-ApiHost $accountA
    $accountAId = Get-AccountId $accountA
    $sessionA = Get-Session $accountA
    $tokenA = $sessionA.accessToken

    # A. New authenticated account with zero memberships.
    $listA = Get-Workspaces $tokenA
    Assert-True ($listA.items.Count -eq 0) 'A: listMyWorkspaces returns zero memberships'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountAId';") -eq 0) 'A: database holds zero memberships'

    # B. Abandoned Initial Setup creates nothing, including in a later session.
    $abandonedSession = Get-Session $accountA
    $listAfterAbandon = Get-Workspaces $abandonedSession.accessToken
    Assert-True ($listAfterAbandon.items.Count -eq 0) 'B: next session still returns zero memberships'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.Workspaces;') -eq 0) 'B: no Workspace was created'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.InitialProvisioningRecords;') -eq 0) 'B: no provisioning record was created'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM access.MembershipRoleAssignments;') -eq 0) 'B: no access assignment was created'

    # C. Finish setup through the single provisioning intent.
    $finishPayload = @{
        name = 'Northwind Trading'
        logoText = 'NT'
        locale = 'vi'
        timeZone = 'Asia/Saigon'
        baseCurrency = 'VND'
    }
    $finishKey = 'idem-provision-finish-' + [Guid]::NewGuid().ToString('N')
    $finish = Invoke-Provisioning $tokenA $finishKey $finishPayload
    Assert-Status $finish 201 'C: provisionInitialWorkspace'
    $finishBody = $finish.Body | ConvertFrom-Json
    Assert-True ($finishBody.outcome -eq 'PROVISIONED') 'C: outcome is PROVISIONED'
    $workspaceA = $finishBody.workspaceId
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.Workspaces;') -eq 1) 'C: exactly one Workspace exists'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountAId' AND Status='Active';") -eq 1) 'C: exactly one ACTIVE creator membership exists'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.BootstrapProjections WHERE WorkspaceId='$workspaceA';") -eq 1) 'C: initial configuration exists'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$workspaceA';") -eq 1) 'C: exactly one initial access assignment exists'
    $roleCapabilityCount = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities c JOIN access.Roles r ON r.RoleId=c.RoleId WHERE r.WorkspaceId='$workspaceA';")
    Assert-True ($roleCapabilityCount -eq $expectedInitialCapabilities.Count) 'C: initial role carries the server-owned capability set'
    $listAfterFinish = Get-Workspaces $tokenA
    Assert-True ($listAfterFinish.items.Count -eq 1 -and $listAfterFinish.items[0].workspaceId -eq $workspaceA) 'C: listMyWorkspaces returns the new Workspace'
    Assert-True ($listAfterFinish.items[0].workspaceKey -eq $finishBody.workspace.workspaceKey) 'C: response carries the authoritative Workspace key'
    $bootstrapA = Assert-ProvisionedRuntime $tokenA $workspaceA 'C'
    Assert-True ($bootstrapA.configuration.locale -eq 'vi' -and $bootstrapA.configuration.timeZone -eq 'Asia/Saigon' -and $bootstrapA.configuration.baseCurrency -eq 'VND') 'C: supplied setup values were applied'
    Assert-True ((($bootstrapA.capabilities | Sort-Object) -join ',') -eq (($expectedInitialCapabilities | Sort-Object) -join ',')) 'C: AccessControl evaluates the initial capability set'

    # E. Retry with the same provisioning intent and idempotency key.
    $retry = Invoke-Provisioning $tokenA $finishKey $finishPayload
    Assert-Status $retry 200 'E: identical retry'
    Assert-True ((($retry.Body | ConvertFrom-Json).outcome) -eq 'REPLAYED') 'E: identical retry is REPLAYED'
    $changedPayload = $finishPayload.Clone()
    $changedPayload.name = 'Different Workspace Name'
    $reused = Invoke-Provisioning $tokenA $finishKey $changedPayload
    Assert-Status $reused 409 'E: reused key with changed values'
    Assert-True ((($reused.Body | ConvertFrom-Json).code) -eq 'IDEMPOTENCY_KEY_REUSED') 'E: reused key fails closed'
    $newKeyRetry = Invoke-Provisioning $tokenA ('idem-provision-second-' + [Guid]::NewGuid().ToString('N')) $finishPayload
    Assert-Status $newKeyRetry 200 'G: repeat provisioning for the self-provisioned account'
    Assert-True ((($newKeyRetry.Body | ConvertFrom-Json).workspaceId) -eq $workspaceA) 'G: repeat provisioning converges on the same Workspace'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.Workspaces;') -eq 1) 'E/G: no duplicate Workspace'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountAId';") -eq 1) 'E/G: no duplicate membership'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$workspaceA';") -eq 1) 'E/G: no duplicate access assignment'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.BootstrapProjections WHERE WorkspaceId='$workspaceA';") -eq 1) 'E/G: no duplicate configuration'

    # Request contract strictness. An unknown member must be rejected, and it must still be
    # rejected when the body arrives chunked, which proves the body is read rather than assumed.
    $unknownField = Invoke-Provisioning $tokenA ('idem-provision-unknown-' + [Guid]::NewGuid().ToString('N')) @{ name = 'Northwind Trading'; unsupportedField = 'x' }
    Assert-Status $unknownField 422 'Contract: unknown request member rejected'
    Assert-True ((($unknownField.Body | ConvertFrom-Json).code) -eq 'VALIDATION_FAILED') 'Contract: unknown member uses VALIDATION_FAILED'
    $chunkedUnknown = Send-ChunkedJson '/workspaces/initial-provisioning' '{"unsupportedField":"x"}' (New-Headers $tokenA $null ('idem-provision-chunked-' + [Guid]::NewGuid().ToString('N')))
    Assert-Status $chunkedUnknown 422 'Contract: chunked body is read and validated'
    $oversized = Invoke-Provisioning $tokenA ('idem-provision-large-' + [Guid]::NewGuid().ToString('N')) @{ name = ('x' * 9000) }
    Assert-Status $oversized 422 'Contract: oversized request body rejected'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.Workspaces;') -eq 1) 'Contract: rejected requests created nothing'

    # I. Owner regressions inside the provisioned Workspace.
    $memberA = $sessionA.session.principal.memberId
    $taskResponse = Send-Json 'POST' '/tasks' (@{
        title = 'Initial provisioning regression task'
        assigneeId = $memberA
        dueAt = '2026-09-01T09:00:00Z'
        priority = 'HIGH'
    } | ConvertTo-Json -Compress) (New-Headers $tokenA $workspaceA ('idem-task-' + [Guid]::NewGuid().ToString('N')))
    Assert-Status $taskResponse 201 'I: createTask in the provisioned Workspace'
    $leadResponse = Send-Json 'POST' '/leads' (@{
        displayName = 'Initial provisioning regression lead'
        source = 'Direct'
        ownerId = $memberA
        estimatedValue = @{ amount = '10.00'; currency = 'VND' }
    } | ConvertTo-Json -Compress -Depth 5) (New-Headers $tokenA $workspaceA ('idem-lead-' + [Guid]::NewGuid().ToString('N')))
    Assert-Status $leadResponse 201 'I: createLead in the provisioned Workspace'
    $sessionRead = Send-Json 'GET' '/auth/session' $null (New-Headers $tokenA)
    Assert-Status $sessionRead 200 'I: getCurrentSession'
    $registerResponse = Send-Json 'POST' '/auth/accounts' (@{
        email = 'provisioning.regression.' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '@example.test'
        password = $password
        displayName = 'Provisioning Regression'
    } | ConvertTo-Json -Compress) (New-Headers $tokenA $null ('idem-register-' + [Guid]::NewGuid().ToString('N')))
    Assert-Status $registerResponse 201 'I: registerAccount'
    $refreshHeaders = New-Headers $abandonedSession.accessToken $null ('idem-refresh-' + [Guid]::NewGuid().ToString('N'))
    $refreshHeaders['Cookie'] = $abandonedSession.refreshCookie
    $refreshed = Send-Json 'POST' '/auth/session/refresh' '{}' $refreshHeaders
    Assert-Status $refreshed 200 'I: refreshSession'
    $signOut = Send-Json 'POST' '/auth/session/logout' '{}' (New-Headers $abandonedSession.accessToken $null ('idem-logout-' + [Guid]::NewGuid().ToString('N')))
    Assert-Status $signOut 200 'I: signOut'

    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # ---------------------------------------------------------------- Case D (Skip)
    $hostProcess = Start-ApiHost $accountB
    $accountBId = Get-AccountId $accountB
    $tokenB = (Get-Session $accountB).accessToken
    Assert-True ((Get-Workspaces $tokenB).items.Count -eq 0) 'D: skip account starts with zero memberships'
    $skip = Invoke-Provisioning $tokenB ('idem-provision-skip-' + [Guid]::NewGuid().ToString('N')) $null
    Assert-Status $skip 201 'D: provisionInitialWorkspace with omitted setup values'
    $skipBody = $skip.Body | ConvertFrom-Json
    $workspaceB = $skipBody.workspaceId
    Assert-True ($skipBody.workspace.name -eq 'My Workspace') 'D: server-owned default name applied'
    Assert-True ($skipBody.workspace.logoText -eq 'MW') 'D: server-owned default logo text applied'
    Assert-True ($skipBody.workspace.workspaceKey -like 'my-workspace-*') 'D: server-owned Workspace key derived'
    $bootstrapB = Assert-ProvisionedRuntime $tokenB $workspaceB 'D'
    Assert-True ($bootstrapB.configuration.locale -eq 'en' -and $bootstrapB.configuration.timeZone -eq 'UTC' -and $bootstrapB.configuration.baseCurrency -eq 'USD') 'D: server-owned configuration defaults applied'
    Assert-True ((($bootstrapB.configuration.enabledModuleKeys | Sort-Object) -join ',') -eq 'deals,leads,tasks') 'D: server-owned enabled modules applied'
    Assert-True ((($bootstrapB.configuration.availableProductSpaces) -join ',') -eq 'crm') 'D: server-owned product spaces applied'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountBId' AND Status='Active';") -eq 1) 'D: exactly one ACTIVE membership for the skip account'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # ---------------------------------------------------------------- Case F (concurrent double submit)
    $hostProcess = Start-ApiHost $accountC
    $accountCId = Get-AccountId $accountC
    $tokenC = (Get-Session $accountC).accessToken
    $concurrentMessages = @()
    for ($index = 0; $index -lt 6; $index++) {
        $concurrentMessages += New-Message 'POST' '/workspaces/initial-provisioning' '{"name":"Concurrent Workspace"}' (New-Headers $tokenC $null ('idem-provision-concurrent-' + $index + '-' + [Guid]::NewGuid().ToString('N')))
    }
    $pending = @($concurrentMessages | ForEach-Object { $client.SendAsync($_) })
    [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]] $pending)
    $statuses = @($pending | ForEach-Object { [int] $_.Result.StatusCode })
    $concurrentMessages | ForEach-Object { $_.Dispose() }
    Assert-True (@($statuses | Where-Object { $_ -eq 201 }).Count -eq 1) 'F: exactly one concurrent submit created the Workspace'
    Assert-True (@($statuses | Where-Object { $_ -eq 200 }).Count -eq 5) 'F: every other concurrent submit converged'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountCId';") -eq 1) 'F: exactly one provisioning record'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountCId';") -eq 1) 'F: exactly one membership'
    $workspaceC = Invoke-SqlScalar "SELECT WorkspaceId FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountCId';"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$workspaceC';") -eq 1) 'F: exactly one access assignment'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.Roles WHERE WorkspaceId='$workspaceC';") -eq 1) 'F: exactly one initial role'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # ---------------------------------------------------------------- Case G/H (existing member)
    $hostProcess = Start-ApiHost $accountD $true
    $accountDId = Get-AccountId $accountD
    $tokenD = (Get-Session $accountD).accessToken
    $listD = Get-Workspaces $tokenD
    Assert-True ($listD.items.Count -ge 1) 'H: existing member already has Workspace access'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountDId';") -eq 0) 'H: no automatic personal Workspace was provisioned'
    $rejected = Invoke-Provisioning $tokenD ('idem-provision-existing-' + [Guid]::NewGuid().ToString('N')) $null
    Assert-Status $rejected 409 'G: provisioning rejected for an account with existing Workspace access'
    Assert-True ((($rejected.Body | ConvertFrom-Json).code) -eq 'WORKSPACE_ALREADY_PROVISIONED') 'G: rejection uses the admitted contract code'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountDId';") -eq 1) 'G: no additional Workspace membership was created'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountDId';") -eq 0) 'G: no provisioning record was created'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # ---------------------------------------------------------------- Case J (partial-failure recovery)
    # Force the AccessControl participant to fail after the Workspace commit, with the durable
    # resume path disabled, so the exact wedge state is created deliberately.
    $hostProcess = Start-ApiHost $accountE $false $true $false
    $accountEId = Get-AccountId $accountE
    $tokenE = (Get-Session $accountE).accessToken
    Assert-True ((Get-Workspaces $tokenE).items.Count -eq 0) 'J: recovery account starts with zero memberships'
    $injected = Invoke-Provisioning $tokenE ('idem-provision-recovery-' + [Guid]::NewGuid().ToString('N')) @{ name = 'Recovery Workspace' }
    Assert-Status $injected 500 'J: injected AccessControl failure surfaces as a server error'
    $workspaceE = Invoke-SqlScalar "SELECT WorkspaceId FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountEId';"
    Assert-True ($workspaceE.Length -gt 0) 'J: Workspace committed before the failure'
    Assert-True ((Invoke-SqlScalar "SELECT State FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountEId';") -eq 'AccessPending') 'J: anchor records outstanding access work'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountEId' AND Status='Active';") -eq 1) 'J: ACTIVE membership exists'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$workspaceE';") -eq 0) 'J: access assignment is missing'
    # The wedge is real: the account now lists an active membership but cannot bootstrap.
    Assert-True ((Get-Workspaces $tokenE).items.Count -eq 1) 'J: listMyWorkspaces already reports the Workspace'
    Assert-Status (Send-Json 'GET' "/workspaces/$workspaceE/bootstrap" $null (New-Headers $tokenE)) 403 'J: bootstrap is wedged before recovery'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # Restart without fault injection. Recovery must converge with no client action at all.
    $hostProcess = Start-ApiHost $accountE
    Assert-True (Wait-ProvisioningState $accountEId 'Completed') 'J: durable resume completed the anchor after restart'
    $tokenE = (Get-Session $accountE).accessToken
    $listE = Get-Workspaces $tokenE
    Assert-True ($listE.items.Count -eq 1 -and $listE.items[0].workspaceId -eq $workspaceE) 'J: listMyWorkspaces returns the recovered Workspace'
    $null = Assert-ProvisionedRuntime $tokenE $workspaceE 'J'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Workspaces WHERE WorkspaceId='$workspaceE';") -eq 1) 'J: exactly one Workspace remains'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountEId';") -eq 1) 'J: exactly one Membership remains'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.BootstrapProjections WHERE WorkspaceId='$workspaceE';") -eq 1) 'J: exactly one configuration seed remains'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.Roles WHERE WorkspaceId='$workspaceE';") -eq 1) 'J: exactly one initial role remains'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$workspaceE';") -eq 1) 'J: exactly one access assignment remains'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountEId';") -eq 1) 'J: exactly one provisioning anchor remains'
    $afterRecovery = Invoke-Provisioning $tokenE ('idem-provision-recovery-retry-' + [Guid]::NewGuid().ToString('N')) @{ name = 'Recovery Workspace' }
    Assert-Status $afterRecovery 200 'J: provisioning after recovery replays'
    Assert-True ((($afterRecovery.Body | ConvertFrom-Json).workspaceId) -eq $workspaceE) 'J: replay converges on the recovered Workspace'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$accountEId';") -eq 1) 'J: replay created no second Workspace membership'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    $totals = @{
        Workspaces = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.Workspaces;')
        Memberships = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.Memberships;')
        ProvisioningRecords = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.InitialProvisioningRecords;')
        OutstandingProvisioning = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE State<>'Completed';")
        AccessAssignments = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM access.MembershipRoleAssignments;')
        BootstrapProjections = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.BootstrapProjections;')
    }

    [pscustomobject] @{
        Status = 'PASS'
        Database = $DatabaseName
        FinishWorkspace = $workspaceA
        SkipWorkspace = $workspaceB
        ConcurrentWorkspace = $workspaceC
        RecoveredWorkspace = $workspaceE
        Totals = $totals
        Checks = $checks
    } | ConvertTo-Json -Depth 6
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
}
