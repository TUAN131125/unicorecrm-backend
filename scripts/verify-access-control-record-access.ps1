<#
.SYNOPSIS
    Reproducible AccessControl Record Access Core runtime verification.

.DESCRIPTION
    Provisions an isolated database, starts UnicoreCRM.ApiHost against it, and exercises
    evaluateEffectiveRecordAccess (POST /access/records/evaluate) against the frozen record-access
    semantics: capability-first authorization, WORKSPACE/OWN scope, fail-closed TEAM and CUSTOM
    scope, field-security precedence, existence-leakage collapse, record-fact spoofing rejection
    and the AccessControl-owned decision audit.

    Data-scope and field-security policies have no admitted write operation yet, so the harness
    seeds them directly into the AccessControl-owned tables and reads the effect back over HTTP.
    That is deliberate: it exercises the stored policy the evaluator actually reads, without
    inventing an administration surface this task does not admit.

    Windows PowerShell 5.1 compatible: no pipeline chain operators, ternary, null-coalescing or
    -AsHashtable.

.EXAMPLE
    ./verify-access-control-record-access.ps1 -DatabaseName UnicoreCRM_RecordAccess_Verify
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    # Omit both to use a trusted connection.
    [string] $SqlUserId,
    [string] $SqlPassword,

    [int] $Port = 5317,

    [int] $ReadyTimeoutSeconds = 420,

    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$script:Passed = 0
$script:Failed = 0
$script:Results = New-Object System.Collections.ArrayList

function Add-Result {
    param([string] $Name, [string] $Expected, [string] $Actual)
    if ($Expected -eq $Actual) {
        $script:Passed++
        [void]$script:Results.Add(('PASS | {0} | {1}' -f $Name, $Actual))
    }
    else {
        $script:Failed++
        [void]$script:Results.Add(('FAIL | {0} | expected={1} actual={2}' -f $Name, $Expected, $Actual))
    }
}

function New-ConnectionString {
    param([string] $Database)
    # Encryption is deliberately not forced: LocalDB, the default target, rejects it under the
    # System.Data.SqlClient provider that Windows PowerShell 5.1 ships with.
    $builder = "Server=$SqlServer;Database=$Database;TrustServerCertificate=True;MultipleActiveResultSets=True"
    if ([string]::IsNullOrWhiteSpace($SqlUserId)) {
        return "$builder;Trusted_Connection=True"
    }
    return "$builder;User Id=$SqlUserId;Password=$SqlPassword"
}

function Invoke-Sql {
    param([string] $Query, [string] $Database = 'master')
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString -Database $Database)
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        $reader = $command.ExecuteReader()
        $rows = New-Object System.Collections.ArrayList
        while ($reader.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $row[$reader.GetName($i)] = $reader.GetValue($i)
            }
            [void]$rows.Add([pscustomobject]$row)
        }
        $reader.Close()
        return $rows
    }
    finally {
        $connection.Close()
    }
}

function Invoke-SqlNonQuery {
    param([string] $Query, [string] $Database = 'master')
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString -Database $Database)
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Close()
    }
}

function Get-Scalar {
    param([string] $Query, [string] $Database)
    $rows = Invoke-Sql -Query $Query -Database $Database
    if ($rows.Count -eq 0) { return $null }
    $first = $rows[0]
    $name = ($first.PSObject.Properties | Select-Object -First 1).Name
    return $first.$name
}

# ---------------------------------------------------------------- HTTP helpers

$script:BaseUrl = "http://127.0.0.1:$Port"
$script:RequestCounter = 0

