param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not load System.Net.Http on demand.
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$baseUrl = 'http://127.0.0.1:5087'
$email = 'inbound.webhook.owner@example.test'
$password = 'Inbound-Webhook-Smoke!234'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$signingSecret = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-inbound-lead-webhook-' + [Guid]::NewGuid().ToString('N')))
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(20)
$checks = [System.Collections.Generic.List[string]]::new()

function Set-BaseEnvironment {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $email
    $env:IdentityAuth__DevelopmentBootstrap__Password = $password
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Inbound Webhook Owner'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'true'
    $env:Workspace__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Workspace__DevelopmentBootstrap__IdentityEmail = $email
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Key = 'inbound-webhook-main'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Name = 'Inbound Webhook Main'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__LogoText = 'IW'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__TimeZone = 'UTC'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__0 = 'leads'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__1 = 'tasks'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__2 = 'deals'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Key = 'inbound-webhook-foreign'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Name = 'Inbound Webhook Foreign'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__LogoText = 'IF'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__TimeZone = 'UTC'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'true'
    $env:AccessControl__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:AccessControl__DevelopmentBootstrap__IdentityEmail = $email
    $env:AccessControl__DevelopmentBootstrap__WorkspaceKey = 'inbound-webhook-main'
    $env:AccessControl__DevelopmentBootstrap__RoleName = 'Inbound Webhook Owner'
    $capabilities = @(
        'access.read', 'workspace.context.resolve',
        'tasks.read', 'tasks.create', 'tasks.update', 'tasks.assign', 'tasks.complete',
        'leads.read', 'leads.create', 'leads.update', 'leads.qualify',
        'deals.read', 'deals.create', 'deals.update', 'deals.assign', 'deals.close', 'deals.delete', 'deals.bulk'
    )
    for ($index = 0; $index -lt $capabilities.Count; $index++) {
        [Environment]::SetEnvironmentVariable(
            "AccessControl__DevelopmentBootstrap__Capabilities__$index",
            $capabilities[$index],
            'Process')
    }
    $env:Integrations__Secrets__inbound_webhook_smoke = $signingSecret
}

function Start-ApiHost([bool] $enableIntegration, [string] $workspaceId = '', [string] $memberId = '') {
    Set-BaseEnvironment
    $env:Integrations__DevelopmentBootstrap__Enabled = $enableIntegration.ToString().ToLowerInvariant()
    $env:Integrations__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Integrations__DevelopmentBootstrap__IntegrationId = 'int_inbound_lead_webhook'
    $env:Integrations__DevelopmentBootstrap__ProviderCode = 'generic-signed-json'
    $env:Integrations__DevelopmentBootstrap__WorkspaceId = $workspaceId
    $env:Integrations__DevelopmentBootstrap__DelegatedMemberId = $memberId
    $env:Integrations__DevelopmentBootstrap__SecretReference = 'inbound_webhook_smoke'
    $env:Integrations__DevelopmentBootstrap__BindingEnabled = 'true'
    if ($enableIntegration) {
        & dotnet $hostDll --seed-demo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Integration bootstrap failed with exit code $LASTEXITCODE." }
    }
    $standardOutput = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOutput -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($process.HasExited) {
            throw "ApiHost exited during startup: $((Get-Content -LiteralPath $standardError -Raw)) $((Get-Content -LiteralPath $standardOutput -Raw))"
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
        $process.WaitForExit(5000) | Out-Null
    }
}

function Invoke-SqlScalar([string] $query) {
    $value = & sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Invoke-Sql([string] $query) {
    & sqlcmd -S $server -d $DatabaseName -b -Q "SET NOCOUNT ON; $query" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
}

# Windows PowerShell 5.1 runs on .NET Framework, which has neither [Convert]::ToHexString nor the
# static SHA256.HashData added in .NET 5. Both helpers below are exact equivalents: BitConverter
# emits the same uppercase hex, only dash-separated. Harness compatibility only - no assertion,
# input or expected value changes.
function ConvertTo-HexString([byte[]] $bytes) {
    return ([BitConverter]::ToString($bytes) -replace '-', '')
}

function Get-Sha256Hash([byte[]] $bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return $algorithm.ComputeHash($bytes) }
    finally { $algorithm.Dispose() }
}

