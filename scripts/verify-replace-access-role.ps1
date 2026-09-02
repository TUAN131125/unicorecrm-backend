<#
.SYNOPSIS
    Verifies the frozen replaceAccessRole owner-local command (DEC-REPLACEACCESSROLE-AUTHORITY-CLOSURE)
    against an isolated SQL database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5343,
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
$password = 'Replace-Access-Role-Verify!2026'
$email = 'admin@unicorecrm.local'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-replace-access-role-' + [Guid]::NewGuid().ToString('N'))
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
    if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-replace-role-' + $script:Counter.ToString('d6') }
    if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-replace-role-' + $script:Counter.ToString('d6') }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId) }
    if ($CorrelationId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId) }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    if (-not [string]::IsNullOrWhiteSpace($IfMatch)) { $null = $request.Headers.TryAddWithoutValidation('If-Match', $IfMatch) }
    # Windows PowerShell binds $null to a [string] parameter as the empty string, so a body-less
    # GET would still be given a content body and rejected with "Cannot send a content-body with
    # this verb-type" before it ever reached the host.
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
    [object] $Description = $null,
    [object] $SourceTemplateId = $null,
    [object] $IsActive = $true
) {
    $body = [ordered]@{ name = $Name; isActive = $IsActive; capabilities = $Capabilities; dataScopes = $DataScopes; fieldSecurity = $FieldSecurity }
    if ($null -ne $Description) { $body.description = $Description }
    if ($null -ne $SourceTemplateId) { $body.sourceTemplateId = $SourceTemplateId }
    return $body | ConvertTo-Json -Compress -Depth 12
}

function Invoke-Create([string] $Body, [string] $Key) {
    return Invoke-Api 'POST' '/access/roles' $Body $script:Token $script:WorkspaceId $Key
}

function Invoke-Replace(
    [string] $RoleId,
    [string] $Body,
    [string] $Key,
    [string] $IfMatch,
    [string] $Workspace = $script:WorkspaceId,
    [string] $Token = $script:Token,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    return Invoke-Api 'PUT' "/access/roles/$RoleId" $Body $Token $Workspace $Key $IfMatch $RequestId $CorrelationId
}

function New-Role([string] $Name, [string[]] $Capabilities = @('tasks.read'), [object[]] $DataScopes = @(), [object[]] $FieldSecurity = @()) {
    $body = [ordered]@{ name = $Name; capabilities = $Capabilities; dataScopes = $DataScopes; fieldSecurity = $FieldSecurity } | ConvertTo-Json -Compress -Depth 12
    $response = Invoke-Create $body ('idem-seed-' + [Guid]::NewGuid().ToString('N'))
    if ($response.Status -ne 200) { throw "Could not seed role '$Name': HTTP $($response.Status) $($response.Raw)" }
    return [string] $response.Body.aggregateId
}

