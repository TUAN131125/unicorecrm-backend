<#
.SYNOPSIS
    Reproducible Support Core runtime verification.

.DESCRIPTION
    Provisions an isolated database, starts UnicoreCRM.ApiHost against it, and exercises the
    eight admitted Support operations plus the Support-owned idempotency, concurrency,
    workspace-isolation, audit and outbox invariants. It then re-exercises the previously
    verified modules as a regression pass and checks the Support EF model for pending changes.

    The idempotency section deliberately deactivates the Workspace member that owns a committed
    SupportCase and replays the original command, proving that a later member status change
    cannot invalidate a committed replay or create a second SupportCase.

    Windows PowerShell 5.1 compatible: no pipeline chain operators, ternary, null-coalescing or
    -AsHashtable.

.EXAMPLE
    ./verify-support-core.ps1 -DatabaseName UnicoreCRM_SupportCore_Verify
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    # Omit both to use a trusted connection.
    [string] $SqlUserId,
    [string] $SqlPassword,

    [int] $Port = 5312,

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

# A fresh client per call. The harness makes well under a hundred requests, and the .NET
# Framework HttpClient that Windows PowerShell 5.1 ships with keeps per-endpoint ServicePoint
# state: a client created before the host is listening can keep failing against that endpoint
# long after the host is up. Proxy use is disabled so a machine-level proxy cannot intercept
# loopback traffic.
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
    return ('req-support-verify-{0:d6}' -f $script:RequestCounter)
}

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Path,
        [string] $Body,
        [string] $Token,
        [string] $WorkspaceId,
        [string] $IdempotencyKey,
        [string] $IfMatchVersion
    )
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId))
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-support-core-verify-0001')
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
    # An unbound [string] parameter arrives as an empty string, not $null. Attaching empty
    # content to a GET makes the .NET Framework stack reject the request outright, so the
    # emptiness check has to be explicit.
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

# ---------------------------------------------------------------- provisioning

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$hostProject = Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
$operationsProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Operations/UnicoreCRM.Operations.csproj'
$demoEmail = 'admin@unicorecrm.local'
$demoPassword = 'Support-Core-Verify!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-support-verify-$([Guid]::NewGuid().ToString('N')).log")

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

    # A first run builds the host and applies every owner migration to an empty database. On
    # LocalDB that comfortably exceeds two minutes, so the budget is generous.
    $ready = $false
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
        -IdempotencyKey 'idem-support-verify-signin-01' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $token
    $memberId = $session.Body.principal.memberId

    $workspaceId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key] = 'unicore-demo'"
    $foreignWorkspaceId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key] = 'unicore-demo-isolated'"
    if ([string]::IsNullOrWhiteSpace($workspaceId)) { throw 'Development workspace was not provisioned.' }

    # A second active Workspace member, used only as the SupportCase owner. Keeping the owner
    # distinct from the caller lets the idempotency probe suspend the owner without also
    # breaking the caller's own trusted-workspace resolution.
    $ownerMemberId = 'mem-support-verify-owner'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Memberships WHERE MemberId = '$ownerMemberId')
INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, Status, CreatedAt)
VALUES ('wsm-support-verify-owner', '$workspaceId', 'acc-support-verify-owner', '$ownerMemberId', 'Active', SYSUTCDATETIME());
"@

    # ------------------------------------------------------------ security

    $anonymous = Invoke-Api -Method 'GET' -Path '/support/cases' -WorkspaceId $workspaceId
    Add-Result 'security: unauthenticated listSupportCases' '401' $anonymous.Status

    $empty = Invoke-Api -Method 'GET' -Path '/support/cases' -Token $token -WorkspaceId $workspaceId
    Add-Result 'read: listSupportCases empty' '200' $empty.Status
    Add-Result 'read: listSupportCases empty count' '0' $empty.Body.items.Count

    # ------------------------------------------------------------ create

    $createBody = @{
        title           = 'Printer will not start'
        description     = 'Device reports error 42 on boot.'
        priority        = 'high'
        category        = 'usage_issue'
        source          = 'manual'
        channel         = 'email'
        relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_acme_001' }
        contactId       = 'contact_001'
        relatedOrderId  = 'order_001'
        ownerId         = $ownerMemberId
        tags            = @('hardware', 'urgent')
        resolutionDueAt = '2026-09-01T10:00:00.0000000Z'
    } | ConvertTo-Json -Compress -Depth 6

    $created = Invoke-Api -Method 'POST' -Path '/support/cases' -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-create-01' -Body $createBody
    Add-Result 'command: createSupportCase' '201' $created.Status
    Add-Result 'command: createSupportCase outcome' 'COMMITTED' $created.Body.outcome
    Add-Result 'domain: initial lifecycle status' 'new' $created.Body.result.supportCase.status
    Add-Result 'domain: server-assigned case number' 'CASE-2026-0001' $created.Body.result.supportCase.caseNumber
    Add-Result 'concurrency: create starts at version 0' '0' $created.Body.version
    $caseId = $created.Body.aggregateId
    $version = $created.Body.version

    Add-Result 'sla: projection is fail-closed not_applicable' 'not_applicable' $created.Body.result.supportCase.slaStatus
    Add-Result 'contract: customerId omitted (AUTHORITY_GAP)' 'True' `
        ($null -eq $created.Body.result.supportCase.customerId).ToString()
    Add-Result 'contract: customerName omitted (AUTHORITY_GAP)' 'True' `
        ($null -eq $created.Body.result.supportCase.customerName).ToString()
    Add-Result 'contract: comments projection omitted' 'True' `
        ($null -eq $created.Body.result.supportCase.comments).ToString()
    Add-Result 'contract: activities projection omitted' 'True' `
        ($null -eq $created.Body.result.supportCase.activities).ToString()

    # ------------------------------- idempotency across a member status change

    # Suspend the Workspace member that this committed command referenced as owner. A replay
    # must still succeed from stored evidence; only a genuinely new command may be rejected.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
UPDATE workspace.Memberships SET Status = 'Suspended' WHERE MemberId = '$ownerMemberId' AND WorkspaceId = '$workspaceId';
"@
    $deactivated = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) AS N FROM workspace.Memberships WHERE MemberId = '$ownerMemberId' AND WorkspaceId = '$workspaceId' AND Status = 'Suspended'"
    Add-Result 'idempotency: owner member suspended for replay probe' '1' $deactivated

    # Proof that the suspension actually bites a NEW command with the same owner.
    $newWithSuspendedOwner = Invoke-Api -Method 'POST' -Path '/support/cases' -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-create-suspended' -Body $createBody
    Add-Result 'idempotency: new command with suspended owner is rejected' '422' $newWithSuspendedOwner.Status

    $replay = Invoke-Api -Method 'POST' -Path '/support/cases' -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-create-01' -Body $createBody
    Add-Result 'idempotency: replay after member deactivation succeeds' '201' $replay.Status
    Add-Result 'idempotency: replay reports REPLAYED' 'REPLAYED' $replay.Body.outcome
    Add-Result 'idempotency: replay returns the same aggregate' $caseId $replay.Body.aggregateId
    Add-Result 'idempotency: replay does not advance version' $version $replay.Body.version

    $caseCount = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.SupportCases'
    Add-Result 'idempotency: replay created no duplicate SupportCase' '1' $caseCount
    $createEvents = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) AS N FROM support.OutboxMessages WHERE EventType = 'SUPPORT_CASE_CREATED'"
    Add-Result 'idempotency: replay emitted no duplicate outbox message' '1' $createEvents

    # Restore the membership so the remaining new-command checks exercise the normal path.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