function New-ApiClient {
    param([int] $TimeoutSeconds = 60)
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient ($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    return $client
}

function New-RequestId {
    $script:RequestCounter++
    return ('req-access-record-verify-{0:d6}' -f $script:RequestCounter)
}

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Path,
        [string] $Body,
        [string] $Token,
        [string] $WorkspaceId,
        [string] $IdempotencyKey,
        [string] $RequestId
    )
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ([string]::IsNullOrEmpty($RequestId)) {
        [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId))
    }
    elseif ($RequestId -ne 'omit') {
        [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId)
    }
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-access-record-verify-0001')
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        [void]$request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token")
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) {
        [void]$request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId)
    }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) {
        [void]$request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey)
    }
    # An unbound [string] parameter arrives as an empty string, not $null.
    if (-not [string]::IsNullOrEmpty($Body)) {
        $request.Content = New-Object System.Net.Http.StringContent ($Body, [Text.Encoding]::UTF8, 'application/json')
    }

    $client = New-ApiClient
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    }
    finally {
        $client.Dispose()
    }
    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        try { $payload = $text | ConvertFrom-Json } catch { $payload = $null }
    }
    return [pscustomobject]@{
        Status = [int]$response.StatusCode
        Body   = $payload
        Raw    = $text
    }
}

$script:Token = $null
$script:WorkspaceId = $null

function Invoke-Evaluate {
    param([hashtable] $Request, [string] $WorkspaceOverride)
    $workspace = $script:WorkspaceId
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceOverride)) { $workspace = $WorkspaceOverride }
    return Invoke-Api -Method 'POST' -Path '/access/records/evaluate' -Token $script:Token `
        -WorkspaceId $workspace -Body ($Request | ConvertTo-Json -Compress -Depth 6)
}

# The evaluation instant is the only field that legitimately differs between two otherwise
# identical decisions, so it is removed before comparing denial payloads for indistinguishability.
function Get-ComparablePayload {
    param([string] $Raw)
    return ($Raw -replace '"evaluatedAt":"[^"]*"', '"evaluatedAt":"<t>"')
}

# ---------------------------------------------------------------- provisioning

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$hostProject = Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
$platformProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/UnicoreCRM.Platform.csproj'
$demoEmail = 'admin@unicorecrm.local'
$demoPassword = 'Record-Access-Verify!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-record-access-verify-$([Guid]::NewGuid().ToString('N')).log")
$supportProfileFields = @('subject', 'description', 'priority', 'status', 'assigneeId', 'queueId', 'slaPolicyId')
$supportProfileCommands = @('support.create', 'support.update', 'support.assign', 'support.resolve', 'support.close', 'support.reopen', 'support.cancel')

try {
    Write-Host "Provisioning isolated database $DatabaseName on $SqlServer ..."
    Invoke-SqlNonQuery -Query @"
IF DB_ID('$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DatabaseName];
END;
CREATE DATABASE [$DatabaseName];
"@

    $connectionString = New-ConnectionString -Database $DatabaseName
    Write-Host 'Starting UnicoreCRM.ApiHost ...'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = $connectionString
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $env:DevelopmentDemoBootstrap__Email = $demoEmail
    $env:DevelopmentDemoBootstrap__Password = $demoPassword

    $hostProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--no-launch-profile', '--project', $hostProject) `
        -PassThru -NoNewWindow -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

    $ready = $false
    $lastProbeError = '(none)'
    for ($attempt = 0; $attempt -lt $ReadyTimeoutSeconds; $attempt++) {
        Start-Sleep -Seconds 1
        if ($hostProcess.HasExited) {
            throw "ApiHost exited with code $($hostProcess.ExitCode). See $logPath"
        }
        try {
            $probe = Invoke-Api -Method 'GET' -Path '/auth/session'
            if ($probe.Status -gt 0) { $ready = $true; break }
        }
        catch {
            $lastProbeError = $_.Exception.Message
            if ($null -ne $_.Exception.InnerException) {
                $lastProbeError = $_.Exception.InnerException.Message
            }
        }
        if ($attempt -gt 0 -and $attempt % 30 -eq 0) {
            Write-Host ("  still waiting for ApiHost ({0}s): {1}" -f $attempt, $lastProbeError)
        }
    }
    if (-not $ready) {
        throw "ApiHost did not become ready within $ReadyTimeoutSeconds seconds ($lastProbeError). See $logPath"
    }
    Write-Host 'ApiHost is ready.'

    # ------------------------------------------------------------ authenticate

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' `
        -IdempotencyKey 'idem-record-access-verify-signin-01' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    $callerMemberId = $session.Body.principal.memberId

    $script:WorkspaceId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key] = 'unicore-demo'"
    $foreignWorkspaceId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key] = 'unicore-demo-isolated'"
    if ([string]::IsNullOrWhiteSpace($script:WorkspaceId)) { throw 'Development workspace was not provisioned.' }
    if ([string]::IsNullOrWhiteSpace($foreignWorkspaceId)) { throw 'Isolation workspace was not provisioned.' }

    $roleId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT TOP 1 RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)'"
    if ([string]::IsNullOrWhiteSpace($roleId)) { throw 'Development AccessControl role was not provisioned.' }

    # A second active member, used only as a foreign record owner so OWN scope has a real
    # non-caller owner to deny.
    $otherMemberId = 'mem-record-access-verify-other'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Memberships WHERE MemberId = '$otherMemberId')
INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, Status, CreatedAt)
VALUES ('wsm-record-access-verify-other', '$($script:WorkspaceId)', 'acc-record-access-verify-other', '$otherMemberId', 'Active', SYSUTCDATETIME());
"@

    # ------------------------------------------------------------ fixtures

    function New-SupportCase {
        param([string] $Title, [string] $OwnerId, [string] $IdempotencyKey)
        $body = @{
            title           = $Title
            description     = 'Record-access verification fixture.'
            priority        = 'high'
            category        = 'usage_issue'
            source          = 'manual'
            relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_record_access_001' }
            ownerId         = $OwnerId
        } | ConvertTo-Json -Compress -Depth 6
        $created = Invoke-Api -Method 'POST' -Path '/support/cases' -Token $script:Token `
            -WorkspaceId $script:WorkspaceId -IdempotencyKey $IdempotencyKey -Body $body
        if ($created.Status -ne 201) { throw "Fixture case creation failed with $($created.Status): $($created.Raw)" }
        return $created.Body.aggregateId
    }

    $ownCaseId = New-SupportCase -Title 'Owned by caller' -OwnerId $callerMemberId -IdempotencyKey 'idem-record-access-verify-case-own'
    $otherCaseId = New-SupportCase -Title 'Owned by another member' -OwnerId $otherMemberId -IdempotencyKey 'idem-record-access-verify-case-other'

    # A SupportCase that physically exists but belongs to the isolation Workspace. It is inserted
    # directly because the caller is not a member there and therefore cannot create it over HTTP.
    # Cloning an existing row avoids hard-coding the Support column list or its enum storage, and
    # keeps the fixture valid if Support's own mapping changes.
    $foreignCaseId = 'case_recordaccess_foreign'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
SELECT * INTO #foreign_case FROM support.SupportCases WHERE CaseId = '$ownCaseId';
UPDATE #foreign_case
SET CaseId = '$foreignCaseId',
    WorkspaceId = '$foreignWorkspaceId',
    CaseNumber = 'CASE-2026-9001',
    CaseSequence = 9001;
INSERT INTO support.SupportCases SELECT * FROM #foreign_case;
DROP TABLE #foreign_case;
"@

    $baseRequest = @{
        resourceKey       = 'support'
        recordId          = $ownCaseId
        requestedCommands = $supportProfileCommands
        requestedFields   = $supportProfileFields
        includeExport     = $true
    }

    # ------------------------------------------------------------ 1. transport security

    $anonymous = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'support' } | ConvertTo-Json -Compress)
    Add-Result 'security: unauthenticated evaluate' '401' $anonymous.Status

    $foreignWorkspace = Invoke-Evaluate -Request $baseRequest -WorkspaceOverride $foreignWorkspaceId
    Add-Result 'security: unresolved foreign Workspace fails closed' '403' $foreignWorkspace.Status
    Add-Result 'security: foreign Workspace error code' 'ACCESS_DENIED' $foreignWorkspace.Body.code

    $unknownWorkspace = Invoke-Evaluate -Request $baseRequest -WorkspaceOverride 'ws_does_not_exist'
    Add-Result 'security: unknown Workspace fails closed' '403' $unknownWorkspace.Status

    # ------------------------------------------------------------ 2. contract conformance

    $allowed = Invoke-Evaluate -Request $baseRequest
    Add-Result 'contract: evaluate succeeds' '200' $allowed.Status
    Add-Result 'contract: authority is backend' 'backend' $allowed.Body.authority
    Add-Result 'contract: workspaceId is the trusted Workspace' $script:WorkspaceId $allowed.Body.workspaceId
    Add-Result 'contract: resourceKey echoed canonically' 'support' $allowed.Body.resourceKey
    Add-Result 'contract: recordId echoed' $ownCaseId $allowed.Body.recordId
    Add-Result 'contract: evaluatedAt is UTC Z' 'True' ($allowed.Body.evaluatedAt.EndsWith('Z')).ToString()
    $requiredProperties = @('workspaceId', 'resourceKey', 'canRead', 'canUpdate', 'canDelete', 'canExport', 'canApprove', 'allowedCommands', 'fieldAccess', 'decisionReasons', 'evaluatedAt', 'authority')
    $missing = 0
    foreach ($property in $requiredProperties) {
        if ($null -eq $allowed.Body.PSObject.Properties[$property]) { $missing++ }
    }
    Add-Result 'contract: every required property present' '0' $missing

    $caseInsensitive = Invoke-Evaluate -Request @{ resourceKey = 'SUPPORT'; recordId = $ownCaseId }
    Add-Result 'contract: resource key matching is case-insensitive' 'support' $caseInsensitive.Body.resourceKey
    Add-Result 'contract: case-insensitive key still allowed' 'True' ($caseInsensitive.Body.canRead).ToString()

    $missingRequestId = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' -Token $script:Token `
        -WorkspaceId $script:WorkspaceId -RequestId 'omit' -Body (@{ resourceKey = 'support' } | ConvertTo-Json -Compress)
    Add-Result 'contract: missing X-Request-Id rejected' '422' $missingRequestId.Status

    $emptyResourceKey = Invoke-Evaluate -Request @{ resourceKey = '' }
    Add-Result 'contract: empty resourceKey rejected' '422' $emptyResourceKey.Status

    $badRecordId = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = 'not a valid id' }
    Add-Result 'contract: malformed recordId rejected' '422' $badRecordId.Status

    # ------------------------------------------------------------ 3. record-fact spoofing

    $spoofOwner = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $otherCaseId; ownerId = $callerMemberId }
    Add-Result 'security: caller cannot supply ownerId' '422' $spoofOwner.Status
    $spoofWorkspace = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; workspaceId = $foreignWorkspaceId }
    Add-Result 'security: caller cannot supply workspaceId' '422' $spoofWorkspace.Status
    $spoofTeam = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; teamId = 'team_anything' }
    Add-Result 'security: caller cannot supply teamId' '422' $spoofTeam.Status

    # ------------------------------------------------------------ 4. WORKSPACE scope

    Add-Result 'scope WORKSPACE: same-workspace record readable' 'True' ($allowed.Body.canRead).ToString()
    Add-Result 'scope WORKSPACE: reason is workspace scope' 'RECORD_SCOPE_WORKSPACE' `
        (($allowed.Body.decisionReasons | Where-Object { $_.effect -eq 'ALLOW' -and $_.code -like 'RECORD_SCOPE_*' }).code)
    $otherOwned = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $otherCaseId; requestedFields = $supportProfileFields }
    Add-Result 'scope WORKSPACE: record owned by another member readable' 'True' ($otherOwned.Body.canRead).ToString()

    # ------------------------------------------------------------ 5. capability interaction

    Add-Result 'capability: update granted from support.update' 'True' ($allowed.Body.canUpdate).ToString()
    Add-Result 'capability: delete denied (no admitted Support delete)' 'False' ($allowed.Body.canDelete).ToString()
    Add-Result 'capability: export denied (no admitted Support export)' 'False' ($allowed.Body.canExport).ToString()
    Add-Result 'capability: approve denied (no admitted Support approval)' 'False' ($allowed.Body.canApprove).ToString()
    Add-Result 'capability: every profile command allowed' '7' ([int]$allowed.Body.allowedCommands.Count)

    $unknownCommand = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; requestedCommands = @('support.update', 'support.obliterate') }
    Add-Result 'capability: undeclared command is never allowed' '1' ([int]$unknownCommand.Body.allowedCommands.Count)
    Add-Result 'capability: undeclared command absent from allowed list' 'False' `
        ($unknownCommand.Body.allowedCommands -contains 'support.obliterate').ToString()

    # Removing support.update must narrow the record decision without touching read access.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'support.update'"
    $withoutUpdate = Invoke-Evaluate -Request $baseRequest
    Add-Result 'capability: read survives losing support.update' 'True' ($withoutUpdate.Body.canRead).ToString()
    Add-Result 'capability: canUpdate follows the capability' 'False' ($withoutUpdate.Body.canUpdate).ToString()
    Add-Result 'capability: update commands withdrawn' '2' ([int]$withoutUpdate.Body.allowedCommands.Count)
    Add-Result 'field: READ_WRITE demoted to READ_ONLY without update' 'READ_ONLY' $withoutUpdate.Body.fieldAccess.subject
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'support.update')"

    # Removing support.read must deny the record outright: record scope can never restore it.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'support.read'"
    $withoutRead = Invoke-Evaluate -Request $baseRequest
    Add-Result 'capability: losing support.read denies the record' 'False' ($withoutRead.Body.canRead).ToString()
    Add-Result 'capability: denial reason is capability denial' 'CAPABILITY_DENIED' `
        (($withoutRead.Body.decisionReasons | Where-Object { $_.effect -eq 'DENY' }).code)
    Add-Result 'capability: no command granted without read' '0' ([int]$withoutRead.Body.allowedCommands.Count)
    Add-Result 'capability: fields hidden without read' 'HIDDEN' $withoutRead.Body.fieldAccess.subject
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'support.read')"

    # Removing the operation capability must fail the endpoint closed before any record work.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'workspace.context.resolve'"
    $withoutOperationCapability = Invoke-Evaluate -Request $baseRequest
    Add-Result 'capability: missing operation capability returns 403' '403' $withoutOperationCapability.Status
    Add-Result 'capability: operation denial code' 'ACCESS_DENIED' $withoutOperationCapability.Body.code
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'workspace.context.resolve')"

    # ------------------------------------------------------------ 6. existence leakage

    $unknownRecord = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = 'case_does_not_exist_0001'; requestedCommands = $supportProfileCommands; requestedFields = $supportProfileFields; includeExport = $true }
    Add-Result 'leakage: unknown record fails closed' 'False' ($unknownRecord.Body.canRead).ToString()
    Add-Result 'leakage: unknown record still returns 200' '200' $unknownRecord.Status

    $foreignRecord = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $foreignCaseId; requestedCommands = $supportProfileCommands; requestedFields = $supportProfileFields; includeExport = $true }
    Add-Result 'leakage: foreign Workspace record fails closed' 'False' ($foreignRecord.Body.canRead).ToString()

    $unknownComparable = Get-ComparablePayload -Raw $unknownRecord.Raw
    $foreignComparable = Get-ComparablePayload -Raw $foreignRecord.Raw
    # recordId legitimately differs because it is the caller's own input; normalising it leaves the
    # authoritative half of the payload, which must be identical.
    $unknownNormalised = $unknownComparable -replace '"recordId":"[^"]*"', '"recordId":"<id>"'
    $foreignNormalised = $foreignComparable -replace '"recordId":"[^"]*"', '"recordId":"<id>"'
    Add-Result 'leakage: foreign record indistinguishable from unknown record' $unknownNormalised $foreignNormalised

    # ------------------------------------------------------------ 7. OWN scope

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_record_access_verify_support', '$roleId', 'support', 'Own', '[]');
"@

    $ownAllowed = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; requestedCommands = $supportProfileCommands; requestedFields = $supportProfileFields; includeExport = $true }
    Add-Result 'scope OWN: owner equals caller is allowed' 'True' ($ownAllowed.Body.canRead).ToString()
    Add-Result 'scope OWN: allow reason is own-match' 'RECORD_SCOPE_OWN_MATCHED' `
        (($ownAllowed.Body.decisionReasons | Where-Object { $_.effect -eq 'ALLOW' -and $_.code -like 'RECORD_SCOPE_*' }).code)

    $ownDenied = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $otherCaseId; requestedCommands = $supportProfileCommands; requestedFields = $supportProfileFields; includeExport = $true }
    Add-Result 'scope OWN: owner differs from caller is denied' 'False' ($ownDenied.Body.canRead).ToString()
    Add-Result 'scope OWN: denied record grants no command' '0' ([int]$ownDenied.Body.allowedCommands.Count)
    Add-Result 'scope OWN: denied record hides every field' 'HIDDEN' $ownDenied.Body.fieldAccess.subject

    $ownDeniedNormalised = (Get-ComparablePayload -Raw $ownDenied.Raw) -replace '"recordId":"[^"]*"', '"recordId":"<id>"'
    Add-Result 'leakage: scope-hidden record indistinguishable from unknown record' $unknownNormalised $ownDeniedNormalised

    # OWN scope must not be evaluated when no record is supplied: the create form asks a
    # resource-level question and must still receive an honest capability answer.
    $noRecord = Invoke-Evaluate -Request @{ resourceKey = 'support'; requestedCommands = @('support.create'); requestedFields = $supportProfileFields }
    Add-Result 'scope OWN: resource-level question still readable' 'True' ($noRecord.Body.canRead).ToString()
    Add-Result 'scope OWN: resource-level question reports not-evaluated' 'RECORD_SCOPE_NOT_EVALUATED' `
        (($noRecord.Body.decisionReasons | Where-Object { $_.effect -eq 'LIMIT' -and $_.code -like 'RECORD_SCOPE_*' }).code)
    Add-Result 'scope OWN: create command allowed at resource level' 'True' `
        ($noRecord.Body.allowedCommands -contains 'support.create').ToString()

    # ------------------------------------------------------------ 8. unsupported scopes fail closed

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "UPDATE access.RoleDataScopes SET Scope = 'Team' WHERE PolicyId = 'scope_record_access_verify_support'"
    $teamScope = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; requestedFields = $supportProfileFields }
    Add-Result 'scope TEAM: unproven team authority fails closed' 'False' ($teamScope.Body.canRead).ToString()

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "UPDATE access.RoleDataScopes SET Scope = 'Custom' WHERE PolicyId = 'scope_record_access_verify_support'"
    $customScope = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; requestedFields = $supportProfileFields }
    Add-Result 'scope CUSTOM: unadmitted custom scope fails closed' 'False' ($customScope.Body.canRead).ToString()

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_record_access_verify_support'"

    # ------------------------------------------------------------ 9. field security

    $defaultField = Invoke-Evaluate -Request $baseRequest
    Add-Result 'field: unrestricted field is READ_WRITE' 'READ_WRITE' $defaultField.Body.fieldAccess.subject
    Add-Result 'field: only requested fields are projected' '7' ([int]($defaultField.Body.fieldAccess.PSObject.Properties | Measure-Object).Count)

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_record_access_verify_desc', '$roleId', 'support', 'description', 'Masked'),
('field_record_access_verify_sla', '$roleId', 'support', 'slaPolicyId', 'Hidden'),
('field_record_access_verify_status', '$roleId', 'support', 'status', 'ReadOnly');
"@

    $restrictedFields = Invoke-Evaluate -Request $baseRequest
    Add-Result 'field: policy MASKED is honoured' 'MASKED' $restrictedFields.Body.fieldAccess.description
    Add-Result 'field: policy HIDDEN is honoured' 'HIDDEN' $restrictedFields.Body.fieldAccess.slaPolicyId
    Add-Result 'field: policy READ_ONLY is honoured' 'READ_ONLY' $restrictedFields.Body.fieldAccess.status
    Add-Result 'field: unlisted field remains READ_WRITE' 'READ_WRITE' $restrictedFields.Body.fieldAccess.subject
    # @() is required: a single object returned by Where-Object carries no Count in Windows
    # PowerShell 5.1, so an unwrapped .Count silently reads as $null.
    Add-Result 'field: restriction reported as a LIMIT reason' 'True' `
        ((@($restrictedFields.Body.decisionReasons | Where-Object { $_.code -eq 'FIELD_ACCESS_RESTRICTED' }).Count -gt 0)).ToString()

    # Most-restrictive-wins across roles: a second role granting READ_WRITE must not widen MASKED.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.Roles (RoleId, WorkspaceId, Name, Description, SourceTemplateId, IsActive, Version, CreatedAt, UpdatedAt)
VALUES ('role_record_access_verify_second', '$($script:WorkspaceId)', 'Record Access Verify Second', NULL, NULL, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
INSERT INTO access.MembershipRoleAssignments (AssignmentId, WorkspaceId, MembershipId, RoleId, AssignedAt)
SELECT 'assign_record_access_verify_second', WorkspaceId, MembershipId, 'role_record_access_verify_second', SYSUTCDATETIME()
FROM access.MembershipRoleAssignments WHERE RoleId = '$roleId';
INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('role_record_access_verify_second', 'support.read');
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_record_access_verify_desc2', 'role_record_access_verify_second', 'support', 'description', 'ReadWrite');
"@
    $multiRole = Invoke-Evaluate -Request $baseRequest
    Add-Result 'field: most restrictive role wins across roles' 'MASKED' $multiRole.Body.fieldAccess.description
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.Roles WHERE RoleId = 'role_record_access_verify_second'"

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_record_access_verify%'"

    # ------------------------------------------------------------ 10. unowned resource keys

    $unknownResource = Invoke-Evaluate -Request @{ resourceKey = 'leads'; recordId = 'lead_anything_0001'; requestedFields = @('fullName') }
    Add-Result 'authority: resource with no fact owner fails closed' 'False' ($unknownResource.Body.canRead).ToString()
    Add-Result 'authority: unowned resource reports the missing authority' 'RESOURCE_FACT_AUTHORITY_UNAVAILABLE' `
        (($unknownResource.Body.decisionReasons | Where-Object { $_.effect -eq 'DENY' }).code)
    Add-Result 'authority: unowned resource hides requested fields' 'HIDDEN' $unknownResource.Body.fieldAccess.fullName

    # ------------------------------------------------------------ 11. no foreign mutation

    $supportAuditBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.AuditRecords'
    $supportOutboxBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.OutboxMessages'
    $supportCasesBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.SupportCases'
    $supportVersionBefore = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$ownCaseId'"

    for ($i = 0; $i -lt 3; $i++) {
        [void](Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = 'case_does_not_exist_0002' })
        [void](Invoke-Evaluate -Request $baseRequest)
    }

    $supportAuditAfter = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.AuditRecords'
    $supportOutboxAfter = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.OutboxMessages'
    $supportCasesAfter = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.SupportCases'
    $supportVersionAfter = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$ownCaseId'"

    Add-Result 'isolation: evaluation writes no Support audit' $supportAuditBefore $supportAuditAfter
    Add-Result 'isolation: evaluation emits no Support outbox event' $supportOutboxBefore $supportOutboxAfter
    Add-Result 'isolation: evaluation creates no SupportCase' $supportCasesBefore $supportCasesAfter
    Add-Result 'isolation: evaluation does not bump the record version' $supportVersionBefore $supportVersionAfter

    $foreignTables = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = 'access' AND TABLE_NAME IN ('SupportCases','Tasks','Leads','Deals')
