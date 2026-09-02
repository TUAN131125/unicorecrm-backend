<#
.SYNOPSIS
    Verifies GET /access/directory against an isolated SQL database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5601,
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
$password = 'Access-Directory-Verify!2026'
$email = 'admin@unicorecrm.local'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-access-directory-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $logRoot

function Add-Result([string] $Name, [object] $Expected, [object] $Actual) {
    $expectedText = [string]$Expected
    $actualText = [string]$Actual
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
            $row = [ordered]@{}
            for ($index = 0; $index -lt $reader.FieldCount; $index++) {
                $name = $reader.GetName($index)
                if ([string]::IsNullOrWhiteSpace($name)) { $name = "Column$index" }
                $row[$name] = $reader.GetValue($index)
            }
            $rows.Add([pscustomobject]$row)
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
    return ($rows[0].PSObject.Properties | Select-Object -First 1).Value
}

function Invoke-Api(
    [string] $Method,
    [string] $Path,
    [string] $Token,
    [string] $WorkspaceId,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    $script:Counter++
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') {
        if ($null -eq $RequestId) { $RequestId = 'req-access-directory-' + $script:Counter.ToString('d6') }
        $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId)
    }
    if ($CorrelationId -ne 'omit') {
        if ($null -eq $CorrelationId) { $CorrelationId = 'corr-access-directory-' + $script:Counter.ToString('d6') }
        $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId)
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(90)
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { } }
        return [pscustomobject]@{ Status=[int]$response.StatusCode; Raw=$raw; Body=$payload }
    }
    finally { $request.Dispose(); $client.Dispose() }
}

function Invoke-Directory(
    [string] $Workspace = $script:WorkspaceId,
    [string] $Token = $script:Token,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    if ($Token -eq 'omit') { $Token = $null }
    if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-access-directory-' + ([Guid]::NewGuid().ToString('N')) }
    if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-access-directory-' + ([Guid]::NewGuid().ToString('N')) }
    return Invoke-Api 'GET' '/access/directory' $Token $Workspace $RequestId $CorrelationId
}

function Get-EvidenceCount {
    return [long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.DirectoryReadAccessRecords WHERE OperationId=N'getWorkspaceAccessDirectory'")
}

function Measure-Directory(
    [string] $Workspace = $script:WorkspaceId,
    [string] $Token = $script:Token,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    $before = Get-EvidenceCount
    $response = Invoke-Directory $Workspace $Token $RequestId $CorrelationId
    return [pscustomobject]@{ Response=$response; Delta=((Get-EvidenceCount)-$before) }
}

function Get-BusinessSnapshot {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.Roles'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleCapabilities'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleDataScopes'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleFieldSecurity'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.WorkspaceDirectoryRevisions'),
        (Get-Scalar 'SELECT COALESCE(SUM(Revision),0) FROM access.WorkspaceDirectoryRevisions'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.AccessRoleCommandIdempotencyRecords'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.OutboxEvents')
    ))
}

function PropertyNames([object] $Value) {
    return (($Value.PSObject.Properties.Name | Sort-Object) -join ',')
}

function Normalize-DirectoryJson([object] $Body) {
    return (($Body | ConvertTo-Json -Compress -Depth 20) -replace '"generatedAt":"[^"]+"','"generatedAt":"<generatedAt>"')
}

