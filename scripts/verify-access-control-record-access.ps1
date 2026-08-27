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
        [string] $RequestId,
        [string] $IfMatchVersion
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
    if (-not [string]::IsNullOrWhiteSpace($IfMatchVersion)) {
        [void]$request.Headers.TryAddWithoutValidation('If-Match', ('"{0}"' -f $IfMatchVersion))
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

function Invoke-Support {
    param([string] $Method, [string] $Path, [string] $Body, [string] $IdempotencyKey, [string] $IfMatchVersion)
    return Invoke-Api -Method $Method -Path $Path -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body $Body -IdempotencyKey $IdempotencyKey -IfMatchVersion $IfMatchVersion
}

function Set-SupportScope {
    param([string] $RoleId, [string] $Database, [string] $Scope)
    Invoke-SqlNonQuery -Database $Database -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_record_access_verify_support';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_record_access_verify_support', '$RoleId', 'support', '$Scope', '[]');
"@
}

# Every retrofitted module has its own resource key, so a scope policy has to be written per
# resource. A single `support` row proves nothing about Tasks, Leads, Deals or Products - which is
# exactly what an earlier draft of this harness got wrong.
function Set-ModuleScope {
    param([string] $RoleId, [string] $Database, [string] $Scope)
    Invoke-SqlNonQuery -Database $Database -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId LIKE 'scope_retro_%';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson) VALUES
('scope_retro_tasks', '$RoleId', 'tasks', '$Scope', '[]'),
('scope_retro_leads', '$RoleId', 'leads', '$Scope', '[]'),
('scope_retro_deals', '$RoleId', 'deals', '$Scope', '[]'),
('scope_retro_products', '$RoleId', 'products', '$Scope', '[]');
"@
}

function Clear-ModuleScope {
    param([string] $Database)
    Invoke-SqlNonQuery -Database $Database -Query "DELETE FROM access.RoleDataScopes WHERE PolicyId LIKE 'scope_retro_%'"
}

