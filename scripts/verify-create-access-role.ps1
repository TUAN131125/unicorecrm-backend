<#
.SYNOPSIS
    Verifies the frozen createAccessRole owner-local command against an isolated SQL database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5341,
    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$script:Passed = 0
$script:Failed = 0
$script:Results = [System.Collections.Generic.List[string]]::new()
$script:Counter = 0
$script:BaseUrl = "http://127.0.0.1:$Port"
$script:Token = $null
$script:WorkspaceId = $null
$script:HostProcess = $null
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost')).Path
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$password = 'Create-Access-Role-Verify!2026'
$email = 'admin@unicorecrm.local'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-create-access-role-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $logRoot

function Add-Result([string] $Name, [object] $Expected, [object] $Actual) {
    $expectedText = [string] $Expected
    $actualText = [string] $Actual
    if ($expectedText -ceq $actualText) {
        $script:Passed++
        $script:Results.Add("PASS | $Name | $actualText")
    }
    else {
        $script:Failed++
        $script:Results.Add("FAIL | $Name | expected=$expectedText actual=$actualText")
    }
}

function Assert-True([string] $Name, [bool] $Condition) {
    Add-Result $Name 'True' $Condition.ToString()
}

function New-Connection([string] $Database = $DatabaseName) {
    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=$Database;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True")
    $connection.Open()
    return $connection
}

function Invoke-Sql([string] $Query, [string] $Database = $DatabaseName) {
    $connection = New-Connection $Database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        $reader = $command.ExecuteReader()
        $rows = [System.Collections.Generic.List[object]]::new()
        while ($reader.Read()) {
            $row = @{}
            for ($index = 0; $index -lt $reader.FieldCount; $index++) {
                $name = $reader.GetName($index)
                if ([string]::IsNullOrWhiteSpace($name)) { $name = "Column$index" }
                $row[$name] = $reader.GetValue($index)
            }
            $rows.Add([pscustomobject] $row)
        }
        $reader.Close()
        return $rows
    }
    finally { $connection.Dispose() }
}

function Invoke-SqlNonQuery([string] $Query, [string] $Database = $DatabaseName) {
    $connection = New-Connection $Database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        $null = $command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Get-Scalar([string] $Query) {
    $rows = @(Invoke-Sql $Query)
    if ($rows.Count -eq 0) { return $null }
    $property = $rows[0].PSObject.Properties | Select-Object -First 1
    return $property.Value
}

function New-Client([int] $TimeoutSeconds = 60) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    return $client
}

function Invoke-Api(
    [string] $Method,
    [string] $Path,
    [string] $Body,
    [string] $Token,
    [string] $WorkspaceId,
    [string] $IdempotencyKey,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    $script:Counter++
    if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-create-role-' + $script:Counter.ToString('d6') }
    if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-create-role-' + $script:Counter.ToString('d6') }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId) }
    if ($CorrelationId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId) }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    # Windows PowerShell binds $null to a [string] parameter as the empty string, so a
    # body-less GET would still be given a content body and rejected with "Cannot send a
    # content-body with this verb-type" before it ever reached the host.
    if (-not [string]::IsNullOrEmpty($Body)) { $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json') }
    $client = New-Client
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            try { $payload = $raw | ConvertFrom-Json } catch { }
        }
        return [pscustomobject] @{ Status = [int] $response.StatusCode; Raw = $raw; Body = $payload }
    }
    finally { $request.Dispose(); $client.Dispose() }
}

function New-Body(
    [string] $Name,
    [string[]] $Capabilities = @('tasks.read'),
    [object[]] $DataScopes = @(),
    [object[]] $FieldSecurity = @(),
    [string] $Description = $null,
    [string] $SourceTemplateId = $null
) {
    $body = [ordered]@{ name = $Name; capabilities = $Capabilities; dataScopes = $DataScopes; fieldSecurity = $FieldSecurity }
    if ($null -ne $Description) { $body.description = $Description }
    if ($null -ne $SourceTemplateId) { $body.sourceTemplateId = $SourceTemplateId }
    return $body | ConvertTo-Json -Compress -Depth 12
}

