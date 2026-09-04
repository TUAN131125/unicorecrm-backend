param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName
)

# Proves the Initial Workspace Provisioning migration chain and the corrective migration against
# real databases and a real ApiHost.
#
#   A. legacy fabricated anchor plus an existing AccessControl role and assignment;
#   B. legacy fabricated anchor with no AccessControl assignment;
#   C. genuinely completed anchor, which must not be reset or replayed;
#   D. fresh database applying the full migration chain;
#   E. database that never applied the faulty migration, upgrading across the whole chain.
#   J. completed historical access definition, which normal startup must not converge or rewrite.
#
# A legacy fabricated anchor is the exact result of 20260824135117_InitialWorkspaceProvisioningRecovery:
# State = 'Completed' with CompletedAt equal to ProvisionedAt.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$baseUrl = 'http://127.0.0.1:5090'
$password = 'Initial-Provisioning-Upgrade!234'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$recoveryMigration = 'InitialWorkspaceProvisioningRecovery'
$provisioningMigration = 'InitialWorkspaceProvisioning'
$correctionMigration = 'InitialWorkspaceProvisioningRecoveryCorrection'
$correctionDatabase = $DatabaseName
$freshDatabase = "$($DatabaseName)_Fresh"
$chainDatabase = "$($DatabaseName)_Chain"
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-provisioning-upgrade-' + [Guid]::NewGuid().ToString('N')))
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()
$activeDatabase = $DatabaseName

# The exact frozen server-owned snapshots admitted by InitialWorkspaceAccessPolicy. Only the
# pre-Contacts set may upgrade; arbitrary subsets and sets with unexpected extras must fail closed.
$preContactsCapabilities = @(
    'deals.assign', 'deals.bulk', 'deals.close', 'deals.create', 'deals.delete', 'deals.read', 'deals.update',
    'leads.create', 'leads.qualify', 'leads.read', 'leads.update',
    'products.create', 'products.delete', 'products.edit', 'products.read',
    'support.assign', 'support.create', 'support.read', 'support.update',
    'tasks.assign', 'tasks.complete', 'tasks.create', 'tasks.read', 'tasks.update',
    'workspace.context.resolve'
)
$initialCapabilities = @('contacts.read') + $preContactsCapabilities
$initialCapabilitiesSql = ($initialCapabilities | ForEach-Object { "'$_'" }) -join ','

$ownerContexts = @(
    @{ Project = 'src/UnicoreCRM.Platform'; Context = 'IdentityAuthDbContext' },
    @{ Project = 'src/UnicoreCRM.Platform'; Context = 'AccessControlDbContext' },
    @{ Project = 'src/UnicoreCRM.Operations'; Context = 'TasksDbContext' },
    @{ Project = 'src/UnicoreCRM.Crm'; Context = 'LeadsDbContext' },
    @{ Project = 'src/UnicoreCRM.Crm'; Context = 'DealsDbContext' },
    @{ Project = 'src/UnicoreCRM.Crm'; Context = 'ContactsDbContext' },
    @{ Project = 'src/UnicoreCRM.Integrations'; Context = 'IntegrationsDbContext' },
    @{ Project = 'src/UnicoreCRM.PlatformOperations'; Context = 'InboxDbContext' }
)