UPDATE workspace.Memberships SET Status = 'Active' WHERE MemberId = '$ownerMemberId' AND WorkspaceId = '$workspaceId';
"@

    $changedIntent = Invoke-Api -Method 'POST' -Path '/support/cases' -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-create-01' -Body ($createBody -replace 'Printer', 'Scanner')
    Add-Result 'idempotency: same key changed intent rejected' '409' $changedIntent.Status
    Add-Result 'idempotency: reuse error code' 'IDEMPOTENCY_KEY_REUSED' $changedIntent.Body.code

    # ------------------------------------------------------------ reads

    $detail = Invoke-Api -Method 'GET' -Path "/support/cases/$caseId" -Token $token -WorkspaceId $workspaceId
    Add-Result 'read: getSupportCase' '200' $detail.Status
    Add-Result 'read: getSupportCase identity' $caseId $detail.Body.id
    $filtered = Invoke-Api -Method 'GET' -Path '/support/cases?status=new&priority=high' -Token $token -WorkspaceId $workspaceId
    Add-Result 'read: listSupportCases filtered count' '1' $filtered.Body.items.Count
    $breached = Invoke-Api -Method 'GET' -Path '/support/cases?slaStatus=breached' -Token $token -WorkspaceId $workspaceId
    Add-Result 'sla: unresolvable slaStatus filter matches nothing' '0' $breached.Body.items.Count
    $badFilter = Invoke-Api -Method 'GET' -Path '/support/cases?status=bogus' -Token $token -WorkspaceId $workspaceId
    Add-Result 'read: undeclared status filter rejected' '422' $badFilter.Status

    # ------------------------------------------------------------ concurrency

    $replaceBody = @{
        title           = 'Printer will not start (triaged)'
        description     = 'Device reports error 42 on boot.'
        priority        = 'critical'
        category        = 'technical_support'
        source          = 'manual'
        channel         = 'phone'
        relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = 'org_acme_001' }
        tags            = @('hardware')
    } | ConvertTo-Json -Compress -Depth 6

    $stale = Invoke-Api -Method 'PUT' -Path "/support/cases/$caseId" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-replace-stale' -IfMatchVersion '99' -Body $replaceBody
    Add-Result 'concurrency: stale If-Match rejected' '412' $stale.Status
    Add-Result 'concurrency: stale If-Match error code' 'VERSION_CONFLICT' $stale.Body.code
    $afterStale = Invoke-Api -Method 'GET' -Path "/support/cases/$caseId" -Token $token -WorkspaceId $workspaceId
    Add-Result 'concurrency: stale If-Match left version unchanged' $version $afterStale.Body.resourceVersion
    Add-Result 'concurrency: stale If-Match left title unchanged' 'Printer will not start' $afterStale.Body.title

    $missingIfMatch = Invoke-Api -Method 'PUT' -Path "/support/cases/$caseId" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-replace-noetag' -Body $replaceBody
    Add-Result 'concurrency: missing If-Match rejected' '422' $missingIfMatch.Status

    $replaced = Invoke-Api -Method 'PUT' -Path "/support/cases/$caseId" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-replace-01' -IfMatchVersion $version -Body $replaceBody
    Add-Result 'command: replaceSupportCaseProfile' '200' $replaced.Status
    Add-Result 'concurrency: replace advanced version' ([int]$version + 1) $replaced.Body.version
    Add-Result 'domain: replace accepts legacy category' 'technical_support' $replaced.Body.result.supportCase.category
    Add-Result 'domain: replace clears omitted optional field' 'True' `
        ($null -eq $replaced.Body.result.supportCase.contactId).ToString()
    $version = $replaced.Body.version

    $legacyCreate = Invoke-Api -Method 'POST' -Path '/support/cases' -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-create-legacy' -Body (@{
            title = 'legacy'; description = 'legacy'; priority = 'low'; category = 'technical_support'
            source = 'manual'; relationshipRef = @{ type = 'CONTACT'; id = 'contact_9' }
        } | ConvertTo-Json -Compress -Depth 6)
    Add-Result 'domain: create rejects legacy-only category' '422' $legacyCreate.Status

    # ------------------------------------------------------------ assignment

    $badOwner = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/assign" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-assign-bad' -IfMatchVersion $version `
        -Body (@{ ownerId = 'member_not_in_workspace' } | ConvertTo-Json -Compress)
    Add-Result 'security: assign rejects non-member owner' '422' $badOwner.Status

    $assigned = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/assign" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-assign-01' -IfMatchVersion $version `
        -Body (@{ ownerId = $memberId } | ConvertTo-Json -Compress)
    Add-Result 'command: assignSupportCase' '200' $assigned.Status
    Add-Result 'domain: assignment does not change lifecycle' 'new' $assigned.Body.result.supportCase.status
    $version = $assigned.Body.version

    # ------------------------------------------------------------ lifecycle

    $illegal = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/transition" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-tr-bad' -IfMatchVersion $version `
        -Body (@{ nextStatus = 'resolved' } | ConvertTo-Json -Compress)
    Add-Result 'domain: new -> resolved rejected' '409' $illegal.Status
    Add-Result 'domain: invalid transition error code' 'SUPPORT_CASE_INVALID_TRANSITION' $illegal.Body.code
    $afterIllegal = Invoke-Api -Method 'GET' -Path "/support/cases/$caseId" -Token $token -WorkspaceId $workspaceId
    Add-Result 'domain: rejected transition mutated nothing' 'new' $afterIllegal.Body.status

    $path = @(
        @{ Next = 'in_progress';      Key = 'idem-support-verify-tr-1' },
        @{ Next = 'waiting_customer'; Key = 'idem-support-verify-tr-2' },
        @{ Next = 'resolved';         Key = 'idem-support-verify-tr-3' },
        @{ Next = 'closed';           Key = 'idem-support-verify-tr-4' },
        @{ Next = 'reopened';         Key = 'idem-support-verify-tr-5' }
    )
    foreach ($step in $path) {
        $payload = @{ nextStatus = $step.Next }
        if ($step.Next -eq 'resolved') { $payload['resolutionSummary'] = 'Firmware reflashed.' }
        $transitioned = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/transition" -Token $token -WorkspaceId $workspaceId `
            -IdempotencyKey $step.Key -IfMatchVersion $version -Body ($payload | ConvertTo-Json -Compress)
        Add-Result ("domain: transition to {0}" -f $step.Next) '200' $transitioned.Status
        $version = $transitioned.Body.version
        $current = $transitioned.Body.result.supportCase
        if ($step.Next -eq 'resolved') {
            Add-Result 'domain: resolve stamps resolvedAt' 'True' ($null -ne $current.resolvedAt).ToString()
        }
        if ($step.Next -eq 'closed') {
            Add-Result 'domain: close stamps closedAt' 'True' ($null -ne $current.closedAt).ToString()
        }
        if ($step.Next -eq 'reopened') {
            Add-Result 'domain: reopen stamps reopenedAt' 'True' ($null -ne $current.reopenedAt).ToString()
            Add-Result 'domain: reopen clears resolvedAt' 'True' ($null -eq $current.resolvedAt).ToString()
            Add-Result 'domain: reopen clears closedAt' 'True' ($null -eq $current.closedAt).ToString()
        }
    }

    $sameState = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/transition" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-tr-same' -IfMatchVersion $version `
        -Body (@{ nextStatus = 'reopened' } | ConvertTo-Json -Compress)
    Add-Result 'domain: same-state replay admitted' '200' $sameState.Status
    $version = $sameState.Body.version

    $reopenedToClosed = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/transition" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-tr-bad2' -IfMatchVersion $version `
        -Body (@{ nextStatus = 'closed' } | ConvertTo-Json -Compress)
    Add-Result 'domain: reopened -> closed rejected' '409' $reopenedToClosed.Status

    # ------------------------------------------------- replies / internal notes

    $reply = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/replies" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-reply-01' -IfMatchVersion $version `
        -Body (@{ body = 'We have shipped a replacement unit.' } | ConvertTo-Json -Compress)
    Add-Result 'command: addSupportCaseReply' '200' $reply.Status
    Add-Result 'concurrency: reply advances version' ([int]$version + 1) $reply.Body.version
    $version = $reply.Body.version

    $note = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/internal-notes" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-note-01' -IfMatchVersion $version `
        -Body (@{ body = 'Escalated to hardware team; do not disclose.' } | ConvertTo-Json -Compress)
    Add-Result 'command: addSupportCaseInternalNote' '200' $note.Status
    $version = $note.Body.version

    $emptyBody = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/replies" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-reply-empty' -IfMatchVersion $version `
        -Body (@{ body = '' } | ConvertTo-Json -Compress)
    Add-Result 'command: empty reply body rejected' '422' $emptyBody.Status

    $noKey = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/replies" -Token $token -WorkspaceId $workspaceId `
        -IfMatchVersion $version -Body (@{ body = 'x' } | ConvertTo-Json -Compress)
    Add-Result 'idempotency: missing Idempotency-Key rejected' '422' $noKey.Status

    $unmapped = Invoke-Api -Method 'POST' -Path "/support/cases/$caseId/replies" -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-reply-extra' -IfMatchVersion $version `
        -Body '{"body":"x","authorName":"spoofed"}'
    Add-Result 'contract: unmapped request member rejected' '422' $unmapped.Status

    $internalRow = Invoke-Sql -Database $DatabaseName `
        -Query 'SELECT Type, IsInternal FROM support.SupportCaseComments ORDER BY CreatedAt'
    Add-Result 'persistence: two comments persisted' '2' $internalRow.Count
    Add-Result 'persistence: reply is not internal' 'False' $internalRow[0].IsInternal.ToString()
    Add-Result 'persistence: note is internal' 'True' $internalRow[1].IsInternal.ToString()

    # ------------------------------------------------------ workspace isolation

    if (-not [string]::IsNullOrWhiteSpace($foreignWorkspaceId)) {
        $foreign = Invoke-Api -Method 'GET' -Path "/support/cases/$caseId" -Token $token -WorkspaceId $foreignWorkspaceId
        Add-Result 'security: cross-workspace read fails closed' '403' $foreign.Status
    }
    $unknown = Invoke-Api -Method 'GET' -Path '/support/cases/case_deadbeefdeadbeefdeadbeefdeadbeef' -Token $token -WorkspaceId $workspaceId
    Add-Result 'security: unknown case identifier' '404' $unknown.Status

    $isolation = Get-Scalar -Database $DatabaseName `
        -Query 'SELECT COUNT(DISTINCT WorkspaceId) AS N FROM support.SupportCases'
    Add-Result 'security: all Support state is single-workspace scoped' '1' $isolation

    # ------------------------------------------------------------ audit/outbox

    $commandAudits = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) AS N FROM support.AuditRecords WHERE Outcome = 'COMMITTED'"
    $readAudits = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) AS N FROM support.AuditRecords WHERE Outcome = 'READ'"
    Add-Result 'audit: command audits recorded' 'True' ([int]$commandAudits -gt 0).ToString()
    Add-Result 'audit: read access log recorded' 'True' ([int]$readAudits -gt 0).ToString()

    $undeclared = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM support.OutboxMessages