try {
    Invoke-SqlNonQuery "IF DB_ID(N'$DatabaseName') IS NOT NULL THROW 50001, 'Verification database already exists.', 1; CREATE DATABASE [$DatabaseName];" 'master'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = $connectionString
    $env:Development__ApplyMigrations = 'true'
    $env:UNICORE_DEV_SEED_ENABLED = 'true'
    $env:UNICORE_DEV_SEED_EMAIL = $email
    $env:UNICORE_DEV_SEED_PASSWORD = $password
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'access.read'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $stdout = Join-Path $logRoot 'host.out.log'
    $stderr = Join-Path $logRoot 'host.err.log'
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Api 'GET' '/auth/session' $null $null 'omit' 'omit'
            if ($probe.Status -eq 401) { $ready=$true; break }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }

    $signInRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/auth/sessions")
    $signInRequest.Headers.TryAddWithoutValidation('X-Request-Id','req-access-directory-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-access-directory-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('Idempotency-Key','idem-access-directory-signin') | Out-Null
    $signInRequest.Content = [System.Net.Http.StringContent]::new((@{email=$email;password=$password}|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json')
    $signInClient = [System.Net.Http.HttpClient]::new()
    $signInResponse = $signInClient.SendAsync($signInRequest).GetAwaiter().GetResult()
    $signInBody = $signInResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Add-Result 'authentication fixture sign-in' 200 ([int]$signInResponse.StatusCode)
    $script:Token = [string]$signInBody.accessToken
    $signInRequest.Dispose();$signInResponse.Dispose();$signInClient.Dispose()

    $script:WorkspaceId = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo'")
    $foreignWorkspace = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo-isolated'")
    $accountId = [string](Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail=N'$($email.ToUpperInvariant())'")
    $memberId = [string](Get-Scalar "SELECT MemberId FROM iam.Accounts WHERE AccountId=N'$accountId'")
    $membershipId = [string](Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId=N'$script:WorkspaceId' AND AccountId=N'$accountId'")
    $roleId = [string](Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.WorkspaceId=N'$script:WorkspaceId' AND a.MembershipId=N'$membershipId' AND c.Capability=N'access.read'")
    Assert-True 'trusted fixture exists' (-not [string]::IsNullOrWhiteSpace($roleId))

    Invoke-SqlNonQuery "INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES(N'scope_access_directory_verify',N'$script:WorkspaceId',N'$roleId',N'contacts',N'Workspace',N'[]'); INSERT INTO access.RoleFieldSecurity(PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES(N'field_access_directory_verify',N'$script:WorkspaceId',N'$roleId',N'contacts',N'email',N'ReadOnly');"

    $unauthenticated = Measure-Directory $script:WorkspaceId 'omit'
    Add-Result 'unauthenticated rejected' 401 $unauthenticated.Response.Status
    Add-Result 'unauthenticated successful evidence' 0 $unauthenticated.Delta
    $unknown = Measure-Directory 'ws_unknown_access_directory'
    Add-Result 'unknown Workspace rejected' 403 $unknown.Response.Status
    Add-Result 'unknown Workspace successful evidence' 0 $unknown.Delta
    $foreign = Measure-Directory $foreignWorkspace
    Add-Result 'foreign Workspace rejected' 403 $foreign.Response.Status
    Add-Result 'foreign Workspace successful evidence' 0 $foreign.Delta

    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Suspended' WHERE MembershipId=N'$membershipId'"
    $suspended = Measure-Directory
    Add-Result 'suspended membership rejected' 403 $suspended.Response.Status
    Add-Result 'suspended membership successful evidence' 0 $suspended.Delta
    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Active' WHERE MembershipId=N'$membershipId'"

    $activeAssignedRoleIds = @((Invoke-Sql "SELECT DISTINCT r.RoleId FROM access.MembershipRoleAssignments a JOIN access.Roles r ON r.RoleId=a.RoleId AND r.WorkspaceId=a.WorkspaceId WHERE a.WorkspaceId=N'$script:WorkspaceId' AND a.MembershipId=N'$membershipId' AND r.IsActive=1") | ForEach-Object { [string]$_.RoleId })
    foreach ($activeAssignedRoleId in $activeAssignedRoleIds) {
        Invoke-SqlNonQuery "UPDATE access.Roles SET IsActive=0 WHERE RoleId=N'$activeAssignedRoleId'"
    }
    Assert-True 'capability-denial fixture deactivates assigned roles' ($activeAssignedRoleIds.Count -gt 0 -and [long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments a JOIN access.Roles r ON r.RoleId=a.RoleId AND r.WorkspaceId=a.WorkspaceId WHERE a.WorkspaceId=N'$script:WorkspaceId' AND a.MembershipId=N'$membershipId' AND r.IsActive=1") -eq 0)
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText=N'' WHERE WorkspaceId=N'$script:WorkspaceId'"
    $denied = Measure-Directory $script:WorkspaceId $script:Token 'short' 'short'
    Add-Result 'access.read required' 403 $denied.Response.Status
    Add-Result 'capability-first error' 'ACCESS_DENIED' $denied.Response.Body.code
    Add-Result 'capability authorization decision denied' 'False' ([string](Get-Scalar "SELECT TOP 1 Allowed FROM access.AuthorizationDecisions WHERE WorkspaceId=N'$script:WorkspaceId' AND MembershipId=N'$membershipId' AND RequiredCapability=N'access.read' ORDER BY EvaluatedAt DESC"))
    Add-Result 'capability denial precedes metadata and invalid provider' 0 $denied.Delta
    foreach ($activeAssignedRoleId in $activeAssignedRoleIds) {
        Invoke-SqlNonQuery "UPDATE access.Roles SET IsActive=1 WHERE RoleId=N'$activeAssignedRoleId'"
    }
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText=N'UC' WHERE WorkspaceId=N'$script:WorkspaceId'"

    $missingRequest = Measure-Directory $script:WorkspaceId $script:Token 'omit'
    Add-Result 'missing X-Request-Id rejected' 422 $missingRequest.Response.Status
    Add-Result 'missing metadata successful evidence' 0 $missingRequest.Delta
    $invalidCorrelation = Measure-Directory $script:WorkspaceId $script:Token 'req-access-directory-invalid-corr' 'short'
    Add-Result 'invalid optional correlation rejected' 422 $invalidCorrelation.Response.Status
    Add-Result 'invalid correlation successful evidence' 0 $invalidCorrelation.Delta

    $businessBefore = Get-BusinessSnapshot
    $requestId = 'req-access-directory-success-0001'
    $correlationId = 'corr-access-directory-success-0001'
    $success = Measure-Directory $script:WorkspaceId $script:Token $requestId $correlationId
    Add-Result 'GET access directory status' 200 $success.Response.Status
    Add-Result 'successful read evidence cardinality' 1 $success.Delta
    if ($success.Response.Status -ne 200) { throw "Directory GET failed: $($success.Response.Raw)" }
    $directory = $success.Response.Body
    Add-Result 'exact top-level response shape' 'assignments,dataScopes,fieldSecurity,generatedAt,invitations,memberProfiles,members,revision,roles,workspaceId' (PropertyNames $directory)
    Add-Result 'trusted Workspace response' $script:WorkspaceId $directory.workspaceId
    Add-Result 'persisted revision response' ([long](Get-Scalar "SELECT Revision FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId=N'$script:WorkspaceId'")) ([long]$directory.revision)
    Assert-True 'generatedAt UTC' ($success.Response.Raw -match '"generatedAt":"[^"]+(Z|\+00:00)"')
    Add-Result 'confirmed invitation absence' 0 @($directory.invitations).Count
    Assert-True 'Workspace provider supplied member facts' (@($directory.members | Where-Object membershipId -eq $membershipId).Count -eq 1)
    Add-Result 'Identity provider supplied email' $email ($directory.memberProfiles | Where-Object membershipId -eq $membershipId).email
    Assert-True 'AccessControl role projected' (@($directory.roles | Where-Object roleId -eq $roleId).Count -eq 1)
    Assert-True 'AccessControl assignment projected' (@($directory.assignments | Where-Object { $_.roleId -eq $roleId -and $_.membershipId -eq $membershipId }).Count -eq 1)
    Add-Result 'dataScope exact enum' 'WORKSPACE' ($directory.dataScopes | Where-Object policyId -eq 'scope_access_directory_verify').scope
    Add-Result 'fieldSecurity exact enum' 'READ_ONLY' ($directory.fieldSecurity | Where-Object policyId -eq 'field_access_directory_verify').access
    Add-Result 'role overlay on member' $roleId (($directory.members | Where-Object membershipId -eq $membershipId).roleIds -join ',')
    Add-Result 'sole active roleLabel derived' (($directory.roles | Where-Object roleId -eq $roleId).name) (($directory.memberProfiles | Where-Object membershipId -eq $membershipId).roleLabel)
    Assert-True 'optional null role values omitted' (-not ($directory.roles | Where-Object roleId -eq $roleId).PSObject.Properties.Name.Contains('sourceTemplateId'))
    Assert-True 'deterministic role ordering' ((@($directory.roles.roleId) -join ',') -ceq (@($directory.roles.roleId | Sort-Object) -join ','))
    Assert-True 'deterministic assignment ordering' ((@($directory.assignments.assignmentId) -join ',') -ceq (@($directory.assignments.assignmentId | Sort-Object) -join ','))

    $evidence = @(Invoke-Sql "SELECT TOP 1 * FROM access.DirectoryReadAccessRecords WHERE RequestId=N'$requestId' ORDER BY OccurredAt DESC")[0]
    Assert-True 'owner-generated evidence ID' ([string]$evidence.EvidenceId -match '^audit_[0-9a-f]{32}$')
    Add-Result 'evidence operationId' 'getWorkspaceAccessDirectory' $evidence.OperationId
    Add-Result 'evidence trusted Workspace' $script:WorkspaceId $evidence.WorkspaceId
    Add-Result 'evidence trusted account' $accountId $evidence.ActorAccountId
    Add-Result 'evidence trusted membership' $membershipId $evidence.ActorMembershipId
    Add-Result 'evidence trusted member' $memberId $evidence.ActorMemberId
    Add-Result 'evidence requestId' $requestId $evidence.RequestId
    Add-Result 'evidence correlationId' $correlationId $evidence.CorrelationId
    Add-Result 'evidence READ discriminator' 'READ' $evidence.Outcome
    Assert-True 'evidence occurredAt UTC' (([DateTimeOffset]$evidence.OccurredAt).Offset -eq [TimeSpan]::Zero)
    Add-Result 'evidence schema stores no business payload' 0 (Get-Scalar "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'access.DirectoryReadAccessRecords') AND [name] IN (N'RecordId',N'Revision',N'ResultCount',N'Payload',N'Directory')")

    $second = Measure-Directory
    Add-Result 'second successful read status' 200 $second.Response.Status
    Add-Result 'second successful read evidence cardinality' 1 $second.Delta
    Add-Result 'repeated read projection deterministic' (Normalize-DirectoryJson $directory) (Normalize-DirectoryJson $second.Response.Body)
    Add-Result 'GET mutates no AccessControl business state' $businessBefore (Get-BusinessSnapshot)

    $zeroWorkspace = 'ws_access_directory_revision_zero'
    $zeroMembership = 'wsm_access_directory_revision_zero'
    $zeroRole = 'role_access_directory_revision_zero'
    Invoke-SqlNonQuery @"
INSERT INTO workspace.Workspaces(WorkspaceId,[Key],Name,LogoText,CreatedAt) VALUES(N'$zeroWorkspace',N'access-directory-zero',N'Revision Zero Workspace',N'RZ',SYSUTCDATETIME());
INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES(N'$zeroMembership',N'$zeroWorkspace',N'$accountId',N'$memberId',N'Active',SYSUTCDATETIME());
INSERT INTO access.Roles(RoleId,WorkspaceId,Name,Description,SourceTemplateId,IsActive,Version,CreatedAt,UpdatedAt,NormalizedName) VALUES(N'$zeroRole',N'$zeroWorkspace',N'Revision Zero Reader',NULL,NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'REVISION ZERO READER');
INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES(N'$zeroRole',N'access.read');
INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES(N'assignment_access_directory_revision_zero',N'$zeroWorkspace',N'$zeroMembership',N'$zeroRole',SYSUTCDATETIME());
"@
    Add-Result 'revision-zero row absent before GET' 0 (Get-Scalar "SELECT COUNT(*) FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId=N'$zeroWorkspace'")
    $zero = Measure-Directory $zeroWorkspace
    Add-Result 'revision-zero GET status' 200 $zero.Response.Status
    Add-Result 'revision-zero response' 0 $zero.Response.Body.revision
    Add-Result 'revision-zero successful evidence' 1 $zero.Delta
    Add-Result 'GET does not initialize revision row' 0 (Get-Scalar "SELECT COUNT(*) FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId=N'$zeroWorkspace'")

    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText=N'' WHERE WorkspaceId=N'$script:WorkspaceId'"
    $invalidProvider = Measure-Directory
    Add-Result 'invalid provider snapshot fails closed' 503 $invalidProvider.Response.Status
    Add-Result 'invalid provider snapshot successful evidence' 0 $invalidProvider.Delta
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText=N'UC' WHERE WorkspaceId=N'$script:WorkspaceId'"

    Invoke-SqlNonQuery "EXEC sp_rename N'workspace.Workspaces', N'WorkspacesUnavailable'"
    try {
        $providerUnavailable = Measure-Directory
        Add-Result 'provider unavailable fails closed' 503 $providerUnavailable.Response.Status
        Add-Result 'provider unavailable successful evidence' 0 $providerUnavailable.Delta
    }
    finally { Invoke-SqlNonQuery "EXEC sp_rename N'workspace.WorkspacesUnavailable', N'Workspaces'" }

    Invoke-SqlNonQuery "UPDATE iam.Accounts SET DisplayName=N'' WHERE AccountId=N'$accountId'"
    $invalidIdentity = Measure-Directory
    Add-Result 'invalid identity provider snapshot fails closed' 503 $invalidIdentity.Response.Status
    Add-Result 'invalid identity provider successful evidence' 0 $invalidIdentity.Delta
    Invoke-SqlNonQuery "UPDATE iam.Accounts SET DisplayName=N'Unicore Admin' WHERE AccountId=N'$accountId'"

    Invoke-SqlNonQuery "CREATE TRIGGER access.TR_VerifyDirectoryReadEvidenceFailure ON access.DirectoryReadAccessRecords INSTEAD OF INSERT AS THROW 51000, 'forced directory read evidence failure', 1"
    try {
        $beforeFailedAudit = Get-EvidenceCount
        $auditFailure = Invoke-Directory
        Add-Result 'audit persistence failure returns no success' 500 $auditFailure.Status
        Add-Result 'audit persistence failure leaves no durable evidence' $beforeFailedAudit (Get-EvidenceCount)
    }
    finally { Invoke-SqlNonQuery 'DROP TRIGGER access.TR_VerifyDirectoryReadEvidenceFailure' }

    $accessSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Infrastructure/Persistence/EfAccessDirectoryPersistence.cs')
    $handlerSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Application/GetWorkspaceAccessDirectory/Handler.cs')
    $composerSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Application/AccessDirectory/DirectoryComposer.cs')
    $endpointSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Contracts/AccessControlEndpoints.cs')
    Assert-True 'no foreign DbContext in AccessControl read' (($accessSource+$handlerSource+$composerSource) -notmatch '\b(Workspace|IdentityAuth)DbContext\b')
    Assert-True 'no cross-owner SQL in AccessControl read' ($accessSource -notmatch '\[(workspace|iam)\]|\b(workspace|iam)\.')
    Assert-True 'GET handler writes no command idempotency governance audit or outbox' ($handlerSource -notmatch 'Idempotency|GovernanceCommand|Outbox')
    Add-Result 'exactly one directory GET route' 1 ([regex]::Matches($endpointSource,'MapGet\("/access/directory"').Count)
    Add-Result 'exactly one create role route preserved' 1 ([regex]::Matches($endpointSource,'MapPost\("/access/roles"').Count)
    Add-Result 'migration has no foreign key' 0 (Get-Scalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'access.DirectoryReadAccessRecords')")
}
finally {
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit()
    }
    if (-not $KeepDatabase) {
        try { Invoke-SqlNonQuery "IF DB_ID(N'$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END" 'master' } catch { }
    }
    if (-not $KeepDatabase) {
        Remove-Item -LiteralPath $logRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "Verification logs retained at $logRoot"
    }
}

$script:Results | ForEach-Object { Write-Host $_ }
Write-Host "RESULT | passed=$script:Passed failed=$script:Failed"
if ($script:Failed -ne 0) { throw "getWorkspaceAccessDirectory verification failed: $script:Failed check(s)." }