"@
    Add-Result 'isolation: AccessControl owns no business table' '0' $foreignTables

    # ------------------------------------------------------------ 12. decision audit

    $allowedAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions
WHERE WorkspaceId = '$($script:WorkspaceId)' AND ResourceKey = 'support' AND RecordId = '$ownCaseId'
      AND Allowed = 1 AND DecisionCode = 'RECORD_SCOPE_WORKSPACE' AND RequiredCapability = 'support.read'
"@
    Add-Result 'audit: allowed evaluation is recorded' 'True' ([int]$allowedAudit -gt 0).ToString()

    $deniedAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions
WHERE Allowed = 0 AND DecisionCode = 'RECORD_ACCESS_DENIED' AND ResourceKey = 'support'
"@
    Add-Result 'audit: denied evaluation is recorded' 'True' ([int]$deniedAudit -gt 0).ToString()

    $ownMatchAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions
WHERE DecisionCode = 'RECORD_SCOPE_OWN_MATCHED' AND EvaluatedScope = 'OWN' AND OwnerMatch = 1
"@
    Add-Result 'audit: own-scope match is reproducible' 'True' ([int]$ownMatchAudit -gt 0).ToString()

    $correlationAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions
WHERE CorrelationId <> 'corr-access-record-verify-0001' OR RequestId NOT LIKE 'req-access-record-verify-%'
      OR MemberId <> '$callerMemberId' OR LEN(MembershipId) = 0
