<#
.SYNOPSIS
    Verifies DEC-REPLACEWORKSPACEMEMBERACCESS-AUTHORITY-CLOSURE against isolated SQL and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5353,
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
$password = 'Replace-Member-Access-Verify!2026'
$email = 'admin@unicorecrm.local'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-replace-member-access-' + [Guid]::NewGuid().ToString('N'))
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
    return ($rows[0].PSObject.Properties | Select-Object -First 1).Value
}

function New-Client([int] $TimeoutSeconds = 90) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    return $client
}

function New-Request(
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
    if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-member-access-' + $script:Counter.ToString('d6') }
    if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-member-access-' + $script:Counter.ToString('d6') }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId) }
    if ($CorrelationId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId) }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    if (-not [string]::IsNullOrWhiteSpace($IfMatch)) { $null = $request.Headers.TryAddWithoutValidation('If-Match', $IfMatch) }
    if (-not [string]::IsNullOrEmpty($Body)) { $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json') }
    return $request
}

function Convert-Response([System.Net.Http.HttpResponseMessage] $Response) {
    $raw = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $body = $null
    if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $body = $raw | ConvertFrom-Json } catch { } }
    return [pscustomobject] @{ Status = [int] $Response.StatusCode; Raw = $raw; Body = $body }
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
    $request = New-Request $Method $Path $Body $Token $WorkspaceId $IdempotencyKey $IfMatch $RequestId $CorrelationId
    $client = New-Client
    try { return Convert-Response ($client.SendAsync($request).GetAwaiter().GetResult()) }
    finally { $request.Dispose(); $client.Dispose() }
}

function New-Body([string[]] $RoleIds = @(), [object[]] $TeamIds = @()) {
    return [ordered]@{ roleIds = $RoleIds; teamIds = $TeamIds } | ConvertTo-Json -Compress -Depth 5
}

function Invoke-MemberReplace(
    [string] $MembershipId,
    [string] $Body,
    [string] $Key,
    [string] $IfMatch,
    [string] $Workspace = $script:WorkspaceId,
    [string] $Token = $script:Token,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    return Invoke-Api 'POST' "/access/members/$MembershipId/access" $Body $Token $Workspace $Key $IfMatch $RequestId $CorrelationId
}

function Invoke-CreateRole([string] $Name, [string[]] $Capabilities, [string] $Key = $null) {
    if ([string]::IsNullOrWhiteSpace($Key)) { $Key = 'idem-member-role-' + [Guid]::NewGuid().ToString('N') }
    $body = [ordered]@{ name = $Name; capabilities = $Capabilities; dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress -Depth 6
    return Invoke-Api 'POST' '/access/roles' $body $script:Token $script:WorkspaceId $Key
}

function New-Role([string] $Name, [string[]] $Capabilities = @('tasks.read')) {
    $response = Invoke-CreateRole $Name $Capabilities
    if ($response.Status -ne 200) { throw "Could not create role '$Name': HTTP $($response.Status) $($response.Raw)" }
    return [string] $response.Body.aggregateId
}

function New-Membership([string] $Label, [string] $Status = 'Active', [string] $Workspace = $script:WorkspaceId) {
    $suffix = [Guid]::NewGuid().ToString('N')
    $membershipId = 'membership_' + $suffix
    $memberId = 'member_' + $suffix
    $syntheticAccountId = 'account_' + $suffix
    Invoke-SqlNonQuery "INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES('$membershipId','$Workspace','$syntheticAccountId','$memberId','$Status',SYSUTCDATETIME());"
    return $membershipId
}

function Add-Assignment([string] $MembershipId, [string] $RoleId, [string] $AssignmentId = $null, [string] $AssignedAt = $null) {
    if ([string]::IsNullOrWhiteSpace($AssignmentId)) { $AssignmentId = 'assignment_' + [Guid]::NewGuid().ToString('N') }
    if ([string]::IsNullOrWhiteSpace($AssignedAt)) { $AssignedAt = '2026-01-02T03:04:05+00:00' }
    Invoke-SqlNonQuery "INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES('$AssignmentId','$script:WorkspaceId','$MembershipId','$RoleId','$AssignedAt');"
    return $AssignmentId
}

function Get-Effects {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments'),
        (Get-Scalar "SELECT COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)),0) FROM access.MembershipRoleAssignments"),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MemberAccessVersions'),
        (Get-Scalar 'SELECT COALESCE(SUM(Version),0) FROM access.MemberAccessVersions'),
        (Get-Scalar 'SELECT COALESCE(SUM(Revision),0) FROM access.WorkspaceDirectoryRevisions'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MemberAccessCommandIdempotencyRecords'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.OutboxEvents'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.DirectoryReadAccessRecords')
    ))
}