function Send-Json([string] $method, [string] $path, [string] $body, [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$baseUrl$path")
    # An unbound [string] parameter arrives as an empty string, not $null, so this attached an empty
    # JSON body to every GET, which the .NET Framework HttpClient in Windows PowerShell 5.1 refuses.
    # Harness defect only: no API semantics are changed to accommodate it.
    if (-not [string]::IsNullOrEmpty($body)) {
        $message.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    }
    foreach ($entry in $headers.GetEnumerator()) {
        $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
    }
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $message.Dispose()
    return [pscustomobject] @{ Status = [int] $response.StatusCode; Body = $text }
}

function Assert-Status($response, [int] $expected, [string] $name) {
    if ($response.Status -ne $expected) {
        throw "$name expected HTTP $expected but got $($response.Status): $($response.Body)"
    }
    $checks.Add("$name=$expected")
}

function Assert-SourceGuard([bool] $condition, [string] $name) {
    if (-not $condition) { throw "Source guard failed: $name" }
    $checks.Add("$name=PASS")
}

function New-Signature([string] $timestamp, [string] $deliveryId, [string] $body) {
    $prefixBytes = [Text.Encoding]::UTF8.GetBytes($timestamp + [char] 10 + $deliveryId + [char] 10)
    $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
    $material = [byte[]]::new($prefixBytes.Length + $bodyBytes.Length)
    [Array]::Copy($prefixBytes, 0, $material, 0, $prefixBytes.Length)
    [Array]::Copy($bodyBytes, 0, $material, $prefixBytes.Length, $bodyBytes.Length)
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($signingSecret))
    try { return 'sha256=' + (ConvertTo-HexString $hmac.ComputeHash($material)).ToLowerInvariant() }
    finally { $hmac.Dispose() }
}

function Send-Webhook(
    [string] $integrationId,
    [string] $deliveryId,
    [string] $body,
    [long] $timestamp,
    [string] $signature = '',
    [hashtable] $additionalHeaders = @{}) {
    $timestampText = $timestamp.ToString([Globalization.CultureInfo]::InvariantCulture)
    if ($signature.Length -eq 0) { $signature = New-Signature $timestampText $deliveryId $body }
    $headers = @{
        'X-Unicore-Delivery-Id' = $deliveryId
        'X-Unicore-Timestamp' = $timestampText
        'X-Unicore-Signature' = $signature
        'X-Correlation-Id' = 'corr-' + [Guid]::NewGuid().ToString('N')
    }
    foreach ($entry in $additionalHeaders.GetEnumerator()) {
        $headers[[string] $entry.Key] = [string] $entry.Value
    }
    return Send-Json 'POST' "/integrations/inbound/leads/$integrationId" $body $headers
}

$createAuthorizerSource = Get-Content -Raw -LiteralPath "$PSScriptRoot/../src/UnicoreCRM.Crm/Leads/Application/CreateLead/DelegatedLeadCreateAuthorizer.cs"
$ingressSource = Get-Content -Raw -LiteralPath "$PSScriptRoot/../src/UnicoreCRM.Crm/Leads/Application/CreateLead/InboundLeadIngress.cs"
$admissionSource = Get-Content -Raw -LiteralPath "$PSScriptRoot/../src/UnicoreCRM.Crm/Leads/Application/CreateLead/LeadCreateAdmission.cs"
$executionSource = Get-Content -Raw -LiteralPath "$PSScriptRoot/../src/UnicoreCRM.Crm/Leads/Application/CreateLead/LeadCreateExecution.cs"
$coordinatorSource = Get-Content -Raw -LiteralPath "$PSScriptRoot/../src/UnicoreCRM.Integrations/Webhooks/Inbound/Application/InboundLeadWebhookCoordinator.cs"
$payloadSource = Get-Content -Raw -LiteralPath "$PSScriptRoot/../src/UnicoreCRM.Integrations/Webhooks/Inbound/Application/GenericLeadWebhookPayload.cs"

Assert-SourceGuard ($createAuthorizerSource -match 'private DelegatedLeadIngressAuthorization\s*\(') `
    'Proof source guard private constructor'
Assert-SourceGuard (([regex]::Matches($createAuthorizerSource, 'new DelegatedLeadIngressAuthorization\s*\(')).Count -eq 1) `
    'Proof source guard single issuer construction'
Assert-SourceGuard ($createAuthorizerSource -notmatch 'AccessRequirement|FromAllowedDecision') `
    'Proof source guard no generic requirement or decision factory'
Assert-SourceGuard (([regex]::Matches($createAuthorizerSource, 'LeadCapabilities\.Create')).Count -eq 1) `
    'Proof source guard hard-coded leads.create'