"@
    Add-Result 'audit: every row carries request, correlation and membership evidence' '0' $correlationAudit

    $noOwnerLeak = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'access' AND TABLE_NAME = 'RecordAccessDecisions' AND COLUMN_NAME = 'OwnerMemberId'
"@
    Add-Result 'audit: no foreign owner identity is persisted' '0' $noOwnerLeak

    $capabilityAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.AuthorizationDecisions WHERE RequiredCapability = 'workspace.context.resolve'
"@
    Add-Result 'audit: capability decision evidence still written' 'True' ([int]$capabilityAudit -gt 0).ToString()

    $foreignWorkspaceAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions WHERE WorkspaceId = '$foreignWorkspaceId'
"@
    Add-Result 'audit: no decision recorded against a foreign Workspace' '0' $foreignWorkspaceAudit

    # ------------------------------------------------------------ 13. regressions

    $context = Invoke-Api -Method 'GET' -Path '/access/context' -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'regression: getCurrentAuthorizationContext still reads' '200' $context.Status
    Add-Result 'regression: authorization context still projects capabilities' 'True' `
        ($context.Body.capabilities.Count -gt 0).ToString()

    $regressionRoutes = @(
        @{ Name = 'auth/session';   Path = '/auth/session';   Workspace = $false },
        @{ Name = 'workspaces';     Path = '/workspaces';     Workspace = $false },
        @{ Name = 'tasks';          Path = '/tasks';          Workspace = $true },
        @{ Name = 'leads';          Path = '/leads';          Workspace = $true },
        @{ Name = 'deals';          Path = '/deals';          Workspace = $true },
        @{ Name = 'products';       Path = '/products';       Workspace = $true },
        @{ Name = 'support/cases';  Path = '/support/cases';  Workspace = $true }
    )
    foreach ($route in $regressionRoutes) {
        if ($route.Workspace) {
            $probe = Invoke-Api -Method 'GET' -Path $route.Path -Token $script:Token -WorkspaceId $script:WorkspaceId
        }
        else {
            $probe = Invoke-Api -Method 'GET' -Path $route.Path -Token $script:Token
        }
        Add-Result ("regression: {0} still reads" -f $route.Name) '200' $probe.Status
    }

    $regressionDetail = Invoke-Api -Method 'GET' -Path "/support/cases/$ownCaseId" -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'regression: getSupportCase still reads' '200' $regressionDetail.Status

    # ------------------------------------------------------------ 14. EF model

    Push-Location $repositoryRoot
    try {
        $pending = & dotnet ef migrations has-pending-model-changes --project $platformProject --context AccessControlDbContext 2>&1
        $pendingText = ($pending | Out-String)
        Add-Result 'migration: no pending AccessControl model changes' 'True' `
            ($pendingText -match 'No changes have been made to the model').ToString()
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($null -ne $hostProcess) {
        Write-Host 'Stopping ApiHost ...'
        Get-CimInstance Win32_Process -Filter "Name = 'UnicoreCRM.ApiHost.exe'" |
            Where-Object { $_.CommandLine -like '*UnicoreCRM.ApiHost*' } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        try { Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
    if (-not $KeepDatabase) {
        try {
            Invoke-SqlNonQuery -Query @"
IF DB_ID('$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DatabaseName];
END;
"@
        }
        catch { Write-Warning "Could not drop $DatabaseName : $_" }
    }
}

Write-Host ''
Write-Host '===== ACCESSCONTROL RECORD ACCESS VERIFICATION ====='
foreach ($line in $script:Results) { Write-Host $line }
Write-Host ''
Write-Host ("PASS={0} FAIL={1}" -f $script:Passed, $script:Failed)
Write-Host ("Host log: {0}" -f $logPath)

if ($script:Failed -gt 0) { exit 1 }
exit 0