function Assert-NoEffect([string] $Name, [scriptblock] $Action, [int] $Status, [string] $Code) {
    $before = Get-Effects
    $response = & $Action
    Add-Result "$Name status" $Status $response.Status
    Add-Result "$Name code" $Code $response.Body.code
    Add-Result "$Name zero owner effect" $before (Get-Effects)
    return $response
}

function Test-AtomicFailure(
    [string] $Name,
    [string] $Table,
    [string] $Action,
    [string] $MembershipId,
    [string] $Body,
    [long] $Version
) {
    $trigger = 'TR_VerifyMemberAccess_' + ($Name -replace '[^A-Za-z0-9]', '')
    Invoke-SqlNonQuery "CREATE TRIGGER [access].[$trigger] ON $Table AFTER $Action AS THROW 51000, 'forced replaceWorkspaceMemberAccess persistence failure', 1;"
    try {
        $before = Get-Effects
        $response = Invoke-MemberReplace $MembershipId $Body ('idem-fault-' + [Guid]::NewGuid().ToString('N')) "`"$Version`""
        Add-Result "atomic $Name returns 500" 500 $response.Status
        Add-Result "atomic $Name full rollback" $before (Get-Effects)
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

    $signIn = Invoke-Api 'POST' '/auth/sessions' (@{ email = $email; password = $password } | ConvertTo-Json -Compress) $null $null 'idem-member-signin-01'
    Add-Result 'authentication fixture sign-in' 200 $signIn.Status
    $script:Token = $signIn.Body.accessToken
    $script:WorkspaceId = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo'")
    $foreignWorkspace = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo-isolated'")
    $accountId = [string] (Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())'")
    $actorMembershipId = [string] (Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId='$script:WorkspaceId' AND AccountId='$accountId'")
    $adminRoleId = [string] (Get-Scalar "SELECT TOP(1) a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId AND c.Capability='access.configure' WHERE a.WorkspaceId='$script:WorkspaceId' AND a.MembershipId='$actorMembershipId'")
    $logo = [string] (Get-Scalar "SELECT LogoText FROM workspace.Workspaces WHERE WorkspaceId='$script:WorkspaceId'")
    Assert-True 'trusted fixtures exist' (-not [string]::IsNullOrWhiteSpace($actorMembershipId) -and -not [string]::IsNullOrWhiteSpace($adminRoleId))
    Add-Result 'new migration table exists' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='access' AND TABLE_NAME='MemberAccessVersions'")
    Add-Result 'member-access version has no foreign key' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('access.MemberAccessVersions')")

    $emptyBody = New-Body
    $unauthenticated = Invoke-MemberReplace 'membership_unknown000000000000000000' $emptyBody 'idem-unauthenticated-01' '"0"' $script:WorkspaceId $null
    Add-Result 'unauthenticated' 401 $unauthenticated.Status
    $workspaceMismatch = Invoke-MemberReplace 'membership_unknown000000000000000000' $emptyBody 'idem-workspace-mismatch-01' '"0"' $foreignWorkspace
    Add-Result 'Workspace mismatch' 403 $workspaceMismatch.Status

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE RoleId='$adminRoleId' AND Capability='access.configure';"
    $denied = Invoke-MemberReplace 'membership_unknown000000000000000000' $emptyBody 'idem-no-capability-01' '"0"'
    Add-Result 'access.configure required' 403 $denied.Status
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('$adminRoleId','access.configure');"

    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
    $malformed = Invoke-MemberReplace 'membership_unknown000000000000000000' $emptyBody 'idem-malformed-ifmatch-01' '0'
    Add-Result 'malformed If-Match precedes provider lookup' 422 $malformed.Status
    Add-Result 'malformed If-Match code' 'VALIDATION_FAILED' $malformed.Body.code
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"

    $unknownMembership = 'membership_00000000000000000000000000000000'
    $foreignMembership = New-Membership 'foreign' 'Active' $foreignWorkspace
    $unknown = Assert-NoEffect 'unknown membership' { Invoke-MemberReplace $unknownMembership $emptyBody 'idem-unknown-member-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    $foreign = Assert-NoEffect 'foreign membership' { Invoke-MemberReplace $foreignMembership $emptyBody 'idem-foreign-member-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    Add-Result 'membership nondisclosure status' $unknown.Status $foreign.Status
    Add-Result 'membership nondisclosure code' $unknown.Body.code $foreign.Body.code
    Add-Result 'membership nondisclosure title' $unknown.Body.title $foreign.Body.title

    $activeMember = New-Membership 'active'
    Add-Result 'read-only setup has no version anchor' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MemberAccessVersions WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$activeMember'")
    $logicalZeroRead = Invoke-MemberReplace $activeMember (New-Body @('role_00000000000000000000000000000000')) 'idem-logical-zero-read-01' '"0"'
    Add-Result 'logical zero reaches role validation' 404 $logicalZeroRead.Status
    Add-Result 'logical zero read creates no member-access anchor' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MemberAccessVersions WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$activeMember'")
    $activeSuccess = Invoke-MemberReplace $activeMember $emptyBody 'idem-active-member-01' '"0"'
    Add-Result 'active member succeeds' 200 $activeSuccess.Status
    Add-Result 'active member first version' 1 $activeSuccess.Body.version
    Add-Result 'active member anchor is one' 1 (Get-Scalar "SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$activeMember'")

    $suspendedMember = New-Membership 'suspended' 'Suspended'
    $suspendedSuccess = Invoke-MemberReplace $suspendedMember $emptyBody 'idem-suspended-member-01' '"0"'
    Add-Result 'suspended target succeeds' 200 $suspendedSuccess.Status

    $invalidProviderTarget = New-Membership 'invalid-provider'
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
    $null = Assert-NoEffect 'invalid provider snapshot' { Invoke-MemberReplace $invalidProviderTarget $emptyBody 'idem-invalid-provider-01' '"0"' } 503 'INTEGRATION_UNAVAILABLE'
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"

    $teamTarget = New-Membership 'teams'
    $null = Assert-NoEffect 'non-empty teamIds' { Invoke-MemberReplace $teamTarget (New-Body @() @('team_forbidden')) 'idem-team-nonempty-01' '"0"' } 422 'VALIDATION_FAILED'
    Add-Result 'team links remain zero' 0 (Get-Scalar 'SELECT COUNT_BIG(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=''access'' AND TABLE_NAME LIKE ''%Team%''')

    $roleTarget = New-Membership 'role-validation'
    $null = Assert-NoEffect 'unknown role' { Invoke-MemberReplace $roleTarget (New-Body @('role_00000000000000000000000000000000')) 'idem-unknown-role-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    $foreignRoleId = 'role_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO access.Roles(RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES('$foreignRoleId','$foreignWorkspace','Foreign Role','$($foreignRoleId.ToUpperInvariant())',NULL,NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME());"
    $null = Assert-NoEffect 'foreign role' { Invoke-MemberReplace $roleTarget (New-Body @($foreignRoleId)) 'idem-foreign-role-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    $inactiveRole = New-Role 'Member Access Inactive Role'
    Invoke-SqlNonQuery "UPDATE access.Roles SET IsActive=0 WHERE RoleId='$inactiveRole';"
    $null = Assert-NoEffect 'inactive role rejected' { Invoke-MemberReplace $roleTarget (New-Body @($inactiveRole)) 'idem-inactive-role-01' '"0"' } 409 'ROLE_INACTIVE'
    $duplicateBody = '{"roleIds":["' + $adminRoleId + '","' + $adminRoleId + '"],"teamIds":[]}'
    $null = Assert-NoEffect 'duplicate role IDs' { Invoke-MemberReplace $roleTarget $duplicateBody 'idem-duplicate-role-01' '"0"' } 422 'VALIDATION_FAILED'

    $archivedAssignmentTarget = New-Membership 'archived-assignment'
    $archivedAssignmentId = Add-Assignment $archivedAssignmentTarget $inactiveRole
    $removedArchived = Invoke-MemberReplace $archivedAssignmentTarget $emptyBody 'idem-remove-archived-01' '"0"'
    Add-Result 'archived assignment can be removed' 200 $removedArchived.Status
    Add-Result 'archived assignment removed' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE AssignmentId='$archivedAssignmentId'")
    $null = Assert-NoEffect 'archived role cannot be re-added' { Invoke-MemberReplace $archivedAssignmentTarget (New-Body @($inactiveRole)) 'idem-readd-archived-01' '"1"' } 409 'ROLE_INACTIVE'

    $roleA = New-Role 'Member Access Role A'
    $roleB = New-Role 'Member Access Role B'
    $roleC = New-Role 'Member Access Role C'
    $replaceTarget = New-Membership 'full-replace'
    $stableAssignedAt = '2026-02-03T04:05:06+00:00'
    $stableId = Add-Assignment $replaceTarget $roleA $null $stableAssignedAt
    $omittedId = Add-Assignment $replaceTarget $roleB
    $replaceRevisionBefore = [long] (Get-Scalar "SELECT Revision FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId='$script:WorkspaceId'")
    $fullReplace = Invoke-MemberReplace $replaceTarget (New-Body @($roleC,$roleA)) 'idem-full-replace-01' '"0"'
    Add-Result 'full replacement succeeds' 200 $fullReplace.Status
    Add-Result 'omitted assignment deleted' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE AssignmentId='$omittedId'")
    Add-Result 'unchanged assignment ID preserved' $stableId (Get-Scalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$replaceTarget' AND RoleId='$roleA'")
    Add-Result 'unchanged AssignedAt preserved' ([DateTimeOffset] $stableAssignedAt) ([DateTimeOffset] (Get-Scalar "SELECT AssignedAt FROM access.MembershipRoleAssignments WHERE AssignmentId='$stableId'"))
    $newAssignmentId = [string] (Get-Scalar "SELECT AssignmentId FROM access.MembershipRoleAssignments WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$replaceTarget' AND RoleId='$roleC'")
    Assert-True 'new assignment gets canonical ID' ($newAssignmentId -cmatch '^assignment_[0-9a-f]{32}$')
    Add-Result 'fresh replacement revision +1' ($replaceRevisionBefore + 1) (Get-Scalar "SELECT Revision FROM access.WorkspaceDirectoryRevisions WHERE WorkspaceId='$script:WorkspaceId'")
    $emptySafe = Invoke-MemberReplace $replaceTarget $emptyBody 'idem-empty-safe-01' '"1"'
    Add-Result 'empty assignment set valid when safe' 200 $emptySafe.Status

    $versionTarget = New-Membership 'versions'
    $versionOne = Invoke-MemberReplace $versionTarget (New-Body @($roleA)) 'idem-version-one-01' '"0"'
    $versionTwo = Invoke-MemberReplace $versionTarget (New-Body @($roleA)) 'idem-version-two-01' '"1"'
    Add-Result 'first commit zero to one' 1 $versionOne.Body.version
    Add-Result 'second effective-identical commit one to two' 2 $versionTwo.Body.version
    $null = Assert-NoEffect 'stale member-access version' { Invoke-MemberReplace $versionTarget (New-Body @($roleA)) 'idem-version-stale-01' '"1"' } 412 'VERSION_CONFLICT'
    $versionMember = @($versionOne.Body.result.members | Where-Object membershipId -eq $versionTarget)[0]
    Add-Result 'response version is MemberAccessVersion' 1 $versionOne.Body.version
    Add-Result 'directory member version remains Workspace version' 0 $versionMember.version
    Assert-True 'directory revision is distinct' ([long] $versionOne.Body.result.revision -ne [long] $versionOne.Body.version)

    $concurrentTarget = New-Membership 'concurrent-version'
    $concurrentBody = New-Body @($roleA)
    $client = New-Client
    $requestA = New-Request 'POST' "/access/members/$concurrentTarget/access" $concurrentBody $script:Token $script:WorkspaceId 'idem-concurrent-version-a' '"0"'
    $requestB = New-Request 'POST' "/access/members/$concurrentTarget/access" $concurrentBody $script:Token $script:WorkspaceId 'idem-concurrent-version-b' '"0"'
    try {
        $taskA = $client.SendAsync($requestA)
        $taskB = $client.SendAsync($requestB)
        $responseA = Convert-Response ($taskA.GetAwaiter().GetResult())
        $responseB = Convert-Response ($taskB.GetAwaiter().GetResult())
        Add-Result 'concurrent fresh same-version one commit one conflict' '200,412' ((@($responseA.Status,$responseB.Status) | Sort-Object) -join ',')
        Add-Result 'concurrent fresh version increments once' 1 (Get-Scalar "SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$concurrentTarget'")
    }
    finally { $requestA.Dispose(); $requestB.Dispose(); $client.Dispose() }

    $lastAdminBefore = Get-Effects
    $lastAdmin = Invoke-MemberReplace $actorMembershipId $emptyBody 'idem-last-admin-01' '"0"'
    Add-Result 'remove sole effective administrator' 409 $lastAdmin.Status
    Add-Result 'last administrator code' 'LAST_WORKSPACE_ADMINISTRATOR' $lastAdmin.Body.code
    Add-Result 'last administrator zero effect' $lastAdminBefore (Get-Effects)

    $otherAdminMember = New-Membership 'other-admin'
    $null = Add-Assignment $otherAdminMember $adminRoleId
    $selfRemoval = Invoke-MemberReplace $actorMembershipId $emptyBody 'idem-self-removal-01' '"0"'
    Add-Result 'self removal allowed with another administrator' 200 $selfRemoval.Status
    $null = Add-Assignment $actorMembershipId $adminRoleId

    $secondAdminRole = New-Role 'Member Access Alternate Administrator' @('access.configure')
    $null = Add-Assignment $actorMembershipId $secondAdminRole
    Invoke-SqlNonQuery "DELETE FROM access.MembershipRoleAssignments WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$otherAdminMember';"
    $retainAdmin = Invoke-MemberReplace $actorMembershipId (New-Body @($secondAdminRole)) 'idem-retain-admin-01' '"1"'
    Add-Result 'retaining another admin role succeeds' 200 $retainAdmin.Status
    Invoke-SqlNonQuery "DELETE FROM access.MembershipRoleAssignments WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$actorMembershipId';"
    $null = Add-Assignment $actorMembershipId $adminRoleId

    $suspendedAdmin = New-Membership 'suspended-admin' 'Suspended'
    $null = Add-Assignment $suspendedAdmin $adminRoleId
    $null = Assert-NoEffect 'suspended membership does not count as administrator' { Invoke-MemberReplace $actorMembershipId $emptyBody 'idem-suspended-admin-guard' '"2"' } 409 'LAST_WORKSPACE_ADMINISTRATOR'
    Invoke-SqlNonQuery "DELETE FROM access.MembershipRoleAssignments WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$suspendedAdmin';"
    $archivedAdminRole = New-Role 'Archived Administrator Candidate' @('access.configure')
    Invoke-SqlNonQuery "UPDATE access.Roles SET IsActive=0 WHERE RoleId='$archivedAdminRole';"
    $archivedAdminMember = New-Membership 'archived-admin-member'
    $null = Add-Assignment $archivedAdminMember $archivedAdminRole
    $null = Assert-NoEffect 'archived role does not count as administrator' { Invoke-MemberReplace $actorMembershipId $emptyBody 'idem-archived-admin-guard' '"2"' } 409 'LAST_WORKSPACE_ADMINISTRATOR'

    $idemTarget = New-Membership 'idempotency'
    $idemKey = 'idem-member-happy-0001'
    $idemBody = New-Body @($roleA)
    $idemFirst = Invoke-MemberReplace $idemTarget $idemBody $idemKey '"0"'
    $idemEffects = Get-Effects
    $idemReplay = Invoke-MemberReplace $idemTarget $idemBody $idemKey '"0"'
    Add-Result 'idempotent replay status' 200 $idemReplay.Status
    Add-Result 'idempotent replay outcome' 'REPLAYED' $idemReplay.Body.outcome
    Add-Result 'idempotent replay command identity' $idemFirst.Body.commandId $idemReplay.Body.commandId
    Add-Result 'idempotent replay version' $idemFirst.Body.version $idemReplay.Body.version
    Add-Result 'idempotent replay audit identity' $idemFirst.Body.auditEvidenceIds[0] $idemReplay.Body.auditEvidenceIds[0]
    Add-Result 'idempotent replay event identity' $idemFirst.Body.emittedEventIds[0] $idemReplay.Body.emittedEventIds[0]
    Add-Result 'idempotent replay zero owner effect' $idemEffects (Get-Effects)
    $null = Assert-NoEffect 'changed roleIds under same key' { Invoke-MemberReplace $idemTarget $emptyBody $idemKey '"0"' } 409 'IDEMPOTENCY_KEY_REUSED'
    $otherIdemTarget = New-Membership 'idempotency-other-target'
    $null = Assert-NoEffect 'same key changed target' { Invoke-MemberReplace $otherIdemTarget $idemBody $idemKey '"0"' } 409 'IDEMPOTENCY_KEY_REUSED'
    $sharedScopeKey = 'idem-operation-scope-01'
    $roleScope = Invoke-CreateRole 'Member Access Operation Scope' @('tasks.read') $sharedScopeKey
    Add-Result 'role command accepts operation-scope key' 200 $roleScope.Status
    $scopeTarget = New-Membership 'operation-scope'
    $memberScope = Invoke-MemberReplace $scopeTarget $emptyBody $sharedScopeKey '"0"'
    Add-Result 'member command has independent operation scope' 200 $memberScope.Status

    $sameKeyTarget = New-Membership 'same-key-concurrency'
    $sameKey = 'idem-member-converge-01'
    $client = New-Client
    $requestC = New-Request 'POST' "/access/members/$sameKeyTarget/access" $idemBody $script:Token $script:WorkspaceId $sameKey '"0"'
    $requestD = New-Request 'POST' "/access/members/$sameKeyTarget/access" $idemBody $script:Token $script:WorkspaceId $sameKey '"0"'
    try {
        $taskC = $client.SendAsync($requestC)
        $taskD = $client.SendAsync($requestD)
        $responseC = Convert-Response ($taskC.GetAwaiter().GetResult())
        $responseD = Convert-Response ($taskD.GetAwaiter().GetResult())
        Add-Result 'concurrent same-key response C' 200 $responseC.Status
        Add-Result 'concurrent same-key response D' 200 $responseD.Status
        Add-Result 'concurrent same-key command convergence' $responseC.Body.commandId $responseD.Body.commandId
        Add-Result 'concurrent same-key one version increment' 1 (Get-Scalar "SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$sameKeyTarget'")
    }
    finally { $requestC.Dispose(); $requestD.Dispose(); $client.Dispose() }

    $audit = @(Invoke-Sql "SELECT * FROM access.GovernanceCommandAudits WHERE CommandId='$($idemFirst.Body.commandId)'")[0]
    Add-Result 'one governance audit' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits WHERE CommandId='$($idemFirst.Body.commandId)'")
    Add-Result 'audit evidence type' 'ACCESS_GOVERNANCE_COMMAND' $audit.EvidenceType
    Add-Result 'audit operation' 'replaceWorkspaceMemberAccess' $audit.OperationId
    Add-Result 'audit target membership' $idemTarget $audit.TargetMembershipId
    Add-Result 'audit role ID absent' ([DBNull]::Value) $audit.RoleId
    Add-Result 'audit prior MemberAccessVersion' 0 $audit.PriorVersion
    Add-Result 'audit resulting MemberAccessVersion' 1 $audit.ResultingVersion
    $event = @(Invoke-Sql "SELECT * FROM access.OutboxEvents WHERE CausationId='$($idemFirst.Body.commandId)'")[0]
    Add-Result 'one member access event' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE CausationId='$($idemFirst.Body.commandId)'")
    Add-Result 'event type' 'WORKSPACE_MEMBER_ACCESS_REPLACED' $event.EventType
    Add-Result 'event aggregate type' 'WORKSPACE_MEMBER_ACCESS' $event.AggregateType
    Add-Result 'event aggregate ID' $idemTarget $event.AggregateId
    Add-Result 'event aggregate MemberAccessVersion' 1 $event.AggregateVersion
    Add-Result 'event minimal payload' ("{`"membershipId`":`"$idemTarget`",`"version`":1}") $event.PayloadJson
    Add-Result 'command response creates no directory-read audit' 0 (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.DirectoryReadAccessRecords')
    $auditColumns = ((Invoke-Sql "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='access' AND TABLE_NAME='GovernanceCommandAudits' ORDER BY ORDINAL_POSITION").COLUMN_NAME -join ',')
    Assert-True 'governance audit excludes role/team arrays and directory' ($auditColumns -notmatch 'RoleIds|TeamIds|Directory|Payload')

    $faultTarget = New-Membership 'atomicity'
    $null = Add-Assignment $faultTarget $roleA
    $faultInitial = Invoke-MemberReplace $faultTarget (New-Body @($roleA)) 'idem-fault-initial-01' '"0"'
    Add-Result 'atomicity fixture version' 1 $faultInitial.Body.version
    Test-AtomicFailure 'assignment-delete' '[access].[MembershipRoleAssignments]' 'DELETE' $faultTarget $emptyBody 1
    Test-AtomicFailure 'assignment-insert' '[access].[MembershipRoleAssignments]' 'INSERT' $faultTarget (New-Body @($roleA,$roleB)) 1
    Test-AtomicFailure 'member-access-version' '[access].[MemberAccessVersions]' 'UPDATE' $faultTarget (New-Body @($roleA)) 1
    Test-AtomicFailure 'directory-revision' '[access].[WorkspaceDirectoryRevisions]' 'UPDATE' $faultTarget (New-Body @($roleA)) 1
    Test-AtomicFailure 'idempotency' '[access].[MemberAccessCommandIdempotencyRecords]' 'INSERT' $faultTarget (New-Body @($roleA)) 1
    Test-AtomicFailure 'governance-audit' '[access].[GovernanceCommandAudits]' 'INSERT' $faultTarget (New-Body @($roleA)) 1
    Test-AtomicFailure 'outbox' '[access].[OutboxEvents]' 'INSERT' $faultTarget (New-Body @($roleA)) 1

    $providerTarget = New-Membership 'post-commit-provider'
    $providerKey = 'idem-post-commit-provider-01'
    $delayTrigger = 'TR_VerifyMemberAccess_PostCommitDelay'
    Invoke-SqlNonQuery "CREATE TRIGGER [access].[$delayTrigger] ON [access].[MemberAccessCommandIdempotencyRecords] AFTER INSERT AS WAITFOR DELAY '00:00:04';"
    $client = New-Client 120
    $providerRequest = New-Request 'POST' "/access/members/$providerTarget/access" $emptyBody $script:Token $script:WorkspaceId $providerKey '"0"'
    try {
        $providerTask = $client.SendAsync($providerRequest)
        Start-Sleep -Milliseconds 1200
        Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
        $providerFailure = Convert-Response ($providerTask.GetAwaiter().GetResult())
    }
    finally {
        Invoke-SqlNonQuery "DROP TRIGGER [access].[$delayTrigger];"
        $providerRequest.Dispose()
        $client.Dispose()
    }
    Add-Result 'post-commit provider failure status' 503 $providerFailure.Status
    Add-Result 'post-commit provider failure code' 'INTEGRATION_UNAVAILABLE' $providerFailure.Body.code
    Add-Result 'post-commit provider failure preserves version' 1 (Get-Scalar "SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId='$script:WorkspaceId' AND MembershipId='$providerTarget'")
    $providerRecord = @(Invoke-Sql "SELECT * FROM access.MemberAccessCommandIdempotencyRecords WHERE IdempotencyKey='$providerKey'")[0]
    $providerEffects = Get-Effects
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"
    $providerReplay = Invoke-MemberReplace $providerTarget $emptyBody $providerKey '"0"'
    Add-Result 'provider recovery replay status' 200 $providerReplay.Status
    Add-Result 'provider recovery replay outcome' 'REPLAYED' $providerReplay.Body.outcome
    Add-Result 'provider recovery command identity' $providerRecord.CommandId $providerReplay.Body.commandId
    Add-Result 'provider recovery MemberAccessVersion' $providerRecord.MemberAccessVersion $providerReplay.Body.version
    Add-Result 'provider recovery zero owner effect' $providerEffects (Get-Effects)

    $accessRoot = Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl'
    $accessText = (Get-ChildItem $accessRoot -Recurse -File -Filter '*.cs' | Get-Content -Raw) -join "`n"
    Assert-True 'no WorkspaceDbContext in AccessControl' ($accessText -notmatch 'WorkspaceDbContext')
    Assert-True 'no IdentityAuthDbContext in AccessControl' ($accessText -notmatch 'IdentityAuthDbContext')
    Assert-True 'no cross-owner SQL in AccessControl' ($accessText -notmatch '\[workspace\]|\[iam\]|(?i)\b(?:FROM|JOIN|UPDATE|INSERT INTO|DELETE FROM)\s+(?:workspace|iam)\.')
    Assert-True 'member access uses shared DirectoryComposer' ($accessText -match 'ReplaceWorkspaceMemberAccess[\s\S]*DirectoryComposer')
    Add-Result 'exactly one member-access endpoint' 1 ([regex]::Matches($accessText, 'MapPost\("/access/members/\{membershipId\}/access"').Count)
    Assert-True 'no team mutation in member-access persistence' ((Get-Content -Raw (Join-Path $accessRoot 'Infrastructure/Persistence/EfReplaceWorkspaceMemberAccessPersistence.cs')) -notmatch 'Team')
    Assert-True 'no role-definition mutation in member-access persistence' ((Get-Content -Raw (Join-Path $accessRoot 'Infrastructure/Persistence/EfReplaceWorkspaceMemberAccessPersistence.cs')) -notmatch '\.Replace\(|\.Archive\(')
}
finally {
    if ($script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit()
    }
    Remove-Item Env:ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue
    if (-not $KeepDatabase) {
        try { Invoke-SqlNonQuery "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;" 'master' }
        catch { $script:Results.Add("WARN | database cleanup | $($_.Exception.Message)") }
    }
}

$script:Results | ForEach-Object { Write-Output $_ }
Write-Output "SUMMARY | passed=$script:Passed failed=$script:Failed"
if ($script:Failed -ne 0) { exit 1 }