function Get-EffectSnapshot {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.Roles'),
        (Get-Scalar 'SELECT COALESCE(SUM(Version),0) FROM access.Roles'),
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

function Test-AtomicFailure([string] $Name, [string] $Table, [string] $Action, [string] $RoleId, [string] $Body, [string] $IfMatch) {
    $trigger = 'TR_VerifyReplaceRole_' + ($Name -replace '[^A-Za-z0-9]', '')
    Invoke-SqlNonQuery "CREATE TRIGGER [access].[$trigger] ON $Table INSTEAD OF $Action AS THROW 51000, 'forced replaceAccessRole persistence failure', 1;"
    try {
        $before = Get-EffectSnapshot
        $response = Invoke-Replace $RoleId $Body ('idem-failure-' + [Guid]::NewGuid().ToString('N')) $IfMatch
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
    # The durable Initial Workspace Provisioning resume pass is the real provisioning
    # replay path; a short interval lets the verifier observe it without a long wait.
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

    $signIn = Invoke-Api 'POST' '/auth/sessions' (@{ email = $email; password = $password } | ConvertTo-Json -Compress) $null $null 'idem-replace-role-signin-01'
    Add-Result 'authentication fixture sign-in' 200 $signIn.Status
    $script:Token = $signIn.Body.accessToken
    $script:WorkspaceId = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo'")
    $foreignWorkspace = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo-isolated'")
    $accountId = [string] (Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())'")
    $membershipId = [string] (Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId='$script:WorkspaceId' AND AccountId='$accountId'")
    $memberId = [string] (Get-Scalar "SELECT MemberId FROM workspace.Memberships WHERE MembershipId='$membershipId'")
    $adminRoleId = [string] (Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.MembershipId='$membershipId' AND c.Capability='access.configure'")
    Assert-True 'trusted fixture exists' (-not [string]::IsNullOrWhiteSpace($script:WorkspaceId) -and -not [string]::IsNullOrWhiteSpace($adminRoleId))

    # ---------------------------------------------------------------- AUTHORIZATION
    $targetId = New-Role 'Authorization Target'
    $authBody = New-Body 'Authorization Target'
    Add-Result 'unauthenticated rejected' 401 (Invoke-Replace $targetId $authBody 'idem-unauth-000001' '"0"' $script:WorkspaceId $null).Status
    Add-Result 'unknown Workspace rejected' 403 (Invoke-Replace $targetId $authBody 'idem-unknownws-0001' '"0"' 'ws_does_not_exist').Status
    Add-Result 'foreign Workspace rejected' 403 (Invoke-Replace $targetId $authBody 'idem-foreignws-0001' '"0"' $foreignWorkspace).Status

    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='$membershipId';"
    Add-Result 'suspended membership rejected' 403 (Invoke-Replace $targetId $authBody 'idem-suspended-0001' '"0"').Status
    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='$membershipId';"

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE RoleId='$adminRoleId' AND Capability='access.configure';"
    $deniedUnknownRole = Invoke-Replace 'role_00000000000000000000000000000000' '{"name":""}' 'idem-capfirst-000001' 'garbage'
    Add-Result 'access.configure required' 403 $deniedUnknownRole.Status
    Add-Result 'capability denial precedes metadata, body and target checks' 'ACCESS_DENIED' $deniedUnknownRole.Body.code
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
        $rejected = Invoke-Replace $targetId $authBody ('idem-ifmatch-' + [Guid]::NewGuid().ToString('N')) $case.Value
        Add-Result "$($case.Name) status" 422 $rejected.Status
        Add-Result "$($case.Name) code" 'VALIDATION_FAILED' $rejected.Body.code
        Assert-True "$($case.Name) field error" ($null -ne $rejected.Body.fieldErrors.'If-Match')
    }

    # ---------------------------------------------------------------- TARGET SEMANTICS
    $unknown = Assert-NoEffect 'unknown role' { Invoke-Replace 'role_00000000000000000000000000000000' $authBody 'idem-unknownrole-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    $foreignRoleId = 'role_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$foreignRoleId','$foreignWorkspace','Foreign Target','FOREIGN TARGET',NULL,NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME());"
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$foreignRoleId','tasks.read');"
    $foreignTarget = Assert-NoEffect 'foreign Workspace role' { Invoke-Replace $foreignRoleId $authBody 'idem-foreignrole-01' '"0"' } 404 'RESOURCE_NOT_FOUND'
    Add-Result 'foreign role indistinguishable from unknown role' $unknown.Body.code $foreignTarget.Body.code

    # ---------------------------------------------------------------- ACTIVE STATE
    $inactiveBody = New-Body 'Authorization Target' @('tasks.read') @() @() $null $null $false
    $inactive = Assert-NoEffect 'isActive false' { Invoke-Replace $targetId $inactiveBody 'idem-isactivefalse-1' '"0"' } 422 'VALIDATION_FAILED'
    Assert-True 'isActive false field error' ($null -ne $inactive.Body.fieldErrors.isActive)
    $omittedActiveBody = '{"name":"Authorization Target","capabilities":["tasks.read"],"dataScopes":[],"fieldSecurity":[]}'
    $omittedActive = Assert-NoEffect 'isActive omitted' { Invoke-Replace $targetId $omittedActiveBody 'idem-isactivemiss-1' '"0"' } 422 'VALIDATION_FAILED'
    Assert-True 'isActive omitted field error' ($null -ne $omittedActive.Body.fieldErrors.isActive)

    $archivedId = New-Role 'Archived Target'
    Invoke-SqlNonQuery "UPDATE access.Roles SET IsActive=0 WHERE RoleId='$archivedId';"
    $null = Assert-NoEffect 'inactive target' { Invoke-Replace $archivedId (New-Body 'Archived Target') 'idem-inactive-00001' '"0"' } 409 'ROLE_INACTIVE'

    # ---------------------------------------------------------------- HAPPY PATH
    $originalId = New-Role 'Replacement Origin' @('tasks.read','tasks.create') @(
        @{ resourceKey = 'contacts'; scope = 'OWN' },
        @{ resourceKey = 'leads'; scope = 'WORKSPACE' }
    ) @(
        @{ resourceKey = 'contacts'; fieldKey = 'email'; access = 'READ_ONLY' },
        @{ resourceKey = 'leads'; fieldKey = 'phone'; access = 'HIDDEN' }
    )
    Invoke-SqlNonQuery "UPDATE access.Roles SET Description='original description', SourceTemplateId='original-template' WHERE RoleId='$originalId';"
    $keptScopeId = [string] (Get-Scalar "SELECT PolicyId FROM access.RoleDataScopes WHERE RoleId='$originalId' AND ResourceKey='contacts'")
    $removedScopeId = [string] (Get-Scalar "SELECT PolicyId FROM access.RoleDataScopes WHERE RoleId='$originalId' AND ResourceKey='leads'")
    $keptFieldId = [string] (Get-Scalar "SELECT PolicyId FROM access.RoleFieldSecurity WHERE RoleId='$originalId' AND ResourceKey='contacts' AND FieldKey='email'")
    $removedFieldId = [string] (Get-Scalar "SELECT PolicyId FROM access.RoleFieldSecurity WHERE RoleId='$originalId' AND ResourceKey='leads' AND FieldKey='phone'")

    $happyKey = 'idem-replace-happy-0001'
    $happyRequestId = 'req-replace-happy-0001'
    $happyCorrelationId = 'corr-replace-happy-0001'
    $happyBody = New-Body "  $([char]0x2003)Replaced Managers$([char]0x2003)  " @('access.read','tasks.read') @(
        @{ resourceKey = ' Contacts '; scope = 'CUSTOM'; allowedOwnerIds = @('mem_owner_002','mem_owner_001') },
        @{ resourceKey = 'deals'; scope = 'TEAM' }
    ) @(
        @{ resourceKey = ' Contacts '; fieldKey = ' Email '; access = 'MASKED' },
        @{ resourceKey = 'deals'; fieldKey = 'amount'; access = 'READ_WRITE' }
    )
    $revisionBefore = Get-Revision
    $assignmentsBefore = [long] (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments')
    $createdAtBefore = [string] (Get-Scalar "SELECT CONVERT(varchar(33), CreatedAt, 127) FROM access.Roles WHERE RoleId='$originalId'")
    $happy = Invoke-Replace $originalId $happyBody $happyKey '"0"' $script:WorkspaceId $script:Token $happyRequestId $happyCorrelationId
    Add-Result 'valid replacement status' 200 $happy.Status
    if ($happy.Status -ne 200) { throw "Happy-path replacement failed: HTTP $($happy.Status) $($happy.Raw)" }

    Add-Result 'aggregate id matches target' $originalId $happy.Body.aggregateId
    Add-Result 'aggregate type' 'ACCESS_ROLE' $happy.Body.aggregateType
    Add-Result 'resulting version is prior + 1' 1 $happy.Body.version
    Add-Result 'fresh outcome' 'COMMITTED' $happy.Body.outcome
    Assert-True 'command ID format' ([string] $happy.Body.commandId -cmatch '^command_[0-9a-f]{32}$')
    Assert-True 'audit ID format' ([string] $happy.Body.auditEvidenceIds[0] -cmatch '^audit_[0-9a-f]{32}$')
    Assert-True 'event ID format' ([string] $happy.Body.emittedEventIds[0] -cmatch '^event_[0-9a-f]{32}$')
    $requiredTop = @('commandId','correlationId','aggregateId','aggregateType','version','occurredAt','outcome','result')
    $requiredDirectory = @('workspaceId','revision','generatedAt','members','memberProfiles','invitations','roles','assignments','dataScopes','fieldSecurity')
    Add-Result 'response required top-level shape' 0 (@($requiredTop | Where-Object { $null -eq $happy.Body.PSObject.Properties[$_] }).Count)
    Add-Result 'response exact directory required shape' 0 (@($requiredDirectory | Where-Object { $null -eq $happy.Body.result.PSObject.Properties[$_] }).Count)

    Add-Result 'display name normalized' 'Replaced Managers' (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$originalId'")
    Add-Result 'normalized name persisted' 'REPLACED MANAGERS' (Get-Scalar "SELECT NormalizedName FROM access.Roles WHERE RoleId='$originalId'")
    Add-Result 'omitted description cleared to null' 'True' ([string]::IsNullOrEmpty([string] (Get-Scalar "SELECT ISNULL(Description,'') FROM access.Roles WHERE RoleId='$originalId'"))).ToString()
    Add-Result 'omitted sourceTemplateId cleared to null' 'True' ([string]::IsNullOrEmpty([string] (Get-Scalar "SELECT ISNULL(SourceTemplateId,'') FROM access.Roles WHERE RoleId='$originalId'"))).ToString()
    Add-Result 'active state unchanged' 'True' ([bool] (Get-Scalar "SELECT IsActive FROM access.Roles WHERE RoleId='$originalId'")).ToString()
    Add-Result 'role version persisted' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$originalId'")
    Add-Result 'createdAt unchanged' $createdAtBefore (Get-Scalar "SELECT CONVERT(varchar(33), CreatedAt, 127) FROM access.Roles WHERE RoleId='$originalId'")
    Add-Result 'workspace unchanged' $script:WorkspaceId (Get-Scalar "SELECT WorkspaceId FROM access.Roles WHERE RoleId='$originalId'")

    Add-Result 'capabilities exactly replaced' 'access.read,tasks.read' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$originalId' ORDER BY Capability").Capability -join ',')
    Add-Result 'stale capability removed' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities WHERE RoleId='$originalId' AND Capability='tasks.create'")
    Add-Result 'data scopes exactly replaced' 'contacts,deals' ((Invoke-Sql "SELECT ResourceKey FROM access.RoleDataScopes WHERE RoleId='$originalId' ORDER BY ResourceKey").ResourceKey -join ',')
    Add-Result 'stale data scope removed' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleDataScopes WHERE PolicyId='$removedScopeId'")
    Add-Result 'field security exactly replaced' 'contacts/email,deals/amount' ((Invoke-Sql "SELECT ResourceKey + '/' + FieldKey AS K FROM access.RoleFieldSecurity WHERE RoleId='$originalId' ORDER BY ResourceKey, FieldKey").K -join ',')
    Add-Result 'stale field security removed' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleFieldSecurity WHERE PolicyId='$removedFieldId'")

    Add-Result 'unchanged data-scope key keeps policy identity' $keptScopeId (Get-Scalar "SELECT PolicyId FROM access.RoleDataScopes WHERE RoleId='$originalId' AND ResourceKey='contacts'")
    Add-Result 'retained data-scope value replaced' 'Custom' (Get-Scalar "SELECT Scope FROM access.RoleDataScopes WHERE PolicyId='$keptScopeId'")
    Add-Result 'retained data-scope owners canonical' '["mem_owner_001","mem_owner_002"]' (Get-Scalar "SELECT AllowedOwnerIdsJson FROM access.RoleDataScopes WHERE PolicyId='$keptScopeId'")
    Add-Result 'unchanged field-security key keeps policy identity' $keptFieldId (Get-Scalar "SELECT PolicyId FROM access.RoleFieldSecurity WHERE RoleId='$originalId' AND ResourceKey='contacts' AND FieldKey='email'")
    Add-Result 'retained field-security value replaced' 'Masked' (Get-Scalar "SELECT Access FROM access.RoleFieldSecurity WHERE PolicyId='$keptFieldId'")
    Assert-True 'new data-scope key gets fresh owner ID' ([string] (Get-Scalar "SELECT PolicyId FROM access.RoleDataScopes WHERE RoleId='$originalId' AND ResourceKey='deals'") -cmatch '^scope_[0-9a-f]{32}$')
    Assert-True 'new field-security key gets fresh owner ID' ([string] (Get-Scalar "SELECT PolicyId FROM access.RoleFieldSecurity WHERE RoleId='$originalId' AND ResourceKey='deals' AND FieldKey='amount'") -cmatch '^field_[0-9a-f]{32}$')

    Add-Result 'no assignment mutation' $assignmentsBefore (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments')
    Add-Result 'directory revision increments once' ($revisionBefore + 1) (Get-Revision)
    Add-Result 'directory revision in response' ($revisionBefore + 1) $happy.Body.result.revision
    $replacedRoleDocument = @($happy.Body.result.roles | Where-Object { $_.roleId -eq $originalId })[0]
    Add-Result 'returned directory reflects replaced role name' 'Replaced Managers' $replacedRoleDocument.name
    Add-Result 'returned directory reflects resulting version' 1 $replacedRoleDocument.version
    Add-Result 'returned directory reflects replaced capabilities' 'access.read,tasks.read' ($replacedRoleDocument.capabilities -join ',')
    Assert-True 'shared composer supplied Workspace and Identity facts' ($happy.Body.result.members[0].workspaceKey.Length -gt 0 -and @($happy.Body.result.memberProfiles).Count -gt 0)
    Add-Result 'command writes no directory read evidence' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.DirectoryReadAccessRecords")

    # ---------------------------------------------------------------- AUDIT / EVENT
    $audit = @(Invoke-Sql "SELECT * FROM access.GovernanceCommandAudits WHERE CommandId='$($happy.Body.commandId)'")[0]
    Add-Result 'exactly one governance audit' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits WHERE CommandId='$($happy.Body.commandId)'")
    Add-Result 'audit discriminator' 'ACCESS_GOVERNANCE_COMMAND' $audit.EvidenceType
    Add-Result 'audit operation' 'replaceAccessRole' $audit.OperationId
    Add-Result 'audit role' $originalId $audit.RoleId
    Add-Result 'audit trusted account' $accountId $audit.ActorAccountId
    Add-Result 'audit trusted membership' $membershipId $audit.ActorMembershipId
    Add-Result 'audit trusted member' $memberId $audit.ActorMemberId
    Add-Result 'audit request provenance' $happyRequestId $audit.RequestId
    Add-Result 'audit correlation provenance' $happyCorrelationId $audit.CorrelationId
    Add-Result 'audit prior version' 0 $audit.PriorVersion
    Add-Result 'audit resulting version' 1 $audit.ResultingVersion
    Add-Result 'audit outcome' 'COMMITTED' $audit.Outcome
    Add-Result 'audit timestamp shared with response' ([DateTimeOffset] $happy.Body.occurredAt) ([DateTimeOffset] $audit.OccurredAt)
    Assert-True 'audit occurredAt UTC offset zero' ([DateTimeOffset] $audit.OccurredAt).Offset.Equals([TimeSpan]::Zero)
    $auditColumns = ((Invoke-Sql "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='access' AND TABLE_NAME='GovernanceCommandAudits' ORDER BY ORDINAL_POSITION").COLUMN_NAME -join ',')
    Assert-True 'audit excludes business arrays and directory' ($auditColumns -notmatch 'Capability|DataScope|FieldSecurity|Directory|Name|Description|Template')

    $event = @(Invoke-Sql "SELECT * FROM access.OutboxEvents WHERE CausationId='$($happy.Body.commandId)'")[0]
    Add-Result 'exactly one replacement event' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE CausationId='$($happy.Body.commandId)'")
    Add-Result 'event type' 'ACCESS_ROLE_REPLACED' $event.EventType
    Add-Result 'no archive event emitted' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE EventType='ACCESS_ROLE_ARCHIVED'")
    Add-Result 'event aggregate id' $originalId $event.AggregateId
    Add-Result 'event aggregate type' 'ACCESS_ROLE' $event.AggregateType
    Add-Result 'event aggregate version' 1 $event.AggregateVersion
    Add-Result 'event correlation provenance' $happyCorrelationId $event.CorrelationId
    Add-Result 'event timestamp shared with response' ([DateTimeOffset] $happy.Body.occurredAt) ([DateTimeOffset] $event.OccurredAt)
    Add-Result 'event minimal payload' ("{`"roleId`":`"$originalId`",`"version`":1}") $event.PayloadJson

    # ---------------------------------------------------------------- IDEMPOTENCY
    $beforeReplay = Get-EffectSnapshot
    $replay = Invoke-Replace $originalId $happyBody $happyKey '"0"'
    Add-Result 'same-key replay status' 200 $replay.Status
    Add-Result 'replay outcome' 'REPLAYED' $replay.Body.outcome
    Add-Result 'replay command identity' $happy.Body.commandId $replay.Body.commandId
    Add-Result 'replay role identity' $happy.Body.aggregateId $replay.Body.aggregateId
    Add-Result 'replay version identity' $happy.Body.version $replay.Body.version
    Add-Result 'replay audit identity' $happy.Body.auditEvidenceIds[0] $replay.Body.auditEvidenceIds[0]
    Add-Result 'replay event identity' $happy.Body.emittedEventIds[0] $replay.Body.emittedEventIds[0]
    Add-Result 'replay creates no effects' $beforeReplay (Get-EffectSnapshot)

    $changed = Assert-NoEffect 'changed payload under same key' { Invoke-Replace $originalId (New-Body 'Changed Replacement') $happyKey '"0"' } 409 'IDEMPOTENCY_KEY_REUSED'
    Add-Result 'idempotency conflict echoes key' $happyKey $changed.Body.idempotencyKey
    $otherRoleId = New-Role 'Other Idempotency Target'
    $null = Assert-NoEffect 'same key aimed at another role' { Invoke-Replace $otherRoleId $happyBody $happyKey '"0"' } 409 'IDEMPOTENCY_KEY_REUSED'
    $createKey = 'idem-shared-scope-0001'
    $createdForScope = Invoke-Create (@{ name = 'Shared Key Scope'; capabilities = @('tasks.read'); dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress) $createKey
    Add-Result 'createAccessRole accepts the shared key' 200 $createdForScope.Status
    $scopeReplace = Invoke-Replace ([string] $createdForScope.Body.aggregateId) (New-Body 'Shared Key Scope Replaced') $createKey '"0"'
    Add-Result 'replace scope is independent of create scope' 200 $scopeReplace.Status
    Add-Result 'replace under reused create key still commits' 'COMMITTED' $scopeReplace.Body.outcome

    # ---------------------------------------------------------------- VERSION / CONCURRENCY
    $current = [long] (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$originalId'")
    $null = Assert-NoEffect 'stale expected version' { Invoke-Replace $originalId (New-Body 'Stale Version Attempt') ('idem-stale-' + [Guid]::NewGuid().ToString('N')) '"0"' } 412 'VERSION_CONFLICT'
    $identical = Invoke-Replace $originalId $happyBody ('idem-identical-' + [Guid]::NewGuid().ToString('N')) "`"$current`""
    Add-Result 'effective-identical replacement still commits' 200 $identical.Status
    Add-Result 'effective-identical replacement increments version' ($current + 1) $identical.Body.version
    Add-Result 'effective-identical replacement increments revision' $identical.Body.result.revision (Get-Revision)

    $concurrentId = New-Role 'Concurrent Replacement Target'
    $concurrentBody = New-Body 'Concurrent Replacement Result' @('tasks.read')
    $clientA = New-Client 120
    $clientB = New-Client 120
    function New-ConcurrentRequest([int] $Suffix) {
        $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "$script:BaseUrl/access/roles/$concurrentId")
        $message.Content = [System.Net.Http.StringContent]::new($concurrentBody, [Text.Encoding]::UTF8, 'application/json')
        $null = $message.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $message.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $message.Headers.TryAddWithoutValidation('X-Request-Id', "req-replace-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('X-Correlation-Id', "corr-replace-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('Idempotency-Key', "idem-replace-concurrent-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('If-Match', '"0"')
        return $message
    }
    $messageA = New-ConcurrentRequest 1
    $messageB = New-ConcurrentRequest 2
    try {
        $taskA = $clientA.SendAsync($messageA)
        $taskB = $clientB.SendAsync($messageB)
        [Threading.Tasks.Task]::WaitAll(@($taskA, $taskB))
        $statuses = @([int] $taskA.Result.StatusCode, [int] $taskB.Result.StatusCode) | Sort-Object
        Add-Result 'concurrent same-version replacements resolve to one commit and one conflict' '200,412' ($statuses -join ',')
        Add-Result 'concurrent replacement leaves exactly one version step' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$concurrentId'")
    }
    finally { $messageA.Dispose(); $messageB.Dispose(); $clientA.Dispose(); $clientB.Dispose() }

    $convergeId = New-Role 'Same Key Convergence Target'
    $convergeBody = New-Body 'Same Key Convergence Result' @('tasks.read')
    $clientC = New-Client 120
    $clientD = New-Client 120
    function New-SameKeyRequest([int] $Suffix) {
        $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "$script:BaseUrl/access/roles/$convergeId")
        $message.Content = [System.Net.Http.StringContent]::new($convergeBody, [Text.Encoding]::UTF8, 'application/json')
        $null = $message.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $message.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $message.Headers.TryAddWithoutValidation('X-Request-Id', "req-replace-samekey-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('X-Correlation-Id', "corr-replace-samekey-000$Suffix")
        $null = $message.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-replace-samekey-0001')
        $null = $message.Headers.TryAddWithoutValidation('If-Match', '"0"')
        return $message
    }
    $messageC = New-SameKeyRequest 1
    $messageD = New-SameKeyRequest 2
    try {
        $taskC = $clientC.SendAsync($messageC)
        $taskD = $clientD.SendAsync($messageD)
        [Threading.Tasks.Task]::WaitAll(@($taskC, $taskD))
        $rawC = $taskC.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $rawD = $taskD.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Add-Result 'concurrent same-key response C' 200 ([int] $taskC.Result.StatusCode)
        Add-Result 'concurrent same-key response D' 200 ([int] $taskD.Result.StatusCode)
        Add-Result 'concurrent same-key converges on one command' (($rawC | ConvertFrom-Json).commandId) (($rawD | ConvertFrom-Json).commandId)
        Add-Result 'concurrent same-key commits exactly one version step' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$convergeId'")
    }
    finally { $messageC.Dispose(); $messageD.Dispose(); $clientC.Dispose(); $clientD.Dispose() }

    # ---------------------------------------------------------------- NAME + VALIDATION
    $selfNameId = New-Role 'Self Name Role'
    $selfName = Invoke-Replace $selfNameId (New-Body ' self name role ' @('tasks.read')) 'idem-selfname-000001' '"0"'
    Add-Result 'role may retain its own normalized name' 200 $selfName.Status
    Add-Result 'retained normalized name' 'SELF NAME ROLE' (Get-Scalar "SELECT NormalizedName FROM access.Roles WHERE RoleId='$selfNameId'")
    Add-Result 'retained display name is the new trimmed spelling' 'self name role' (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$selfNameId'")
    $null = Assert-NoEffect 'other-role normalized name conflict' { Invoke-Replace $selfNameId (New-Body '  REPLACED managers  ') 'idem-nameconflict-01' '"1"' } 409 'ROLE_NAME_CONFLICT'
    Assert-True 'database uniqueness backstop present' ((Get-Scalar "SELECT COUNT_BIG(*) FROM sys.indexes i JOIN sys.objects o ON o.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE s.name='access' AND o.name='Roles' AND i.is_unique=1 AND i.name LIKE '%NormalizedName%'") -ge 1)
    $unicodeId = New-Role 'Unicode Replacement Target'
    # Built from code points rather than literals: Windows PowerShell decodes a BOM-less script as
    # ANSI, so a literal non-ASCII character would be mangled before it ever reached the host.
    $uDieresis = [char]0x00DC
    $oCircumflexLower = [char]0x00F4
    $oCircumflexUpper = [char]0x00D4
    $unicodeName = "$($uDieresis)nicode R$($oCircumflexLower)le"
    $unicodeReplace = Invoke-Replace $unicodeId (New-Body "  $([char]0x2003)$unicodeName$([char]0x2003)  ") 'idem-unicode-0000001' '"0"'
    Add-Result 'Unicode replacement status' 200 $unicodeReplace.Status
    Add-Result 'Unicode display name trimmed' $unicodeName (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$unicodeId'")
    Add-Result 'Unicode normalized name uppercased' "$($uDieresis)NICODE R$($oCircumflexUpper)LE" (Get-Scalar "SELECT NormalizedName FROM access.Roles WHERE RoleId='$unicodeId'")

    $validationId = New-Role 'Validation Target'
    foreach ($case in @(
        @{ Name = 'blocked capability'; Body = (New-Body 'Validation Target' @('contacts.create')) },
        @{ Name = 'unknown capability'; Body = (New-Body 'Validation Target' @('unknown.capability')) },
        @{ Name = 'wrong-case capability'; Body = (New-Body 'Validation Target' @('Tasks.Read')) },
        @{ Name = 'duplicate capability'; Body = (New-Body 'Validation Target' @('tasks.read','tasks.read')) },
        @{ Name = 'over 500 capabilities'; Body = (New-Body 'Validation Target' (@('tasks.read') * 501)) },
        @{ Name = 'empty trimmed name'; Body = (New-Body " $([char]0x2003) ") },
        @{ Name = 'duplicate canonical data scope'; Body = (New-Body 'Validation Target' @('tasks.read') @(@{resourceKey='Contacts';scope='OWN'},@{resourceKey=' contacts ';scope='TEAM'})) },
        @{ Name = 'duplicate canonical field pair'; Body = (New-Body 'Validation Target' @('tasks.read') @() @(@{resourceKey='Contacts';fieldKey='Email';access='READ_ONLY'},@{resourceKey=' contacts ';fieldKey=' email ';access='HIDDEN'})) },
        @{ Name = 'non-CUSTOM owner ids'; Body = (New-Body 'Validation Target' @('tasks.read') @(@{resourceKey='contacts';scope='OWN';allowedOwnerIds=@('mem_owner_001')})) },
        @{ Name = 'invalid scope enum'; Body = (New-Body 'Validation Target' @('tasks.read') @(@{resourceKey='contacts';scope='EVERYTHING'})) },
        @{ Name = 'invalid access enum'; Body = (New-Body 'Validation Target' @('tasks.read') @() @(@{resourceKey='contacts';fieldKey='email';access='WRITE_ONLY'})) }
    )) {
        $null = Assert-NoEffect $case.Name { Invoke-Replace $validationId $case.Body ('idem-val-' + [Guid]::NewGuid().ToString('N')) '"0"' } 422 'VALIDATION_FAILED'
    }
    $missingKey = Invoke-Replace $validationId (New-Body 'Validation Target') $null '"0"'
    Add-Result 'missing idempotency key rejected' 422 $missingKey.Status

    # ---------------------------------------------------------------- LAST ADMINISTRATOR
    $adminReplaceBody = New-Body 'Sole Administrator Role' @('tasks.read')
    $adminVersion = [long] (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$adminRoleId'")
    $adminName = [string] (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$adminRoleId'")
    $soleAdminBody = New-Body $adminName @('tasks.read')
    $null = Assert-NoEffect 'removing access.configure from the last administrator' { Invoke-Replace $adminRoleId $soleAdminBody 'idem-lastadmin-00001' "`"$adminVersion`"" } 409 'LAST_WORKSPACE_ADMINISTRATOR'

    $secondAdminId = New-Role 'Second Administrator Role' @('access.configure','tasks.read')
    Invoke-SqlNonQuery "INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES('assignment_$(( [Guid]::NewGuid().ToString('N') ))','$script:WorkspaceId','$membershipId','$secondAdminId',SYSUTCDATETIME());"
    $withPeer = Invoke-Replace $adminRoleId $soleAdminBody 'idem-adminpeer-000001' "`"$adminVersion`""
    Add-Result 'removing access.configure with another administrator present succeeds' 200 $withPeer.Status
    Add-Result 'administrative capability actually removed' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities WHERE RoleId='$adminRoleId' AND Capability='access.configure'")
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('$adminRoleId','access.configure');"

    # A suspended membership is not an active administrator, so the guard must engage again.
    Invoke-SqlNonQuery "DELETE FROM access.MembershipRoleAssignments WHERE RoleId='$secondAdminId';"
    $keepsCapability = Invoke-Replace $secondAdminId (New-Body 'Second Administrator Role' @('access.configure','tasks.create')) 'idem-adminkeep-000001' '"0"'
    Add-Result 'replacement that keeps access.configure needs no provider facts' 200 $keepsCapability.Status

    $logo = [string] (Get-Scalar "SELECT LogoText FROM workspace.Workspaces WHERE WorkspaceId='$script:WorkspaceId'")
    $adminVersion2 = [long] (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$adminRoleId'")
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
    $guardStale = [long] (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$adminRoleId'")
    $guardVersionConflict = Assert-NoEffect 'stale version outranks the provider-dependent guard' { Invoke-Replace $adminRoleId $soleAdminBody ('idem-guardorder-' + [Guid]::NewGuid().ToString('N')) '"0"' } 412 'VERSION_CONFLICT'
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"

    # ---------------------------------------------------------------- ATOMICITY
    $atomicId = New-Role 'Atomic Failure Target' @('tasks.read') @(@{resourceKey='tasks';scope='WORKSPACE'}) @(@{resourceKey='tasks';fieldKey='title';access='READ_ONLY'})
    $atomicBody = New-Body 'Atomic Failure Replacement' @('access.read') @(@{resourceKey='deals';scope='OWN'}) @(@{resourceKey='deals';fieldKey='amount';access='HIDDEN'})
    Test-AtomicFailure 'role' 'access.Roles' 'UPDATE' $atomicId $atomicBody '"0"'
    Test-AtomicFailure 'capabilities' 'access.RoleCapabilities' 'INSERT' $atomicId $atomicBody '"0"'
    Test-AtomicFailure 'scopes' 'access.RoleDataScopes' 'INSERT' $atomicId $atomicBody '"0"'
    Test-AtomicFailure 'fields' 'access.RoleFieldSecurity' 'INSERT' $atomicId $atomicBody '"0"'
    Test-AtomicFailure 'revision' 'access.WorkspaceDirectoryRevisions' 'UPDATE' $atomicId $atomicBody '"0"'
    Test-AtomicFailure 'idempotency' 'access.AccessRoleCommandIdempotencyRecords' 'INSERT' $atomicId $atomicBody '"0"'
    Test-AtomicFailure 'audit' 'access.GovernanceCommandAudits' 'INSERT' $atomicId $atomicBody '"0"'
    Test-AtomicFailure 'outbox' 'access.OutboxEvents' 'INSERT' $atomicId $atomicBody '"0"'

    # ---------------------------------------------------------------- PROVIDER FAILURE / REPLAY
    $providerId = New-Role 'Provider Recovery Target'
    $providerBody = New-Body 'Provider Recovery Replacement' @('tasks.read')
    $providerKey = 'idem-replace-provider-01'
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='' WHERE WorkspaceId='$script:WorkspaceId';"
    $providerFailure = Invoke-Replace $providerId $providerBody $providerKey '"0"'
    Add-Result 'post-commit invalid provider returns 503' 503 $providerFailure.Status
    Add-Result 'post-commit provider error code' 'INTEGRATION_UNAVAILABLE' $providerFailure.Body.code
    $providerRecord = @(Invoke-Sql "SELECT * FROM access.AccessRoleCommandIdempotencyRecords WHERE IdempotencyKey='$providerKey'")[0]
    Add-Result 'provider failure keeps the replacement committed' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$providerId'")
    Add-Result 'provider failure audit remains committed' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits WHERE CommandId='$($providerRecord.CommandId)'")
    Add-Result 'provider failure event remains committed' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.OutboxEvents WHERE CausationId='$($providerRecord.CommandId)'")
    $providerEffects = Get-EffectSnapshot
    Invoke-SqlNonQuery "UPDATE workspace.Workspaces SET LogoText='$logo' WHERE WorkspaceId='$script:WorkspaceId';"
    $providerReplay = Invoke-Replace $providerId $providerBody $providerKey '"0"'
    Add-Result 'provider recovery replay status' 200 $providerReplay.Status
    Add-Result 'provider recovery replay outcome' 'REPLAYED' $providerReplay.Body.outcome
    Add-Result 'provider recovery preserves command' $providerRecord.CommandId $providerReplay.Body.commandId
    Add-Result 'provider recovery preserves role' $providerRecord.RoleId $providerReplay.Body.aggregateId
    Add-Result 'provider recovery preserves version' $providerRecord.RoleVersion $providerReplay.Body.version
    Add-Result 'provider recovery preserves audit' $providerRecord.AuditEvidenceId $providerReplay.Body.auditEvidenceIds[0]
    Add-Result 'provider recovery preserves event' $providerRecord.EventId $providerReplay.Body.emittedEventIds[0]
    Add-Result 'provider recovery creates no effects' $providerEffects (Get-EffectSnapshot)

    # ---------------------------------------------------------------- INITIAL PROVISIONING RECONCILIATION
    # The dev-seeded Workspace is not produced by Initial Workspace Provisioning, so the
    # reconciliation is proved against explicit anchors in the shape the provisioning participant
    # actually sees. Every fixture row is an owner-local scalar insert: there is no cross-owner
    # foreign key, so building them mutates no foreign owner through this command.
    function New-ProvisioningFixture(
        [string] $Label,
        [string] $RoleName,
        [string] $NormalizedRoleName,
        [long] $RoleVersion,
        [string[]] $Capabilities,
        [string] $RoleDescription = 'Initial Workspace provisioning role for the account that created this Workspace.'
    ) {
        $suffix = [Guid]::NewGuid().ToString('N')
        $fixture = [pscustomobject] @{
            Label = $Label
            AccountId = "acc_$suffix"
            MemberId = "mem_$suffix"
            WorkspaceId = "ws_$suffix"
            MembershipId = "wsm_$suffix"
            RoleId = "role_$suffix"
            AssignmentId = "assignment_$suffix"
        }
        Invoke-SqlNonQuery "INSERT INTO workspace.Workspaces (WorkspaceId,[Key],Name,LogoText,CreatedAt) VALUES ('$($fixture.WorkspaceId)','replace-$Label-$($suffix.Substring(0,8))','Replace $Label Workspace','RP',SYSUTCDATETIME());"
        Invoke-SqlNonQuery "INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('$($fixture.MembershipId)','$($fixture.WorkspaceId)','$($fixture.AccountId)','$($fixture.MemberId)','Active',SYSUTCDATETIME());"
        Invoke-SqlNonQuery "INSERT INTO workspace.BootstrapProjections (WorkspaceId,ContextVersion,ConfigurationVersion,Locale,TimeZone,BaseCurrency,CapabilitiesJson,EnabledModuleKeysJson,AvailableProductSpacesJson) VALUES ('$($fixture.WorkspaceId)',0,0,'en','UTC','USD','[]','[""leads"",""deals"",""tasks""]','[""crm""]');"
        Invoke-SqlNonQuery "INSERT INTO workspace.InitialProvisioningRecords (AccountId,MemberId,WorkspaceId,MembershipId,IdempotencyKey,RequestFingerprint,State,CompletedAt,ProvisionedAt) VALUES ('$($fixture.AccountId)','$($fixture.MemberId)','$($fixture.WorkspaceId)','$($fixture.MembershipId)','idem-replace-$Label','0000000000000000000000000000000000000000000000000000000000000000','AccessPending',NULL,SYSUTCDATETIME());"
        Invoke-SqlNonQuery "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$($fixture.RoleId)','$($fixture.WorkspaceId)','$RoleName','$NormalizedRoleName','$RoleDescription',NULL,1,$RoleVersion,SYSUTCDATETIME(),SYSUTCDATETIME());"
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

    # Replaced: the initial role carries a caller-chosen name and a non-zero version, exactly the
    # state an admitted replaceAccessRole leaves behind. Before this reconciliation the participant
    # anchored on the seeded display name and threw, leaving the anchor outstanding forever.
    $replacedFixture = New-ProvisioningFixture 'replaced' 'Renamed Owner Role' 'RENAMED OWNER ROLE' 1 @('access.configure', 'tasks.read') 'Caller-owned description after replacement.'
    # Impostor: an unrelated role that merely took the freed seeded display name. The assignment
    # anchor must never adopt it as the canonical seed.
    $impostorRoleId = 'role_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery "INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt) VALUES ('$impostorRoleId','$($replacedFixture.WorkspaceId)','Workspace Owner','WORKSPACE OWNER','Unrelated role that took the seeded name.',NULL,1,2,SYSUTCDATETIME(),SYSUTCDATETIME());"
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$impostorRoleId','tasks.read');"
    # Drift: never replaced, so the server-owned seed invariants still apply and it must fail closed
    # exactly as before. This proves the reconciliation did not weaken the existing guard.
    $driftFixture = New-ProvisioningFixture 'drift' 'Workspace Owner' 'WORKSPACE OWNER' 0 @('tasks.read')

    $provisioningEffectsBefore = Get-EffectSnapshot
    Assert-True 'provisioning replay converges after a legitimate replacement' (Wait-AnchorState $replacedFixture.AccountId 'Completed')
    Add-Result 'provisioning replay does not rewrite the replaced name' 'Renamed Owner Role' (Get-Scalar "SELECT Name FROM access.Roles WHERE RoleId='$($replacedFixture.RoleId)'")
    Add-Result 'provisioning replay does not rewrite the replaced capabilities' 'access.configure,tasks.read' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$($replacedFixture.RoleId)' ORDER BY Capability").Capability -join ',')
    Add-Result 'provisioning replay does not reset the replaced version' 1 (Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId='$($replacedFixture.RoleId)'")
    Add-Result 'provisioning replay keeps the original assignment' $replacedFixture.AssignmentId (Get-Scalar "SELECT TOP 1 AssignmentId FROM access.MembershipRoleAssignments WHERE MembershipId='$($replacedFixture.MembershipId)'")
    Add-Result 'provisioning replay creates no second assignment' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE MembershipId='$($replacedFixture.MembershipId)'")
    Add-Result 'provisioning replay creates no seeded-name duplicate role' 1 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.Roles WHERE WorkspaceId='$($replacedFixture.WorkspaceId)' AND NormalizedName='WORKSPACE OWNER'")
    Add-Result 'impostor role capabilities untouched' 'tasks.read' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$impostorRoleId' ORDER BY Capability").Capability -join ',')
    Add-Result 'impostor role gains no assignment' 0 (Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE RoleId='$impostorRoleId'")
    Add-Result 'never-replaced drift still fails closed' 'AccessPending' (Get-AnchorState $driftFixture.AccountId)
    Add-Result 'drifted role receives no server-owned capabilities' 'tasks.read' ((Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId='$($driftFixture.RoleId)' ORDER BY Capability").Capability -join ',')
    Add-Result 'provisioning replay mutates no AccessControl state' $provisioningEffectsBefore (Get-EffectSnapshot)

    # ---------------------------------------------------------------- BOUNDARIES
    $accessFiles = Get-ChildItem (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl') -Recurse -Filter '*.cs'
    $accessText = ($accessFiles | Get-Content -Raw) -join "`n"
    Assert-True 'no foreign DbContext in AccessControl' ($accessText -notmatch 'WorkspaceDbContext|IdentityAuthDbContext')
    Assert-True 'no cross-owner SQL in AccessControl' ($accessText -notmatch '\[workspace\]|\[iam\]|(?i)\b(?:FROM|JOIN)\s+(?:workspace|iam)\.')
    Add-Result 'exactly one replace route mapping' 1 ([regex]::Matches($accessText, 'MapPut\("/access/roles/\{roleId\}"').Count)
    # archiveAccessRole is a separately admitted operation that exclusively owns the lifecycle
    # transition. What must hold here is that replaceAccessRole never performs it.
    Add-Result 'exactly one archive route mapping' 1 ([regex]::Matches($accessText, 'MapPost\("/access/roles/\{roleId\}/archive"').Count)
    Add-Result 'replacement performs no lifecycle transition' 0 ([regex]::Matches(((Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Infrastructure/Persistence/EfReplaceAccessRolePersistence.cs'))), '\.Archive\(|IsActive\s*=').Count)
    Assert-True 'replacement uses the shared directory composer' ($accessText -match 'namespace UnicoreCRM\.Platform\.AccessControl\.Application\.AccessDirectory')
    Add-Result 'no invitation or membership mutation surface' 0 ([regex]::Matches($accessText, 'Invitations\.(Add|Remove|Update)|Memberships\.(Add|Remove|Update)').Count)
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