WHERE EventType NOT IN (
    'SUPPORT_CASE_CREATED','SUPPORT_CASE_PROFILE_REPLACED','SUPPORT_CASE_ASSIGNED',
    'SUPPORT_CASE_STATUS_CHANGED','SUPPORT_CASE_REPLY_ADDED','SUPPORT_CASE_INTERNAL_NOTE_ADDED')
"@
    Add-Result 'outbox: no undeclared Support event type emitted' '0' $undeclared

    $outboxTotal = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) AS N FROM support.OutboxMessages'
    $committedTotal = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) AS N FROM support.AuditRecords WHERE Outcome = 'COMMITTED'"
    Add-Result 'outbox: exactly one event per committed mutation' $committedTotal $outboxTotal

    $foreignTables = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) AS N FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = 'support' AND TABLE_NAME IN ('Tasks','Activities')
"@
    Add-Result 'persistence: Support shares no table with Tasks' '0' $foreignTables

    # ------------------------------------------------------------ regressions

    $regressionRoutes = @(
        @{ Name = 'auth/session'; Path = '/auth/session'; Workspace = $false },
        @{ Name = 'workspaces';   Path = '/workspaces';   Workspace = $false },
        @{ Name = 'tasks';        Path = '/tasks';        Workspace = $true },
        @{ Name = 'activities';   Path = '/activities';   Workspace = $true },
        @{ Name = 'leads';        Path = '/leads';        Workspace = $true },
        @{ Name = 'deals';        Path = '/deals';        Workspace = $true },
        @{ Name = 'products';     Path = '/products';     Workspace = $true }
    )
    foreach ($route in $regressionRoutes) {
        if ($route.Workspace) {
            $probe = Invoke-Api -Method 'GET' -Path $route.Path -Token $token -WorkspaceId $workspaceId
        }
        else {
            $probe = Invoke-Api -Method 'GET' -Path $route.Path -Token $token
        }
        Add-Result ("regression: {0} still reads" -f $route.Name) '200' $probe.Status
    }

    $regressionTask = Invoke-Api -Method 'POST' -Path '/tasks' -Token $token -WorkspaceId $workspaceId `
        -IdempotencyKey 'idem-support-verify-regress-task' `
        -Body (@{ title = 'Regression probe'; assigneeId = $memberId; dueAt = '2026-09-30T09:00:00.0000000Z' } | ConvertTo-Json -Compress)
    Add-Result 'regression: createTask still commits' '201' $regressionTask.Status
    Add-Result 'regression: createTask outcome' 'COMMITTED' $regressionTask.Body.outcome

    # ------------------------------------------------------------ EF model

    Push-Location $repositoryRoot
    try {
        $pending = & dotnet ef migrations has-pending-model-changes --project $operationsProject --context SupportDbContext 2>&1
        $pendingText = ($pending | Out-String)
        Add-Result 'migration: no pending Support model changes' 'True' `
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
Write-Host '===== SUPPORT CORE VERIFICATION ====='
foreach ($line in $script:Results) { Write-Host $line }
Write-Host ''
Write-Host ("PASS={0} FAIL={1}" -f $script:Passed, $script:Failed)
Write-Host ("Host log: {0}" -f $logPath)

if ($script:Failed -gt 0) { exit 1 }
exit 0