function Clear-SupportScope {
    param([string] $Database)
    Invoke-SqlNonQuery -Database $Database -Query "DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_record_access_verify_support'"
}

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
$operationsProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Operations/UnicoreCRM.Operations.csproj'
$crmProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Crm/UnicoreCRM.Crm.csproj'
$salesProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/UnicoreCRM.Sales.csproj'
$demoEmail = 'admin@unicorecrm.local'
$demoPassword = 'Record-Access-Verify!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-record-access-verify-$([Guid]::NewGuid().ToString('N')).log")
# Support's own declared field vocabulary. The frontend form names (`subject`, `assigneeId`,
# `queueId`, `slaPolicyId`) are deliberately not used here: a key the owner does not declare is
# not enforceable and now fails closed, which section 23.9 tests on purpose rather than by
# accident.
$supportProfileFields = @('title', 'description', 'priority', 'status', 'ownerId', 'channel', 'tags')
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
            tags            = @('verify-tag')
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
    Add-Result 'field: READ_WRITE demoted to READ_ONLY without update' 'READ_ONLY' $withoutUpdate.Body.fieldAccess.title
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
    Add-Result 'capability: fields hidden without read' 'HIDDEN' $withoutRead.Body.fieldAccess.title
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
    Add-Result 'scope OWN: denied record hides every field' 'HIDDEN' $ownDenied.Body.fieldAccess.title

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
    Add-Result 'field: unrestricted field is READ_WRITE' 'READ_WRITE' $defaultField.Body.fieldAccess.title
    Add-Result 'field: only requested fields are projected' '7' ([int]($defaultField.Body.fieldAccess.PSObject.Properties | Measure-Object).Count)

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_record_access_verify_desc', '$roleId', 'support', 'description', 'Masked'),
('field_record_access_verify_sla', '$roleId', 'support', 'channel', 'Hidden'),
('field_record_access_verify_status', '$roleId', 'support', 'status', 'ReadOnly');
"@

    $restrictedFields = Invoke-Evaluate -Request $baseRequest
    # No masking representation is admitted anywhere, so a MASKED policy is enforced by withholding
    # the value and is reported as HIDDEN - which is exactly what the caller will observe. Reporting
    # MASKED would promise a masked value that never arrives.
    Add-Result 'field: policy MASKED is enforced as withheld' 'HIDDEN' $restrictedFields.Body.fieldAccess.description
    Add-Result 'field: policy HIDDEN is honoured' 'HIDDEN' $restrictedFields.Body.fieldAccess.channel
    Add-Result 'field: policy READ_ONLY is honoured' 'READ_ONLY' $restrictedFields.Body.fieldAccess.status
    Add-Result 'field: a declared field with no policy stays READ_WRITE' 'READ_WRITE' $restrictedFields.Body.fieldAccess.title
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
    Add-Result 'field: most restrictive role wins across roles' 'HIDDEN' $multiRole.Body.fieldAccess.description
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.Roles WHERE RoleId = 'role_record_access_verify_second'"

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_record_access_verify%'"

    # ------------------------------------------------------------ 10. unowned resource keys

    # `contacts` has no implemented owner at all, so it is the honest example of a resource key with
    # no registered fact authority. `leads` is no longer one: it was retrofitted and now has a provider.
    $unknownResource = Invoke-Evaluate -Request @{ resourceKey = 'contacts'; recordId = 'contact_anything_0001'; requestedFields = @('fullName') }
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

    # ------------------------------------------------ 15. BACKEND ENFORCEMENT (bypass)

    # Everything above proves the evaluation is right. This block proves the evaluation is also
    # enforced: the business API is called directly, exactly as an attacker who ignores the
    # frontend would, and must refuse without the browser being involved at all.

    Set-SupportScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'

    $hiddenEvaluation = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $otherCaseId }
    Add-Result 'enforce: evaluation reports the other-owner case as unreadable' 'False' ($hiddenEvaluation.Body.canRead).ToString()

    $bypassRead = Invoke-Support -Method 'GET' -Path "/support/cases/$otherCaseId"
    Add-Result 'enforce: direct GET of a hidden case is refused' '404' $bypassRead.Status
    Add-Result 'enforce: direct GET leaks no hidden title' 'True' `
        ($bypassRead.Raw -notmatch 'Owned by another member').ToString()
    Add-Result 'enforce: hidden case is indistinguishable from an unknown case' `
        ((Invoke-Support -Method 'GET' -Path '/support/cases/case_does_not_exist_0003').Body.code) $bypassRead.Body.code

    $ownRead = Invoke-Support -Method 'GET' -Path "/support/cases/$ownCaseId"
    Add-Result 'enforce: the caller own case still reads' '200' $ownRead.Status
    $ownVersion = $ownRead.Body.resourceVersion

    # Mutation bypass: every existing-record Support command against a hidden case.
    $profileBody = @{
        title           = 'Bypass attempt'
        description     = 'Should never commit.'
        priority        = 'high'
        category        = 'usage_issue'
        source          = 'manual'
        relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_record_access_001' }
    } | ConvertTo-Json -Compress -Depth 6
    $bypassProfile = Invoke-Support -Method 'PUT' -Path "/support/cases/$otherCaseId" -Body $profileBody `
        -IdempotencyKey 'idem-record-access-bypass-profile' -IfMatchVersion '0'
    Add-Result 'enforce: direct profile replacement on a hidden case is refused' '404' $bypassProfile.Status

    $bypassAssign = Invoke-Support -Method 'POST' -Path "/support/cases/$otherCaseId/assign" `
        -Body (@{ ownerId = $callerMemberId } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-record-access-bypass-assign' -IfMatchVersion '0'
    Add-Result 'enforce: direct assignment on a hidden case is refused' '404' $bypassAssign.Status

    $bypassTransition = Invoke-Support -Method 'POST' -Path "/support/cases/$otherCaseId/transition" `
        -Body (@{ nextStatus = 'in_progress' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-record-access-bypass-transition' -IfMatchVersion '0'
    Add-Result 'enforce: direct transition on a hidden case is refused' '404' $bypassTransition.Status

    $bypassReply = Invoke-Support -Method 'POST' -Path "/support/cases/$otherCaseId/replies" `
        -Body (@{ body = 'Bypass reply' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-record-access-bypass-reply' -IfMatchVersion '0'
    Add-Result 'enforce: direct reply on a hidden case is refused' '404' $bypassReply.Status

    $bypassNote = Invoke-Support -Method 'POST' -Path "/support/cases/$otherCaseId/internal-notes" `
        -Body (@{ body = 'Bypass note' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-record-access-bypass-note' -IfMatchVersion '0'
    Add-Result 'enforce: direct internal note on a hidden case is refused' '404' $bypassNote.Status

    $hiddenVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$otherCaseId'"
    Add-Result 'enforce: no refused command mutated the hidden case' '0' $hiddenVersion
    $hiddenComments = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM support.SupportCaseComments WHERE CaseId = '$otherCaseId'"
    Add-Result 'enforce: no refused command wrote a comment' '0' $hiddenComments

    # List scope pushdown.
    $ownList = Invoke-Support -Method 'GET' -Path '/support/cases'
    Add-Result 'enforce: OWN list returns only caller-owned cases' '1' ([int]$ownList.Body.items.Count)
    Add-Result 'enforce: OWN list returns the caller own case' $ownCaseId $ownList.Body.items[0].id
    Add-Result 'enforce: OWN list totalCount excludes hidden rows' '1' ([int]$ownList.Body.pageInfo.totalCount)
    Add-Result 'enforce: OWN list page reports no hidden next page' 'False' ($ownList.Body.pageInfo.hasNextPage).ToString()

    $ownPage = Invoke-Support -Method 'GET' -Path '/support/cases?limit=1'
    Add-Result 'enforce: OWN pagination is not padded by hidden rows' 'False' ($ownPage.Body.pageInfo.hasNextPage).ToString()

    # WORKSPACE scope restores the other-owner record through the same enforcement path.
    Set-SupportScope -RoleId $roleId -Database $DatabaseName -Scope 'Workspace'
    $workspaceRead = Invoke-Support -Method 'GET' -Path "/support/cases/$otherCaseId"
    Add-Result 'enforce: WORKSPACE scope reads the other-owner case' '200' $workspaceRead.Status
    $workspaceList = Invoke-Support -Method 'GET' -Path '/support/cases'
    Add-Result 'enforce: WORKSPACE list returns every case' '2' ([int]$workspaceList.Body.pageInfo.totalCount)

    # TEAM and CUSTOM fail closed at the enforcement point, not only in the evaluation.
    Set-SupportScope -RoleId $roleId -Database $DatabaseName -Scope 'Team'
    Add-Result 'enforce: TEAM scope refuses a direct read' '404' (Invoke-Support -Method 'GET' -Path "/support/cases/$ownCaseId").Status
    Add-Result 'enforce: TEAM scope returns an empty list' '0' ([int](Invoke-Support -Method 'GET' -Path '/support/cases').Body.pageInfo.totalCount)

    Set-SupportScope -RoleId $roleId -Database $DatabaseName -Scope 'Custom'
    Add-Result 'enforce: CUSTOM scope refuses a direct read' '404' (Invoke-Support -Method 'GET' -Path "/support/cases/$ownCaseId").Status
    Add-Result 'enforce: CUSTOM scope returns an empty list' '0' ([int](Invoke-Support -Method 'GET' -Path '/support/cases').Body.pageInfo.totalCount)

    Clear-SupportScope -Database $DatabaseName

    # ------------------------------------------------ 16. FIELD ENFORCEMENT (backend)

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_enforce_%';
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_enforce_owner', '$roleId', 'support', 'ownerId', 'Hidden'),
('field_enforce_tags', '$roleId', 'support', 'TAGS', 'ReadOnly');
"@

    $hiddenField = Invoke-Support -Method 'GET' -Path "/support/cases/$ownCaseId"
    Add-Result 'field: HIDDEN value is absent from the raw backend JSON' 'True' `
        ($hiddenField.Raw -notmatch '"ownerId"').ToString()
    Add-Result 'field: HIDDEN does not leak the owner identifier anywhere' 'True' `
        ($hiddenField.Raw -notmatch [regex]::Escape($callerMemberId)).ToString()
    Add-Result 'field: the record itself still reads' '200' $hiddenField.Status
    Add-Result 'field: READ_ONLY value is still readable' 'True' `
        ($hiddenField.Raw -match 'verify-tag').ToString()

    $listHidden = Invoke-Support -Method 'GET' -Path '/support/cases'
    Add-Result 'field: HIDDEN is enforced on the list projection too' 'True' `
        ($listHidden.Raw -notmatch '"ownerId"').ToString()

    # A READ_ONLY field cannot be written, and the policy key casing must not matter.
    $currentVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$ownCaseId'"
    $readOnlyWrite = @{
        title           = 'Owned by caller'
        description     = 'Record-access verification fixture.'
        priority        = 'high'
        category        = 'usage_issue'
        source          = 'manual'
        relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_record_access_001' }
        tags            = @('newly-added-tag')
    } | ConvertTo-Json -Compress -Depth 6
    $readOnlyAttempt = Invoke-Support -Method 'PUT' -Path "/support/cases/$ownCaseId" -Body $readOnlyWrite `
        -IdempotencyKey 'idem-record-access-readonly-write' -IfMatchVersion $currentVersion
    Add-Result 'field: writing a READ_ONLY field is refused' '403' $readOnlyAttempt.Status
    Add-Result 'field: READ_ONLY refusal is an access denial' 'ACCESS_DENIED' $readOnlyAttempt.Body.code
    Add-Result 'field: case-insensitive field policy cannot be bypassed by casing' $currentVersion `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$ownCaseId'")

    # A restrictive policy on a required wire field cannot be honoured, so the operation fails closed
    # rather than returning a value the policy forbids.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_enforce_required', '$roleId', 'support', 'description', 'Hidden');
"@
    $requiredField = Invoke-Support -Method 'GET' -Path "/support/cases/$ownCaseId"
    Add-Result 'field: unwithholdable required field fails the read closed' '403' $requiredField.Status
    Add-Result 'field: unwithholdable required field never returns the value' 'True' `
        ($requiredField.Raw -notmatch 'Record-access verification fixture').ToString()
    $requiredEvaluation = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; requestedFields = @('description') }
    Add-Result 'field: the evaluation surfaces the unenforceable policy' 'True' `
        ((@($requiredEvaluation.Body.decisionReasons | Where-Object { $_.code -eq 'FIELD_POLICY_UNENFORCEABLE' }).Count -gt 0)).ToString()

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_enforce_%'"

    # ------------------------------------------------ 17. OWNER ASSIGNMENT PRIVILEGE

    # support.update must not carry assignment authority. The profile contract also carries ownerId,
    # which is exactly how the privilege used to leak.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'support.assign'"

    $version = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$ownCaseId'"
    $reassign = @{
        title           = 'Owned by caller'
        description     = 'Record-access verification fixture.'
        priority        = 'high'
        category        = 'usage_issue'
        source          = 'manual'
        relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_record_access_001' }
        ownerId         = $otherMemberId
    } | ConvertTo-Json -Compress -Depth 6
    $reassignAttempt = Invoke-Support -Method 'PUT' -Path "/support/cases/$ownCaseId" -Body $reassign `
        -IdempotencyKey 'idem-record-access-owner-change' -IfMatchVersion $version
    Add-Result 'owner: support.update alone cannot reassign the owner' '403' $reassignAttempt.Status

    $clearOwner = @{
        title           = 'Owned by caller'
        description     = 'Record-access verification fixture.'
        priority        = 'high'
        category        = 'usage_issue'
        source          = 'manual'
        relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_record_access_001' }
    } | ConvertTo-Json -Compress -Depth 6
    $clearAttempt = Invoke-Support -Method 'PUT' -Path "/support/cases/$ownCaseId" -Body $clearOwner `
        -IdempotencyKey 'idem-record-access-owner-clear' -IfMatchVersion $version
    Add-Result 'owner: support.update alone cannot clear the owner' '403' $clearAttempt.Status

    $assignAttempt = Invoke-Support -Method 'POST' -Path "/support/cases/$ownCaseId/assign" `
        -Body (@{ ownerId = $otherMemberId } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-record-access-owner-assign-denied' -IfMatchVersion $version
    Add-Result 'owner: the assignment operation itself is denied without support.assign' '403' $assignAttempt.Status

    Add-Result 'owner: no refused owner change was committed' $callerMemberId `
        (Get-Scalar -Database $DatabaseName -Query "SELECT OwnerId FROM support.SupportCases WHERE CaseId = '$ownCaseId'")

    # A profile replacement that leaves the owner untouched is still allowed on support.update.
    $keepOwner = @{
        title           = 'Owned by caller renamed'
        description     = 'Record-access verification fixture.'
        priority        = 'high'
        category        = 'usage_issue'
        source          = 'manual'
        relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_record_access_001' }
        ownerId         = $callerMemberId
    } | ConvertTo-Json -Compress -Depth 6
    $keepAttempt = Invoke-Support -Method 'PUT' -Path "/support/cases/$ownCaseId" -Body $keepOwner `
        -IdempotencyKey 'idem-record-access-owner-keep' -IfMatchVersion $version
    Add-Result 'owner: an unchanged owner is not an assignment' '200' $keepAttempt.Status

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'support.assign')"
    $version = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$ownCaseId'"
    $allowedAssign = Invoke-Support -Method 'POST' -Path "/support/cases/$ownCaseId/assign" `
        -Body (@{ ownerId = $otherMemberId } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-record-access-owner-assign-allowed' -IfMatchVersion $version
    Add-Result 'owner: support.assign still assigns through the admitted path' '200' $allowedAssign.Status
    Add-Result 'owner: the admitted assignment committed' $otherMemberId `
        (Get-Scalar -Database $DatabaseName -Query "SELECT OwnerId FROM support.SupportCases WHERE CaseId = '$ownCaseId'")

    # Restore the fixture owner so later assertions read the original arrangement.
    $version = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM support.SupportCases WHERE CaseId = '$ownCaseId'"
    [void](Invoke-Support -Method 'POST' -Path "/support/cases/$ownCaseId/assign" `
        -Body (@{ ownerId = $callerMemberId } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-record-access-owner-restore' -IfMatchVersion $version)

    # ------------------------------------------------ 18. POLICY KEY CONSISTENCY

    # Two roles spelling one resource key differently must not produce two effective entries: the
    # restrictive one has to win, or a casing difference becomes a scope bypass.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.Roles (RoleId, WorkspaceId, Name, Description, SourceTemplateId, IsActive, Version, CreatedAt, UpdatedAt)
VALUES ('role_record_access_case', '$($script:WorkspaceId)', 'Record Access Case Casing', NULL, NULL, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
INSERT INTO access.MembershipRoleAssignments (AssignmentId, WorkspaceId, MembershipId, RoleId, AssignedAt)
SELECT 'assign_record_access_case', WorkspaceId, MembershipId, 'role_record_access_case', SYSUTCDATETIME()
FROM access.MembershipRoleAssignments WHERE RoleId = '$roleId';
INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('role_record_access_case', 'support.read');
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_record_access_case', 'role_record_access_case', 'SUPPORT', 'Own', '[]');
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_record_access_case', 'role_record_access_case', 'SuPPoRt', 'ownerId', 'Hidden');
"@

    # The primary role spells the key `support` and the second spells it `SUPPORT`. One canonical
    # identity means one effective entry; two entries would let resolution pick whichever it matched
    # first and silently ignore the other role's policy.
    Set-SupportScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'
    $mixedCaseContext = Invoke-Api -Method 'GET' -Path '/access/context' -Token $script:Token -WorkspaceId $script:WorkspaceId
    $supportScopeEntries = @($mixedCaseContext.Body.dataScopes | Where-Object { $_.resourceKey -match '(?i)^support$' })
    Add-Result 'keys: mixed-case resource keys collapse to one effective entry' '1' ([int]$supportScopeEntries.Count)

    $mixedCaseRead = Invoke-Support -Method 'GET' -Path "/support/cases/$otherCaseId"
    Add-Result 'keys: the merged OWN scope is enforced under mixed casing' '404' $mixedCaseRead.Status

    # Field security is most-restrictive-wins, so a HIDDEN policy stored under a differently cased
    # resource key must still withhold the value.
    $mixedCaseOwn = Invoke-Support -Method 'GET' -Path "/support/cases/$ownCaseId"
    Add-Result 'keys: a mixed-case HIDDEN field policy still withholds the value' 'True' `
        ($mixedCaseOwn.Raw -notmatch '"ownerId"').ToString()

    Clear-SupportScope -Database $DatabaseName

    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.Roles WHERE RoleId = 'role_record_access_case'"

    # ------------------------------------------------ 19. RESOURCE-LEVEL CREATE SEMANTICS

    # A create-only caller must not have creation withheld because it cannot read.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'support.read'"
    $createOnly = Invoke-Evaluate -Request @{ resourceKey = 'support'; requestedCommands = @('support.create', 'support.update') }
    Add-Result 'create: resource-level create survives losing support.read' 'True' `
        ($createOnly.Body.allowedCommands -contains 'support.create').ToString()
    Add-Result 'create: resource-level read is still reported as denied' 'False' ($createOnly.Body.canRead).ToString()
    $recordLevel = Invoke-Evaluate -Request @{ resourceKey = 'support'; recordId = $ownCaseId; requestedCommands = @('support.update') }
    Add-Result 'create: record-level commands still require a readable record' '0' ([int]$recordLevel.Body.allowedCommands.Count)
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'support.read')"

    # ------------------------------------------------ 20. ENFORCEMENT AUDIT AND COST

    $enforcementRows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions WHERE EnforcementPoint = 'getSupportCase'
"@
    Add-Result 'audit: owner enforcement writes its own decision evidence' 'True' ([int]$enforcementRows -gt 0).ToString()

    $deniedEnforcement = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions
WHERE EnforcementPoint IN ('replaceSupportCaseProfile','assignSupportCase','transitionSupportCase','addSupportCaseReply','addSupportCaseInternalNote')
      AND Allowed = 0
"@
    Add-Result 'audit: refused mutations are recorded as denied decisions' 'True' ([int]$deniedEnforcement -gt 0).ToString()

    $fingerprints = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions WHERE LEN(PolicyFingerprint) <> 64
"@
    Add-Result 'audit: every decision records the effective policy fingerprint' '0' $fingerprints

    $fingerprintChanged = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(DISTINCT PolicyFingerprint) AS N FROM access.RecordAccessDecisions WHERE EnforcementPoint = 'getSupportCase'
"@
    Add-Result 'audit: a policy change is visible in the fingerprint' 'True' ([int]$fingerprintChanged -gt 1).ToString()

    # One list request must not evaluate one decision per row.
    $before = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.RecordAccessDecisions'
    [void](Invoke-Support -Method 'GET' -Path '/support/cases')
    $after = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.RecordAccessDecisions'
    Add-Result 'cost: listing evaluates no per-row record decision' '0' ([int]$after - [int]$before)

    # One authorization per request, not one per enforcement point.
    $capabilityBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions'
    [void](Invoke-Support -Method 'GET' -Path "/support/cases/$ownCaseId")
    $capabilityAfter = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions'
    Add-Result 'authority: an enforced read authorizes exactly once' '1' ([int]$capabilityAfter - [int]$capabilityBefore)

    # ------------------------------------------ 21. RETROFITTED MODULE ENFORCEMENT

    # Tasks, Leads, Deals and Products were retrofitted onto the same canonical AccessControl
    # boundary as Support. Each is proven the same way and without the browser: a record the caller's
    # record scope hides must be unreachable through the business API itself.

    $otherOwnerId = $otherMemberId

    # ---- fixtures, one record owned by the caller and one owned by another member ----
    $taskOwn = Invoke-Support -Method 'POST' -Path '/tasks' -IdempotencyKey 'idem-retro-task-own' `
        -Body (@{ title = 'Retro task own'; assigneeId = $callerMemberId; dueAt = '2026-12-01T09:00:00.0000000Z' } | ConvertTo-Json -Compress)
    Add-Result 'tasks: fixture owned by caller created' '201' $taskOwn.Status
    $taskOwnId = $taskOwn.Body.aggregateId
    $taskOther = Invoke-Support -Method 'POST' -Path '/tasks' -IdempotencyKey 'idem-retro-task-other' `
        -Body (@{ title = 'Retro task other'; assigneeId = $otherOwnerId; dueAt = '2026-12-01T09:00:00.0000000Z' } | ConvertTo-Json -Compress)
    $taskOtherId = $taskOther.Body.aggregateId

    $leadOwn = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-retro-lead-own' `
        -Body (@{ displayName = 'Retro lead own'; ownerId = $callerMemberId; source = 'manual'; estimatedValue = @{ amount = '1000'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'leads: fixture owned by caller created' '201' $leadOwn.Status
    $leadOwnId = $leadOwn.Body.aggregateId
    $leadOther = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-retro-lead-other' `
        -Body (@{ displayName = 'Retro lead other'; ownerId = $otherOwnerId; source = 'manual'; estimatedValue = @{ amount = '1000'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)
    $leadOtherId = $leadOther.Body.aggregateId

    $dealOwn = Invoke-Support -Method 'POST' -Path '/deals' -IdempotencyKey 'idem-retro-deal-own' `
        -Body (@{
            name                 = 'Retro deal own'
            buyerRef             = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_retro_001' }
            stageCode            = 'DISCOVERY'
            amount               = @{ amount = '1000.00'; currency = 'USD' }
            opportunityScore     = '10'
            ownerId              = $callerMemberId
            expectedCloseDate    = '2026-12-31'
            interestedProductIds = @()
            lineItems            = @()
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'deals: fixture owned by caller created' '201' $dealOwn.Status
    if ($dealOwn.Status -ne 201) { Write-Host ("  deal fixture refused: {0}" -f $dealOwn.Raw) }
    $dealOwnId = $dealOwn.Body.aggregateId
    $dealOther = Invoke-Support -Method 'POST' -Path '/deals' -IdempotencyKey 'idem-retro-deal-other' `
        -Body (@{
            name                 = 'Retro deal other'
            buyerRef             = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_retro_001' }
            stageCode            = 'DISCOVERY'
            amount               = @{ amount = '2000.00'; currency = 'USD' }
            opportunityScore     = '10'
            ownerId              = $otherOwnerId
            expectedCloseDate    = '2026-12-31'
            interestedProductIds = @()
            lineItems            = @()
        } | ConvertTo-Json -Compress -Depth 6)
    $dealOtherId = $dealOther.Body.aggregateId

    $productOne = Invoke-Support -Method 'POST' -Path '/products' -IdempotencyKey 'idem-retro-product-01' `
        -Body (@{
            sku            = 'RETRO-001'
            name           = 'Retro product'
            type           = 'service'
            status         = 'ACTIVE'
            category       = 'Professional Services'
            description    = 'Record-access retrofit fixture'
            unit           = 'hour'
            unitPrice      = @{ amount = '10.125'; currency = 'USD' }
            costPrice      = @{ amount = '4.25'; currency = 'USD' }
            taxRate        = '10'
            taxMode        = 'exclusive'
            billingCycle   = 'one_time'
            isSubscription = $false
            isRenewable    = $false
            tags           = @('verified', 'core')
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'products: fixture created' '201' $productOne.Status
    if ($productOne.Status -ne 201) { Write-Host ("  product fixture refused: {0}" -f $productOne.Raw) }
    $productOneId = $productOne.Body.aggregateId

    # ---- OWN scope: the caller's own record stays visible, another member's disappears ----
    Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'

    Add-Result 'tasks: OWN allows the caller own task' '200' (Invoke-Support -Method 'GET' -Path "/tasks/$taskOwnId").Status
    $taskHidden = Invoke-Support -Method 'GET' -Path "/tasks/$taskOtherId"
    Add-Result 'tasks: OWN hides another member task from a direct GET' '404' $taskHidden.Status
    Add-Result 'tasks: hidden task leaks no title' 'True' ($taskHidden.Raw -notmatch 'Retro task other').ToString()

    Add-Result 'leads: OWN allows the caller own lead' '200' (Invoke-Support -Method 'GET' -Path "/leads/$leadOwnId").Status
    $leadHidden = Invoke-Support -Method 'GET' -Path "/leads/$leadOtherId"
    Add-Result 'leads: OWN hides another member lead from a direct GET' '404' $leadHidden.Status
    Add-Result 'leads: hidden lead leaks no display name' 'True' ($leadHidden.Raw -notmatch 'Retro lead other').ToString()

    Add-Result 'deals: OWN allows the caller own deal' '200' (Invoke-Support -Method 'GET' -Path "/deals/$dealOwnId").Status
    $dealHidden = Invoke-Support -Method 'GET' -Path "/deals/$dealOtherId"
    Add-Result 'deals: OWN hides another member deal from a direct GET' '404' $dealHidden.Status
    Add-Result 'deals: hidden deal leaks no name' 'True' ($dealHidden.Raw -notmatch 'Retro deal other').ToString()

    $dealList = Invoke-Support -Method 'GET' -Path '/deals'
    Add-Result 'deals: OWN list excludes the hidden deal' 'False' ($dealList.Body.items.id -contains $dealOtherId).ToString()
    Add-Result 'deals: OWN list totalCount excludes hidden rows' '1' ([int]$dealList.Body.pageInfo.totalCount)

    # The forecast aggregates amounts, so a hidden deal must not reach the totals either.
    $forecast = Invoke-Support -Method 'GET' -Path '/deals/forecast-summary'
    Add-Result 'deals: OWN forecast excludes the hidden deal amount' 'True' `
        ($forecast.Raw -notmatch '2000').ToString()

    $dealMutation = Invoke-Support -Method 'POST' -Path "/deals/$dealOtherId/archive" `
        -Body (@{ reason = 'bypass attempt' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-retro-deal-bypass' -IfMatchVersion '0'
    Add-Result 'deals: direct mutation on a hidden deal is refused' '404' $dealMutation.Status
    Add-Result 'deals: refused mutation did not change the record' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM deals.Deals WHERE DealId = '$dealOtherId'")

    # Product has no member owner, so OWN denies every Product rather than inventing ownership.
    Add-Result 'products: OWN fails closed for a resource with no owner concept' '404' `
        (Invoke-Support -Method 'GET' -Path "/products/$productOneId").Status
    Add-Result 'products: OWN empties the list rather than leaking it' '0' `
        ([int](Invoke-Support -Method 'GET' -Path '/products').Body.Count)

    # ---- direct mutation bypass ----
    $taskMutation = Invoke-Support -Method 'POST' -Path "/tasks/$taskOtherId/complete" `
        -Body (@{ outcome = 'bypass' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-retro-task-bypass' -IfMatchVersion '0'
    Add-Result 'tasks: direct mutation on a hidden task is refused' '404' $taskMutation.Status
    Add-Result 'tasks: refused mutation did not change the record' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM tasks.Tasks WHERE TaskId = '$taskOtherId'")

    $leadMutation = Invoke-Support -Method 'POST' -Path "/leads/$leadOtherId/disqualify" `
        -Body (@{ reason = 'not_interested'; evidence = 'Bypass attempt evidence.' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-retro-lead-bypass' -IfMatchVersion '0'
    Add-Result 'leads: direct mutation on a hidden lead is refused' '404' $leadMutation.Status
    Add-Result 'leads: refused mutation did not change the record' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM leads.Leads WHERE LeadId = '$leadOtherId'")

    # ---- list scope: hidden rows are absent and uncounted ----
    $taskList = Invoke-Support -Method 'GET' -Path '/tasks'
    Add-Result 'tasks: OWN list excludes the hidden task' 'False' `
        ($taskList.Body.items.id -contains $taskOtherId).ToString()
    Add-Result 'tasks: OWN list totalCount excludes hidden rows' '1' ([int]$taskList.Body.pageInfo.totalCount)
    Add-Result 'tasks: OWN pagination is not padded by hidden rows' 'False' `
        ((Invoke-Support -Method 'GET' -Path '/tasks?limit=1').Body.pageInfo.hasNextPage).ToString()

    $leadList = Invoke-Support -Method 'GET' -Path '/leads'
    Add-Result 'leads: OWN list excludes the hidden lead' 'False' ($leadList.Body.id -contains $leadOtherId).ToString()
    Add-Result 'leads: OWN list returns only the caller own lead' '1' ([int]$leadList.Body.Count)

    # ---- WORKSPACE restores every record through the same enforcement path ----
    Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope 'Workspace'
    Add-Result 'tasks: WORKSPACE restores the other-member task' '200' (Invoke-Support -Method 'GET' -Path "/tasks/$taskOtherId").Status
    Add-Result 'leads: WORKSPACE restores the other-member lead' '200' (Invoke-Support -Method 'GET' -Path "/leads/$leadOtherId").Status
    Add-Result 'products: WORKSPACE restores the product' '200' (Invoke-Support -Method 'GET' -Path "/products/$productOneId").Status
    Add-Result 'deals: WORKSPACE restores the other-member deal' '200' (Invoke-Support -Method 'GET' -Path "/deals/$dealOtherId").Status
    Add-Result 'tasks: WORKSPACE list counts both tasks' '2' ([int](Invoke-Support -Method 'GET' -Path '/tasks').Body.pageInfo.totalCount)
    Add-Result 'deals: WORKSPACE list counts both deals' '2' ([int](Invoke-Support -Method 'GET' -Path '/deals').Body.pageInfo.totalCount)

    # ---- TEAM and CUSTOM fail closed in every retrofitted module ----
    foreach ($unsupported in @('Team', 'Custom')) {
        Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope $unsupported
        Add-Result ("tasks: {0} scope fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Support -Method 'GET' -Path "/tasks/$taskOwnId").Status
        Add-Result ("leads: {0} scope fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Support -Method 'GET' -Path "/leads/$leadOwnId").Status
        Add-Result ("deals: {0} scope fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Support -Method 'GET' -Path "/deals/$dealOwnId").Status
        Add-Result ("products: {0} scope fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Support -Method 'GET' -Path "/products/$productOneId").Status
        Add-Result ("tasks: {0} scope empties the list" -f $unsupported.ToUpperInvariant()) '0' `
            ([int](Invoke-Support -Method 'GET' -Path '/tasks').Body.pageInfo.totalCount)
    }
    Clear-ModuleScope -Database $DatabaseName

    # ---- field enforcement per module, proven on the raw backend JSON ----
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_retro_%';
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_retro_task_desc', '$roleId', 'tasks', 'description', 'Hidden'),
('field_retro_lead_email', '$roleId', 'LEADS', 'email', 'Hidden'),
('field_retro_product_cost', '$roleId', 'products', 'costPrice', 'Masked');
"@

    $taskFields = Invoke-Support -Method 'GET' -Path "/tasks/$taskOwnId"
    Add-Result 'tasks: HIDDEN field is absent from the raw backend JSON' 'True' `
        ($taskFields.Raw -notmatch '"description"').ToString()
    Add-Result 'tasks: the record itself still reads' '200' $taskFields.Status

    # The Leads policy is stored as `LEADS`; a casing difference must not bypass it.
    $leadFields = Invoke-Support -Method 'GET' -Path "/leads/$leadOwnId"
    Add-Result 'leads: mixed-case field policy is still enforced' 'True' `
        ($leadFields.Raw -notmatch '"email"').ToString()

    # MASKED has no admitted representation, so it is enforced as withheld.
    $productFields = Invoke-Support -Method 'GET' -Path "/products/$productOneId"
    Add-Result 'products: MASKED is enforced as withheld' 'True' `
        ($productFields.Raw -notmatch '"costPrice"').ToString()

    # A restrictive policy on a required field cannot be represented, so the read fails closed.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_retro_task_required', '$roleId', 'tasks', 'title', 'Hidden');
"@
    $taskRequired = Invoke-Support -Method 'GET' -Path "/tasks/$taskOwnId"
    Add-Result 'tasks: unwithholdable required field fails the read closed' '403' $taskRequired.Status
    Add-Result 'tasks: unwithholdable required field never returns the value' 'True' `
        ($taskRequired.Raw -notmatch 'Retro task own').ToString()

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_retro_%'"

    # ---- capability denial cannot be restored by scope, in every retrofitted module ----
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'tasks.read'"
    Add-Result 'tasks: losing the read capability denies the record' '403' `
        (Invoke-Support -Method 'GET' -Path "/tasks/$taskOwnId").Status
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'tasks.read')"

    # ---- one authorization and no per-row decision on a list, per module ----
    foreach ($module in @(
            @{ Name = 'tasks'; Path = '/tasks' },
            @{ Name = 'leads'; Path = '/leads' },
            @{ Name = 'deals'; Path = '/deals' },
            @{ Name = 'products'; Path = '/products' })) {
        $before = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.RecordAccessDecisions'
        $capabilityBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions'
        [void](Invoke-Support -Method 'GET' -Path $module.Path)
        $after = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.RecordAccessDecisions'
        $capabilityAfter = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions'
        Add-Result ("cost: {0} list evaluates no per-row record decision" -f $module.Name) '0' ([int]$after - [int]$before)
        Add-Result ("authority: {0} list authorizes exactly once" -f $module.Name) '1' ([int]$capabilityAfter - [int]$capabilityBefore)
    }

    # ---- enforcement evidence exists for every retrofitted module ----
    foreach ($resource in @('tasks', 'leads', 'deals', 'products')) {
        $rows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.RecordAccessDecisions WHERE ResourceKey = '$resource'
"@
        Add-Result ("audit: {0} enforcement writes decision evidence" -f $resource) 'True' ([int]$rows -gt 0).ToString()
    }

    # ------------------------------ 22. AI SUMMARY READERS AFTER THE RETROFIT

    # The Tasks, Leads and Deals summary readers each carried their own copy of the record-scope and
    # field-visibility rules and were rewritten onto the canonical boundary. `verify-ai-assistant.ps1`
    # covers them end to end again now that its Windows PowerShell 5.1 harness defects are fixed;
    # this block keeps their record-scope behaviour proven inside the AccessControl harness too.

    $advisoryBody = @{
        question          = 'What should I focus on next?'
        locale            = 'en'
        contextReferences = @{ leadId = $leadOwnId; dealId = $dealOwnId; taskId = $taskOwnId }
    } | ConvertTo-Json -Compress -Depth 6

    $advisoryWorkspace = Invoke-Support -Method 'POST' -Path '/ai/advisories' -Body $advisoryBody
    Add-Result 'ai: advisory still resolves every summary reader' '200' $advisoryWorkspace.Status
    Add-Result 'ai: advisory resolves the Lead reference' $leadOwnId $advisoryWorkspace.Body.contextReferences.leadId
    Add-Result 'ai: advisory resolves the Deal reference' $dealOwnId $advisoryWorkspace.Body.contextReferences.dealId
    Add-Result 'ai: advisory resolves the Task reference' $taskOwnId $advisoryWorkspace.Body.contextReferences.taskId

    # Under OWN scope the readers must refuse a record the caller does not own, exactly as the
    # module read endpoints do - the rewrite must not have widened what AI can see.
    Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'
    $advisoryHidden = @{
        question          = 'What should I focus on next?'
        locale            = 'en'
        contextReferences = @{ leadId = $leadOtherId; dealId = $dealOtherId; taskId = $taskOtherId }
    } | ConvertTo-Json -Compress -Depth 6
    $advisoryDenied = Invoke-Support -Method 'POST' -Path '/ai/advisories' -Body $advisoryHidden
    Add-Result 'ai: a hidden record is not summarised for AI' 'True' `
        (($advisoryDenied.Status -ne 200) -or ($advisoryDenied.Raw -notmatch 'Retro (lead|deal|task) other')).ToString()

    # The caller's own records stay readable through the same readers under OWN scope.
    $advisoryOwn = Invoke-Support -Method 'POST' -Path '/ai/advisories' -Body $advisoryBody
    Add-Result 'ai: the caller own records remain summarisable under OWN' '200' $advisoryOwn.Status
    Clear-ModuleScope -Database $DatabaseName

    # ------------------------------ 23. SYSTEM-WIDE ENFORCEMENT HARDENING

    # Everything in this section covers a defect class that was reachable through the business API
    # itself: batch replay outrunning current record scope, batch and create responses escaping field
    # security, full-profile replacement writing fields the caller may not write, committed replays
    # being invalidated by later mutable state, the public evaluation disagreeing with direct
    # enforcement, and an unknown field key widening access.

    function Set-GateField {
        param([string] $Resource, [string] $Field, [string] $Access)
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_gate_%';
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_gate_01', '$roleId', '$Resource', '$Field', '$Access');
"@
    }

    function Clear-GateField {
        Invoke-SqlNonQuery -Database $DatabaseName `
            -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_gate_%'"
    }

    function Set-MembershipStatus {
        param([string] $MemberId, [string] $Status)
        Invoke-SqlNonQuery -Database $DatabaseName `
            -Query "UPDATE workspace.Memberships SET Status = '$Status' WHERE MemberId = '$MemberId'"
    }

    Clear-ModuleScope -Database $DatabaseName
    Clear-GateField

    # ---- 23.1 batch mutation authorization order: replay cannot outrun current record scope ----

    # A deal owned by the other member, archived in a batch while the caller's scope is WORKSPACE.
    $batchDeal = Invoke-Support -Method 'POST' -Path '/deals' -IdempotencyKey 'idem-gate-deal-batch-fixture' `
        -Body (@{
            name                 = 'Gate batch deal'
            buyerRef             = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_retro_001' }
            stageCode            = 'DISCOVERY'
            amount               = @{ amount = '4200.00'; currency = 'USD' }
            opportunityScore     = '10'
            ownerId              = $otherOwnerId
            expectedCloseDate    = '2026-12-31'
            interestedProductIds = @()
            lineItems            = @()
            notes                = 'GATE-DEAL-NOTES-SECRET'
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'batch: deal fixture created' '201' $batchDeal.Status
    $batchDealId = $batchDeal.Body.aggregateId

    $batchDealBody = @{
        reason = 'Gate batch archive'
        items  = @(@{ dealId = $batchDealId; expectedVersion = 0 })
    } | ConvertTo-Json -Compress -Depth 6
    $dealBatchCommit = Invoke-Support -Method 'POST' -Path '/deals/archive-batch' `
        -Body $batchDealBody -IdempotencyKey 'idem-gate-deal-batch'
    Add-Result 'batch: deal batch commits under WORKSPACE scope' '200' $dealBatchCommit.Status
    Add-Result 'batch: deal batch response is projected, not raw' 'True' `
        ($dealBatchCommit.Raw -match 'GATE-DEAL-NOTES-SECRET').ToString()

    # Scope narrows to OWN. The batch named a deal owned by another member, so the caller no longer
    # reaches it and the committed key must not replay.
    Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'
    $dealBatchReplayDenied = Invoke-Support -Method 'POST' -Path '/deals/archive-batch' `
        -Body $batchDealBody -IdempotencyKey 'idem-gate-deal-batch'
    Add-Result 'batch: deal batch replay after scope loss is denied' '404' $dealBatchReplayDenied.Status
    Add-Result 'batch: denied deal batch replay leaks no stored projection' 'True' `
        ($dealBatchReplayDenied.Raw -notmatch 'GATE-DEAL-NOTES-SECRET').ToString()
    Clear-ModuleScope -Database $DatabaseName

    # Same key, access still valid: the replay is answered from stored evidence.
    $dealBatchReplayAllowed = Invoke-Support -Method 'POST' -Path '/deals/archive-batch' `
        -Body $batchDealBody -IdempotencyKey 'idem-gate-deal-batch'
    Add-Result 'batch: deal batch replay still succeeds while access holds' '200' $dealBatchReplayAllowed.Status
    Add-Result 'batch: deal batch replay reports REPLAYED' 'REPLAYED' $dealBatchReplayAllowed.Body.outcome

    # ---- 23.2 batch responses obey current field policy, replay included ----
    Set-GateField -Resource 'deals' -Field 'notes' -Access 'Hidden'
    $dealBatchHidden = Invoke-Support -Method 'POST' -Path '/deals/archive-batch' `
        -Body $batchDealBody -IdempotencyKey 'idem-gate-deal-batch'
    Add-Result 'batch: replay under a newly restrictive policy still replays' '200' $dealBatchHidden.Status
    Add-Result 'batch: HIDDEN field is absent from the replayed batch response' 'True' `
        ($dealBatchHidden.Raw -notmatch 'GATE-DEAL-NOTES-SECRET').ToString()
    Add-Result 'batch: HIDDEN field key is absent from the replayed batch response' 'True' `
        ($dealBatchHidden.Raw -notmatch '"notes"').ToString()
    Clear-GateField

    # Products carry no member owner, so OWN denies every Product and the batch replay must fail.
    $batchProduct = Invoke-Support -Method 'POST' -Path '/products' -IdempotencyKey 'idem-gate-product-batch-fixture' `
        -Body (@{
            sku            = 'GATE-BATCH-001'
            name           = 'Gate batch product'
            type           = 'service'
            status         = 'ACTIVE'
            category       = 'Professional Services'
            description    = 'GATE-PRODUCT-DESC-SECRET'
            unit           = 'hour'
            unitPrice      = @{ amount = '10.00'; currency = 'USD' }
            taxRate        = '10'
            taxMode        = 'exclusive'
            billingCycle   = 'one_time'
            isSubscription = $false
            isRenewable    = $false
            tags           = @('gate')
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'batch: product fixture created' '201' $batchProduct.Status
    $batchProductId = $batchProduct.Body.aggregateId

    $batchProductBody = @{
        reason = 'Gate batch archive'
        items  = @(@{ productId = $batchProductId; expectedVersion = 0 })
    } | ConvertTo-Json -Compress -Depth 6
    $productBatchCommit = Invoke-Support -Method 'POST' -Path '/products/archive-batch' `
        -Body $batchProductBody -IdempotencyKey 'idem-gate-product-batch'
    Add-Result 'batch: product batch commits under WORKSPACE scope' '200' $productBatchCommit.Status
    Add-Result 'batch: product batch response is projected, not raw' 'True' `
        ($productBatchCommit.Raw -match 'GATE-PRODUCT-DESC-SECRET').ToString()

    Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'
    $productBatchReplayDenied = Invoke-Support -Method 'POST' -Path '/products/archive-batch' `
        -Body $batchProductBody -IdempotencyKey 'idem-gate-product-batch'
    Add-Result 'batch: product batch replay after scope loss is denied' '404' $productBatchReplayDenied.Status
    Add-Result 'batch: denied product batch replay leaks no stored projection' 'True' `
        ($productBatchReplayDenied.Raw -notmatch 'GATE-PRODUCT-DESC-SECRET').ToString()
    Clear-ModuleScope -Database $DatabaseName

    Set-GateField -Resource 'products' -Field 'description' -Access 'Hidden'
    $productBatchHidden = Invoke-Support -Method 'POST' -Path '/products/archive-batch' `
        -Body $batchProductBody -IdempotencyKey 'idem-gate-product-batch'
    Add-Result 'batch: product batch replay under a restrictive policy still replays' '200' $productBatchHidden.Status
    Add-Result 'batch: product HIDDEN field is absent from the replayed batch response' 'True' `
        ($productBatchHidden.Raw -notmatch 'GATE-PRODUCT-DESC-SECRET').ToString()
    Clear-GateField

    # A restore batch proves the same ordering on the second Products batch operation.
    $restoreBody = @{
        reason = 'Gate batch restore'
        items  = @(@{ productId = $batchProductId; expectedVersion = 1 })
    } | ConvertTo-Json -Compress -Depth 6
    $restoreCommit = Invoke-Support -Method 'POST' -Path '/products/restore-batch' `
        -Body $restoreBody -IdempotencyKey 'idem-gate-product-restore'
    Add-Result 'batch: product restore batch commits' '200' $restoreCommit.Status
    Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'
    Add-Result 'batch: product restore replay after scope loss is denied' '404' `
        (Invoke-Support -Method 'POST' -Path '/products/restore-batch' -Body $restoreBody -IdempotencyKey 'idem-gate-product-restore').Status
    Clear-ModuleScope -Database $DatabaseName

    # ---- 23.3 create-time field-write enforcement, per module ----

    $auditBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM tasks.AuditRecords'
    $outboxBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM tasks.OutboxMessages'
    $idemBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM tasks.IdempotencyRecords'

    Set-GateField -Resource 'tasks' -Field 'description' -Access 'Hidden'
    $taskCreateDenied = Invoke-Support -Method 'POST' -Path '/tasks' -IdempotencyKey 'idem-gate-task-create-denied' `
        -Body (@{ title = 'Gate task'; assigneeId = $callerMemberId; dueAt = '2026-12-01T09:00:00.0000000Z'; description = 'forbidden' } | ConvertTo-Json -Compress)
    Add-Result 'create: task HIDDEN field supplied is refused' '403' $taskCreateDenied.Status
    Add-Result 'create: task refusal is an access denial' 'ACCESS_DENIED' $taskCreateDenied.Body.code
    Add-Result 'create: refused task create wrote no audit' $auditBefore `
        (Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM tasks.AuditRecords')
    Add-Result 'create: refused task create wrote no outbox event' $outboxBefore `
        (Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM tasks.OutboxMessages')
    Add-Result 'create: refused task create wrote no idempotency evidence' $idemBefore `
        (Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM tasks.IdempotencyRecords')

    # The same request without the forbidden field still creates.
    $taskCreateAllowed = Invoke-Support -Method 'POST' -Path '/tasks' -IdempotencyKey 'idem-gate-task-create-allowed' `
        -Body (@{ title = 'Gate task'; assigneeId = $callerMemberId; dueAt = '2026-12-01T09:00:00.0000000Z' } | ConvertTo-Json -Compress)
    Add-Result 'create: task without the forbidden field still creates' '201' $taskCreateAllowed.Status
    $gateTaskId = $taskCreateAllowed.Body.aggregateId
    Clear-GateField

    Set-GateField -Resource 'tasks' -Field 'description' -Access 'ReadOnly'
    Add-Result 'create: task READ_ONLY field supplied is refused' '403' `
        (Invoke-Support -Method 'POST' -Path '/tasks' -IdempotencyKey 'idem-gate-task-create-readonly' `
            -Body (@{ title = 'Gate task ro'; assigneeId = $callerMemberId; dueAt = '2026-12-01T09:00:00.0000000Z'; description = 'forbidden' } | ConvertTo-Json -Compress)).Status
    Clear-GateField

    Set-GateField -Resource 'leads' -Field 'email' -Access 'Hidden'
    $leadCreateDenied = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-gate-lead-create-denied' `
        -Body (@{ displayName = 'Gate lead'; ownerId = $callerMemberId; source = 'manual'; email = 'gate@example.test'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'create: lead HIDDEN field supplied is refused' '403' $leadCreateDenied.Status
    Add-Result 'create: lead without the forbidden field still creates' '201' `
        (Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-gate-lead-create-allowed' `
            -Body (@{ displayName = 'Gate lead'; ownerId = $callerMemberId; source = 'manual'; phone = '0900000001'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)).Status
    Clear-GateField

    Set-GateField -Resource 'deals' -Field 'notes' -Access 'ReadOnly'
    Add-Result 'create: deal READ_ONLY field supplied is refused' '403' `
        (Invoke-Support -Method 'POST' -Path '/deals' -IdempotencyKey 'idem-gate-deal-create-denied' `
            -Body (@{
                name                 = 'Gate deal'
                buyerRef             = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_retro_001' }
                stageCode            = 'DISCOVERY'
                amount               = @{ amount = '10.00'; currency = 'USD' }
                opportunityScore     = '10'
                ownerId              = $callerMemberId
                expectedCloseDate    = '2026-12-31'
                interestedProductIds = @()
                lineItems            = @()
                notes                = 'forbidden'
            } | ConvertTo-Json -Compress -Depth 6)).Status
    Clear-GateField

    Set-GateField -Resource 'products' -Field 'description' -Access 'Hidden'
    Add-Result 'create: product HIDDEN field supplied is refused' '403' `
        (Invoke-Support -Method 'POST' -Path '/products' -IdempotencyKey 'idem-gate-product-create-denied' `
            -Body (@{
                sku            = 'GATE-CREATE-001'
                name           = 'Gate create product'
                type           = 'service'
                status         = 'ACTIVE'
                category       = 'Professional Services'
                description    = 'forbidden'
                unit           = 'hour'
                unitPrice      = @{ amount = '10.00'; currency = 'USD' }
                taxRate        = '10'
                taxMode        = 'exclusive'
                billingCycle   = 'one_time'
                isSubscription = $false
                isRenewable    = $false
                tags           = @()
            } | ConvertTo-Json -Compress -Depth 6)).Status
    Clear-GateField

    # ---- 23.4 full-profile replacement: only changed fields count as writes ----

    $gateLead = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-gate-lead-profile' `
        -Body (@{ displayName = 'Gate profile lead'; ownerId = $callerMemberId; source = 'manual'; title = 'Original title'; phone = '0900000002'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'replace: lead fixture created' '201' $gateLead.Status
    $gateLeadId = $gateLead.Body.aggregateId
    $gateLeadVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM leads.Leads WHERE LeadId = '$gateLeadId'"

    Set-GateField -Resource 'leads' -Field 'title' -Access 'ReadOnly'
    $leadReplaceDenied = Invoke-Support -Method 'PUT' -Path "/leads/$gateLeadId" -IdempotencyKey 'idem-gate-lead-replace-denied' `
        -IfMatchVersion $gateLeadVersion `
        -Body (@{ displayName = 'Gate profile lead'; ownerId = $callerMemberId; source = 'manual'; title = 'Changed title'; phone = '0900000002'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'replace: lead READ_ONLY field change is refused' '403' $leadReplaceDenied.Status
    Add-Result 'replace: refused lead replacement did not bump the version' $gateLeadVersion `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM leads.Leads WHERE LeadId = '$gateLeadId'")

    # The identical value repeated is not a write and must not be refused for field security.
    $leadReplaceUnchanged = Invoke-Support -Method 'PUT' -Path "/leads/$gateLeadId" -IdempotencyKey 'idem-gate-lead-replace-unchanged' `
        -IfMatchVersion $gateLeadVersion `
        -Body (@{ displayName = 'Gate profile lead renamed'; ownerId = $callerMemberId; source = 'manual'; title = 'Original title'; phone = '0900000002'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'replace: unchanged READ_ONLY value is not refused' '200' $leadReplaceUnchanged.Status
    Clear-GateField

    $gateDealVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM deals.Deals WHERE DealId = '$dealOwnId'"
    Set-GateField -Resource 'deals' -Field 'notes' -Access 'ReadOnly'
    $dealReplaceDenied = Invoke-Support -Method 'POST' -Path "/deals/$dealOwnId/update" -IdempotencyKey 'idem-gate-deal-replace-denied' `
        -IfMatchVersion $gateDealVersion `
        -Body (@{
            name                 = 'Retro deal own'
            buyerRef             = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_retro_001' }
            amount               = @{ amount = '1000.00'; currency = 'USD' }
            interestedProductIds = @()
            lineItems            = @()
            notes                = 'Changed notes'
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'replace: deal READ_ONLY field change is refused' '403' $dealReplaceDenied.Status
    Add-Result 'replace: refused deal replacement did not bump the version' $gateDealVersion `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM deals.Deals WHERE DealId = '$dealOwnId'")
    $dealReplaceUnchanged = Invoke-Support -Method 'POST' -Path "/deals/$dealOwnId/update" -IdempotencyKey 'idem-gate-deal-replace-unchanged' `
        -IfMatchVersion $gateDealVersion `
        -Body (@{
            name                 = 'Retro deal own renamed'
            buyerRef             = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_retro_001' }
            amount               = @{ amount = '1000.00'; currency = 'USD' }
            interestedProductIds = @()
            lineItems            = @()
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'replace: unchanged deal READ_ONLY value is not refused' '200' $dealReplaceUnchanged.Status
    Clear-GateField

    $gateProductVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM products.Products WHERE ProductId = '$productOneId'"
    Set-GateField -Resource 'products' -Field 'description' -Access 'ReadOnly'
    $productReplaceBody = @{
        sku            = 'RETRO-001'
        name           = 'Retro product'
        type           = 'service'
        status         = 'ACTIVE'
        category       = 'Professional Services'
        description    = 'Changed description'
        unit           = 'hour'
        unitPrice      = @{ amount = '10.125'; currency = 'USD' }
        costPrice      = @{ amount = '4.25'; currency = 'USD' }
        taxRate        = '10'
        taxMode        = 'exclusive'
        billingCycle   = 'one_time'
        isSubscription = $false
        isRenewable    = $false
        tags           = @('verified', 'core')
    } | ConvertTo-Json -Compress -Depth 6
    $productReplaceDenied = Invoke-Support -Method 'PUT' -Path "/products/$productOneId" -IdempotencyKey 'idem-gate-product-replace-denied' `
        -IfMatchVersion $gateProductVersion -Body $productReplaceBody
    Add-Result 'replace: product READ_ONLY field change is refused' '403' $productReplaceDenied.Status
    Add-Result 'replace: refused product replacement did not bump the version' $gateProductVersion `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM products.Products WHERE ProductId = '$productOneId'")
    $productReplaceUnchanged = Invoke-Support -Method 'PUT' -Path "/products/$productOneId" -IdempotencyKey 'idem-gate-product-replace-unchanged' `
        -IfMatchVersion $gateProductVersion `
        -Body ($productReplaceBody.Replace('Changed description', 'Record-access retrofit fixture').Replace('Retro product', 'Retro product renamed'))
    Add-Result 'replace: unchanged product READ_ONLY value is not refused' '200' $productReplaceUnchanged.Status
    Clear-GateField

    # ---- 23.5 committed replay survives later mutable member state ----

    $assignBody = @{ assigneeId = $otherOwnerId } | ConvertTo-Json -Compress
    $gateTaskVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM tasks.Tasks WHERE TaskId = '$gateTaskId'"
    $assignCommit = Invoke-Support -Method 'POST' -Path "/tasks/$gateTaskId/assign" -IdempotencyKey 'idem-gate-task-assign' `
        -IfMatchVersion $gateTaskVersion -Body $assignBody
    Add-Result 'replay: task assignment commits' '200' $assignCommit.Status
    Set-MembershipStatus -MemberId $otherOwnerId -Status 'Suspended'
    $assignReplay = Invoke-Support -Method 'POST' -Path "/tasks/$gateTaskId/assign" -IdempotencyKey 'idem-gate-task-assign' `
        -IfMatchVersion $gateTaskVersion -Body $assignBody
    Add-Result 'replay: task assignment replay survives a suspended assignee' '200' $assignReplay.Status
    Add-Result 'replay: task assignment replay reports REPLAYED' 'REPLAYED' $assignReplay.Body.outcome

    # A new command naming the suspended member is still refused - only the replay is durable.
    $assignNew = Invoke-Support -Method 'POST' -Path "/tasks/$gateTaskId/assign" -IdempotencyKey 'idem-gate-task-assign-new' `
        -IfMatchVersion (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM tasks.Tasks WHERE TaskId = '$gateTaskId'") `
        -Body $assignBody
    Add-Result 'replay: a new command still rejects the suspended member' 'True' `
        (($assignNew.Status -ne 200)).ToString()

    # Leads: the owner precondition on a profile replacement behaves the same way.
    $leadOwnerBody = @{ displayName = 'Gate profile lead renamed'; ownerId = $otherOwnerId; source = 'manual'; title = 'Original title'; phone = '0900000002'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6
    Set-MembershipStatus -MemberId $otherOwnerId -Status 'Active'
    $leadOwnerVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM leads.Leads WHERE LeadId = '$gateLeadId'"
    $leadOwnerCommit = Invoke-Support -Method 'PUT' -Path "/leads/$gateLeadId" -IdempotencyKey 'idem-gate-lead-owner' `
        -IfMatchVersion $leadOwnerVersion -Body $leadOwnerBody
    Add-Result 'replay: lead owner replacement commits' '200' $leadOwnerCommit.Status
    Set-MembershipStatus -MemberId $otherOwnerId -Status 'Suspended'
    $leadOwnerReplay = Invoke-Support -Method 'PUT' -Path "/leads/$gateLeadId" -IdempotencyKey 'idem-gate-lead-owner' `
        -IfMatchVersion $leadOwnerVersion -Body $leadOwnerBody
    Add-Result 'replay: lead owner replay survives a suspended owner' '200' $leadOwnerReplay.Status
    Set-MembershipStatus -MemberId $otherOwnerId -Status 'Active'

    # Deals: the owner assignment precondition behaves the same way.
    $dealAssignBody = @{ ownerId = $otherOwnerId; reason = 'Gate owner assignment' } | ConvertTo-Json -Compress
    $dealAssignVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM deals.Deals WHERE DealId = '$dealOwnId'"
    $dealAssignCommit = Invoke-Support -Method 'POST' -Path "/deals/$dealOwnId/assign" -IdempotencyKey 'idem-gate-deal-assign' `
        -IfMatchVersion $dealAssignVersion -Body $dealAssignBody
    Add-Result 'replay: deal owner assignment commits' '200' $dealAssignCommit.Status
    Set-MembershipStatus -MemberId $otherOwnerId -Status 'Suspended'
    $dealAssignReplay = Invoke-Support -Method 'POST' -Path "/deals/$dealOwnId/assign" -IdempotencyKey 'idem-gate-deal-assign' `
        -IfMatchVersion $dealAssignVersion -Body $dealAssignBody
    Add-Result 'replay: deal owner assignment replay survives a suspended owner' '200' $dealAssignReplay.Status
    Set-MembershipStatus -MemberId $otherOwnerId -Status 'Active'

    # ---- 23.6 the public evaluation and direct enforcement share one rule ----

    # The frozen rule is additive: a record-targeting command requires the resource read capability,
    # the command capability and record scope. Every cell of the matrix is checked twice - once as
    # the evaluation reports it, once as the business API enforces it.
    $matrixTaskVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM tasks.Tasks WHERE TaskId = '$taskOwnId'"
    foreach ($cell in @(
            @{ Read = $true;  Command = $true;  Allowed = $true },
            @{ Read = $false; Command = $true;  Allowed = $false },
            @{ Read = $true;  Command = $false; Allowed = $false },
            @{ Read = $false; Command = $false; Allowed = $false })) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability IN ('tasks.read', 'tasks.complete');
"@
        if ($cell.Read) {
            Invoke-SqlNonQuery -Database $DatabaseName `
                -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'tasks.read')"
        }
        if ($cell.Command) {
            Invoke-SqlNonQuery -Database $DatabaseName `
                -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'tasks.complete')"
        }

        $label = ('read={0}/command={1}' -f $cell.Read, $cell.Command)
        $evaluation = Invoke-Evaluate -Request @{
            resourceKey       = 'tasks'
            recordId          = $taskOwnId
            requestedCommands = @('task.complete')
            requestedFields   = @('title')
        }
        $reported = ($evaluation.Body.allowedCommands -contains 'task.complete')
        Add-Result ("semantics: evaluation reports task.complete for {0}" -f $label) `
            ($cell.Allowed).ToString() $reported.ToString()

        $direct = Invoke-Support -Method 'POST' -Path "/tasks/$taskOwnId/complete" `
            -Body (@{ outcome = 'gate matrix' } | ConvertTo-Json -Compress) `
            -IdempotencyKey ("idem-gate-matrix-{0}-{1}" -f $cell.Read, $cell.Command) `
            -IfMatchVersion $matrixTaskVersion
        $enforced = ($direct.Status -eq 200)
        Add-Result ("semantics: direct completeTask enforces the same for {0}" -f $label) `
            ($cell.Allowed).ToString() $enforced.ToString()
        Add-Result ("semantics: report and enforcement agree for {0}" -f $label) `
            $reported.ToString() $enforced.ToString()

        if ($enforced) {
            $matrixTaskVersion = Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM tasks.Tasks WHERE TaskId = '$taskOwnId'"
        }
    }
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability IN ('tasks.read', 'tasks.complete');
INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'tasks.read'), ('$roleId', 'tasks.complete');
"@

    # ---- 23.7 the audited capability is the capability the operation required ----

    $gateAuditTask = Invoke-Support -Method 'POST' -Path '/tasks' -IdempotencyKey 'idem-gate-audit-task' `
        -Body (@{ title = 'Gate audit task'; assigneeId = $callerMemberId; dueAt = '2026-12-01T09:00:00.0000000Z' } | ConvertTo-Json -Compress)
    $gateAuditTaskId = $gateAuditTask.Body.aggregateId

    foreach ($probe in @(
            @{ Name = 'completeTask'; Capability = 'tasks.complete' },
            @{ Name = 'getLead';      Capability = 'leads.read' },
            @{ Name = 'archiveProduct'; Capability = 'products.delete' })) {
        $before = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.AuthorizationDecisions WHERE RequiredCapability = '$($probe.Capability)'
"@
        $totalBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions'
        if ($probe.Name -eq 'completeTask') {
            [void](Invoke-Support -Method 'POST' -Path "/tasks/$gateAuditTaskId/complete" `
                -Body (@{ outcome = 'gate audit' } | ConvertTo-Json -Compress) `
                -IdempotencyKey 'idem-gate-audit-complete' -IfMatchVersion '0')
        }
        elseif ($probe.Name -eq 'getLead') {
            [void](Invoke-Support -Method 'GET' -Path "/leads/$leadOwnId")
        }
        else {
            [void](Invoke-Support -Method 'POST' -Path "/products/$batchProductId/archive" `
                -Body (@{ reason = 'Gate audit archive' } | ConvertTo-Json -Compress) `
                -IdempotencyKey 'idem-gate-audit-archive' `
                -IfMatchVersion (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM products.Products WHERE ProductId = '$batchProductId'"))
        }
        $after = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM access.AuthorizationDecisions WHERE RequiredCapability = '$($probe.Capability)'
"@
        $totalAfter = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions'
        Add-Result ("audit: {0} records its own capability" -f $probe.Name) '1' ([int]$after - [int]$before)
        Add-Result ("audit: {0} authorizes exactly once" -f $probe.Name) '1' ([int]$totalAfter - [int]$totalBefore)
    }

    # ---- 23.8 TaskActivity fails closed outside WORKSPACE scope ----

    $activityBody = @{ type = 'NOTE'; subject = 'GATE-ACTIVITY-SUBJECT'; body = 'Gate activity body' } | ConvertTo-Json -Compress -Depth 4
    $activityCommit = Invoke-Support -Method 'POST' -Path '/activities' -Body $activityBody -IdempotencyKey 'idem-gate-activity'
    Add-Result 'activity: WORKSPACE scope logs an activity' '201' $activityCommit.Status
    Add-Result 'activity: WORKSPACE scope lists activities' 'True' `
        ((Invoke-Support -Method 'GET' -Path '/activities').Raw -match 'GATE-ACTIVITY-SUBJECT').ToString()

    foreach ($restricted in @('Own', 'Team', 'Custom')) {
        Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope $restricted
        $restrictedList = Invoke-Support -Method 'GET' -Path '/activities'
        Add-Result ("activity: {0} scope returns no activity" -f $restricted.ToUpperInvariant()) '0' `
            ([int]$restrictedList.Body.pageInfo.totalCount)
        Add-Result ("activity: {0} scope leaks no activity subject" -f $restricted.ToUpperInvariant()) 'True' `
            ($restrictedList.Raw -notmatch 'GATE-ACTIVITY-SUBJECT').ToString()
        Add-Result ("activity: {0} scope refuses logActivity" -f $restricted.ToUpperInvariant()) '403' `
            (Invoke-Support -Method 'POST' -Path '/activities' -Body $activityBody `
                -IdempotencyKey ("idem-gate-activity-{0}" -f $restricted)).Status
    }
    Clear-ModuleScope -Database $DatabaseName

    # ---- 23.9 an unknown field key fails closed, a known key stays case-insensitive ----

    # Windows PowerShell 5.1 cannot ConvertFrom-Json an object whose keys differ only by case, so
    # each casing is asked for in its own evaluation rather than in one combined request.
    $fieldProbe = Invoke-Evaluate -Request @{
        resourceKey     = 'tasks'
        recordId        = $taskOwnId
        requestedFields = @('assigneId', 'subject', 'assigneeId')
    }
    Add-Result 'fields: a typo in a field key is not writable' 'HIDDEN' $fieldProbe.Body.fieldAccess.assigneId
    Add-Result 'fields: a key the owner does not declare is not readable' 'HIDDEN' $fieldProbe.Body.fieldAccess.subject
    Add-Result 'fields: a declared key is enforced normally' 'READ_WRITE' $fieldProbe.Body.fieldAccess.assigneeId

    foreach ($casing in @('ASSIGNEEID', 'AsSiGnEeId', 'assigneeid')) {
        $casingProbe = Invoke-Evaluate -Request @{
            resourceKey     = 'tasks'
            recordId        = $taskOwnId
            requestedFields = @($casing)
        }
        Add-Result ("fields: the declared key still resolves spelled {0}" -f $casing) 'READ_WRITE' `
            $casingProbe.Body.fieldAccess.$casing
    }

    # The same fail-closed answer must hold under a restrictive policy stored for a real key: the
    # unknown key is not widened by the presence of any policy at all.
    Set-GateField -Resource 'tasks' -Field 'AsSiGnEeId' -Access 'ReadOnly'
    $fieldProbeRestricted = Invoke-Evaluate -Request @{
        resourceKey     = 'tasks'
        recordId        = $taskOwnId
        requestedFields = @('assigneId', 'assigneeId')
    }
    Add-Result 'fields: a mixed-case policy key still restricts the declared field' 'READ_ONLY' `
        $fieldProbeRestricted.Body.fieldAccess.assigneeId
    Add-Result 'fields: the unknown key stays closed under a live policy' 'HIDDEN' `
        $fieldProbeRestricted.Body.fieldAccess.assigneId
    Clear-GateField

    # ---- 23.10 no list authorization regression after the hardening ----
    foreach ($module in @(
            @{ Name = 'tasks'; Path = '/tasks' },
            @{ Name = 'leads'; Path = '/leads' },
            @{ Name = 'deals'; Path = '/deals' },
            @{ Name = 'products'; Path = '/products' },
            @{ Name = 'activities'; Path = '/activities' })) {
        $before = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.RecordAccessDecisions'
        $capabilityBefore = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions'
        [void](Invoke-Support -Method 'GET' -Path $module.Path)
        Add-Result ("cost: {0} list still evaluates no per-row record decision" -f $module.Name) '0' `
            ([int](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.RecordAccessDecisions') - [int]$before)
        Add-Result ("authority: {0} list still authorizes exactly once" -f $module.Name) '1' `
            ([int](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM access.AuthorizationDecisions') - [int]$capabilityBefore)
    }

    # --------------------- 24. FINAL HARDENING: ANTI-LEAK, REPLAY AND REPRESENTATION

    # 24.1 PRODUCT FOREIGN-WORKSPACE DETAIL ANTI-LEAK
    #
    # Product lookups used to load by global identifier and then compare the Workspace, answering
    # 404 for an unknown identifier and 403 WORKSPACE_MISMATCH for a real Product of another
    # Workspace. That difference is an existence oracle. The lookup is now Workspace-scoped in SQL,
    # so a foreign Product is never materialised and both answers collapse.

    $leakProduct = Invoke-Support -Method 'POST' -Path '/products' -IdempotencyKey 'idem-leak-product-fixture' `
        -Body (@{
            sku            = 'LEAK-001'
            name           = 'LEAK-PRODUCT-NAME'
            type           = 'service'
            status         = 'ACTIVE'
            category       = 'Professional Services'
            description    = 'LEAK-PRODUCT-DESCRIPTION'
            unit           = 'hour'
            unitPrice      = @{ amount = '10.00'; currency = 'USD' }
            taxRate        = '10'
            taxMode        = 'exclusive'
            billingCycle   = 'one_time'
            isSubscription = $false
            isRenewable    = $false
            tags           = @('leak')
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'leak: product fixture created' '201' $leakProduct.Status
    $leakProductId = $leakProduct.Body.aggregateId

    # The Product physically exists, but in the isolation Workspace the caller is not a member of.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "UPDATE products.Products SET WorkspaceId = '$foreignWorkspaceId' WHERE ProductId = '$leakProductId'"

    $unknownProductId = 'product_does_not_exist_00001'
    $foreignDetail = Invoke-Support -Method 'GET' -Path "/products/$leakProductId"
    $unknownDetail = Invoke-Support -Method 'GET' -Path "/products/$unknownProductId"
    Add-Result 'leak: foreign-Workspace Product detail is not found' '404' $foreignDetail.Status
    Add-Result 'leak: unknown Product detail is not found' '404' $unknownDetail.Status
    Add-Result 'leak: foreign Product detail is indistinguishable from an unknown one' `
        (Get-ComparablePayload -Raw $unknownDetail.Raw) (Get-ComparablePayload -Raw $foreignDetail.Raw)
    Add-Result 'leak: foreign Product detail leaks no business value' 'True' `
        (($foreignDetail.Raw -notmatch 'LEAK-PRODUCT-NAME') -and ($foreignDetail.Raw -notmatch 'LEAK-001')).ToString()

    # The derived projections read through the same lookup and must collapse identically.
    foreach ($projection in @('availability', 'price-projection?quantity=1')) {
        $foreignProjection = Invoke-Support -Method 'GET' -Path ("/products/{0}/{1}" -f $leakProductId, $projection)
        $unknownProjection = Invoke-Support -Method 'GET' -Path ("/products/{0}/{1}" -f $unknownProductId, $projection)
        # These two validate their own query contract before any lookup, so the status is whatever
        # that contract yields. The security property is that it does not differ between a real
        # foreign Product and one that never existed.
        Add-Result ("leak: foreign Product {0} status matches unknown" -f $projection) `
            ([string]$unknownProjection.Status) ([string]$foreignProjection.Status)
        Add-Result ("leak: foreign Product {0} leaks no business value" -f $projection) 'True' `
            ($foreignProjection.Raw -notmatch 'LEAK-PRODUCT-NAME').ToString()
        Add-Result ("leak: foreign Product {0} is indistinguishable from unknown" -f $projection) `
            (Get-ComparablePayload -Raw $unknownProjection.Raw) (Get-ComparablePayload -Raw $foreignProjection.Raw)
    }

    # The public record-access evaluation reads the same Workspace-scoped fact provider.
    $foreignEvaluation = Invoke-Evaluate -Request @{ resourceKey = 'products'; recordId = $leakProductId; requestedFields = @('name') }
    $unknownEvaluation = Invoke-Evaluate -Request @{ resourceKey = 'products'; recordId = $unknownProductId; requestedFields = @('name') }
    Add-Result 'leak: evaluation of a foreign Product denies' 'False' ($foreignEvaluation.Body.canRead).ToString()
    Add-Result 'leak: evaluation of a foreign Product matches an unknown one' `
        (Get-ComparablePayload -Raw ($unknownEvaluation.Raw -replace [regex]::Escape($unknownProductId), '<id>')) `
        (Get-ComparablePayload -Raw ($foreignEvaluation.Raw -replace [regex]::Escape($leakProductId), '<id>'))

    # 24.2 PRODUCT FOREIGN-WORKSPACE MUTATION ANTI-LEAK
    $foreignVersionBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT Version FROM products.Products WHERE ProductId = '$leakProductId'"
    # Creating the fixture legitimately wrote its own audit and idempotency evidence, so the baseline
    # is taken here: the property under test is that the refused foreign mutations add none.
    $foreignAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) AS N FROM products.AuditRecords WHERE AggregateId = '$leakProductId'"
    $foreignIdempotencyBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) AS N FROM products.IdempotencyRecords WHERE IdempotencyKey LIKE 'idem-leak-%'"

    $foreignArchive = Invoke-Support -Method 'POST' -Path "/products/$leakProductId/archive" `
        -Body (@{ reason = 'Anti-leak probe' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-leak-archive-foreign' -IfMatchVersion '0'
    $unknownArchive = Invoke-Support -Method 'POST' -Path "/products/$unknownProductId/archive" `
        -Body (@{ reason = 'Anti-leak probe' } | ConvertTo-Json -Compress) `
        -IdempotencyKey 'idem-leak-archive-unknown' -IfMatchVersion '0'
    Add-Result 'leak: archiving a foreign Product is not found' '404' $foreignArchive.Status
    Add-Result 'leak: foreign archive is indistinguishable from unknown' `
        (Get-ComparablePayload -Raw $unknownArchive.Raw) (Get-ComparablePayload -Raw $foreignArchive.Raw)

    $foreignReplace = Invoke-Support -Method 'PUT' -Path "/products/$leakProductId" -IfMatchVersion '0' `
        -IdempotencyKey 'idem-leak-replace-foreign' `
        -Body (@{
            sku = 'LEAK-001'; name = 'Renamed'; type = 'service'; status = 'ACTIVE'
            category = 'Professional Services'; unit = 'hour'
            unitPrice = @{ amount = '11.00'; currency = 'USD' }
            taxRate = '10'; taxMode = 'exclusive'; billingCycle = 'one_time'
            isSubscription = $false; isRenewable = $false; tags = @()
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'leak: replacing a foreign Product is not found' '404' $foreignReplace.Status
    Add-Result 'leak: no refused foreign mutation changed the record' $foreignVersionBefore `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM products.Products WHERE ProductId = '$leakProductId'")
    Add-Result 'leak: no refused foreign mutation wrote audit evidence' $foreignAuditBefore `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM products.AuditRecords WHERE AggregateId = '$leakProductId'")
    Add-Result 'leak: no refused foreign mutation wrote idempotency evidence' $foreignIdempotencyBefore `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM products.IdempotencyRecords WHERE IdempotencyKey LIKE 'idem-leak-%'")

    # 24.3 PRODUCT BATCH FOREIGN-WORKSPACE ANTI-LEAK
    foreach ($batchRoute in @('archive-batch', 'restore-batch')) {
        $foreignBatch = Invoke-Support -Method 'POST' -Path ("/products/{0}" -f $batchRoute) `
            -IdempotencyKey ("idem-leak-batch-foreign-{0}" -f $batchRoute) `
            -Body (@{ reason = 'Anti-leak probe'; items = @(@{ productId = $leakProductId; expectedVersion = 0 }) } | ConvertTo-Json -Compress -Depth 6)
        $unknownBatch = Invoke-Support -Method 'POST' -Path ("/products/{0}" -f $batchRoute) `
            -IdempotencyKey ("idem-leak-batch-unknown-{0}" -f $batchRoute) `
            -Body (@{ reason = 'Anti-leak probe'; items = @(@{ productId = $unknownProductId; expectedVersion = 0 }) } | ConvertTo-Json -Compress -Depth 6)
        Add-Result ("leak: {0} naming a foreign Product is not found" -f $batchRoute) '404' $foreignBatch.Status
        Add-Result ("leak: {0} foreign batch is indistinguishable from unknown" -f $batchRoute) `
            (Get-ComparablePayload -Raw $unknownBatch.Raw) (Get-ComparablePayload -Raw $foreignBatch.Raw)
        Add-Result ("leak: {0} foreign batch leaks no business value" -f $batchRoute) 'True' `
            ($foreignBatch.Raw -notmatch 'LEAK-PRODUCT-NAME').ToString()
    }

    # A batch mixing a reachable Product with a foreign one must not reveal which one was the problem.
    $mixedBatch = Invoke-Support -Method 'POST' -Path '/products/archive-batch' -IdempotencyKey 'idem-leak-batch-mixed' `
        -Body (@{
            reason = 'Anti-leak probe'
            items  = @(@{ productId = $productOneId; expectedVersion = 0 }, @{ productId = $leakProductId; expectedVersion = 0 })
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'leak: a mixed batch naming a foreign Product is not found' '404' $mixedBatch.Status
    Add-Result 'leak: the mixed batch archived nothing' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM products.Products WHERE ProductId = '$productOneId' AND ArchivedAt IS NOT NULL")

    # Restore the fixture to the trusted Workspace so later sections see a consistent world.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM products.Products WHERE ProductId = '$leakProductId'"

    # 24.4 REPLAY AFTER RECORD-SCOPE LOSS, SINGLE RECORD
    #
    # 23.1 proves this for a batch. A single-record mutation must behave identically: current record
    # scope is re-evaluated before the idempotency lookup, so losing access denies the replay.
    $scopeTask = Invoke-Support -Method 'POST' -Path '/tasks' -IdempotencyKey 'idem-final-scope-task' `
        -Body (@{ title = 'Final scope task'; assigneeId = $otherOwnerId; dueAt = '2026-12-01T09:00:00.0000000Z' } | ConvertTo-Json -Compress)
    Add-Result 'replay: scope fixture created' '201' $scopeTask.Status
    $scopeTaskId = $scopeTask.Body.aggregateId
    $scopeBody = @{ outcome = 'final scope' } | ConvertTo-Json -Compress
    $scopeCommit = Invoke-Support -Method 'POST' -Path "/tasks/$scopeTaskId/complete" `
        -Body $scopeBody -IdempotencyKey 'idem-final-scope-complete' -IfMatchVersion '0'
    Add-Result 'replay: completion commits under WORKSPACE scope' '200' $scopeCommit.Status

    Set-ModuleScope -RoleId $roleId -Database $DatabaseName -Scope 'Own'
    $scopeReplayDenied = Invoke-Support -Method 'POST' -Path "/tasks/$scopeTaskId/complete" `
        -Body $scopeBody -IdempotencyKey 'idem-final-scope-complete' -IfMatchVersion '0'
    Add-Result 'replay: replay after record-scope loss is denied' '404' $scopeReplayDenied.Status
    Add-Result 'replay: the denied replay returned no stored projection' 'True' `
        ($scopeReplayDenied.Raw -notmatch 'Final scope task').ToString()
    Clear-ModuleScope -Database $DatabaseName
    Add-Result 'replay: the same key replays again once scope is restored' 'REPLAYED' `
        (Invoke-Support -Method 'POST' -Path "/tasks/$scopeTaskId/complete" -Body $scopeBody `
            -IdempotencyKey 'idem-final-scope-complete' -IfMatchVersion '0').Body.outcome

    # 24.5 REPLAY AFTER CAPABILITY LOSS
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'tasks.complete'"
    $capabilityReplay = Invoke-Support -Method 'POST' -Path "/tasks/$scopeTaskId/complete" `
        -Body $scopeBody -IdempotencyKey 'idem-final-scope-complete' -IfMatchVersion '0'
    Add-Result 'replay: replay after capability loss is denied' '403' $capabilityReplay.Status
    Add-Result 'replay: capability denial code' 'ACCESS_DENIED' $capabilityReplay.Body.code
    Add-Result 'replay: the denied replay returned no stored projection' 'True' `
        ($capabilityReplay.Raw -notmatch 'Final scope task').ToString()
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'tasks.complete')"

    # Losing the resource read capability denies the replay too, because a record-targeting command
    # requires read, the command capability and record scope together.
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'tasks.read'"
    Add-Result 'replay: replay after read-capability loss is denied' 'True' `
        ((Invoke-Support -Method 'POST' -Path "/tasks/$scopeTaskId/complete" -Body $scopeBody `
            -IdempotencyKey 'idem-final-scope-complete' -IfMatchVersion '0').Status -ne 200).ToString()
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'tasks.read')"

    # 24.6 / 24.7 REPLAY AFTER A FIELD-WRITE POLICY TIGHTENS
    #
    # The frozen rule: current capability and current record scope gate a replay; the current
    # field-READ policy is applied to the replayed projection; the current field-WRITE policy is
    # required only for a new execution, because a replay writes nothing.
    $policyDeal = Invoke-Support -Method 'POST' -Path '/deals' -IdempotencyKey 'idem-final-policy-deal' `
        -Body (@{
            name                 = 'Final policy deal'
            buyerRef             = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_retro_001' }
            stageCode            = 'DISCOVERY'
            amount               = @{ amount = '900.00'; currency = 'USD' }
            opportunityScore     = '10'
            ownerId              = $callerMemberId
            expectedCloseDate    = '2026-12-31'
            interestedProductIds = @()
            lineItems            = @()
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'replay: policy fixture created' '201' $policyDeal.Status
    $policyDealId = $policyDeal.Body.aggregateId
    $policyArchiveBody = @{ reason = 'FINAL-POLICY-ARCHIVE-REASON' } | ConvertTo-Json -Compress
    $policyCommit = Invoke-Support -Method 'POST' -Path "/deals/$policyDealId/archive" `
        -Body $policyArchiveBody -IdempotencyKey 'idem-final-policy-archive' -IfMatchVersion '0'
    Add-Result 'replay: archive commits while the field is writable' '200' $policyCommit.Status
    Add-Result 'replay: the committed response carries the written value' 'True' `
        ($policyCommit.Raw -match 'FINAL-POLICY-ARCHIVE-REASON').ToString()

    # READ_WRITE -> READ_ONLY. The write already happened, so the replay must still succeed and the
    # value stays readable because READ_ONLY does not withhold it.
    Set-GateField -Resource 'deals' -Field 'archiveReason' -Access 'ReadOnly'
    $readOnlyReplay = Invoke-Support -Method 'POST' -Path "/deals/$policyDealId/archive" `
        -Body $policyArchiveBody -IdempotencyKey 'idem-final-policy-archive' -IfMatchVersion '0'
    Add-Result 'replay: READ_ONLY after commit does not break the replay' '200' $readOnlyReplay.Status
    Add-Result 'replay: the READ_ONLY replay reports REPLAYED' 'REPLAYED' $readOnlyReplay.Body.outcome
    Add-Result 'replay: a READ_ONLY value is still readable on replay' 'True' `
        ($readOnlyReplay.Raw -match 'FINAL-POLICY-ARCHIVE-REASON').ToString()

    # A genuinely new execution under the same policy is refused before any mutation.
    $newUnderReadOnly = Invoke-Support -Method 'POST' -Path "/deals/$dealOwnId/archive" `
        -Body $policyArchiveBody -IdempotencyKey 'idem-final-policy-new-readonly' `
        -IfMatchVersion (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM deals.Deals WHERE DealId = '$dealOwnId'")
    Add-Result 'replay: a new execution under READ_ONLY is refused' '403' $newUnderReadOnly.Status
    Add-Result 'replay: the refused new execution changed nothing' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM deals.Deals WHERE DealId = '$dealOwnId' AND ArchivedAt IS NOT NULL")

    # READ_WRITE -> HIDDEN. The replay still succeeds, but the current read policy is applied to the
    # stored projection, so the value cannot leak out of old idempotency evidence.
    Set-GateField -Resource 'deals' -Field 'archiveReason' -Access 'Hidden'
    $hiddenReplay = Invoke-Support -Method 'POST' -Path "/deals/$policyDealId/archive" `
        -Body $policyArchiveBody -IdempotencyKey 'idem-final-policy-archive' -IfMatchVersion '0'
    Add-Result 'replay: HIDDEN after commit does not break the replay' '200' $hiddenReplay.Status
    Add-Result 'replay: the HIDDEN value is absent from the replayed projection' 'True' `
        ($hiddenReplay.Raw -notmatch 'FINAL-POLICY-ARCHIVE-REASON').ToString()
    Add-Result 'replay: the HIDDEN field key is absent from the replayed projection' 'True' `
        ($hiddenReplay.Raw -notmatch '"archiveReason"').ToString()

    # 24.8 NEW EXECUTION UNDER HIDDEN
    $newUnderHidden = Invoke-Support -Method 'POST' -Path "/deals/$dealOwnId/archive" `
        -Body $policyArchiveBody -IdempotencyKey 'idem-final-policy-new-hidden' `
        -IfMatchVersion (Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM deals.Deals WHERE DealId = '$dealOwnId'")
    Add-Result 'replay: a new execution under HIDDEN is refused' '403' $newUnderHidden.Status
    Clear-GateField

    # 24.9 REPRESENTATION-SPECIFIC WITHHOLDING IS SCOPED TO ITS OWN REPRESENTATION
    #
    # The minimized summary contract declares every field optional, so a HIDDEN policy on a field the
    # full read model makes required is honoured there by omitting the value. The same policy must
    # still fail the full read closed - the override may only turn a refusal into a withheld value,
    # never a withheld value into a returned one.
    Set-GateField -Resource 'tasks' -Field 'title' -Access 'Hidden'
    $summaryUnderHidden = Invoke-Support -Method 'POST' -Path '/ai/advisories' `
        -Body (@{ question = 'What next?'; locale = 'en'; contextReferences = @{ taskId = $taskOwnId } } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'representation: the summary contract withholds a required-elsewhere field' '200' $summaryUnderHidden.Status
    Add-Result 'representation: the withheld title never reaches the summary consumer' 'True' `
        ($summaryUnderHidden.Raw -notmatch 'Retro task own').ToString()
    $fullUnderHidden = Invoke-Support -Method 'GET' -Path "/tasks/$taskOwnId"
    Add-Result 'representation: the full read model still fails closed under the same policy' '403' $fullUnderHidden.Status
    Add-Result 'representation: the full read never emits the withheld value' 'True' `
        ($fullUnderHidden.Raw -notmatch 'Retro task own').ToString()
    Add-Result 'representation: the list projection also fails closed' '403' `
        (Invoke-Support -Method 'GET' -Path '/tasks').Status
    Clear-GateField

    # 24.10 TASKACTIVITY: BOTH GAPS FAIL CLOSED
    #
    # 23.8 covers the record-scope gap. TaskActivity field security is a separate AUTHORITY_GAP: an
    # Activity carries free text and a record label for any module, so it can quote a value some
    # field policy withholds elsewhere, and no authority maps Activity fields to any policy. The
    # gate therefore also fails closed the moment any restrictive `tasks` field policy applies.
    Add-Result 'activity: unrestricted policy still lists activities' 'True' `
        ((Invoke-Support -Method 'GET' -Path '/activities').Raw -match 'GATE-ACTIVITY-SUBJECT').ToString()

    foreach ($restriction in @('Hidden', 'ReadOnly', 'Masked')) {
        Set-GateField -Resource 'tasks' -Field 'description' -Access $restriction
        $restrictedActivities = Invoke-Support -Method 'GET' -Path '/activities'
        Add-Result ("activity: a {0} tasks field policy empties the activity list" -f $restriction.ToUpperInvariant()) '0' `
            ([int]$restrictedActivities.Body.pageInfo.totalCount)
        Add-Result ("activity: a {0} tasks field policy leaks no activity subject" -f $restriction.ToUpperInvariant()) 'True' `
            ($restrictedActivities.Raw -notmatch 'GATE-ACTIVITY-SUBJECT').ToString()
        Add-Result ("activity: a {0} tasks field policy refuses logActivity" -f $restriction.ToUpperInvariant()) '403' `
            (Invoke-Support -Method 'POST' -Path '/activities' `
                -Body (@{ type = 'NOTE'; subject = 'GATE-ACTIVITY-BLOCKED'; body = 'blocked' } | ConvertTo-Json -Compress -Depth 4) `
                -IdempotencyKey ("idem-final-activity-{0}" -f $restriction)).Status
    }
    Clear-GateField
    Add-Result 'activity: no refused logActivity was persisted' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM tasks.Activities WHERE Subject = 'GATE-ACTIVITY-BLOCKED'")
    Add-Result 'activity: clearing the policy restores the activity list' 'True' `
        ((Invoke-Support -Method 'GET' -Path '/activities').Raw -match 'GATE-ACTIVITY-SUBJECT').ToString()

    # 24.11 DELEGATED LEAD INGRESS CANNOT BE ADMITTED WITHOUT AUTHORIZATION
    #
    # The nullable LeadAccess that used to mean "skip enforcement" is gone: creation now takes a
    # closed LeadCreateAdmission, and the delegated case can only be built from an allowed delegated
    # decision whose subject matches the trusted member. That is a compile-time property; what is
    # observable here is that the interactive path still enforces its own field-write policy and that
    # the delegated capability is genuinely evaluated server-side.
    Set-GateField -Resource 'leads' -Field 'email' -Access 'Hidden'
    Add-Result 'delegated: the interactive create path still enforces field-write policy' '403' `
        (Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-final-lead-interactive' `
            -Body (@{ displayName = 'Final interactive lead'; ownerId = $callerMemberId; source = 'manual'; email = 'x@example.test'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)).Status
    Clear-GateField
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'leads.create'"
    Add-Result 'delegated: leads.create is evaluated server-side, not assumed' '403' `
        (Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-final-lead-nocap' `
            -Body (@{ displayName = 'Final denied lead'; ownerId = $callerMemberId; source = 'manual'; estimatedValue = @{ amount = '10'; currency = 'USD' } } | ConvertTo-Json -Compress -Depth 6)).Status
    Add-Result 'delegated: the denied create persisted no Lead' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM leads.Leads WHERE ScopeOwnerId = '$callerMemberId' AND JSON_VALUE(Profile, '$.DisplayName') = 'Final denied lead'")
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'leads.create')"

    # 24.12 NO DENIED OPERATION MUTATED OWNER STATE, AUDIT OR OUTBOX
    Add-Result 'denial: no refused command left a COMMITTED Deals audit for the untouched fixture' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM deals.AuditRecords WHERE AggregateId = '$dealOwnId' AND Operation = 'archiveDealCommand'")
    Add-Result 'denial: no refused command emitted a Deals outbox event for the untouched fixture' '0' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) AS N FROM deals.OutboxMessages WHERE AggregateId = '$dealOwnId' AND EventType = 'DEAL_ARCHIVED'")

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
        foreach ($context in @(
                @{ Name = 'AccessControl'; Project = $platformProject;   Context = 'AccessControlDbContext' },
                @{ Name = 'Tasks';         Project = $operationsProject; Context = 'TasksDbContext' },
                @{ Name = 'Leads';         Project = $crmProject;        Context = 'LeadsDbContext' },
                @{ Name = 'Deals';         Project = $crmProject;        Context = 'DealsDbContext' },
                @{ Name = 'Products';      Project = $salesProject;      Context = 'ProductsDbContext' })) {
            $pending = & dotnet ef migrations has-pending-model-changes --project $context.Project --context $context.Context 2>&1
            $pendingText = ($pending | Out-String)
            Add-Result ("migration: no pending {0} model changes" -f $context.Name) 'True' `
                ($pendingText -match 'No changes have been made to the model').ToString()
        }
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