function Invoke-Create([string] $Body, [string] $Key, [string] $Workspace = $script:WorkspaceId, [string] $Token = $script:Token) {
    return Invoke-Api 'POST' '/access/roles' $Body $Token $Workspace $Key
}

function Get-EffectSnapshot {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.Roles'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleCapabilities'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleDataScopes'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleFieldSecurity'),
        (Get-Scalar 'SELECT COALESCE(SUM(Revision),0) FROM access.WorkspaceDirectoryRevisions'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.AccessRoleCommandIdempotencyRecords'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.OutboxEvents')
    ))
}

function Assert-Rejected([string] $Name, [string] $Body, [int] $Status, [string] $Code) {
    $response = Invoke-Create $Body ('idem-' + [Guid]::NewGuid().ToString('N'))
    Add-Result "$Name status" $Status $response.Status
    Add-Result "$Name code" $Code $response.Body.code
}

function Test-AtomicFailure([string] $Name, [string] $Table, [string] $Action, [string] $Body) {
    $trigger = 'TR_VerifyCreateRole_' + ($Name -replace '[^A-Za-z0-9]', '')
    Invoke-SqlNonQuery "CREATE TRIGGER [access].[$trigger] ON $Table INSTEAD OF $Action AS THROW 51000, 'forced createAccessRole persistence failure', 1;"
    try {
        $before = Get-EffectSnapshot
        $response = Invoke-Create $Body ('idem-failure-' + [Guid]::NewGuid().ToString('N'))
        Add-Result "transaction $Name returns no success" '500' $response.Status
        Add-Result "transaction $Name full rollback" $before (Get-EffectSnapshot)
    }
    finally { Invoke-SqlNonQuery "DROP TRIGGER [access].[$trigger];" }
}

