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

# The frozen server-owned initial capability set. Seeded roles use exactly this set so the
# convergent AccessControl participant recognises them instead of failing closed.
$initialCapabilities = @(
    'deals.assign', 'deals.bulk', 'deals.close', 'deals.create', 'deals.delete', 'deals.read', 'deals.update',
    'leads.create', 'leads.qualify', 'leads.read', 'leads.update',
    'tasks.assign', 'tasks.complete', 'tasks.create', 'tasks.read', 'tasks.update',
    'workspace.context.resolve'
)

$ownerContexts = @(
    @{ Project = 'src/UnicoreCRM.Platform'; Context = 'IdentityAuthDbContext' },
    @{ Project = 'src/UnicoreCRM.Platform'; Context = 'AccessControlDbContext' },
    @{ Project = 'src/UnicoreCRM.Operations'; Context = 'TasksDbContext' },
    @{ Project = 'src/UnicoreCRM.Crm'; Context = 'LeadsDbContext' },
    @{ Project = 'src/UnicoreCRM.Crm'; Context = 'DealsDbContext' },
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

function Set-HostEnvironment([string] $identityEmail, [bool] $resumeEnabled) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
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
    $env:Integrations__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = $resumeEnabled.ToString().ToLowerInvariant()
    $env:Workflows__InitialWorkspaceProvisioning__ResumeIntervalSeconds = '2'
    $env:Workflows__InitialWorkspaceProvisioning__DevelopmentFaultInjection__FailAccessAssignment = 'false'
}

function Start-ApiHost([string] $identityEmail, [bool] $resumeEnabled) {
    Set-HostEnvironment $identityEmail $resumeEnabled
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
    throw 'ApiHost did not listen within the upgrade timeout.'
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
        $process = Start-ApiHost $email $false
        Stop-ApiHost $process
    }
}

function New-SeededAnchor([string] $email, [string] $keySuffix, [bool] $seedAccessAssignment, [int] $completionOffsetSeconds) {
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
        Invoke-Sql "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$roleId','$workspaceId','Workspace Owner','Initial Workspace provisioning role for the account that created this Workspace.',NULL,1,0,'$provisionedAtText','$provisionedAtText');"
        foreach ($capability in $initialCapabilities) {
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
}

function Assert-SingleState($state, [string] $label) {
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Workspaces WHERE WorkspaceId='$($state.WorkspaceId)';") -eq 1) "$label exactly one Workspace"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.Memberships WHERE AccountId='$($state.AccountId)';") -eq 1) "$label exactly one Membership"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.BootstrapProjections WHERE WorkspaceId='$($state.WorkspaceId)';") -eq 1) "$label exactly one configuration seed"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.Roles WHERE WorkspaceId='$($state.WorkspaceId)';") -eq 1) "$label exactly one role"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($state.WorkspaceId)';") -eq 1) "$label exactly one access assignment"
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($state.AccountId)';") -eq 1) "$label exactly one provisioning anchor"
    $capabilityCount = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities c JOIN access.Roles r ON r.RoleId=c.RoleId WHERE r.WorkspaceId='$($state.WorkspaceId)';")
    Assert-True ($capabilityCount -eq $initialCapabilities.Count) "$label role carries the server-owned capability set"
}

$hostProcess = $null
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
    New-Accounts @($emailA, $emailB, $emailC)
    $checks.Add('Correction: previous-version accounts created=PASS')

    # A and B carry the legacy fabricated signature; C carries a real completion time.
    $stateA = New-SeededAnchor $emailA 'legacy-assigned' $true 0
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

    # The current host with the durable resume path enabled must converge A and B without any
    # client action and must not touch C.
    $hostProcess = Start-ApiHost $emailA $true
    Assert-True (Wait-AnchorState $stateA.AccountId 'Completed') 'A: durable resume completed the corrected anchor'
    Assert-True (Wait-AnchorState $stateB.AccountId 'Completed') 'B: durable resume completed the corrected anchor'
    Assert-True ((Invoke-SqlScalar "SELECT CONVERT(varchar(33), CompletedAt, 126) FROM workspace.InitialProvisioningRecords WHERE AccountId='$($stateC.AccountId)';") -eq $completedAtC) 'C: genuine anchor was never replayed'

    Assert-RuntimeUsable $stateA 'A:'
    Assert-RuntimeUsable $stateB 'B:'
    Assert-RuntimeUsable $stateC 'C:'
    Assert-SingleState $stateA 'A:'
    Assert-SingleState $stateB 'B:'
    Assert-SingleState $stateC 'C:'
    Assert-True ((Invoke-SqlScalar "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($stateA.WorkspaceId)';") -eq $stateA.RoleId) 'A: the pre-existing role identity was preserved'
    Assert-True ((Invoke-SqlScalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateA.WorkspaceId)';") -eq $stateA.AssignmentId) 'A: the pre-existing assignment identity was preserved'
    Assert-True ((Invoke-SqlScalar "SELECT MembershipId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq $stateB.MembershipId) 'B: the created assignment targets the creator membership'
    Assert-True ((Invoke-SqlScalar "SELECT Name FROM access.Roles WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq 'Workspace Owner') 'B: the created role is the server-owned initial role'
    Assert-True ((Invoke-SqlScalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateC.WorkspaceId)';") -eq $stateC.AssignmentId) 'C: the untouched assignment identity was preserved'

    Start-Sleep -Seconds 5
    Assert-SingleState $stateA 'A (after another resume window):'
    Assert-SingleState $stateB 'B (after another resume window):'
    Assert-SingleState $stateC 'C (after another resume window):'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE State<>'Completed';") -eq 0) 'Correction: no outstanding provisioning remains'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

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
        PreRecoveryWorkspace = $workspaceE
        Checks = $checks
    } | ConvertTo-Json -Depth 6
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
}