function Get-ConnectionString([string] $database) {
    return "Server=$server;Database=$database;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}

function Invoke-SqlScalar([string] $query) {
    $value = & sqlcmd -S $server -d $activeDatabase -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Invoke-Sql([string] $query) {
    # Routed through a script file so JSON literals containing double quotes survive intact.
    if (-not (Test-Path -LiteralPath $temporaryDirectory)) {
        $null = New-Item -ItemType Directory -Path $temporaryDirectory -Force
    }
    $scriptPath = Join-Path $temporaryDirectory ('seed-' + [Guid]::NewGuid().ToString('N') + '.sql')
    Set-Content -LiteralPath $scriptPath -Value ("SET NOCOUNT ON;`r`n" + $query) -Encoding ascii
    & sqlcmd -S $server -d $activeDatabase -b -i $scriptPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    Remove-Item -LiteralPath $scriptPath -Force
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

function Update-Database([string] $project, [string] $context, [string] $targetMigration = $null) {
    $arguments = @('ef', 'database', 'update')
    if (-not [string]::IsNullOrEmpty($targetMigration)) { $arguments += $targetMigration }
    $arguments += @('--project', (Join-Path $solutionRoot $project), '--context', $context, '--no-build')
    & dotnet @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not apply migrations for $context." }
}

function New-Database([string] $database) {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END; CREATE DATABASE [$database];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create the isolated database $database." }
    $script:activeDatabase = $database
    $env:ConnectionStrings__UnicoreCRM = Get-ConnectionString $database
}

function Update-OwnerDatabases {
    foreach ($entry in $ownerContexts) { Update-Database $entry.Project $entry.Context }
}

function Set-HostEnvironment([string] $identityEmail, [bool] $resumeEnabled, [string] $listenUrl = $baseUrl) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $listenUrl
    $env:ConnectionStrings__UnicoreCRM = Get-ConnectionString $activeDatabase
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $identityEmail
    $env:IdentityAuth__DevelopmentBootstrap__Password = $password
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Provisioning Upgrade Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    # This harness owns the exact schema state under test, so the one-click Development
    # migration pass must stay off.
    $env:Development__ApplyMigrations = 'false'
    $env:Integrations__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = $resumeEnabled.ToString().ToLowerInvariant()
    $env:Workflows__InitialWorkspaceProvisioning__ResumeIntervalSeconds = '2'
    $env:Workflows__InitialWorkspaceProvisioning__DevelopmentFaultInjection__FailAccessAssignment = 'false'
}

function Wait-ApiHost($process, [string] $listenUrl) {
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($process.HasExited) { throw "ApiHost exited during startup." }
        try {
            $probe = $client.GetAsync("$listenUrl/auth/session").GetAwaiter().GetResult()
            if ([int] $probe.StatusCode -eq 401) { return }
        }
        catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'ApiHost did not listen within the upgrade timeout.'
}

function Start-ApiHost(
    [string] $identityEmail,
    [bool] $resumeEnabled,
    [string] $listenUrl = $baseUrl,
    [bool] $waitForReady = $true) {
    Set-HostEnvironment $identityEmail $resumeEnabled $listenUrl
    if (-not (Test-Path -LiteralPath $temporaryDirectory)) {
        $null = New-Item -ItemType Directory -Path $temporaryDirectory -Force
    }
    $standardOut = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOut -RedirectStandardError $standardError -PassThru
    if ($waitForReady) { Wait-ApiHost $process $listenUrl }
    return $process
}

function Stop-ApiHost($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
}

function Send-Json([string] $method, [string] $path, [string] $body, [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$baseUrl$path")
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

function New-Headers([string] $token, [string] $workspaceId = $null) {
    $headers = @{
        Authorization = "Bearer $token"
        'X-Request-Id' = 'req-' + [Guid]::NewGuid().ToString('N')
        'X-Correlation-Id' = 'corr-' + [Guid]::NewGuid().ToString('N')
    }
    if (-not [string]::IsNullOrEmpty($workspaceId)) { $headers['X-Workspace-Id'] = $workspaceId }
    return $headers
}

function Get-Token([string] $email) {
    $attemptId = [Guid]::NewGuid().ToString('N')
    $response = Send-Json 'POST' '/auth/sessions' (@{
        email = $email
        password = $password
        deviceLabel = 'Provisioning upgrade smoke'
    } | ConvertTo-Json -Compress) @{
        'X-Request-Id' = "req-signin-$attemptId"
        'X-Correlation-Id' = "corr-signin-$attemptId"
        'Idempotency-Key' = "idem-signin-$attemptId"
    }
    Assert-Status $response 200 "Identity sign-in $email"
    return ($response.Body | ConvertFrom-Json).accessToken
}

function New-Accounts([string[]] $emails) {
    foreach ($email in $emails) {
        Set-HostEnvironment $email $false
        & dotnet $hostDll --seed-demo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not explicitly seed the Identity fixture for $email." }
    }
}

function New-SeededAnchor(
    [string] $email,
    [string] $keySuffix,
    [bool] $seedAccessAssignment,
    [int] $completionOffsetSeconds,
    [string[]] $roleCapabilities = $initialCapabilities,
    [string] $roleDescription = 'Initial Workspace provisioning role for the account that created this Workspace.') {
    # Realistic provisioned state: Workspace, ACTIVE creator membership, configuration seed and
    # anchor. A completion offset of zero reproduces the legacy fabricated signature exactly.
    $accountId = Invoke-SqlScalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())';"
    $memberId = Invoke-SqlScalar "SELECT MemberId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())';"
    $suffix = [Guid]::NewGuid().ToString('N')
    $workspaceId = "ws_$suffix"
    $membershipId = "wsm_$suffix"
    $workspaceKey = "upgrade-$keySuffix-$($suffix.Substring(0, 8))"
    $provisionedAt = (Get-Date).ToUniversalTime().AddMinutes(-10)
    $provisionedAtText = $provisionedAt.ToString('yyyy-MM-ddTHH:mm:ss.fffffff+00:00')
    $completedAtText = $provisionedAt.AddSeconds($completionOffsetSeconds).ToString('yyyy-MM-ddTHH:mm:ss.fffffff+00:00')
    Invoke-Sql "INSERT INTO workspace.Workspaces (WorkspaceId,[Key],Name,LogoText,CreatedAt) VALUES ('$workspaceId','$workspaceKey','Upgrade $keySuffix Workspace','UP','$provisionedAtText');"
    Invoke-Sql "INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('$membershipId','$workspaceId','$accountId','$memberId','Active','$provisionedAtText');"
    Invoke-Sql "INSERT INTO workspace.BootstrapProjections (WorkspaceId,ContextVersion,ConfigurationVersion,Locale,TimeZone,BaseCurrency,CapabilitiesJson,EnabledModuleKeysJson,AvailableProductSpacesJson) VALUES ('$workspaceId',0,0,'en','UTC','USD','[]','[""leads"",""deals"",""tasks""]','[""crm""]');"
    Invoke-Sql "INSERT INTO workspace.InitialProvisioningRecords (AccountId,MemberId,WorkspaceId,MembershipId,IdempotencyKey,RequestFingerprint,State,CompletedAt,ProvisionedAt) VALUES ('$accountId','$memberId','$workspaceId','$membershipId','idem-legacy-$keySuffix','$(('0' * 64))','Completed','$completedAtText','$provisionedAtText');"

    $roleId = $null
    $assignmentId = $null
    if ($seedAccessAssignment) {
        $roleId = "role_$suffix"
        $assignmentId = "assignment_$suffix"
        Invoke-Sql "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$roleId','$workspaceId','Workspace Owner','WORKSPACE OWNER','$roleDescription',NULL,1,0,'$provisionedAtText','$provisionedAtText');"
        foreach ($capability in $roleCapabilities) {
            Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','$capability');"
        }
        Invoke-Sql "INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES ('$assignmentId','$workspaceId','$membershipId','$roleId','$provisionedAtText');"
    }

    return [pscustomobject] @{
        Email = $email
        AccountId = $accountId
        WorkspaceId = $workspaceId
        MembershipId = $membershipId
        RoleId = $roleId
        AssignmentId = $assignmentId
        CompletedAtText = $completedAtText
    }
}

function Get-AnchorState([string] $accountId) {
    return Invoke-SqlScalar "SELECT State FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountId';"
}

function Wait-AnchorState([string] $accountId, [string] $expectedState, [int] $timeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-AnchorState $accountId) -eq $expectedState) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Assert-RuntimeUsable($state, [string] $label) {
    $token = Get-Token $state.Email
    $list = Send-Json 'GET' '/workspaces' $null (New-Headers $token)
    Assert-Status $list 200 "$label listMyWorkspaces"
    $items = ($list.Body | ConvertFrom-Json).items
    Assert-True ($items.Count -eq 1 -and $items[0].workspaceId -eq $state.WorkspaceId) "$label lists exactly the migrated Workspace"
    Assert-Status (Send-Json 'GET' "/workspaces/$($state.WorkspaceId)/bootstrap" $null (New-Headers $token)) 200 "$label getWorkspaceBootstrap"
    Assert-Status (Send-Json 'GET' '/access/context' $null (New-Headers $token $state.WorkspaceId)) 200 "$label getCurrentAuthorizationContext"
    Assert-Status (Send-Json 'GET' '/tasks' $null (New-Headers $token $state.WorkspaceId)) 200 "$label workspace-required Tasks read"
    Assert-Status (Send-Json 'GET' '/leads' $null (New-Headers $token $state.WorkspaceId)) 200 "$label workspace-required Leads read"
    Assert-Status (Send-Json 'GET' '/deals' $null (New-Headers $token $state.WorkspaceId)) 200 "$label workspace-required Deals read"
    Assert-Status (Send-Json 'GET' '/contacts' $null (New-Headers $token $state.WorkspaceId)) 200 "$label workspace-required Contacts read"
}

function Assert-SingleState($state, [string] $label) {
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Workspaces WHERE WorkspaceId='$($state.WorkspaceId)';") -eq 1) "$label exactly one Workspace"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$($state.AccountId)';") -eq 1) "$label exactly one Membership"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.BootstrapProjections WHERE WorkspaceId='$($state.WorkspaceId)';") -eq 1) "$label exactly one configuration seed"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.Roles WHERE WorkspaceId='$($state.WorkspaceId)' AND Name='Workspace Owner';") -eq 1) "$label exactly one canonical role"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments a JOIN access.Roles r ON r.RoleId=a.RoleId WHERE a.WorkspaceId='$($state.WorkspaceId)' AND r.Name='Workspace Owner';") -eq 1) "$label exactly one canonical access assignment"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($state.AccountId)';") -eq 1) "$label exactly one provisioning anchor"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.BootstrapProjections b WHERE WorkspaceId='$($state.WorkspaceId)' AND ConfigurationVersion=0 AND (SELECT COUNT(*) FROM OPENJSON(b.EnabledModuleKeysJson))=3 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),j.value),',') WITHIN GROUP (ORDER BY CONVERT(int,j.[key])) FROM OPENJSON(b.EnabledModuleKeysJson) j)='leads,deals,tasks';") -eq 1) "$label existing enabled-module configuration is unchanged"
    Assert-True ((Invoke-SqlScalar "SELECT RequestFingerprint FROM workspace.InitialProvisioningRecords WHERE AccountId='$($state.AccountId)';") -eq ('0' * 64)) "$label stored provisioning fingerprint is unchanged"
    $capabilityCount = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities c JOIN access.Roles r ON r.RoleId=c.RoleId WHERE r.WorkspaceId='$($state.WorkspaceId)' AND r.Name='Workspace Owner';")
    Assert-True ($capabilityCount -eq $initialCapabilities.Count) "$label role carries the server-owned capability set"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId=(SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($state.WorkspaceId)' AND Name='Workspace Owner') AND Capability='contacts.read';") -eq 1) "$label contacts.read exists exactly once"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId=(SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($state.WorkspaceId)' AND Name='Workspace Owner') AND Capability NOT IN ($initialCapabilitiesSql);") -eq 0) "$label no unexpected canonical-role capability was introduced"
}