Assert-SourceGuard ($createAuthorizerSource -match 'context\.WorkspaceId' `
        -and $createAuthorizerSource -match 'context\.AccountId' `
        -and $createAuthorizerSource -match 'context\.MemberId' `
        -and $createAuthorizerSource -match 'context\.MembershipId') `
    'Proof source guard exact trusted context binding'
Assert-SourceGuard ($createAuthorizerSource -match 'trustedWorkspace\.MemberId' `
        -and $createAuthorizerSource -match 'delegatedSubjectId') `
    'Proof source guard delegated subject matches trusted member'
Assert-SourceGuard ($ingressSource -match 'IDelegatedLeadCreateAuthorizer' `
        -and $ingressSource -notmatch 'IDelegatedAccessAuthorizer|AccessAuthorizationDecision|LeadCapabilities') `
    'Ingress source guard dedicated authorizer only'
Assert-SourceGuard ($admissionSource -notmatch 'FromAllowedDecision|AccessAuthorizationDecision|LeadAccess\?\s+\w+') `
    'Admission source guard generic decision and nullable access blocked'
Assert-SourceGuard ($admissionSource -match 'profile\.OwnerId is null' `
        -and $admissionSource -match 'metadata\.DelegatedSubjectId' `
        -and $admissionSource -match 'LeadCreateAdmission\(authorization\.Trusted\)') `
    'Admission source guard proof cannot rebind Workspace member or owner'
Assert-SourceGuard ($executionSource -match 'LeadCreateAdmission admission' `
        -and $executionSource -match 'admission\.GuardExecutionBinding' `
        -and $executionSource -notmatch 'LeadAccess\?\s+\w+|skipAuthorization|skipAccessControl') `
    'Execution source guard closed non-nullable admission'
Assert-SourceGuard ($coordinatorSource -match 'binding\.WorkspaceId' `
        -and $coordinatorSource -match 'binding\.DelegatedMemberId' `
        -and $payloadSource -match 'JsonUnmappedMemberHandling\.Disallow') `
    'Sender authority source guard server binding and closed payload'

$hostProcess = $null
try {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to provision the isolated webhook database.' }
    Set-BaseEnvironment
    & dotnet $hostDll --migrate
    if ($LASTEXITCODE -ne 0) { throw "Owner schema migration failed with exit code $LASTEXITCODE." }
    & dotnet $hostDll --seed-demo
    if ($LASTEXITCODE -ne 0) { throw "Development bootstrap failed with exit code $LASTEXITCODE." }
    $hostProcess = Start-ApiHost $false
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    $authority = Invoke-SqlScalar "SELECT TOP (1) m.WorkspaceId + '|' + m.MemberId + '|' + m.MembershipId FROM workspace.Memberships m INNER JOIN workspace.Workspaces w ON w.WorkspaceId=m.WorkspaceId WHERE w.[Key]='inbound-webhook-main';"
    $authorityParts = $authority.Split('|')
    if ($authorityParts.Count -ne 3) { throw "Workspace authority was not resolved: $authority" }
    $workspaceId = $authorityParts[0]
    $memberId = $authorityParts[1]
    $membershipId = $authorityParts[2]
    $foreignWorkspaceId = Invoke-SqlScalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='inbound-webhook-foreign';"
    $hostProcess = Start-ApiHost $true $workspaceId $memberId

    $signIn = Send-Json 'POST' '/auth/sessions' (@{ email = $email; password = $password; deviceLabel = 'Inbound webhook smoke' } | ConvertTo-Json -Compress) @{
        'X-Request-Id' = 'req-inbound-webhook-signin'; 'X-Correlation-Id' = 'corr-inbound-webhook-signin'; 'Idempotency-Key' = 'idem-inbound-webhook-signin'
    }
    Assert-Status $signIn 200 'Identity sign-in'
    $session = $signIn.Body | ConvertFrom-Json
    $token = $session.accessToken
    if ($session.session.principal.memberId -ne $memberId) { throw 'Identity principal/member mismatch.' }
    $authorization = @{
        Authorization = "Bearer $token"; 'X-Workspace-Id' = $workspaceId; 'X-Request-Id' = 'req-inbound-webhook-foundation'; 'X-Correlation-Id' = 'corr-inbound-webhook-foundation'
    }
    Assert-Status (Send-Json 'GET' '/auth/session' $null @{ Authorization = "Bearer $token"; 'X-Request-Id' = 'req-inbound-webhook-session'; 'X-Correlation-Id' = 'corr-inbound-webhook-session' }) 200 'Identity session'
    Assert-Status (Send-Json 'GET' '/workspaces' $null @{ Authorization = "Bearer $token"; 'X-Request-Id' = 'req-inbound-webhook-workspaces'; 'X-Correlation-Id' = 'corr-inbound-webhook-workspaces' }) 200 'Workspace list'
    Assert-Status (Send-Json 'GET' "/workspaces/$workspaceId/bootstrap" $null $authorization) 200 'Workspace bootstrap'
    Assert-Status (Send-Json 'GET' '/access/context' $null $authorization) 200 'AccessControl authorization'

    $taskHeaders = $authorization.Clone(); $taskHeaders['Idempotency-Key'] = 'idem-inbound-webhook-task'
    $task = Send-Json 'POST' '/tasks' (@{ title = 'Inbound webhook regression task'; assigneeId = $memberId; dueAt = [DateTimeOffset]::UtcNow.AddDays(1).ToString('yyyy-MM-ddTHH:mm:ssZ') } | ConvertTo-Json -Compress) $taskHeaders
    Assert-Status $task 201 'Tasks create'
    $taskId = ($task.Body | ConvertFrom-Json).aggregateId
    Assert-Status (Send-Json 'GET' "/tasks/$taskId" $null $authorization) 200 'Tasks get'

    $leadHeaders = $authorization.Clone(); $leadHeaders['Idempotency-Key'] = 'idem-inbound-webhook-normal-lead'
    $normalLeadBody = @{ displayName = 'Inbound webhook normal Lead'; source = 'Direct'; estimatedValue = @{ amount = '10.00'; currency = 'USD' }; email = 'normal@example.test' } | ConvertTo-Json -Compress
    $normalLead = Send-Json 'POST' '/leads' $normalLeadBody $leadHeaders
    Assert-Status $normalLead 201 'Leads create'
    $normalLeadId = ($normalLead.Body | ConvertFrom-Json).aggregateId
    Assert-Status (Send-Json 'GET' "/leads/$normalLeadId" $null $authorization) 200 'Leads get'

    $dealHeaders = $authorization.Clone(); $dealHeaders['Idempotency-Key'] = 'idem-inbound-webhook-deal'
    $dealBody = @{ name = 'Inbound webhook regression deal'; buyerRef = @{ type = 'CONTACT'; id = 'contact_inbound_webhook_scalar' }; stageCode = 'DISCOVERY'; amount = @{ amount = '100.00'; currency = 'USD' }; opportunityScore = '25'; ownerId = $memberId; expectedCloseDate = [DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd'); interestedProductIds = @(); lineItems = @() } | ConvertTo-Json -Compress -Depth 6
    $deal = Send-Json 'POST' '/deals' $dealBody $dealHeaders
    Assert-Status $deal 201 'Deals create'
    $dealId = ($deal.Body | ConvertFrom-Json).aggregateId
    Assert-Status (Send-Json 'GET' "/deals/$dealId" $null $authorization) 200 'Deals get'

    $baselineLeadCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE WorkspaceId='$workspaceId';")
    $body = @{ displayName = 'Webhook Lead'; source = 'Partner form'; estimatedValue = @{ amount = '1000.00'; currency = 'USD' }; email = 'webhook@example.test'; companyName = 'Webhook Co' } | ConvertTo-Json -Compress -Depth 5
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $positive = Send-Webhook 'int_inbound_lead_webhook' 'delivery-positive-1' $body $now
    Assert-Status $positive 200 'Inbound webhook valid signed delivery'
    $positiveReceipt = $positive.Body | ConvertFrom-Json
    $leadId = $positiveReceipt.leadId
    if ($positiveReceipt.outcome -ne 'PROCESSED' -or $leadId -eq 'delivery-positive-1' -or $leadId -eq 'int_inbound_lead_webhook' -or -not $leadId.StartsWith('lead_')) {
        throw 'Positive result or Lead identity is invalid.'
    }
    $afterPositive = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE WorkspaceId='$workspaceId';")
    if ($afterPositive -ne $baselineLeadCount + 1) { throw 'Positive webhook did not create exactly one Lead.' }
    $checks.Add('Inbound webhook server-assigned Lead identity=PASS')
    $capabilityAudit = Invoke-SqlScalar "SELECT COUNT(*) FROM access.AuthorizationDecisions WHERE WorkspaceId='$workspaceId' AND MembershipId='$membershipId' AND RequiredCapability='leads.create' AND Allowed=1 AND CorrelationId='$($positiveReceipt.correlationId)';"
    if ($capabilityAudit -ne '1') { throw 'Positive webhook did not record exactly one canonical delegated leads.create decision.' }
    $checks.Add('Inbound webhook one canonical leads.create authorization decision=PASS')

    # Assignment foundation: real HTTP admission, SQL persistence, shared record access and query predicates.
    if (($normalLead.Body | ConvertFrom-Json).result.ownerId -ne $memberId) { throw 'Interactive create did not resolve actor ownership.' }
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$leadId' AND ScopeOwnerId IS NULL AND JSON_VALUE(Profile,'$.ownerId') IS NULL;") -ne '1') { throw 'External Lead owner was not persisted as null.' }
    $checks.Add('Assignment foundation interactive actor and persisted external null=PASS')
    $roleId = Invoke-SqlScalar "SELECT TOP (1) RoleId FROM access.MembershipRoleAssignments WHERE MembershipId='$membershipId';"
    function Assert-Queue([int] $count, [int] $detailStatus, [string] $label) {
        $list = Send-Json 'GET' '/leads?assignmentState=UNASSIGNED' $null $authorization
        Assert-Status $list 200 "$label queue list"
        $page = $list.Body | ConvertFrom-Json
        if (@($page.items).Count -ne $count -or $page.pageInfo.totalCount -ne $count) { throw "$label queue list/count mismatch: $($list.Body)" }
        Assert-Status (Send-Json 'GET' "/leads/$leadId" $null $authorization) $detailStatus "$label detail"
        $decision = Send-Json 'POST' '/access/records/evaluate' (@{ resourceKey='leads'; recordId=$leadId; requestedCommands=@('lead.update'); requestedFields=@('ownerId','phone') } | ConvertTo-Json -Compress) $authorization
        Assert-Status $decision 200 "$label effective access"
        if (($decision.Body | ConvertFrom-Json).canRead -ne ($detailStatus -eq 200)) { throw "$label effective access disagrees with detail." }
        $all = Send-Json 'GET' '/leads' $null $authorization
        Assert-Status $all 200 "$label unfiltered list"
        if ($count -eq 0 -and @((($all.Body | ConvertFrom-Json).items) | Where-Object { $_.id -eq $leadId }).Count -ne 0) { throw "$label unfiltered list leaked queue." }
        $search = Send-Json 'GET' '/leads?search=Webhook' $null $authorization
        Assert-Status $search 200 "$label search"
        if ($count -eq 0 -and @((($search.Body | ConvertFrom-Json).items) | Where-Object { $_.id -eq $leadId }).Count -ne 0) { throw "$label search leaked queue." }
    }
    Assert-Queue 0 404 'WORKSPACE without queue permission'
    Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','leads.queue.read');"
    Assert-Queue 1 200 'WORKSPACE with queue permission'
    $unassigned = (Send-Json 'GET' "/leads/$leadId" $null $authorization).Body | ConvertFrom-Json
    if (-not ($unassigned.PSObject.Properties.Name -contains 'ownerId') -or $null -ne $unassigned.ownerId) { throw ('Transport must contain explicit null ownerId: ' + ($unassigned | ConvertTo-Json -Compress)) }
    $assigned = Send-Json 'GET' '/leads?assignmentState=ASSIGNED' $null $authorization
    Assert-Status $assigned 200 'Assigned query'
    if (@((($assigned.Body | ConvertFrom-Json).items) | Where-Object { $null -eq $_.ownerId }).Count -ne 0) { throw 'ASSIGNED returned null owner.' }
    Assert-Status (Send-Json 'GET' "/leads?assignmentState=UNASSIGNED&ownerId=$memberId" $null $authorization) 422 'Contradictory assignment filter'
    Assert-Status (Send-Json 'GET' '/leads?assignmentState=QUEUE' $null $authorization) 422 'Invalid assignment vocabulary'
    foreach ($scope in @('Own','Team','Custom')) {
        Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE RoleId='$roleId' AND ResourceKey='leads'; INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('scope_queue_verify','$workspaceId','$roleId','leads','$scope','[]');"
        if ($scope -eq 'Own') {
            Assert-Queue 1 200 'OWN with queue permission'
            $decision = (Send-Json 'POST' '/access/records/evaluate' (@{ resourceKey='leads'; recordId=$leadId; requestedCommands=@('lead.update') } | ConvertTo-Json -Compress) $authorization).Body | ConvertFrom-Json
            if ($decision.canUpdate -or $decision.allowedCommands.Count -ne 0) { throw 'Queue read widened OWN mutations.' }
            $denyHeaders=$authorization.Clone(); $denyHeaders['Idempotency-Key']='queue-own-mutate'; $denyHeaders['If-Match']='"0"'
            Assert-Status (Send-Json 'PUT' "/leads/$leadId" '{"displayName":"Denied","ownerId":null}' $denyHeaders) 404 'OWN queue read cannot update'
            Invoke-Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.queue.read';"
            Assert-Queue 0 404 'OWN without queue permission'
            Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','leads.queue.read');"
        } else { Assert-Queue 0 404 "$scope with queue permission" }
    }
    Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE RoleId='$roleId' AND ResourceKey='leads';"
    foreach ($fakeOwner in @('queue','unassigned','system','sales_queue','member_someone_else')) {
        $headers=$authorization.Clone(); $headers['Idempotency-Key']="fake-owner-$fakeOwner"
        Assert-Status (Send-Json 'POST' '/leads' (@{displayName='Invalid owner';email='invalid@example.test';ownerId=$fakeOwner} | ConvertTo-Json -Compress) $headers) 403 "Interactive rejects $fakeOwner"
        Assert-Status (Send-Webhook 'int_inbound_lead_webhook' "owner-spoof-$fakeOwner" (@{displayName='Spoof';email='invalid@example.test';ownerId=$fakeOwner} | ConvertTo-Json -Compress) ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 400 "External rejects $fakeOwner"
    }
    # Cross-workspace SQL fixture: authenticate the same actor with no foreign membership.
    $foreignHeaders=$authorization.Clone(); $foreignHeaders['X-Workspace-Id']=$foreignWorkspaceId
    foreach ($path in @('/leads?assignmentState=UNASSIGNED',"/leads/$leadId")) {
        $foreign=Send-Json 'GET' $path $null $foreignHeaders
        if ($foreign.Status -notin @(403,404)) { throw "Cross-workspace read accepted: $($foreign.Status)" }
    }
    $foreignHeaders['Idempotency-Key']='foreign-queue-write'; $foreignHeaders['If-Match']='"0"'
    $foreign=Send-Json 'PUT' "/leads/$leadId" '{"displayName":"Foreign","ownerId":null}' $foreignHeaders
    if ($foreign.Status -notin @(403,404)) { throw 'Cross-workspace mutation accepted.' }
    $checks.Add('Assignment foundation cross-workspace list/detail/mutation=PASS')
    # Existing assigned records and external provenance are preserved while projection accepts null.
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$normalLeadId' AND ScopeOwnerId='$memberId';") -ne '1') { throw 'Assigned record changed.' }


    $missingSignature = Send-Json 'POST' '/integrations/inbound/leads/int_inbound_lead_webhook' $body @{
        'X-Unicore-Delivery-Id' = 'delivery-missing-signature'; 'X-Unicore-Timestamp' = $now; 'X-Correlation-Id' = 'corr-inbound-webhook-missing-signature'
    }
    Assert-Status $missingSignature 401 'Inbound webhook missing signature'
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM ops.InboxMessages WHERE DeliveryId='delivery-missing-signature';") -ne '0') { throw 'Missing signature entered Inbox.' }
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-invalid-signature' $body $now ('sha256=' + ('00' * 32))) 401 'Inbound webhook invalid signature'
    $oldSignature = New-Signature $now.ToString() 'delivery-tampered' $body
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-tampered' $body.Replace('Webhook Lead', 'Tampered Lead') $now $oldSignature) 401 'Inbound webhook tampered body'
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-stale' $body ([DateTimeOffset]::UtcNow.AddMinutes(-10).ToUnixTimeSeconds())) 401 'Inbound webhook stale timestamp'

    $replay = Send-Webhook 'int_inbound_lead_webhook' 'delivery-positive-1' $body ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
    Assert-Status $replay 200 'Inbound webhook same delivery replay'
    $replayReceipt = $replay.Body | ConvertFrom-Json
    if ($replayReceipt.outcome -ne 'REPLAYED' -or $replayReceipt.leadId -ne $leadId) { throw 'Replay did not return the original Lead.' }
    if ([int] (Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE WorkspaceId='$workspaceId';") -ne $afterPositive) { throw 'Replay duplicated the Lead.' }
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-positive-1' $body.Replace('Webhook Lead', 'Changed Delivery') ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 409 'Inbound webhook delivery conflict'

    $spoofBody = @{ displayName = 'Spoof'; source = 'Partner form'; estimatedValue = @{ amount = '5.00'; currency = 'USD' }; email = 'spoof@example.test'; workspaceId = $foreignWorkspaceId } | ConvertTo-Json -Compress -Depth 5
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-workspace-spoof' $spoofBody ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 400 'Inbound webhook Workspace spoof'
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE WorkspaceId='$foreignWorkspaceId';") -ne '0') { throw 'Workspace spoof created a foreign Lead.' }
    $productBody = @{ displayName = 'Product Gap'; source = 'Partner form'; estimatedValue = @{ amount = '5.00'; currency = 'USD' }; email = 'product@example.test'; interestedProducts = @(@{ productId = 'product_1' }) } | ConvertTo-Json -Compress -Depth 6
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-product-gap' $productBody ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 400 'Leads interested Products gap'

    Invoke-Sql "INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('wsm_inbound_webhook_denied','$workspaceId','acct_inbound_webhook_denied','member_inbound_webhook_denied','Active',SYSUTCDATETIME()); UPDATE integration.InboundBindings SET DelegatedMemberId='member_inbound_webhook_denied',UpdatedAt=SYSUTCDATETIME() WHERE IntegrationId='int_inbound_lead_webhook';"
    $deniedLeadCount = Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.Leads;'
    $deniedAuditCount = Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.AuditRecords;'
    $deniedOutboxCount = Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.OutboxMessages;'
    $deniedIdempotencyCount = Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.IdempotencyRecords;'
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-auth-denied' $body ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 403 'Inbound webhook authorization denial'
    if ([int] (Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE WorkspaceId='$workspaceId';") -ne $afterPositive) { throw 'Authorization denial created a Lead.' }
    if ((Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.Leads;') -ne $deniedLeadCount `
        -or (Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.AuditRecords;') -ne $deniedAuditCount `
        -or (Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.OutboxMessages;') -ne $deniedOutboxCount `
        -or (Invoke-SqlScalar 'SELECT COUNT(*) FROM leads.IdempotencyRecords;') -ne $deniedIdempotencyCount) {
        throw 'Denied delegated authorization mutated Lead business state, audit, outbox, or idempotency.'
    }
    $checks.Add('Inbound webhook authorization denial no Lead business mutation=PASS')
    $deniedCapabilityAudit = Invoke-SqlScalar "SELECT COUNT(*) FROM access.AuthorizationDecisions WHERE WorkspaceId='$workspaceId' AND MembershipId='wsm_inbound_webhook_denied' AND RequiredCapability='leads.create' AND Allowed=0;"
    if ($deniedCapabilityAudit -ne '1') { throw 'Denied webhook did not record exactly one canonical delegated leads.create decision.' }
    $checks.Add('Inbound webhook denied canonical leads.create authorization decision=PASS')
    Invoke-Sql "UPDATE integration.InboundBindings SET DelegatedMemberId='member_inbound_webhook_missing',UpdatedAt=SYSUTCDATETIME() WHERE IntegrationId='int_inbound_lead_webhook';"
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-invalid-member' $body ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 403 'Inbound webhook invalid delegated member'
    Invoke-Sql "UPDATE integration.InboundBindings SET DelegatedMemberId='$memberId',IsEnabled=0,UpdatedAt=SYSUTCDATETIME() WHERE IntegrationId='int_inbound_lead_webhook';"
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' 'delivery-disabled' $body ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 404 'Inbound webhook disabled binding'
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook_unknown' 'delivery-unknown' $body ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 404 'Inbound webhook unknown binding'
    Invoke-Sql "UPDATE integration.InboundBindings SET IsEnabled=1,UpdatedAt=SYSUTCDATETIME() WHERE IntegrationId='int_inbound_lead_webhook';"

    Invoke-Sql "UPDATE ops.InboxMessages SET Status='Received',ResultLeadId=NULL,LastResultCode=NULL,ProcessedAt=NULL,UpdatedAt=SYSUTCDATETIME() WHERE IntegrationId='int_inbound_lead_webhook' AND DeliveryId='delivery-positive-1';"
    $recovery = Send-Webhook 'int_inbound_lead_webhook' 'delivery-positive-1' $body ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
    Assert-Status $recovery 200 'Inbound webhook post-Lead-commit recovery'
    if (($recovery.Body | ConvertFrom-Json).leadId -ne $leadId -or [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE WorkspaceId='$workspaceId';") -ne $afterPositive) {
        throw 'Recovery did not converge idempotently.'
    }

    $concurrentBody = $body.Replace('Webhook Lead', 'Concurrent Lead')
    $concurrentDelivery = 'delivery-concurrent'
    $concurrentTimestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $concurrentSignature = New-Signature $concurrentTimestamp $concurrentDelivery $concurrentBody
    function New-ConcurrentMessage {
        $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$baseUrl/integrations/inbound/leads/int_inbound_lead_webhook")
        $message.Content = [System.Net.Http.StringContent]::new($concurrentBody, [Text.Encoding]::UTF8, 'application/json')
        $null = $message.Headers.TryAddWithoutValidation('X-Unicore-Delivery-Id', $concurrentDelivery)
        $null = $message.Headers.TryAddWithoutValidation('X-Unicore-Timestamp', $concurrentTimestamp)
        $null = $message.Headers.TryAddWithoutValidation('X-Unicore-Signature', $concurrentSignature)
        $null = $message.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-' + [Guid]::NewGuid().ToString('N'))
        return $message
    }
    $messageOne = New-ConcurrentMessage
    $messageTwo = New-ConcurrentMessage
    $taskOne = $client.SendAsync($messageOne)
    $taskTwo = $client.SendAsync($messageTwo)
    [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]] @($taskOne, $taskTwo))
    $statusOne = [int] $taskOne.Result.StatusCode
    $statusTwo = [int] $taskTwo.Result.StatusCode
    $messageOne.Dispose(); $messageTwo.Dispose()
    if ($statusOne -ne 200 -or $statusTwo -ne 200) { throw "Concurrent duplicate statuses were $statusOne/$statusTwo." }
    if ([int] (Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE JSON_VALUE(Profile,'$.displayName')='Concurrent Lead';") -ne 1) { throw 'Concurrent delivery did not create exactly one Lead.' }
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE SourceReference='delivery-concurrent' AND ActorType='Integration' AND ActorId='int_inbound_lead_webhook';") -ne '1') { throw 'Concurrent replay duplicated Lead audit evidence.' }
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM ops.InboxMessages WHERE IntegrationId='int_inbound_lead_webhook' AND DeliveryId='delivery-concurrent' AND Status='Processed';") -ne '1') { throw 'Concurrent replay duplicated Inbox evidence.' }
    $checks.Add('Inbound webhook concurrent duplicate=200/200, one Lead')

    $headerSpoof = Send-Webhook 'int_inbound_lead_webhook' 'delivery-header-spoof' $body.Replace('Webhook Lead', 'Header Spoof Lead') `
        ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) '' @{
            'X-Workspace-Id' = $foreignWorkspaceId
            'X-Delegated-Subject-Id' = 'member_sender_chosen'
        }
    Assert-Status $headerSpoof 200 'Inbound webhook sender authority headers ignored'
    $headerSpoofLeadId = ($headerSpoof.Body | ConvertFrom-Json).leadId
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$headerSpoofLeadId' AND WorkspaceId='$workspaceId' AND ScopeOwnerId IS NULL;") -ne '1') {
        throw 'Sender authority headers changed trusted Workspace or unassigned ownership.'
    }
    $checks.Add('Inbound webhook sender cannot choose Workspace or delegated subject=PASS')

    $inboxEvidence = Invoke-SqlScalar "SELECT COUNT(*) FROM ops.InboxMessages WHERE IntegrationId='int_inbound_lead_webhook' AND DeliveryId='delivery-positive-1' AND Status='Processed' AND ResultLeadId='$leadId';"
    $auditEvidence = Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$leadId' AND ActorType='Integration' AND ActorId='int_inbound_lead_webhook' AND DelegatedSubjectId='$memberId' AND SourceReference='delivery-positive-1';"
    $idempotencyBytes = [Text.Encoding]::UTF8.GetBytes('PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK' + [char] 10 + 'int_inbound_lead_webhook' + [char] 10 + 'delivery-positive-1')
    $idempotencyKey = 'inbound-lead-webhook_' + (ConvertTo-HexString (Get-Sha256Hash $idempotencyBytes))
    if ($inboxEvidence -ne '1' -or $auditEvidence -ne '1' -or $leadId -eq $idempotencyKey) { throw 'Persistence, audit, or identity evidence failed.' }
    $checks.Add('Inbound webhook Inbox persistence=PASS')
    $checks.Add('Inbound webhook integration actor audit=PASS')
    $checks.Add('Inbound webhook delivery/idempotency identity negative=PASS')

    # Assignment foundation regressions run after the webhook count/idempotency assertions.
    Invoke-Sql "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_queue_owner','$workspaceId','$roleId','leads','ownerId','Hidden');"
    Assert-Status (Send-Json 'GET' '/leads?assignmentState=UNASSIGNED' $null $authorization) 403 'Queue hidden required owner fails closed'
    Assert-Status (Send-Json 'GET' "/leads/$leadId" $null $authorization) 403 'Detail hidden required owner fails closed'
    Invoke-Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='field_queue_owner';"
    $editHeaders=$authorization.Clone(); $editHeaders['Idempotency-Key']='queue-preserve-null'; $editHeaders['If-Match']='"0"'
    Assert-Status (Send-Json 'PUT' "/leads/$leadId" '{"displayName":"Queue edited","ownerId":null,"phone":"0909988776"}' $editHeaders) 200 'Queue profile preserves null owner'
    Invoke-Sql "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_queue_phone','$workspaceId','$roleId','leads','phone','Hidden');"
    $hiddenPhone=Send-Json 'GET' "/leads/$leadId" $null $authorization
    Assert-Status $hiddenPhone 200 'Queue hidden phone detail'
    if ($hiddenPhone.Body -match '0909988776') { throw 'Hidden phone disclosed.' }
    $hiddenSearch=Send-Json 'GET' '/leads?assignmentState=UNASSIGNED&search=0909988776' $null $authorization
    Assert-Status $hiddenSearch 200 'Queue hidden phone search'
    if (($hiddenSearch.Body|ConvertFrom-Json).pageInfo.totalCount -ne 0) { throw 'Hidden phone search disclosed record.' }
    Invoke-Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='field_queue_phone';"
    $seen=[System.Collections.Generic.HashSet[string]]::new()
    $cursor=$null
    do {
        $path='/leads?assignmentState=UNASSIGNED&limit=1'
        if ($cursor) { $path += '&cursor=' + [Uri]::EscapeDataString($cursor) }
        $response=Send-Json 'GET' $path $null $authorization
        Assert-Status $response 200 'Queue cursor page'
        $page=$response.Body|ConvertFrom-Json
        foreach($item in $page.items) {
            if ($null -ne $item.ownerId -or -not $seen.Add($item.id)) { throw 'Queue page owner or duplicate invariant failed.' }
        }
        $cursor=$page.pageInfo.nextCursor
    } while($page.pageInfo.hasNextPage)
    if ($seen.Count -ne $page.pageInfo.totalCount) { throw 'Queue count/pagination mismatch.' }
    $editHeaders['Idempotency-Key']='queue-reject-assign'; $editHeaders['If-Match']='"1"'
    Assert-Status (Send-Json 'PUT' "/leads/$leadId" (@{displayName='Unauthorized assignment';ownerId=$memberId}|ConvertTo-Json -Compress) $editHeaders) 403 'Profile cannot assign Queue'
    $editHeaders['Idempotency-Key']='assigned-profile'; $editHeaders['If-Match']='"0"'
    Assert-Status (Send-Json 'PUT' "/leads/$normalLeadId" (@{displayName='Assigned preserved';ownerId=$memberId;email='normal@example.test'}|ConvertTo-Json -Compress) $editHeaders) 200 'Assigned profile regression'
    $editHeaders['Idempotency-Key']='assigned-cannot-unassign'; $editHeaders['If-Match']='"1"'
    Assert-Status (Send-Json 'PUT' "/leads/$normalLeadId" '{"displayName":"Cannot unassign","ownerId":null}' $editHeaders) 403 'Profile cannot unassign existing owner'
    $editHeaders['Idempotency-Key']='assigned-state'; $editHeaders['If-Match']='"1"'
    Assert-Status (Send-Json 'POST' "/leads/$normalLeadId/advance-work-state" '{"targetWorkState":"CONTACTING"}' $editHeaders) 200 'Assigned lifecycle regression'
    Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','leads.delete');"
    $editHeaders['Idempotency-Key']='assigned-archive'; $editHeaders['If-Match']='"2"'
    Assert-Status (Send-Json 'POST' "/leads/$normalLeadId/archive" '{"reason":"Regression fixture"}' $editHeaders) 200 'Assigned archive regression'
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$normalLeadId' AND ScopeOwnerId='$memberId' AND ArchivedAt IS NOT NULL;") -ne '1') { throw 'Archive changed assigned ownership.' }
    $checks.Add('Assignment foundation field-security and cursor invariants=PASS')

    [pscustomobject] @{
        Status = 'PASS'
        Database = $DatabaseName
        WorkspaceId = $workspaceId
        MemberId = $memberId
        PositiveLeadId = $leadId
        BaselineLeadCount = $baselineLeadCount
        FinalLeadCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE WorkspaceId='$workspaceId';")
        InboxCount = [int] (Invoke-SqlScalar 'SELECT COUNT(*) FROM ops.InboxMessages;')
        Checks = $checks
    } | ConvertTo-Json -Depth 5
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
}