try {
    Invoke-SqlNonQuery "IF DB_ID('$DatabaseName') IS NOT NULL THROW 50001, 'Verification database already exists.', 1; CREATE DATABASE [$DatabaseName];" 'master'

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = $connectionString
    $env:UNICORE_DEV_SEED_ENABLED = 'true'
    $env:UNICORE_DEV_SEED_EMAIL = $email
    $env:UNICORE_DEV_SEED_PASSWORD = $password
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'access.configure'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $stdout = Join-Path $logRoot 'host.out.log'
    $stderr = Join-Path $logRoot 'host.err.log'
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Api 'GET' '/auth/session' $null $null $null $null
            if ($probe.Status -eq 401) { $ready = $true; break }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }

    $signIn = Invoke-Api 'POST' '/auth/sessions' (@{ email = $email; password = $password } | ConvertTo-Json -Compress) $null $null 'idem-create-role-signin-0001'
    Add-Result 'authentication fixture sign-in' 200 $signIn.Status
    $script:Token = $signIn.Body.accessToken
    $script:WorkspaceId = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo'")
    $foreignWorkspace = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo-isolated'")
    $accountId = [string] (Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())'")
    $membershipId = [string] (Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId='$script:WorkspaceId' AND AccountId='$accountId'")
    $memberId = [string] (Get-Scalar "SELECT MemberId FROM workspace.Memberships WHERE MembershipId='$membershipId'")
    $adminRoleId = [string] (Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.MembershipId='$membershipId' AND c.Capability='access.configure'")
    $adminRoleName = [string] (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$adminRoleId'")
    Assert-True 'trusted fixture exists' (-not [string]::IsNullOrWhiteSpace($script:WorkspaceId) -and -not [string]::IsNullOrWhiteSpace($adminRoleId))

    $minimalBody = New-Body 'Minimal Role'
    Add-Result 'unauthenticated rejected' 401 (Invoke-Create $minimalBody 'idem-unauthenticated-0001' $script:WorkspaceId $null).Status
    Add-Result 'unknown Workspace rejected' 403 (Invoke-Create $minimalBody 'idem-unknown-workspace-0001' 'ws_does_not_exist').Status
    Add-Result 'foreign Workspace rejected' 403 (Invoke-Create $minimalBody 'idem-foreign-workspace-0001' $foreignWorkspace).Status

    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='$membershipId';"
    Add-Result 'suspended membership rejected' 403 (Invoke-Create $minimalBody 'idem-suspended-0001').Status
    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='$membershipId';"

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE RoleId='$adminRoleId' AND Capability='access.configure';"
    $deniedMalformed = Invoke-Create '{"name":""}' 'idem-capability-first-0001'
    Add-Result 'access.configure required' 403 $deniedMalformed.Status
    Add-Result 'capability denial precedes validation' 'ACCESS_DENIED' $deniedMalformed.Body.code
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('$adminRoleId','access.configure');"

    $happyBody = New-Body "  $([char]0x2003)Custom Managers$([char]0x2003)  " @('tasks.read','access.read') @(
        @{ resourceKey = ' Contacts '; scope = 'WORKSPACE' },
        @{ resourceKey = 'leads'; scope = 'CUSTOM'; allowedOwnerIds = @() },
        @{ resourceKey = 'deals'; scope = 'CUSTOM'; allowedOwnerIds = @('mem_owner_002','mem_owner_001') }
    ) @(
        @{ resourceKey = ' Contacts '; fieldKey = ' WorkEmail '; access = 'READ_ONLY' },
        @{ resourceKey = 'leads'; fieldKey = 'email'; access = 'HIDDEN' }
    ) '  Custom command role  ' '  opaque-template-01  '
    $happyKey = 'idem-create-role-happy-0001'
    $happyRequestId = 'req-create-role-happy-0001'
    $happyCorrelationId = 'corr-create-role-happy-0001'
    $revisionBefore = [long] (Get-Scalar "SELECT Revision FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId='$script:WorkspaceId'")
    $assignmentsBefore = [long] (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments')
    $happy = Invoke-Api 'POST' '/access/roles' $happyBody $script:Token $script:WorkspaceId $happyKey $happyRequestId $happyCorrelationId
    Add-Result 'valid create status' 200 $happy.Status
    if ($happy.Status -ne 200) { throw "Happy-path create failed: HTTP $($happy.Status) $($happy.Raw)" }
    $roleId = [string] $happy.Body.aggregateId
    Add-Result 'aggregate type' 'ACCESS_ROLE' $happy.Body.aggregateType
    Add-Result 'initial version' 0 $happy.Body.version
    Add-Result 'fresh outcome' 'COMMITTED' $happy.Body.outcome
    Assert-True 'role ID format' ($roleId -cmatch '^role_[0-9a-f]{32}$')
    Assert-True 'command ID format' ([string] $happy.Body.commandId -cmatch '^command_[0-9a-f]{32}$')
    Assert-True 'audit ID format' ([string] $happy.Body.auditEvidenceIds[0] -cmatch '^audit_[0-9a-f]{32}$')
    Assert-True 'event ID format' ([string] $happy.Body.emittedEventIds[0] -cmatch '^event_[0-9a-f]{32}$')
    Add-Result 'role display name trimmed' 'Custom Managers' (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$roleId'")
    Add-Result 'normalized name persisted' 'CUSTOM MANAGERS' (Get-Scalar "SELECT NormalizedName FROM access.Roles WHERE RoleId='$roleId'")
    Add-Result 'description normalized' 'Custom command role' (Get-Scalar "SELECT Description FROM access.Roles WHERE RoleId='$roleId'")
    Add-Result 'opaque template provenance normalized' 'opaque-template-01' (Get-Scalar "SELECT SourceTemplateId FROM access.Roles WHERE RoleId='$roleId'")
    Add-Result 'role active' 'True' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$roleId'")).ToString()
    Add-Result 'role version persisted' 0 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$roleId'")
    Add-Result 'exact capabilities persisted' 'access.read,tasks.read' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$roleId' ORDER BY Capability").Capability -join ',')
    Add-Result 'data scopes count' 3 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleDataScopes WHERE RoleId='$roleId'")
    Add-Result 'field security count' 2 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleFieldSecurity WHERE RoleId='$roleId'")
    Assert-True 'data-scope policy ID formats' (@(Invoke-Sql "SELECT PolicyId FROM access.RoleDataScopes WHERE RoleId='$roleId'" | Where-Object { $_.PolicyId -cnotmatch '^scope_[0-9a-f]{32}$' }).Count -eq 0)
    Assert-True 'field-security policy ID formats' (@(Invoke-Sql "SELECT PolicyId FROM access.RoleFieldSecurity WHERE RoleId='$roleId'" | Where-Object { $_.PolicyId -cnotmatch '^field_[0-9a-f]{32}$' }).Count -eq 0)
    Add-Result 'resource normalization' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleDataScopes WHERE RoleId='$roleId' AND ResourceKey='contacts'")
    Add-Result 'field normalization' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleFieldSecurity WHERE RoleId='$roleId' AND ResourceKey='contacts' AND FieldKey='workemail'")
    Add-Result 'CUSTOM empty is deny-all representation' '[]' (Get-Scalar "SELECT AllowedOwnerIdsJson FROM access.RoleDataScopes WHERE RoleId='$roleId' AND ResourceKey='leads'")
    Add-Result 'CUSTOM owners canonical order' '["mem_owner_001","mem_owner_002"]' (Get-Scalar "SELECT AllowedOwnerIdsJson FROM access.RoleDataScopes WHERE RoleId='$roleId' AND ResourceKey='deals'")
    Add-Result 'no assignment created' $assignmentsBefore (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments')
    Add-Result 'directory revision increments once' ($revisionBefore + 1) (Get-Scalar "SELECT Revision FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId='$script:WorkspaceId'")

    $requiredTop = @('commandId','correlationId','aggregateId','aggregateType','version','occurredAt','outcome','result')
    $requiredDirectory = @('workspaceId','revision','generatedAt','members','memberProfiles','invitations','roles','assignments','dataScopes','fieldSecurity')
    Add-Result 'response required top-level shape' 0 (@($requiredTop | Where-Object { $null -eq $happy.Body.PSObject.Properties[$_] }).Count)
    Add-Result 'response exact directory required shape' 0 (@($requiredDirectory | Where-Object { $null -eq $happy.Body.result.PSObject.Properties[$_] }).Count)
    Add-Result 'directory trusted Workspace' $script:WorkspaceId $happy.Body.result.workspaceId
    Add-Result 'directory revision response' ($revisionBefore + 1) $happy.Body.result.revision
    Assert-True 'generatedAt UTC' ($happy.Raw -match '"generatedAt":"[^"]+Z"')
    Add-Result 'confirmed invitation absence' 0 @($happy.Body.result.invitations).Count
    Assert-True 'created role in full directory' (@($happy.Body.result.roles | Where-Object { $_.roleId -eq $roleId }).Count -eq 1)
    $callerProfile = @($happy.Body.result.memberProfiles | Where-Object { $_.membershipId -eq $membershipId })[0]
    Assert-True 'Workspace provider supplied member facts' ($null -ne $callerProfile -and $happy.Body.result.members[0].workspaceKey.Length -gt 0)
    Add-Result 'Identity provider supplied account facts' $email $callerProfile.email
    Add-Result 'roleLabel derives the sole assigned active role' $adminRoleName $callerProfile.roleLabel
    Assert-True 'optional null fields omitted' ($happy.Raw -notmatch '"acceptedAt":null|"revokedAt":null|"allowedOwnerIds":null')

    $audit = @(Invoke-Sql "SELECT * FROM access.GovernanceCommandAudits WHERE RoleId='$roleId'")[0]
    Add-Result 'exactly one governance audit' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits WHERE RoleId='$roleId'")
    Add-Result 'audit discriminator' 'ACCESS_GOVERNANCE_COMMAND' $audit.EvidenceType
    Add-Result 'audit operation' 'createAccessRole' $audit.OperationId
    Add-Result 'audit trusted account' $accountId $audit.ActorAccountId
    Add-Result 'audit trusted membership' $membershipId $audit.ActorMembershipId
    Add-Result 'audit trusted member' $memberId $audit.ActorMemberId
    Add-Result 'audit request provenance' $happyRequestId $audit.RequestId
    Add-Result 'audit correlation provenance' $happyCorrelationId $audit.CorrelationId
    Add-Result 'audit outcome' 'COMMITTED' $audit.Outcome
    Add-Result 'audit version' 0 $audit.ResultingVersion
    Add-Result 'audit timestamp shared with response' ([DateTimeOffset] $happy.Body.occurredAt) ([DateTimeOffset] $audit.OccurredAt)
    Assert-True 'audit occurredAt UTC offset zero' ([DateTimeOffset] $audit.OccurredAt).Offset.Equals([TimeSpan]::Zero)
    $auditColumns = ((Invoke-Sql "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='access' AND TABLE_NAME='GovernanceCommandAudits' ORDER BY ORDINAL_POSITION").COLUMN_NAME -join ',')
    Assert-True 'audit excludes business arrays and directory' ($auditColumns -notmatch 'Capability|DataScope|FieldSecurity|Directory|Name|Description|Template')

    $event = @(Invoke-Sql "SELECT * FROM access.OutboxEvents WHERE AggregateId='$roleId'")[0]
    Add-Result 'exactly one role-created event' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE AggregateId='$roleId'")
    Add-Result 'event type' 'ACCESS_ROLE_CREATED' $event.EventType
    Add-Result 'event aggregate type' 'ACCESS_ROLE' $event.AggregateType
    Add-Result 'event aggregate version' 0 $event.AggregateVersion
    Add-Result 'event causation command' $happy.Body.commandId $event.CausationId
    Add-Result 'event correlation provenance' $happyCorrelationId $event.CorrelationId
    Add-Result 'event timestamp shared with response' ([DateTimeOffset] $happy.Body.occurredAt) ([DateTimeOffset] $event.OccurredAt)
    Add-Result 'event minimal payload' ("{`"roleId`":`"$roleId`",`"version`":0}") $event.PayloadJson

    Assert-Rejected 'blocked capability' (New-Body 'Blocked Capability' @('contacts.create')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'authority-gap capability' (New-Body 'Authority Gap Capability' @('identity.account.recover')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'reconciliation-required capability' (New-Body 'Reconciliation Capability' @('studio.configure')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'no-operation-authority capability' (New-Body 'No Operation Capability' @('dashboard.read')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'unknown capability' (New-Body 'Unknown Capability' @('unknown.capability')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'wrong-case capability' (New-Body 'Wrong Case Capability' @('Tasks.Read')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'duplicate capability' (New-Body 'Duplicate Capability' @('tasks.read','tasks.read')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'non-Workspace capability' (New-Body 'Non Workspace Capability' @('identity.account.register')) 422 'VALIDATION_FAILED'
    Assert-Rejected 'over 500 capabilities' (New-Body 'Too Many Capabilities' (@('tasks.read') * 501)) 422 'VALIDATION_FAILED'
    Assert-Rejected 'empty trimmed name' (New-Body " $([char]0x2003) ") 422 'VALIDATION_FAILED'
    Assert-Rejected 'over 160 Unicode scalars' (New-Body ([string]::Concat((1..161 | ForEach-Object { [char]::ConvertFromUtf32(0x1F600) })))) 422 'VALIDATION_FAILED'
    Assert-Rejected 'duplicate normalized data scope' (New-Body 'Duplicate Scope' @('tasks.read') @(@{resourceKey='Contacts';scope='OWN'},@{resourceKey=' contacts ';scope='TEAM'})) 422 'VALIDATION_FAILED'
    Assert-Rejected 'duplicate normalized field security' (New-Body 'Duplicate Field' @('tasks.read') @() @(@{resourceKey='Contacts';fieldKey='Email';access='READ_ONLY'},@{resourceKey=' contacts ';fieldKey=' email ';access='HIDDEN'})) 422 'VALIDATION_FAILED'
    Assert-Rejected 'non-CUSTOM owners invalid' (New-Body 'Invalid Owners' @('tasks.read') @(@{resourceKey='contacts';scope='OWN';allowedOwnerIds=@('mem_owner_001')})) 422 'VALIDATION_FAILED'
    $missingKey = Invoke-Create (New-Body 'Missing Key') $null
    Add-Result 'missing idempotency key rejected' 422 $missingKey.Status

    $nameConflict = Invoke-Create (New-Body ' custom managers ') 'idem-role-name-conflict-0001'
    Add-Result 'case-insensitive name conflict status' 409 $nameConflict.Status
    Add-Result 'case-insensitive name conflict code' 'ROLE_NAME_CONFLICT' $nameConflict.Body.code

    $beforeReplay = Get-EffectSnapshot
    $replay = Invoke-Create $happyBody $happyKey
    Add-Result 'same-key replay status' 200 $replay.Status
    Add-Result 'replay outcome' 'REPLAYED' $replay.Body.outcome
    Add-Result 'replay command identity' $happy.Body.commandId $replay.Body.commandId
    Add-Result 'replay role identity' $happy.Body.aggregateId $replay.Body.aggregateId
    Add-Result 'replay audit identity' $happy.Body.auditEvidenceIds[0] $replay.Body.auditEvidenceIds[0]
    Add-Result 'replay event identity' $happy.Body.emittedEventIds[0] $replay.Body.emittedEventIds[0]
    Add-Result 'replay creates no effects' $beforeReplay (Get-EffectSnapshot)
    $changedReplay = Invoke-Create (New-Body 'Changed Request') $happyKey
    Add-Result 'changed request idempotency conflict status' 409 $changedReplay.Status
    Add-Result 'changed request idempotency conflict code' 'IDEMPOTENCY_KEY_REUSED' $changedReplay.Body.code

    $concurrentBody = New-Body 'Concurrent Convergence Role' @('tasks.read','access.read')
    $concurrentKey = 'idem-create-role-concurrent-0001'
    $clientA = New-Client 120
    $clientB = New-Client 120
    function New-ConcurrentRequest([int] $Suffix) {
        $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/access/roles")
        $message.Content = [System.Net.Http.StringContent]::new($concurrentBody, [Text.Encoding]::UTF8, 'application/json')
        $null = $message.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $message.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $message.Headers.TryAddWithoutValidation('X-Request-Id', "req-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('X-Correlation-Id', "corr-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('Idempotency-Key', $concurrentKey)
        return $message
    }
    $messageA = New-ConcurrentRequest 1
    $messageB = New-ConcurrentRequest 2
    try {
        $taskA = $clientA.SendAsync($messageA)
        $taskB = $clientB.SendAsync($messageB)
        [Threading.Tasks.Task]::WaitAll(@($taskA,$taskB))
        $responseA = $taskA.Result
        $responseB = $taskB.Result
        $rawA = $responseA.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $rawB = $responseB.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Add-Result 'concurrent response A' 200 ([int] $responseA.StatusCode)
        Add-Result 'concurrent response B' 200 ([int] $responseB.StatusCode)
        Add-Result 'concurrent requests converge role' (($rawA | ConvertFrom-Json).aggregateId) (($rawB | ConvertFrom-Json).aggregateId)
        Add-Result 'concurrent one durable role' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.Roles WHERE NormalizedName='CONCURRENT CONVERGENCE ROLE'")
    }
    finally { $messageA.Dispose(); $messageB.Dispose(); $clientA.Dispose(); $clientB.Dispose() }

    $failureBody = New-Body 'Atomic Failure Role' @('tasks.read') @(@{resourceKey='tasks';scope='WORKSPACE'}) @(@{resourceKey='tasks';fieldKey='description';access='READ_ONLY'})
    Test-AtomicFailure 'role' 'access.Roles' 'INSERT' $failureBody
    Test-AtomicFailure 'policy' 'access.RoleDataScopes' 'INSERT' $failureBody
    Test-AtomicFailure 'audit' 'access.GovernanceCommandAudits' 'INSERT' $failureBody
    Test-AtomicFailure 'outbox' 'access.OutboxEvents' 'INSERT' $failureBody
    Test-AtomicFailure 'idempotency completion' 'access.AccessRoleCommandIdempotencyRecords' 'INSERT' $failureBody
    Test-AtomicFailure 'revision' 'access.WorkspaceDirectoryRevisions' 'UPDATE' $failureBody

    $providerBody = New-Body 'Provider Recovery Role' @('tasks.read')
    $providerKey = 'idem-provider-recovery-0001'
    $logo = [string] (Get-Scalar "SELECT LogoText FROM workspace.Workspaces WHERE WorkspaceId='$script:WorkspaceId'")
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
    $providerFailure = Invoke-Create $providerBody $providerKey
    Add-Result 'post-commit invalid provider returns 503' 503 $providerFailure.Status
    Add-Result 'post-commit provider error code' 'INTEGRATION_UNAVAILABLE' $providerFailure.Body.code
    $providerRecord = @(Invoke-Sql "SELECT * FROM access.AccessRoleCommandIdempotencyRecords WHERE IdempotencyKey='$providerKey'")[0]
    Assert-True 'provider failure role remains committed' ([string] $providerRecord.RoleId -cmatch '^role_[0-9a-f]{32}$')
    Add-Result 'provider failure audit remains committed' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits WHERE CommandId='$($providerRecord.CommandId)'")
    Add-Result 'provider failure event remains committed' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE CausationId='$($providerRecord.CommandId)'")
    $providerEffects = Get-EffectSnapshot
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"
    $providerReplay = Invoke-Create $providerBody $providerKey
    Add-Result 'provider recovery replay status' 200 $providerReplay.Status
    Add-Result 'provider recovery replay outcome' 'REPLAYED' $providerReplay.Body.outcome
    Add-Result 'provider recovery preserves command' $providerRecord.CommandId $providerReplay.Body.commandId
    Add-Result 'provider recovery preserves role' $providerRecord.RoleId $providerReplay.Body.aggregateId
    Add-Result 'provider recovery preserves audit' $providerRecord.AuditEvidenceId $providerReplay.Body.auditEvidenceIds[0]
    Add-Result 'provider recovery preserves event' $providerRecord.EventId $providerReplay.Body.emittedEventIds[0]
    Add-Result 'provider recovery creates no effects' $providerEffects (Get-EffectSnapshot)

    $accessFiles = Get-ChildItem (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl') -Recurse -Filter '*.cs'
    $accessText = ($accessFiles | Get-Content -Raw) -join "`n"
    Assert-True 'no foreign DbContext in AccessControl' ($accessText -notmatch 'WorkspaceDbContext|IdentityAuthDbContext')
    Assert-True 'no foreign SQL join in command' ($accessText -notmatch '\[workspace\]|\[iam\]|(?i)\b(?:FROM|JOIN)\s+(?:workspace|iam)\.')
    Add-Result 'exactly one create route mapping' 1 ([regex]::Matches($accessText, 'MapPost\("/access/roles"').Count)
    Add-Result 'no member-role assignment created by command' $assignmentsBefore (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments')
}
finally {
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit(10000) | Out-Null
    }
    if (-not $KeepDatabase) {
        try {
            Invoke-SqlNonQuery "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;" 'master'
        }
        catch { Write-Warning "Could not remove isolated database ${DatabaseName}: $($_.Exception.Message)" }
    }
}

$script:Results | ForEach-Object { Write-Host $_ }
Write-Host "RESULT | passed=$script:Passed failed=$script:Failed"
if ($script:Failed -ne 0) { exit 1 }