function Assert-HistoricalAccessUnchanged($state, [string] $roleState, [string] $capabilityState, [string] $label) {
    $currentRoleState = Invoke-SqlScalar "SELECT CONCAT(Name,'|',NormalizedName,'|',Description,'|',COALESCE(SourceTemplateId,''),'|',IsActive,'|',[Version],'|',CONVERT(varchar(33),UpdatedAt,126)) FROM access.Roles WHERE RoleId='$($state.RoleId)';"
    $currentCapabilityState = Invoke-SqlScalar "SELECT STRING_AGG(CONVERT(nvarchar(max),Capability),',') WITHIN GROUP (ORDER BY Capability) FROM access.RoleCapabilities WHERE RoleId='$($state.RoleId)';"
    Assert-True ($currentRoleState -eq $roleState) "$label completed Workspace role definition is unchanged"
    Assert-True ($currentCapabilityState -eq $capabilityState) "$label completed Workspace capability set is unchanged"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$($state.RoleId)' AND Capability='contacts.read';") -eq 0) "$label completed historical role was not upgraded"
}

$hostProcess = $null
$hostProcessSecondary = $null
try {
    # ---------------------------------------------------------------- D. fresh full chain
    New-Database $freshDatabase
    Update-OwnerDatabases
    Update-Database 'src/UnicoreCRM.Platform' 'WorkspaceDbContext'
    $appliedChain = Invoke-SqlScalar "SELECT MigrationId FROM workspace.__EFMigrationsHistory WHERE MigrationId LIKE '%InitialWorkspaceProvisioning%' ORDER BY MigrationId;"
    Assert-True ($appliedChain -like "*$provisioningMigration*") 'D: fresh chain applied the provisioning migration'
    Assert-True ($appliedChain -like "*$recoveryMigration*") 'D: fresh chain applied the recovery migration'
    Assert-True ($appliedChain -like "*$correctionMigration*") 'D: fresh chain applied the correction migration'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('workspace.InitialProvisioningRecords') AND name IN ('State','CompletedAt');") -eq 2) 'D: fresh chain produced the current anchor schema'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.InitialProvisioningRecords;') -eq 0) 'D: fresh chain leaves no anchors'

    # ---------------------------------------------------------------- A / B / C. correction
    # Build the database at the schema state that already applied the faulty recovery migration.
    New-Database $correctionDatabase
    Update-OwnerDatabases
    Update-Database 'src/UnicoreCRM.Platform' 'WorkspaceDbContext' $recoveryMigration
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.__EFMigrationsHistory WHERE MigrationId LIKE '%$correctionMigration';") -eq '0') 'Correction: database starts before the corrective migration'

    $emailA = 'upgrade.legacy.assigned@example.test'
    $emailB = 'upgrade.legacy.unassigned@example.test'
    $emailC = 'upgrade.genuine.completed@example.test'
    $emailF = 'upgrade.unexpected.drift@example.test'
    $emailG = 'upgrade.arbitrary.partial@example.test'
    $emailH = 'upgrade.identity.drift@example.test'
    $emailI = 'upgrade.concurrent.retry@example.test'
    $emailJ = 'upgrade.completed.precontacts@example.test'
    New-Accounts @($emailA, $emailB, $emailC, $emailF, $emailG, $emailH, $emailI, $emailJ)
    $checks.Add('Correction: previous-version accounts created=PASS')

    # A and B carry the legacy fabricated signature; C carries a real completion time.
    $stateA = New-SeededAnchor $emailA 'legacy-assigned' $true 0 $preContactsCapabilities
    $stateB = New-SeededAnchor $emailB 'legacy-unassigned' $false 0
    $stateC = New-SeededAnchor $emailC 'genuine-completed' $true 5
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateA.WorkspaceId)';") -eq 1) 'A: legacy anchor has an existing access assignment'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq 0) 'B: legacy anchor has no access assignment'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE State='Completed' AND CompletedAt=ProvisionedAt;") -eq 2) 'Correction: exactly the two legacy anchors carry the fabricated signature'

    Update-Database 'src/UnicoreCRM.Platform' 'WorkspaceDbContext'
    $checks.Add('Correction: corrective migration applied=PASS')
    foreach ($pair in @(@{ Label = 'A'; State = $stateA }, @{ Label = 'B'; State = $stateB })) {
        $accountId = $pair.State.AccountId
        Assert-True ((Get-AnchorState $accountId) -eq 'AccessPending') "$($pair.Label): legacy anchor corrected to AccessPending"
        Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountId' AND CompletedAt IS NULL;") -eq '1') "$($pair.Label): corrected anchor has no completion time"
    }
    Assert-True ((Get-AnchorState $stateC.AccountId) -eq 'Completed') 'C: genuinely completed anchor was not reset'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($stateC.AccountId)' AND CompletedAt IS NOT NULL;") -eq '1') 'C: genuine completion time was preserved'
    $completedAtC = Invoke-SqlScalar "SELECT CONVERT(varchar(33), CompletedAt, 126) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($stateC.AccountId)';"

    # Runtime-negative controls use the current anchor schema directly. Each has a real creator
    # assignment, but only A carries the one exact historical capability snapshot admitted for
    # upgrade and the exact canonical role metadata.
    $unexpectedCapabilities = @($preContactsCapabilities + 'contacts.create' | Sort-Object)
    $stateF = New-SeededAnchor $emailF 'unexpected-drift' $true 5 $unexpectedCapabilities
    $partialCapabilities = @($preContactsCapabilities | Where-Object { $_ -ne 'tasks.read' })
    $stateG = New-SeededAnchor $emailG 'arbitrary-partial' $true 5 $partialCapabilities
    $stateH = New-SeededAnchor $emailH 'identity-drift' $true 5 $preContactsCapabilities 'Custom role with a misleading canonical name.'
    $stateJ = New-SeededAnchor $emailJ 'completed-precontacts' $true 5 $preContactsCapabilities
    $completedAtJ = Invoke-SqlScalar "SELECT CONVERT(varchar(33), CompletedAt, 126) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($stateJ.AccountId)';"
    $roleStateJ = Invoke-SqlScalar "SELECT CONCAT(Name,'|',NormalizedName,'|',Description,'|',COALESCE(SourceTemplateId,''),'|',IsActive,'|',[Version],'|',CONVERT(varchar(33),UpdatedAt,126)) FROM access.Roles WHERE RoleId='$($stateJ.RoleId)';"
    $capabilityStateJ = Invoke-SqlScalar "SELECT STRING_AGG(CONVERT(nvarchar(max),Capability),',') WITHIN GROUP (ORDER BY Capability) FROM access.RoleCapabilities WHERE RoleId='$($stateJ.RoleId)';"
    $stateI = New-SeededAnchor $emailI 'concurrent-retry' $true 5 $preContactsCapabilities
    Invoke-Sql "UPDATE workspace.InitialProvisioningRecords SET State='AccessPending',CompletedAt=NULL WHERE AccountId='$($stateI.AccountId)';"
    foreach ($state in @($stateF, $stateG, $stateH)) {
        Invoke-Sql "UPDATE workspace.InitialProvisioningRecords SET State='AccessPending',CompletedAt=NULL WHERE AccountId='$($state.AccountId)';"
    }

    $customRoleId = 'role_custom_' + [Guid]::NewGuid().ToString('N')
    $customAssignmentId = 'assignment_custom_' + [Guid]::NewGuid().ToString('N')
    Invoke-Sql "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$customRoleId','$($stateA.WorkspaceId)','Custom Observer','CUSTOM OBSERVER','Verifier-owned unrelated custom role.',NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME()); INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$customRoleId','contacts.create'); INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES ('$customAssignmentId','$($stateA.WorkspaceId)','$($stateA.MembershipId)','$customRoleId',SYSUTCDATETIME());"

    # The current host with durable recovery enabled must converge only outstanding anchors without
    # client action. Completed anchors C and J must remain entirely outside the recovery scan.
    $hostProcess = Start-ApiHost $emailA $true $baseUrl $false
    $hostProcessSecondary = Start-ApiHost $emailA $true 'http://127.0.0.1:5091' $false
    Wait-ApiHost $hostProcess $baseUrl
    Wait-ApiHost $hostProcessSecondary 'http://127.0.0.1:5091'
    Assert-True (Wait-AnchorState $stateA.AccountId 'Completed') 'A: durable resume completed the corrected anchor'
    Assert-True (Wait-AnchorState $stateB.AccountId 'Completed') 'B: durable resume completed the corrected anchor'
    Assert-True (Wait-AnchorState $stateI.AccountId 'Completed') 'I: concurrent resume workers converged the pre-Contacts role'
    Assert-True ((Invoke-SqlScalar "SELECT CONVERT(varchar(33), CompletedAt, 126) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($stateC.AccountId)';") -eq $completedAtC) 'C: genuine anchor was never replayed'
    Assert-True ((Get-AnchorState $stateJ.AccountId) -eq 'Completed') 'J: completed provisioning state remains completed'
    Assert-True ((Invoke-SqlScalar "SELECT CONVERT(varchar(33), CompletedAt, 126) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($stateJ.AccountId)';") -eq $completedAtJ) 'J: startup recovery does not rewrite completion evidence'
    Assert-HistoricalAccessUnchanged $stateJ $roleStateJ $capabilityStateJ 'J:'
    Start-Sleep -Seconds 3
    Assert-True ((Get-AnchorState $stateF.AccountId) -eq 'AccessPending') 'F: unexpected extra capability drift fails closed'
    Assert-True ((Get-AnchorState $stateG.AccountId) -eq 'AccessPending') 'G: arbitrary partial capability set is not reclassified as server-owned'
    Assert-True ((Get-AnchorState $stateH.AccountId) -eq 'AccessPending') 'H: canonical-name role with identity drift is not upgraded'

    Assert-RuntimeUsable $stateA 'A:'
    Assert-RuntimeUsable $stateB 'B:'
    Assert-RuntimeUsable $stateC 'C:'
    Assert-RuntimeUsable $stateI 'I:'
    Assert-SingleState $stateA 'A:'
    Assert-SingleState $stateB 'B:'
    Assert-SingleState $stateC 'C:'
    Assert-SingleState $stateI 'I:'
    Assert-True ((Invoke-SqlScalar "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($stateA.WorkspaceId)' AND Name='Workspace Owner';") -eq $stateA.RoleId) 'A: the pre-existing role identity was preserved'
    Assert-True ((Invoke-SqlScalar "SELECT a.AssignmentId FROM access.MembershipRoleAssignments a JOIN access.Roles r ON r.RoleId=a.RoleId WHERE a.WorkspaceId='$($stateA.WorkspaceId)' AND r.Name='Workspace Owner';") -eq $stateA.AssignmentId) 'A: the pre-existing assignment identity was preserved'
    Assert-True ((Invoke-SqlScalar "SELECT MembershipId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq $stateB.MembershipId) 'B: the created assignment targets the creator membership'
    Assert-True ((Invoke-SqlScalar "SELECT Name FROM access.Roles WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq 'Workspace Owner') 'B: the created role is the server-owned initial role'
    Assert-True ((Invoke-SqlScalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateC.WorkspaceId)';") -eq $stateC.AssignmentId) 'C: the untouched assignment identity was preserved'
    Assert-True ((Invoke-SqlScalar "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($stateI.WorkspaceId)' AND Name='Workspace Owner';") -eq $stateI.RoleId) 'I: concurrent convergence preserved the role identity'
    Assert-True ((Invoke-SqlScalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateI.WorkspaceId)';") -eq $stateI.AssignmentId) 'I: concurrent convergence preserved the assignment identity'
    Assert-True ((Invoke-SqlScalar "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($stateJ.WorkspaceId)' AND Name='Workspace Owner';") -eq $stateJ.RoleId) 'J: completed-anchor role identity is unchanged'
    Assert-True ((Invoke-SqlScalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateJ.WorkspaceId)';") -eq $stateJ.AssignmentId) 'J: completed-anchor assignment identity is unchanged'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$customRoleId' AND Capability='contacts.create';") -eq '1') 'A: unrelated custom role capability set is unchanged'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$customRoleId';") -eq '1') 'A: unrelated custom role received no canonical capabilities'
    Assert-True ((Invoke-SqlScalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE RoleId='$customRoleId';") -eq $customAssignmentId) 'A: unrelated custom assignment identity is unchanged'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$($stateF.RoleId)' AND Capability='contacts.create';") -eq '1') 'F: unexpected extra capability is not silently deleted or reclassified'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$($stateF.RoleId)' AND Capability='contacts.read';") -eq '0') 'F: drifted role receives no contacts.read grant'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$($stateG.RoleId)' AND Capability='contacts.read';") -eq '0') 'G: arbitrary partial role receives no contacts.read grant'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$($stateH.RoleId)' AND Capability='contacts.read';") -eq '0') 'H: identity-drifted role receives no contacts.read grant'

    Start-Sleep -Seconds 5
    Assert-SingleState $stateA 'A (after another resume window):'
    Assert-SingleState $stateB 'B (after another resume window):'
    Assert-SingleState $stateC 'C (after another resume window):'
    Assert-SingleState $stateI 'I (after another resume window):'
    Assert-HistoricalAccessUnchanged $stateJ $roleStateJ $capabilityStateJ 'J (after another resume window):'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE State<>'Completed';") -eq 3) 'Correction: only the three deliberately invalid upgrade fixtures remain outstanding'
    Stop-ApiHost $hostProcess
    $hostProcess = $null
    Stop-ApiHost $hostProcessSecondary
    $hostProcessSecondary = $null

    # ---------------------------------------------------------------- E. never applied the faulty migration
    New-Database $chainDatabase
    Update-OwnerDatabases
    Update-Database 'src/UnicoreCRM.Platform' 'WorkspaceDbContext' $provisioningMigration
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('workspace.InitialProvisioningRecords') AND name IN ('State','CompletedAt');") -eq 0) 'E: database starts before the recovery migration'
    $emailE = 'upgrade.prerecovery@example.test'
    New-Accounts @($emailE)
    $accountE = Invoke-SqlScalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($emailE.ToUpperInvariant())';"
    $memberE = Invoke-SqlScalar "SELECT MemberId FROM iam.Accounts WHERE NormalizedEmail='$($emailE.ToUpperInvariant())';"
    $suffixE = [Guid]::NewGuid().ToString('N')
    $workspaceE = "ws_$suffixE"
    $membershipE = "wsm_$suffixE"
    $nowE = (Get-Date).ToUniversalTime().AddMinutes(-10).ToString('yyyy-MM-ddTHH:mm:ss.fffffff+00:00')
    Invoke-Sql "INSERT INTO workspace.Workspaces (WorkspaceId,[Key],Name,LogoText,CreatedAt) VALUES ('$workspaceE','upgrade-prerecovery-$($suffixE.Substring(0,8))','Upgrade Pre Recovery Workspace','UP','$nowE');"
    Invoke-Sql "INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('$membershipE','$workspaceE','$accountE','$memberE','Active','$nowE');"
    Invoke-Sql "INSERT INTO workspace.BootstrapProjections (WorkspaceId,ContextVersion,ConfigurationVersion,Locale,TimeZone,BaseCurrency,CapabilitiesJson,EnabledModuleKeysJson,AvailableProductSpacesJson) VALUES ('$workspaceE',0,0,'en','UTC','USD','[]','[""leads"",""deals"",""tasks""]','[""crm""]');"
    Invoke-Sql "INSERT INTO workspace.InitialProvisioningRecords (AccountId,MemberId,WorkspaceId,MembershipId,IdempotencyKey,RequestFingerprint,ProvisionedAt) VALUES ('$accountE','$memberE','$workspaceE','$membershipE','idem-prerecovery','$(('0' * 64))','$nowE');"

    Update-Database 'src/UnicoreCRM.Platform' 'WorkspaceDbContext'
    Assert-True ((Get-AnchorState $accountE) -eq 'AccessPending') 'E: the whole chain leaves the pre-recovery anchor as outstanding work'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountE' AND CompletedAt IS NULL;") -eq '1') 'E: the pre-recovery anchor has no completion time'
    $stateE = [pscustomobject] @{ Email = $emailE; AccountId = $accountE; WorkspaceId = $workspaceE; MembershipId = $membershipE }
    $hostProcess = Start-ApiHost $emailE $true
    Assert-True (Wait-AnchorState $accountE 'Completed') 'E: durable resume completed the pre-recovery anchor'
    Assert-RuntimeUsable $stateE 'E:'
    Assert-SingleState $stateE 'E:'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    [pscustomobject] @{
        Status = 'PASS'
        CorrectionDatabase = $correctionDatabase
        FreshDatabase = $freshDatabase
        ChainDatabase = $chainDatabase
        LegacyAssignedWorkspace = $stateA.WorkspaceId
        LegacyUnassignedWorkspace = $stateB.WorkspaceId
        GenuinelyCompletedWorkspace = $stateC.WorkspaceId
        ConcurrentUpgradeWorkspace = $stateI.WorkspaceId
        CompletedPreContactsWorkspace = $stateJ.WorkspaceId
        PreRecoveryWorkspace = $workspaceE
        Checks = $checks
    } | ConvertTo-Json -Depth 6
}
finally {
    Stop-ApiHost $hostProcess
    Stop-ApiHost $hostProcessSecondary
    $client.Dispose()
}
