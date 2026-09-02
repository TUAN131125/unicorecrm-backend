<#
.SYNOPSIS
    Verifies the frozen archiveAccessRole owner-local command (DEC-ARCHIVEACCESSROLE-AUTHORITY-CLOSURE)
    against an isolated SQL database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5351,
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
$password = 'Archive-Access-Role-Verify!2026'
$email = 'admin@unicorecrm.local'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-archive-access-role-' + [Guid]::NewGuid().ToString('N'))
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
    [string] $IfMatch = $null,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    $script:Counter++
    if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-archive-role-' + $script:Counter.ToString('d6') }
    if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-archive-role-' + $script:Counter.ToString('d6') }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId) }
    if ($CorrelationId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId) }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    if (-not [string]::IsNullOrWhiteSpace($IfMatch)) { $null = $request.Headers.TryAddWithoutValidation('If-Match', $IfMatch) }
    # Windows PowerShell binds $null to a [string] parameter as the empty string, so a body-less GET
    # would still be given a content body and rejected before it ever reached the host.
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

function New-ArchiveBody([object] $Reason = $null) {
    if ($null -eq $Reason) { return '{}' }
    return (@{ reason = $Reason } | ConvertTo-Json -Compress)
}

function Invoke-Archive(
    [string] $RoleId,
    [string] $Body,
    [string] $Key,
    [string] $IfMatch,
    [string] $Workspace = $script:WorkspaceId,
    [string] $Token = $script:Token,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    return Invoke-Api 'POST' "/access/roles/$RoleId/archive" $Body $Token $Workspace $Key $IfMatch $RequestId $CorrelationId
}

function New-Role([string] $Name, [string[]] $Capabilities = @('tasks.read'), [object[]] $DataScopes = @(), [object[]] $FieldSecurity = @()) {
    $body = [ordered]@{ name = $Name; capabilities = $Capabilities; dataScopes = $DataScopes; fieldSecurity = $FieldSecurity } | ConvertTo-Json -Compress -Depth 12
    $response = Invoke-Api 'POST' '/access/roles' $body $script:Token $script:WorkspaceId ('idem-seed-' + [Guid]::NewGuid().ToString('N'))
    if ($response.Status -ne 200) { throw "Could not seed role '$Name': HTTP $($response.Status) $($response.Raw)" }
    return [string] $response.Body.aggregateId
}

function Get-EffectSnapshot {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.Roles'),
        (Get-Scalar 'SELECT COALESCE(SUM(Version),0) FROM access.Roles'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.Roles WHERE IsActive=1'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleCapabilities'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleDataScopes'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleFieldSecurity'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments'),
        (Get-Scalar 'SELECT COALESCE(SUM(Revision),0) FROM access.WorkspaceDirectoryRevisions'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.AccessRoleCommandIdempotencyRecords'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.OutboxEvents')
    ))
}

function Get-Revision {
    return [long] (Get-Scalar "SELECT Revision FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId='$script:WorkspaceId'")
}

function Assert-NoEffect([string] $Name, [scriptblock] $Action, [int] $Status, [string] $Code) {
    $before = Get-EffectSnapshot
    $response = & $Action
    Add-Result "$Name status" $Status $response.Status
    Add-Result "$Name code" $Code $response.Body.code
    Add-Result "$Name zero mutation" $before (Get-EffectSnapshot)
    return $response
}

