param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName
)

# Proves the Initial Workspace Provisioning upgrade path against a real previous-schema database:
# anchors written by the version before InitialWorkspaceProvisioningRecovery are migrated as
# outstanding work and are completed by the convergent durable resume path, whether or not the
# AccessControl assignment already existed.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$baseUrl = 'http://127.0.0.1:5090'
$password = 'Initial-Provisioning-Upgrade!234'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$accountAssigned = 'upgrade.assigned@example.test'
$accountUnassigned = 'upgrade.unassigned@example.test'
$previousMigration = 'InitialWorkspaceProvisioning'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-provisioning-upgrade-' + [Guid]::NewGuid().ToString('N')))
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()

# The frozen server-owned initial capability set. Case A seeds exactly this set, so the convergent
# AccessControl participant recognises the existing role instead of failing closed.
$initialCapabilities = @(
    'deals.assign', 'deals.bulk', 'deals.close', 'deals.create', 'deals.delete', 'deals.read', 'deals.update',
    'leads.create', 'leads.qualify', 'leads.read', 'leads.update',
    'tasks.assign', 'tasks.complete', 'tasks.create', 'tasks.read', 'tasks.update',
    'workspace.context.resolve'
)

function Invoke-SqlScalar([string] $query) {
    $value = & sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Invoke-Sql([string] $query) {
    # Routed through a script file so JSON literals containing double quotes survive intact.
    $scriptPath = Join-Path $temporaryDirectory ('seed-' + [Guid]::NewGuid().ToString('N') + '.sql')
    Set-Content -LiteralPath $scriptPath -Value ("SET NOCOUNT ON;`r`n" + $query) -Encoding ascii
    & sqlcmd -S $server -d $DatabaseName -b -i $scriptPath | Out-Null
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

function Initialize-PreviousSchema {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated upgrade database.' }
    $env:ConnectionStrings__UnicoreCRM = $connection
    # Every owner is current except Workspace, which stops at the migration immediately before
    # InitialWorkspaceProvisioningRecovery. That is the real previous schema state.
    Update-Database 'src/UnicoreCRM.Platform' 'IdentityAuthDbContext'
    Update-Database 'src/UnicoreCRM.Platform' 'AccessControlDbContext'
    Update-Database 'src/UnicoreCRM.Operations' 'TasksDbContext'
    Update-Database 'src/UnicoreCRM.Crm' 'LeadsDbContext'
    Update-Database 'src/UnicoreCRM.Crm' 'DealsDbContext'
    Update-Database 'src/UnicoreCRM.Integrations' 'IntegrationsDbContext'
    Update-Database 'src/UnicoreCRM.PlatformOperations' 'InboxDbContext'
    Update-Database 'src/UnicoreCRM.Platform' 'WorkspaceDbContext' $previousMigration
    $columns = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('workspace.InitialProvisioningRecords') AND name IN ('State','CompletedAt');")
    Assert-True ($columns -eq 0) 'Upgrade: database starts at the previous Workspace schema'
}

function Set-HostEnvironment([string] $identityEmail, [bool] $resumeEnabled) {
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

function Get-AccountId([string] $email) {
    return Invoke-SqlScalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())';"
}

function Get-MemberId([string] $email) {
    return Invoke-SqlScalar "SELECT MemberId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())';"
}

function New-PreviousVersionState([string] $email, [string] $keySuffix, [bool] $seedAccessAssignment) {
    # Realistic state written by the previous version: Workspace, ACTIVE creator membership,
    # configuration seed and provisioning anchor, all at the previous schema.
    $accountId = Get-AccountId $email
    $memberId = Get-MemberId $email
    $suffix = [Guid]::NewGuid().ToString('N')
    $workspaceId = "ws_$suffix"
    $membershipId = "wsm_$suffix"
    $workspaceKey = "upgrade-$keySuffix-$($suffix.Substring(0, 8))"
    $now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffffff+00:00')
    $capabilitiesJson = '[]'
    $modulesJson = '["leads","deals","tasks"]'
    $spacesJson = '["crm"]'
    Invoke-Sql "INSERT INTO workspace.Workspaces (WorkspaceId,[Key],Name,LogoText,CreatedAt) VALUES ('$workspaceId','$workspaceKey','Upgrade $keySuffix Workspace','UP','$now');"
    Invoke-Sql "INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('$membershipId','$workspaceId','$accountId','$memberId','Active','$now');"
    Invoke-Sql "INSERT INTO workspace.BootstrapProjections (WorkspaceId,ContextVersion,ConfigurationVersion,Locale,TimeZone,BaseCurrency,CapabilitiesJson,EnabledModuleKeysJson,AvailableProductSpacesJson) VALUES ('$workspaceId',0,0,'en','UTC','USD','$capabilitiesJson','$modulesJson','$spacesJson');"
    Invoke-Sql "INSERT INTO workspace.InitialProvisioningRecords (AccountId,MemberId,WorkspaceId,MembershipId,IdempotencyKey,RequestFingerprint,ProvisionedAt) VALUES ('$accountId','$memberId','$workspaceId','$membershipId','idem-previous-version-$keySuffix','$(('0' * 64))','$now');"

    $roleId = $null
    $assignmentId = $null
    if ($seedAccessAssignment) {
        $roleId = "role_$suffix"
        $assignmentId = "assignment_$suffix"
        Invoke-Sql "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$roleId','$workspaceId','Workspace Owner','Initial Workspace provisioning role for the account that created this Workspace.',NULL,1,0,'$now','$now');"
        foreach ($capability in $initialCapabilities) {
            Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','$capability');"
        }
        Invoke-Sql "INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES ('$assignmentId','$workspaceId','$membershipId','$roleId','$now');"
    }

    return [pscustomobject] @{
        Email = $email
        AccountId = $accountId
        MemberId = $memberId
        WorkspaceId = $workspaceId
        WorkspaceKey = $workspaceKey
        MembershipId = $membershipId
        RoleId = $roleId
        AssignmentId = $assignmentId
    }
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
    Initialize-PreviousSchema

    # The previous version's accounts have to exist before the recovery migration runs. The
    # Workspace Development bootstrap and the resume pass stay disabled so nothing touches the
    # previous-schema Workspace tables through the current model.
    foreach ($email in @($accountAssigned, $accountUnassigned)) {
        $hostProcess = Start-ApiHost $email $false
        Stop-ApiHost $hostProcess
        $hostProcess = $null
    }
    $checks.Add('Upgrade: previous-version accounts created=PASS')

    # A. anchor plus an existing AccessControl role and assignment.
    # B. anchor with no AccessControl assignment at all.
    $stateA = New-PreviousVersionState $accountAssigned 'assigned' $true
    $stateB = New-PreviousVersionState $accountUnassigned 'unassigned' $false
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateA.WorkspaceId)';") -eq 1) 'A: previous version left an access assignment'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq 0) 'B: previous version left no access assignment'

    # Apply only the recovery migration.
    Update-Database 'src/UnicoreCRM.Platform' 'WorkspaceDbContext'
    $checks.Add('Upgrade: recovery migration applied=PASS')
    foreach ($pair in @(@{ Label = 'A'; State = $stateA }, @{ Label = 'B'; State = $stateB })) {
        $accountId = $pair.State.AccountId
        Assert-True ((Invoke-SqlScalar "SELECT State FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountId';") -eq 'AccessPending') "$($pair.Label): migrated anchor is AccessPending"
        Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE AccountId='$accountId' AND CompletedAt IS NULL;") -eq '1') "$($pair.Label): migrated anchor has no completion time"
    }

    # Start the current host with the resume pass enabled. Convergence must need no client action.
    $hostProcess = Start-ApiHost $accountAssigned $true
    Assert-True (Wait-ProvisioningState $stateA.AccountId 'Completed') 'A: durable resume completed the migrated anchor'
    Assert-True (Wait-ProvisioningState $stateB.AccountId 'Completed') 'B: durable resume completed the migrated anchor'

    Assert-RuntimeUsable $stateA 'A:'
    Assert-RuntimeUsable $stateB 'B:'
    Assert-SingleState $stateA 'A:'
    Assert-SingleState $stateB 'B:'

    # A: the existing role and assignment must be reused, not replaced or duplicated.
    Assert-True ((Invoke-SqlScalar "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($stateA.WorkspaceId)';") -eq $stateA.RoleId) 'A: the pre-existing role identity was preserved'
    Assert-True ((Invoke-SqlScalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateA.WorkspaceId)';") -eq $stateA.AssignmentId) 'A: the pre-existing assignment identity was preserved'

    # B: the missing assignment must have been created exactly once.
    Assert-True ((Invoke-SqlScalar "SELECT MembershipId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq $stateB.MembershipId) 'B: the created assignment targets the creator membership'
    Assert-True ((Invoke-SqlScalar "SELECT Name FROM access.Roles WHERE WorkspaceId='$($stateB.WorkspaceId)';") -eq 'Workspace Owner') 'B: the created role is the server-owned initial role'

    # A second resume window must not add anything.
    Start-Sleep -Seconds 5
    Assert-SingleState $stateA 'A (after another resume window):'
    Assert-SingleState $stateB 'B (after another resume window):'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.InitialProvisioningRecords WHERE State<>'Completed';") -eq 0) 'Upgrade: no outstanding provisioning remains'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM workspace.Workspaces;') -eq 2) 'Upgrade: exactly the two migrated Workspaces exist'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    [pscustomobject] @{
        Status = 'PASS'
        Database = $DatabaseName
        PreviousMigration = $previousMigration
        AssignedWorkspace = $stateA.WorkspaceId
        UnassignedWorkspace = $stateB.WorkspaceId
        Checks = $checks
    } | ConvertTo-Json -Depth 6
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
}