function Test-AtomicFailure([string] $Name, [string] $Table, [string] $Action, [string] $RoleId, [string] $IfMatch) {
    $trigger = 'TR_VerifyArchiveRole_' + ($Name -replace '[^A-Za-z0-9]', '')
    Invoke-SqlNonQuery "CREATE TRIGGER [access].[$trigger] ON $Table INSTEAD OF $Action AS THROW 51000, 'forced archiveAccessRole persistence failure', 1;"
    try {
        $before = Get-EffectSnapshot
        $response = Invoke-Archive $RoleId (New-ArchiveBody 'atomic failure probe') ('idem-failure-' + [Guid]::NewGuid().ToString('N')) $IfMatch
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
    $env:Workflows__InitialWorkspaceProvisioning__ResumeIntervalSeconds = '2'
    $stdout = Join-Path $logRoot 'host.out.log'
    $stderr = Join-Path $logRoot 'host.err.log'
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 600; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Api 'GET' '/auth/session' $null $null $null $null
            if ($probe.Status -eq 401) { $ready = $true; break }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }

    $signIn = Invoke-Api 'POST' '/auth/sessions' (@{ email = $email; password = $password } | ConvertTo-Json -Compress) $null $null 'idem-archive-role-signin-1'
    Add-Result 'authentication fixture sign-in' 200 $signIn.Status
    $script:Token = $signIn.Body.accessToken
    $script:WorkspaceId = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo'")
    $foreignWorkspace = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo-isolated'")
    $accountId = [string] (Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())'")
    $membershipId = [string] (Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId='$script:WorkspaceId' AND AccountId='$accountId'")
    $memberId = [string] (Get-Scalar "SELECT MemberId FROM workspace.Memberships WHERE MembershipId='$membershipId'")
    $adminRoleId = [string] (Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.MembershipId='$membershipId' AND c.Capability='access.configure'")
    Assert-True 'trusted fixture exists' (-not [string]::IsNullOrWhiteSpace($script:WorkspaceId) -and -not [string]::IsNullOrWhiteSpace($adminRoleId))

    # ---------------------------------------------------------------- AUTHORIZATION / PRECEDENCE
    $authTarget = New-Role 'Archive Authorization Target'
    $authBody = New-ArchiveBody
    Add-Result 'unauthenticated rejected' 401 (Invoke-Archive $authTarget $authBody 'idem-unauth-000001' '"0"' $script:WorkspaceId $null).Status
    Add-Result 'unknown Workspace rejected' 403 (Invoke-Archive $authTarget $authBody 'idem-unknownws-0001' '"0"' 'ws_does_not_exist').Status
    Add-Result 'foreign Workspace rejected' 403 (Invoke-Archive $authTarget $authBody 'idem-foreignws-0001' '"0"' $foreignWorkspace).Status

    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='$membershipId';"
    Add-Result 'suspended membership rejected' 403 (Invoke-Archive $authTarget $authBody 'idem-suspended-0001' '"0"').Status
    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='$membershipId';"

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE RoleId='$adminRoleId' AND Capability='access.configure';"
    $denied = Invoke-Archive 'role_00000000000000000000000000000000' '{"reason":123}' 'idem-capfirst-000001' 'garbage'
    Add-Result 'access.configure required' 403 $denied.Status
    Add-Result 'capability denial precedes metadata, body and target checks' 'ACCESS_DENIED' $denied.Body.code
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('$adminRoleId','access.configure');"

    # ---------------------------------------------------------------- IF-MATCH SYNTAX
    foreach ($case in @(
        @{ Name = 'missing If-Match'; Value = $null },
        @{ Name = 'unquoted If-Match'; Value = '0' },
        @{ Name = 'weak If-Match'; Value = 'W/"0"' },
        @{ Name = 'wildcard If-Match'; Value = '*' },
        @{ Name = 'negative If-Match'; Value = '"-1"' },
        @{ Name = 'non-decimal If-Match'; Value = '"abc"' },
        @{ Name = 'empty If-Match'; Value = '""' },
        @{ Name = 'multi-value If-Match'; Value = '"0", "1"' }
    )) {
        $rejected = Invoke-Archive $authTarget $authBody ('idem-ifmatch-' + [Guid]::NewGuid().ToString('N')) $case.Value
        Add-Result "$($case.Name) status" 422 $rejected.Status
        Assert-True "$($case.Name) field error" ($null -ne $rejected.Body.fieldErrors.'If-Match')
    }
    # A malformed If-Match must be refused before the target is ever read.
    $unknownWithBadIfMatch = Invoke-Archive 'role_00000000000000000000000000000000' $authBody 'idem-ifmatchorder-01' 'nonsense'
    Add-Result 'malformed If-Match precedes target lookup' 422 $unknownWithBadIfMatch.Status

    # ---------------------------------------------------------------- TARGET
    $unknown = Assert-NoEffect 'unknown role' { Invoke-Archive 'role_00000000000000000000000000000000' $authBody 'idem-unknownrole-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    $foreignRoleId = 'role_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$foreignRoleId','$foreignWorkspace','Foreign Archive Target','FOREIGN ARCHIVE TARGET',NULL,NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME());"
    $foreign = Assert-NoEffect 'foreign Workspace role' { Invoke-Archive $foreignRoleId $authBody 'idem-foreignrole-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    Add-Result 'foreign role indistinguishable from unknown role' $unknown.Body.code $foreign.Body.code

    # ---------------------------------------------------------------- REASON
    $reasonTarget = New-Role 'Archive Reason Target'
    $overLimit = Assert-NoEffect 'reason over 500 Unicode scalars' { Invoke-Archive $reasonTarget (New-ArchiveBody ([string]::Concat((1..501 | ForEach-Object { 'a' })))) 'idem-reasonlong-0001' '"0"' } 422 'VALIDATION_FAILED'
    Assert-True 'reason over-limit field error' ($null -ne $overLimit.Body.fieldErrors.reason)
    # 500 supplementary scalars occupy 1000 UTF-16 code units, which is the storage width the
    # governance-audit column is sized for.
    $supplementary = [string]::Concat((1..500 | ForEach-Object { [char]::ConvertFromUtf32(0x1F600) }))
    $supplementaryTarget = New-Role 'Archive Supplementary Reason Target'
    $supplementaryArchive = Invoke-Archive $supplementaryTarget (New-ArchiveBody $supplementary) 'idem-reasonsupp-0001' '"0"'
    Add-Result 'exactly 500 supplementary scalars accepted' 200 $supplementaryArchive.Status
    Add-Result 'supplementary reason round-trips through SQL' $supplementary (Get-Scalar "SELECT Reason FROM access.GovernanceCommandAudits WHERE CommandId='$($supplementaryArchive.Body.commandId)'")
    $overSupplementary = Assert-NoEffect 'reason 501 supplementary scalars rejected' { Invoke-Archive $reasonTarget (New-ArchiveBody ($supplementary + [char]::ConvertFromUtf32(0x1F600))) 'idem-reasonsupp2-001' '"0"' } 422 'VALIDATION_FAILED'

    $blankReasonTarget = New-Role 'Archive Blank Reason Target'
    $blankArchive = Invoke-Archive $blankReasonTarget (New-ArchiveBody "  $([char]0x2003)  ") 'idem-reasonblank-001' '"0"'
    Add-Result 'whitespace-only reason accepted' 200 $blankArchive.Status
    Add-Result 'whitespace-only reason stored as null' 'True' ([string]::IsNullOrEmpty([string] (Get-Scalar "SELECT ISNULL(Reason,'') FROM access.GovernanceCommandAudits WHERE CommandId='$($blankArchive.Body.commandId)'"))).ToString()

    $omittedReasonTarget = New-Role 'Archive Omitted Reason Target'
    $omittedArchive = Invoke-Archive $omittedReasonTarget '{}' 'idem-reasonomit-0001' '"0"'
    Add-Result 'omitted reason accepted' 200 $omittedArchive.Status
    Add-Result 'omitted reason stored as null' 'True' ([string]::IsNullOrEmpty([string] (Get-Scalar "SELECT ISNULL(Reason,'') FROM access.GovernanceCommandAudits WHERE CommandId='$($omittedArchive.Body.commandId)'"))).ToString()

    # ---------------------------------------------------------------- HAPPY PATH / LIFECYCLE
    $target = New-Role 'Archive Happy Target' @('tasks.read','tasks.create') @(
        @{ resourceKey = 'contacts'; scope = 'OWN' }
    ) @(
        @{ resourceKey = 'contacts'; fieldKey = 'email'; access = 'READ_ONLY' }
    )
    Invoke-SqlNonQuery "UPDATE access.Roles SET Description='keep me', SourceTemplateId='keep-template' WHERE RoleId='$target';"
    $assignmentId = 'assignment_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES('$assignmentId','$script:WorkspaceId','$membershipId','$target',SYSUTCDATETIME());"
    $createdAtBefore = [string] (Get-Scalar "SELECT CONVERT(varchar(33), CreatedAt, 127) FROM access.Roles WHERE RoleId='$target'")
    $scopePolicyId = [string] (Get-Scalar "SELECT PolicyId FROM access.RoleDataScopes WHERE RoleId='$target'")
    $fieldPolicyId = [string] (Get-Scalar "SELECT PolicyId FROM access.RoleFieldSecurity WHERE RoleId='$target'")
    $revisionBefore = Get-Revision
    $assignmentsBefore = [long] (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments')

    $happyKey = 'idem-archive-happy-0001'
    $happyRequestId = 'req-archive-happy-0001'
    $happyCorrelationId = 'corr-archive-happy-0001'
    $happyReason = '  Superseded by the consolidated managers role.  '
    $happy = Invoke-Archive $target (New-ArchiveBody $happyReason) $happyKey '"0"' $script:WorkspaceId $script:Token $happyRequestId $happyCorrelationId
    Add-Result 'valid archive status' 200 $happy.Status
    if ($happy.Status -ne 200) { throw "Happy-path archive failed: HTTP $($happy.Status) $($happy.Raw)" }

    Add-Result 'aggregate id matches target' $target $happy.Body.aggregateId
    Add-Result 'aggregate type' 'ACCESS_ROLE' $happy.Body.aggregateType
    Add-Result 'resulting version is prior + 1' 1 $happy.Body.version
    Add-Result 'fresh outcome' 'COMMITTED' $happy.Body.outcome
    Assert-True 'command ID format' ([string] $happy.Body.commandId -cmatch '^command_[0-9a-f]{32}$')
    Assert-True 'audit ID format' ([string] $happy.Body.auditEvidenceIds[0] -cmatch '^audit_[0-9a-f]{32}$')
    Assert-True 'event ID format' ([string] $happy.Body.emittedEventIds[0] -cmatch '^event_[0-9a-f]{32}$')

    Add-Result 'role row still exists' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.Roles WHERE RoleId='$target'")
    Add-Result 'role deactivated' 'False' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$target'")).ToString()
    Add-Result 'role version persisted' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$target'")
    Add-Result 'createdAt unchanged' $createdAtBefore (Get-Scalar "SELECT CONVERT(varchar(33), CreatedAt, 127) FROM access.Roles WHERE RoleId='$target'")
    Add-Result 'name preserved' 'Archive Happy Target' (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$target'")
    Add-Result 'normalized name preserved' 'ARCHIVE HAPPY TARGET' (Get-Scalar "SELECT NormalizedName FROM access.Roles WHERE RoleId='$target'")
    Add-Result 'description preserved' 'keep me' (Get-Scalar "SELECT Description FROM access.Roles WHERE RoleId='$target'")
    Add-Result 'sourceTemplateId preserved' 'keep-template' (Get-Scalar "SELECT SourceTemplateId FROM access.Roles WHERE RoleId='$target'")
    Add-Result 'capabilities preserved' 'tasks.create,tasks.read' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$target' ORDER BY Capability").Capability -join ',')
    Add-Result 'data-scope policy preserved with identity' $scopePolicyId (Get-Scalar "SELECT PolicyId FROM access.RoleDataScopes WHERE RoleId='$target'")
    Add-Result 'field-security policy preserved with identity' $fieldPolicyId (Get-Scalar "SELECT PolicyId FROM access.RoleFieldSecurity WHERE RoleId='$target'")
    Add-Result 'assignment preserved with identity' $assignmentId (Get-Scalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE RoleId='$target'")
    Add-Result 'no assignment count change' $assignmentsBefore (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments')
    Add-Result 'directory revision increments once' ($revisionBefore + 1) (Get-Revision)

    # Archiving must not free the role name.
    $nameStillReserved = Invoke-Api 'POST' '/access/roles' (@{ name = ' archive happy target '; capabilities = @('tasks.read'); dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress) $script:Token $script:WorkspaceId 'idem-namereserved-001'
    Add-Result 'archived role still reserves its normalized name' 409 $nameStillReserved.Status
    Add-Result 'archived name conflict code' 'ROLE_NAME_CONFLICT' $nameStillReserved.Body.code

    $alreadyInactive = Assert-NoEffect 'already inactive target' { Invoke-Archive $target (New-ArchiveBody 'second attempt') 'idem-alreadyinactive-1' '"1"' } 409 'ROLE_INACTIVE'

    # ---------------------------------------------------------------- DIRECTORY
    $archivedRoleDocument = @($happy.Body.result.roles | Where-Object { $_.roleId -eq $target })[0]
    Assert-True 'archived role remains in the directory' ($null -ne $archivedRoleDocument)
    Add-Result 'directory reports archived role inactive' 'False' ([bool] $archivedRoleDocument.isActive).ToString()
    Add-Result 'directory preserves archived role capabilities' 'tasks.create,tasks.read' ($archivedRoleDocument.capabilities -join ',')
    Add-Result 'directory reports resulting version' 1 $archivedRoleDocument.version
    Add-Result 'directory revision in response' ($revisionBefore + 1) $happy.Body.result.revision
    Assert-True 'directory retains the assignment' (@($happy.Body.result.assignments | Where-Object { $_.roleId -eq $target }).Count -eq 1)
    $callerMember = @($happy.Body.result.members | Where-Object { $_.membershipId -eq $membershipId })[0]
    Assert-True 'member roleIds still include the archived role' ($callerMember.roleIds -contains $target)
    $callerProfile = @($happy.Body.result.memberProfiles | Where-Object { $_.membershipId -eq $membershipId })[0]
    Assert-True 'roleLabel excludes the archived role' ($callerProfile.roleLabel -ne 'Archive Happy Target')
    Add-Result 'command writes no directory read evidence' 0 (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.DirectoryReadAccessRecords')

    # ---------------------------------------------------------------- AUDIT / EVENT
    $audit = @(Invoke-Sql "SELECT * FROM access.GovernanceCommandAudits WHERE CommandId='$($happy.Body.commandId)'")[0]
    Add-Result 'exactly one governance audit' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits WHERE CommandId='$($happy.Body.commandId)'")
    Add-Result 'audit discriminator' 'ACCESS_GOVERNANCE_COMMAND' $audit.EvidenceType
    Add-Result 'audit operation' 'archiveAccessRole' $audit.OperationId
    Add-Result 'audit role' $target $audit.RoleId
    Add-Result 'audit trusted account' $accountId $audit.ActorAccountId
    Add-Result 'audit trusted membership' $membershipId $audit.ActorMembershipId
    Add-Result 'audit trusted member' $memberId $audit.ActorMemberId
    Add-Result 'audit request provenance' $happyRequestId $audit.RequestId
    Add-Result 'audit correlation provenance' $happyCorrelationId $audit.CorrelationId
    Add-Result 'audit prior version' 0 $audit.PriorVersion
    Add-Result 'audit resulting version' 1 $audit.ResultingVersion
    Add-Result 'audit reason trimmed' 'Superseded by the consolidated managers role.' $audit.Reason
    Add-Result 'audit outcome' 'COMMITTED' $audit.Outcome
    Add-Result 'audit timestamp shared with response' ([DateTimeOffset] $happy.Body.occurredAt) ([DateTimeOffset] $audit.OccurredAt)
    Assert-True 'audit occurredAt UTC offset zero' ([DateTimeOffset] $audit.OccurredAt).Offset.Equals([TimeSpan]::Zero)
    $auditColumns = ((Invoke-Sql "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='access' AND TABLE_NAME='GovernanceCommandAudits' ORDER BY ORDINAL_POSITION").COLUMN_NAME -join ',')
    Assert-True 'audit excludes business arrays and directory' ($auditColumns -notmatch 'Capability|DataScope|FieldSecurity|Directory|Assignment|Name|Description|Template')

    $event = @(Invoke-Sql "SELECT * FROM access.OutboxEvents WHERE CausationId='$($happy.Body.commandId)'")[0]
    Add-Result 'exactly one archive event' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE CausationId='$($happy.Body.commandId)'")
    Add-Result 'event type' 'ACCESS_ROLE_ARCHIVED' $event.EventType
    Add-Result 'event aggregate id' $target $event.AggregateId
    Add-Result 'event aggregate type' 'ACCESS_ROLE' $event.AggregateType
    Add-Result 'event aggregate version' 1 $event.AggregateVersion
    Add-Result 'event correlation provenance' $happyCorrelationId $event.CorrelationId
    Add-Result 'event timestamp shared with response' ([DateTimeOffset] $happy.Body.occurredAt) ([DateTimeOffset] $event.OccurredAt)
    Add-Result 'event minimal payload without reason' ("{`"roleId`":`"$target`",`"version`":1}") $event.PayloadJson
    Add-Result 'no replaced event emitted for this role' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE AggregateId='$target' AND EventType='ACCESS_ROLE_REPLACED'")
    Add-Result 'exactly one created event for this role' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE AggregateId='$target' AND EventType='ACCESS_ROLE_CREATED'")

    # ---------------------------------------------------------------- IDEMPOTENCY
    $beforeReplay = Get-EffectSnapshot
    $replay = Invoke-Archive $target (New-ArchiveBody $happyReason) $happyKey '"0"'
    Add-Result 'same-key replay status' 200 $replay.Status
    Add-Result 'replay does not become ROLE_INACTIVE' 'REPLAYED' $replay.Body.outcome
    Add-Result 'replay command identity' $happy.Body.commandId $replay.Body.commandId
    Add-Result 'replay role identity' $happy.Body.aggregateId $replay.Body.aggregateId
    Add-Result 'replay version identity' $happy.Body.version $replay.Body.version
    Add-Result 'replay audit identity' $happy.Body.auditEvidenceIds[0] $replay.Body.auditEvidenceIds[0]
    Add-Result 'replay event identity' $happy.Body.emittedEventIds[0] $replay.Body.emittedEventIds[0]
    Add-Result 'replay creates no effects' $beforeReplay (Get-EffectSnapshot)

    $changedReason = Assert-NoEffect 'changed reason under same key' { Invoke-Archive $target (New-ArchiveBody 'a different reason') $happyKey '"0"' } 409 'IDEMPOTENCY_KEY_REUSED'
    Add-Result 'idempotency conflict echoes key' $happyKey $changedReason.Body.idempotencyKey
    $otherTarget = New-Role 'Archive Other Idempotency Target'
    $null = Assert-NoEffect 'same key aimed at another role' { Invoke-Archive $otherTarget (New-ArchiveBody $happyReason) $happyKey '"0"' } 409 'IDEMPOTENCY_KEY_REUSED'
    $sharedKey = 'idem-archive-scope-0001'
    $scopeRole = New-Role 'Archive Scope Independence Target'
    $scopeReplace = Invoke-Api 'PUT' "/access/roles/$scopeRole" (@{ name = 'Archive Scope Independence Target'; isActive = $true; capabilities = @('tasks.read'); dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress) $script:Token $script:WorkspaceId $sharedKey '"0"'
    Add-Result 'replaceAccessRole accepts the shared key' 200 $scopeReplace.Status
    $scopeArchive = Invoke-Archive $scopeRole (New-ArchiveBody 'scope independence') $sharedKey '"1"'
    Add-Result 'archive scope is independent of replace scope' 200 $scopeArchive.Status
    Add-Result 'archive under reused replace key still commits' 'COMMITTED' $scopeArchive.Body.outcome

    # ---------------------------------------------------------------- VERSION / CONCURRENCY
    $staleTarget = New-Role 'Archive Stale Version Target'
    $null = Assert-NoEffect 'stale expected version' { Invoke-Archive $staleTarget $authBody ('idem-stale-' + [Guid]::NewGuid().ToString('N')) '"7"' } 412 'VERSION_CONFLICT'

    $concurrentTarget = New-Role 'Archive Concurrent Target'
    $clientA = New-Client 120
    $clientB = New-Client 120
    function New-ConcurrentArchive([int] $Suffix) {
        $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/access/roles/$concurrentTarget/archive")
        $message.Content = [System.Net.Http.StringContent]::new('{}', [Text.Encoding]::UTF8, 'application/json')
        $null = $message.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $message.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $message.Headers.TryAddWithoutValidation('X-Request-Id', "req-archive-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('X-Correlation-Id', "corr-archive-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('Idempotency-Key', "idem-archive-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('If-Match', '"0"')
        return $message
    }
    $messageA = New-ConcurrentArchive 1
    $messageB = New-ConcurrentArchive 2
    try {
        $taskA = $clientA.SendAsync($messageA)
        $taskB = $clientB.SendAsync($messageB)
        [Threading.Tasks.Task]::WaitAll(@($taskA, $taskB))
        $statuses = @([int] $taskA.Result.StatusCode, [int] $taskB.Result.StatusCode) | Sort-Object
        Add-Result 'concurrent same-version archives resolve to one commit and one conflict' '200,409' ($statuses -join ',')
        Add-Result 'concurrent archive leaves exactly one version step' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$concurrentTarget'")
        Add-Result 'concurrent archive deactivated once' 'False' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$concurrentTarget'")).ToString()
    }
    finally { $messageA.Dispose(); $messageB.Dispose(); $clientA.Dispose(); $clientB.Dispose() }

    $convergeTarget = New-Role 'Archive Same Key Convergence Target'
    $clientC = New-Client 120
    $clientD = New-Client 120
    function New-SameKeyArchive([int] $Suffix) {
        $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/access/roles/$convergeTarget/archive")
        $message.Content = [System.Net.Http.StringContent]::new('{}', [Text.Encoding]::UTF8, 'application/json')
        $null = $message.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $message.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $message.Headers.TryAddWithoutValidation('X-Request-Id', "req-archive-samekey-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('X-Correlation-Id', "corr-archive-samekey-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-archive-samekey-0001')
        $null = $message.Headers.TryAddWithoutValidation('If-Match', '"0"')
        return $message
    }
    $messageC = New-SameKeyArchive 1
    $messageD = New-SameKeyArchive 2
    try {
        $taskC = $clientC.SendAsync($messageC)
        $taskD = $clientD.SendAsync($messageD)
        [Threading.Tasks.Task]::WaitAll(@($taskC, $taskD))
        $rawC = $taskC.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $rawD = $taskD.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Add-Result 'concurrent same-key response C' 200 ([int] $taskC.Result.StatusCode)
        Add-Result 'concurrent same-key response D' 200 ([int] $taskD.Result.StatusCode)
        Add-Result 'concurrent same-key converges on one command' (($rawC | ConvertFrom-Json).commandId) (($rawD | ConvertFrom-Json).commandId)
        Add-Result 'concurrent same-key commits exactly one version step' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$convergeTarget'")
    }
    finally { $messageC.Dispose(); $messageD.Dispose(); $clientC.Dispose(); $clientD.Dispose() }

    # ---------------------------------------------------------------- LAST ADMINISTRATOR
    $adminVersion = [long] (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$adminRoleId'")
    $null = Assert-NoEffect 'archiving the sole active administrator' { Invoke-Archive $adminRoleId (New-ArchiveBody 'should be refused') 'idem-lastadmin-00001' "`"$adminVersion`"" } 409 'LAST_WORKSPACE_ADMINISTRATOR'

    $secondAdminId = New-Role 'Archive Second Administrator' @('access.configure','workspace.context.resolve','tasks.read')
    $secondAdminAssignment = 'assignment_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES('$secondAdminAssignment','$script:WorkspaceId','$membershipId','$secondAdminId',SYSUTCDATETIME());"
    $withPeer = Invoke-Archive $adminRoleId (New-ArchiveBody 'another administrator exists') 'idem-adminpeer-000001' "`"$adminVersion`""
    Add-Result 'archiving an administrator with a peer succeeds' 200 $withPeer.Status
    Add-Result 'peer archive deactivated the former administrator' 'False' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$adminRoleId'")).ToString()

    # A malformed Workspace snapshot must fail the guard closed. The caller now holds
    # access.configure only through the second administrator role, so archiving that role engages
    # the guard and therefore requires the foreign membership facts.
    $malformedAccount = 'acc_malformed_' + [Guid]::NewGuid().ToString('N')
    $malformedMember = 'mem_malformed_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('   ','$script:WorkspaceId','$malformedAccount','$malformedMember','Active',SYSUTCDATETIME());"
    $guardProviderFailure = Assert-NoEffect 'provider failure while the guard is engaged' { Invoke-Archive $secondAdminId (New-ArchiveBody 'guard needs provider facts') 'idem-guardprov-000001' '"0"' } 503 'INTEGRATION_UNAVAILABLE'
    Invoke-SqlNonQuery "DELETE FROM workspace.Memberships WHERE MembershipId='   ';"
    Add-Result 'second administrator survives the guard provider failure' 'True' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$secondAdminId'")).ToString()

    # A non-administrative archive must never depend on the foreign provider before mutating: with
    # the post-commit composer deliberately broken it still commits, and only composition fails.
    $nonAdminTarget = New-Role 'Archive Non Administrator Target'
    $logo = [string] (Get-Scalar "SELECT LogoText FROM workspace.Workspaces WHERE WorkspaceId='$script:WorkspaceId'")
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
    $nonAdminDuringProviderOutage = Invoke-Archive $nonAdminTarget (New-ArchiveBody 'no provider needed') 'idem-nonadminprov-01' '"0"'
    Add-Result 'non-administrative archive reaches post-commit composition' 503 $nonAdminDuringProviderOutage.Status
    Add-Result 'non-administrative archive committed without provider facts' 'False' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$nonAdminTarget'")).ToString()
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"

    # ---------------------------------------------------------------- INACTIVE ROLE AUTHORIZATION
    $authProbeRole = New-Role 'Archive Authorization Probe' @('customers.view') @(
        @{ resourceKey = 'customers'; scope = 'WORKSPACE' }
    ) @(
        @{ resourceKey = 'customers'; fieldKey = 'email'; access = 'MASKED' }
    )
    $authProbeAssignment = 'assignment_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES('$authProbeAssignment','$script:WorkspaceId','$membershipId','$authProbeRole',SYSUTCDATETIME());"
    $contextBefore = Invoke-Api 'GET' '/access/context' $null $script:Token $script:WorkspaceId $null
    Add-Result 'authorization context readable before archive' 200 $contextBefore.Status
    Assert-True 'active role grants its capability' ($contextBefore.Body.capabilities -contains 'customers.view')
    Assert-True 'active role grants its data scope' (@($contextBefore.Body.dataScopes | Where-Object { $_.resourceKey -eq 'customers' }).Count -eq 1)
    Assert-True 'active role grants its field security' (@($contextBefore.Body.fieldSecurity | Where-Object { $_.resourceKey -eq 'customers' -and $_.fieldKey -eq 'email' }).Count -eq 1)
    Assert-True 'active role appears in effective roleIds' ($contextBefore.Body.roleIds -contains $authProbeRole)

    $archiveProbe = Invoke-Archive $authProbeRole (New-ArchiveBody 'revoke the probe authority') 'idem-authprobe-000001' '"0"'
    Add-Result 'authorization probe archived' 200 $archiveProbe.Status
    $contextAfter = Invoke-Api 'GET' '/access/context' $null $script:Token $script:WorkspaceId $null
    Add-Result 'authorization context readable after archive' 200 $contextAfter.Status
    Assert-True 'archived role grants no capability' (-not ($contextAfter.Body.capabilities -contains 'customers.view'))
    Assert-True 'archived role grants no data-scope authority' (@($contextAfter.Body.dataScopes | Where-Object { $_.resourceKey -eq 'customers' }).Count -eq 0)
    Assert-True 'archived role grants no field-security authority' (@($contextAfter.Body.fieldSecurity | Where-Object { $_.resourceKey -eq 'customers' -and $_.fieldKey -eq 'email' }).Count -eq 0)
    Assert-True 'archived role excluded from effective roleIds' (-not ($contextAfter.Body.roleIds -contains $authProbeRole))
    Add-Result 'archived role assignment survives as linkage' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE AssignmentId='$authProbeAssignment'")
    Add-Result 'archived role capabilities survive as history' 'customers.view' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$authProbeRole' ORDER BY Capability").Capability -join ',')
    Assert-True 'another active role keeps granting its own authority' ($contextAfter.Body.capabilities -contains 'access.configure')

    # ---------------------------------------------------------------- ATOMICITY
    $atomicTarget = New-Role 'Archive Atomic Target'
    Test-AtomicFailure 'role' 'access.Roles' 'UPDATE' $atomicTarget '"0"'
    Test-AtomicFailure 'revision' 'access.WorkspaceDirectoryRevisions' 'UPDATE' $atomicTarget '"0"'
    Test-AtomicFailure 'idempotency' 'access.AccessRoleCommandIdempotencyRecords' 'INSERT' $atomicTarget '"0"'
    Test-AtomicFailure 'audit' 'access.GovernanceCommandAudits' 'INSERT' $atomicTarget '"0"'
    Test-AtomicFailure 'outbox' 'access.OutboxEvents' 'INSERT' $atomicTarget '"0"'

    # ---------------------------------------------------------------- PROVIDER FAILURE / REPLAY
    $providerTarget = New-Role 'Archive Provider Recovery Target'
    $providerKey = 'idem-archive-provider-1'
    $providerBody = New-ArchiveBody 'provider recovery'
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
    $providerFailure = Invoke-Archive $providerTarget $providerBody $providerKey '"0"'
    Add-Result 'post-commit invalid provider returns 503' 503 $providerFailure.Status
    Add-Result 'post-commit provider error code' 'INTEGRATION_UNAVAILABLE' $providerFailure.Body.code
    $providerRecord = @(Invoke-Sql "SELECT * FROM access.AccessRoleCommandIdempotencyRecords WHERE IdempotencyKey='$providerKey'")[0]
    Add-Result 'provider failure keeps the archive committed' 'False' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$providerTarget'")).ToString()
    Add-Result 'provider failure audit remains committed' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits WHERE CommandId='$($providerRecord.CommandId)'")
    Add-Result 'provider failure event remains committed' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE CausationId='$($providerRecord.CommandId)'")
    $providerEffects = Get-EffectSnapshot
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"
    $providerReplay = Invoke-Archive $providerTarget $providerBody $providerKey '"0"'
    Add-Result 'provider recovery replay status' 200 $providerReplay.Status
    Add-Result 'provider recovery replay outcome' 'REPLAYED' $providerReplay.Body.outcome
    Add-Result 'provider recovery preserves command' $providerRecord.CommandId $providerReplay.Body.commandId
    Add-Result 'provider recovery preserves role' $providerRecord.RoleId $providerReplay.Body.aggregateId
    Add-Result 'provider recovery preserves version' $providerRecord.RoleVersion $providerReplay.Body.version
    Add-Result 'provider recovery preserves audit' $providerRecord.AuditEvidenceId $providerReplay.Body.auditEvidenceIds[0]
    Add-Result 'provider recovery preserves event' $providerRecord.EventId $providerReplay.Body.emittedEventIds[0]
    Add-Result 'provider recovery creates no effects' $providerEffects (Get-EffectSnapshot)

    # ---------------------------------------------------------------- INITIAL PROVISIONING
    function New-ProvisioningFixture(
        [string] $Label,
        [string] $RoleName,
        [string] $NormalizedRoleName,
        [long] $RoleVersion,
        [bool] $RoleActive,
        [string[]] $Capabilities,
        [string] $RoleDescription = 'Initial Workspace provisioning role for the account that created this Workspace.'
    ) {
        $suffix = [Guid]::NewGuid().ToString('N')
        $fixture = [pscustomobject] @{
            AccountId = "acc_$suffix"
            MemberId = "mem_$suffix"
            WorkspaceId = "ws_$suffix"
            MembershipId = "wsm_$suffix"
            RoleId = "role_$suffix"
            AssignmentId = "assignment_$suffix"
        }
        $active = if ($RoleActive) { 1 } else { 0 }
        Invoke-SqlNonQuery "INSERT INTO workspace.Workspaces (WorkspaceId,[Key],Name,LogoText,CreatedAt) VALUES ('$($fixture.WorkspaceId)','archive-$Label-$($suffix.Substring(0,8))','Archive $Label Workspace','AR',SYSUTCDATETIME());"
        Invoke-SqlNonQuery "INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('$($fixture.MembershipId)','$($fixture.WorkspaceId)','$($fixture.AccountId)','$($fixture.MemberId)','Active',SYSUTCDATETIME());"
        Invoke-SqlNonQuery "INSERT INTO workspace.BootstrapProjections (WorkspaceId,ContextVersion,ConfigurationVersion,Locale,TimeZone,BaseCurrency,CapabilitiesJson,EnabledModuleKeysJson,AvailableProductSpacesJson) VALUES ('$($fixture.WorkspaceId)',0,0,'en','UTC','USD','[]','[""leads"",""deals"",""tasks""]','[""crm""]');"
        Invoke-SqlNonQuery "INSERT INTO workspace.InitialProvisioningRecords (AccountId,MemberId,WorkspaceId,MembershipId,IdempotencyKey,RequestFingerprint,State,CompletedAt,ProvisionedAt) VALUES ('$($fixture.AccountId)','$($fixture.MemberId)','$($fixture.WorkspaceId)','$($fixture.MembershipId)','idem-archive-$Label','$('0' * 64)','AccessPending',NULL,SYSUTCDATETIME());"
        Invoke-SqlNonQuery "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$($fixture.RoleId)','$($fixture.WorkspaceId)','$RoleName','$NormalizedRoleName','$RoleDescription',NULL,$active,$RoleVersion,SYSUTCDATETIME(),SYSUTCDATETIME());"
        foreach ($capability in $Capabilities) {
            Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$($fixture.RoleId)','$capability');"
        }
        Invoke-SqlNonQuery "INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES ('$($fixture.AssignmentId)','$($fixture.WorkspaceId)','$($fixture.MembershipId)','$($fixture.RoleId)',SYSUTCDATETIME());"
        Invoke-SqlNonQuery "INSERT INTO access.WorkspaceDirectoryRevisions (WorkspaceId,Revision) VALUES ('$($fixture.WorkspaceId)',1);"
        return $fixture
    }

    function Get-AnchorState([string] $AccountId) {
        return [string] (Get-Scalar "SELECT State FROM workspace.InitialProvisioningRecords WHERE AccountId='$AccountId'")
    }

    function Wait-AnchorState([string] $AccountId, [string] $Expected) {
        for ($attempt = 0; $attempt -lt 90; $attempt++) {
            if ((Get-AnchorState $AccountId) -ceq $Expected) { return $true }
            Start-Sleep -Seconds 1
        }
        return $false
    }

    # An archived initial role: inactive and at a non-zero version, exactly the state an admitted
    # archive leaves behind. Convergence must accept it without reactivating or recreating anything.
    $archivedSeed = New-ProvisioningFixture 'archivedseed' 'Workspace Owner' 'WORKSPACE OWNER' 1 $false @('tasks.read')
    # A never-archived, never-replaced drifted role must still fail closed exactly as before.
    $driftSeed = New-ProvisioningFixture 'drift' 'Workspace Owner' 'WORKSPACE OWNER' 0 $true @('tasks.read')

    $provisioningEffectsBefore = Get-EffectSnapshot
    Assert-True 'provisioning replay converges after a legitimate archive' (Wait-AnchorState $archivedSeed.AccountId 'Completed')
    Add-Result 'provisioning replay does not reactivate the archived role' 'False' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$($archivedSeed.RoleId)'")).ToString()
    Add-Result 'provisioning replay does not reset the archived version' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$($archivedSeed.RoleId)'")
    Add-Result 'provisioning replay does not rewrite archived capabilities' 'tasks.read' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$($archivedSeed.RoleId)' ORDER BY Capability").Capability -join ',')
    Add-Result 'provisioning replay recreates no owner role' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.Roles WHERE WorkspaceId='$($archivedSeed.WorkspaceId)'")
    Add-Result 'provisioning replay creates no second assignment' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE MembershipId='$($archivedSeed.MembershipId)'")
    Add-Result 'provisioning replay keeps the original assignment' $archivedSeed.AssignmentId (Get-Scalar "SELECT TOP 1 AssignmentId FROM access.MembershipRoleAssignments WHERE MembershipId='$($archivedSeed.MembershipId)'")
    Add-Result 'never-archived drift still fails closed' 'AccessPending' (Get-AnchorState $driftSeed.AccountId)
    Add-Result 'provisioning replay mutates no AccessControl state' $provisioningEffectsBefore (Get-EffectSnapshot)

    # ---------------------------------------------------------------- BOUNDARIES
    $accessFiles = Get-ChildItem (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl') -Recurse -Filter '*.cs'
    $accessText = ($accessFiles | Get-Content -Raw) -join "`n"
    Assert-True 'no foreign DbContext in AccessControl' ($accessText -notmatch 'WorkspaceDbContext|IdentityAuthDbContext')
    Assert-True 'no cross-owner SQL in AccessControl' ($accessText -notmatch '\[workspace\]|\[iam\]|(?i)\b(?:FROM|JOIN)\s+(?:workspace|iam)\.')
    Add-Result 'exactly one archive route mapping' 1 ([regex]::Matches($accessText, 'MapPost\("/access/roles/\{roleId\}/archive"').Count)
    Add-Result 'no reactivation route' 0 ([regex]::Matches($accessText, '/restore"|/reactivate"|/unarchive"').Count)
    Add-Result 'no invitation or membership mutation surface' 0 ([regex]::Matches($accessText, 'Invitations\.(Add|Remove|Update)|Memberships\.(Add|Remove|Update)').Count)
    Add-Result 'archive persistence performs no assignment or policy mutation' 0 ([regex]::Matches(((Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Infrastructure/Persistence/EfArchiveAccessRolePersistence.cs'))), 'MembershipRoleAssignments\.(Add|Remove)|RoleCapabilities\.(Add|Remove)|RoleDataScopes\.(Add|Remove)|RoleFieldSecurity\.(Add|Remove)').Count)
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
