<#
.SYNOPSIS
    Verifies Workspace-owned READ_ACCESS_LOG conformance for GET /workspaces.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5373,

    [int] $ReadyTimeoutSeconds = 420,

    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$baseUrl = "http://127.0.0.1:$Port"
$connection = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$email = 'workspace.read.audit@example.test'
$password = 'Workspace-Read-Audit!2026'
$displayName = 'Workspace Read Audit Fixture'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$workspaceA = 'ws_workspace_read_a'
$workspaceB = 'ws_workspace_read_b'
$membershipA = 'wsm_workspace_read_a'
$membershipB = 'wsm_workspace_read_b'
$solutionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost')).Path
$workspaceRoot = Join-Path $solutionRoot 'src/UnicoreCRM.Platform/Workspace'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-workspace-read-audit-' + [Guid]::NewGuid().ToString('N')))
$standardOutput = Join-Path $temporaryDirectory 'host.out.log'
$standardError = Join-Path $temporaryDirectory 'host.err.log'
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()
$hostProcess = $null

function Invoke-SqlScalar([string] $query, [string] $database = $DatabaseName) {
    $value = & sqlcmd -S $SqlServer -d $database -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Invoke-SqlCommand([string] $query, [string] $database = $DatabaseName) {
    & sqlcmd -S $SqlServer -d $database -b -Q "SET NOCOUNT ON; $query" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
}

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "Assertion failed: $name" }
    $checks.Add("$name=PASS")
}

function Invoke-Api([string] $method, [string] $path, [string] $body, [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$baseUrl$path")
    if ($null -ne $body) {
        $message.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    }
    if ($null -ne $headers) {
        foreach ($entry in $headers.GetEnumerator()) {
            $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
        }
    }
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    try {
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject] @{ Status = [int] $response.StatusCode; Body = $text }
    }
    finally {
        $response.Dispose()
        $message.Dispose()
    }
}

function New-ReadHeaders([string] $token, [string] $requestId, [string] $correlationId) {
    $headers = @{
        'X-Request-Id' = $requestId
        'X-Correlation-Id' = $correlationId
    }
    if (-not [string]::IsNullOrEmpty($token)) { $headers.Authorization = "Bearer $token" }
    return $headers
}

function Get-ReadAuditCount {
    return [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE Operation='listMyWorkspaces';")
}

function Measure-Read([string] $token, [string] $requestId, [string] $correlationId) {
    $before = Get-ReadAuditCount
    $response = Invoke-Api 'GET' '/workspaces' $null (New-ReadHeaders $token $requestId $correlationId)
    $delta = (Get-ReadAuditCount) - $before
    return [pscustomobject] @{ Status = $response.Status; Body = $response.Body; Delta = $delta; Probe = "$($response.Status)|$delta" }
}

function Start-Host {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:Development__ApplyMigrations = 'false'
    $env:UNICORE_DEV_SEED_ENABLED = 'false'
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $email
    $env:IdentityAuth__DevelopmentBootstrap__Password = $password
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = $displayName
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'

    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOutput -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt $ReadyTimeoutSeconds; $attempt++) {
        if ($process.HasExited) { throw "ApiHost exited during startup. See $standardOutput" }
        try {
            $probe = Invoke-Api 'GET' '/workspaces' $null $null
            if ($probe.Status -eq 401) { return $process }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    throw "ApiHost did not become ready. See $standardOutput"
}

function Sign-In {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $suffix = [Guid]::NewGuid().ToString('N')
        $response = Invoke-Api 'POST' '/auth/sessions' (@{
            email = $email
            password = $password
            deviceLabel = 'Workspace read audit verifier'
        } | ConvertTo-Json -Compress) @{
            'X-Request-Id' = "req-workspace-signin-$suffix"
            'X-Correlation-Id' = "corr-workspace-signin-$suffix"
            'Idempotency-Key' = "idem-workspace-signin-$suffix"
        }
        if ($response.Status -eq 200) { return $response.Body | ConvertFrom-Json }
        Start-Sleep -Milliseconds 250
    }
    throw 'Development Identity account did not become available for sign-in.'
}

function Add-WorkspaceFixtures([string] $accountId, [string] $memberId) {
    Invoke-SqlCommand @"
DELETE FROM workspace.Memberships WHERE MembershipId IN ('$membershipA','$membershipB');
DELETE FROM workspace.Workspaces WHERE WorkspaceId IN ('$workspaceA','$workspaceB');
INSERT INTO workspace.Workspaces(WorkspaceId,[Key],[Name],LogoText,CreatedAt) VALUES
('$workspaceA','workspace-read-alpha','Alpha Workspace','AW',SYSUTCDATETIME()),
('$workspaceB','workspace-read-beta','Beta Workspace','BW',SYSUTCDATETIME());
INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,[Status],CreatedAt) VALUES
('$membershipA','$workspaceA','$accountId','$memberId','Active',SYSUTCDATETIME()),
('$membershipB','$workspaceB','$accountId','$memberId','Suspended',SYSUTCDATETIME());
"@
}

try {
    Invoke-SqlCommand "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" 'master'
    $env:ConnectionStrings__UnicoreCRM = $connection
    & dotnet ef database update --project (Join-Path $solutionRoot 'src/UnicoreCRM.Platform') --context IdentityAuthDbContext --no-build | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'IdentityAuth migration application failed.' }
    & dotnet ef database update --project (Join-Path $solutionRoot 'src/UnicoreCRM.Platform') --context WorkspaceDbContext --no-build | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Workspace migration application failed.' }

    $hostProcess = Start-Host
    $signIn = Sign-In
    $token = [string] $signIn.accessToken
    $sessionId = [string] $signIn.session.sessionId
    $accountId = [string] $signIn.session.principal.accountId
    $memberId = [string] $signIn.session.principal.memberId
    Assert-True (-not [string]::IsNullOrEmpty($token) -and -not [string]::IsNullOrEmpty($sessionId) -and -not [string]::IsNullOrEmpty($accountId) -and -not [string]::IsNullOrEmpty($memberId)) 'Trusted authenticated account principal established'

    $columns = Invoke-SqlScalar "SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='workspace' AND TABLE_NAME='AccessRecords';"
    Assert-True ($columns -eq 'AccessRecordId,AccountId,CorrelationId,OccurredAt,Operation,Outcome,RequestId,WorkspaceId') 'Workspace access evidence schema exact'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('workspace.AccessRecords');") -eq '0') 'Workspace access evidence has zero foreign keys'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('workspace.AccessRecords') AND name='IX_AccessRecords_AccountId_OccurredAt';") -eq '1') 'Workspace access evidence has account-leading index'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='workspace' AND TABLE_NAME='AccessRecords' AND COLUMN_NAME IN ('MemberId','RecordId','ResourceVersion','ResultCount','Cardinality');") -eq '0') 'Evidence cannot store member, record, version, or result cardinality'

    Add-WorkspaceFixtures $accountId $memberId
    $requestId = 'req-workspace-read-audit-0001'
    $correlationId = 'corr-workspace-read-audit-0001'
    $multiple = Measure-Read $token $requestId $correlationId
    Assert-True ($multiple.Probe -eq '200|1') 'Multiple-Workspace list writes exactly one owner read row'
    $multipleDocument = $multiple.Body | ConvertFrom-Json
    Assert-True ($multipleDocument.items.Count -eq 2) 'Multiple-Workspace response cardinality unchanged'
    Assert-True ((($multipleDocument.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'generatedAt,items') 'List response top-level contract unchanged'
    Assert-True ((($multipleDocument.items[0].PSObject.Properties.Name | Sort-Object) -join ',') -eq 'logoText,membershipId,name,status,workspaceId,workspaceKey') 'Membership item contract unchanged'
    Assert-True ((($multipleDocument.items | ForEach-Object { $_.name }) -join ',') -eq 'Alpha Workspace,Beta Workspace') 'Workspace ordering unchanged'
    Assert-True ((($multipleDocument.items | ForEach-Object { $_.status }) -join ',') -eq 'active,suspended') 'Active and suspended membership visibility unchanged'
    Assert-True ($multiple.Body -match '"generatedAt":"[^"]+Z"') 'Response generation time remains UTC'

    $evidence = Invoke-SqlScalar "SELECT CONCAT(AccessRecordId,'|',Operation,'|',AccountId,'|',RequestId,'|',CorrelationId,'|',Outcome,'|',CASE WHEN WorkspaceId IS NULL THEN '<NULL>' ELSE WorkspaceId END,'|',DATEPART(TZOFFSET,OccurredAt)) FROM workspace.AccessRecords WHERE RequestId='$requestId';"
    $parts = $evidence.Split('|')
    Assert-True ($parts.Count -eq 8 -and $parts[0].StartsWith('wsa_')) 'Owner-generated audit identity persisted'
    Assert-True ($evidence -eq "$($parts[0])|listMyWorkspaces|$accountId|$requestId|$correlationId|READ|<NULL>|0") 'Trusted account provenance, discriminator, null Workspace, and UTC time exact'

    Invoke-SqlCommand "DELETE FROM workspace.Memberships WHERE MembershipId='$membershipB';"
    $one = Measure-Read $token 'req-workspace-read-audit-0002' 'corr-workspace-read-audit-0002'
    Assert-True ($one.Probe -eq '200|1') 'One-Workspace list writes exactly one owner read row'
    Assert-True ((($one.Body | ConvertFrom-Json).items.Count) -eq 1) 'One-Workspace response remains exact'

    Invoke-SqlCommand "DELETE FROM workspace.Memberships WHERE MembershipId='$membershipA';"
    $empty = Measure-Read $token 'req-workspace-read-audit-0003' 'corr-workspace-read-audit-0003'
    Assert-True ($empty.Probe -eq '200|1') 'Empty Workspace list writes exactly one owner read row'
    Assert-True ((($empty.Body | ConvertFrom-Json).items.Count) -eq 0) 'Empty Workspace response remains exact'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE Operation='listMyWorkspaces' AND WorkspaceId IS NOT NULL;") -eq '0') 'No returned Workspace is fabricated as audit WorkspaceId'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE Operation='listMyWorkspaces' AND (Outcome<>'READ' OR RequestId IS NULL OR RequestId='');") -eq '0') 'Every successful list row has READ and request provenance'

    $securityEventsBeforeFailures = [int] (Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.SecurityEvents;')
    Assert-True ((Measure-Read '' 'req-workspace-unauth-0001' 'corr-workspace-unauth-0001').Probe -eq '401|0') 'Unauthenticated request writes no Workspace owner evidence'
    Assert-True ((Measure-Read 'invalid.jwt.token' 'req-workspace-invalid-0001' 'corr-workspace-invalid-0001').Probe -eq '401|0') 'Invalid authentication writes no Workspace owner evidence'
    Assert-True ((Measure-Read $token 'bad' 'corr-workspace-malformed-0001').Probe -eq '422|0') 'Malformed authorized metadata writes no Workspace owner evidence'

    Invoke-SqlCommand "UPDATE iam.Sessions SET IdleExpiresAt=DATEADD(minute,-2,SYSUTCDATETIME()), AbsoluteExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE SessionId='$sessionId';"
    Assert-True ((Measure-Read $token 'req-workspace-expired-0001' 'corr-workspace-expired-0001').Probe -eq '401|0') 'Expired authoritative session writes no Workspace owner evidence'
    Invoke-SqlCommand "UPDATE iam.Sessions SET IdleExpiresAt=DATEADD(day,1,SYSUTCDATETIME()), AbsoluteExpiresAt=DATEADD(day,2,SYSUTCDATETIME()) WHERE SessionId='$sessionId';"

    Add-WorkspaceFixtures $accountId $memberId
    Invoke-SqlCommand "EXEC sp_rename 'workspace.Memberships', 'MembershipsReadFailureProbe';"
    Assert-True ((Measure-Read $token 'req-workspace-read-fail-0001' 'corr-workspace-read-fail-0001').Probe -eq '500|0') 'Failed authoritative membership query writes no owner read evidence'
    Invoke-SqlCommand "EXEC sp_rename 'workspace.MembershipsReadFailureProbe', 'Memberships';"

    Invoke-SqlCommand @"
EXEC(N'CREATE TRIGGER workspace.TR_AccessRecords_WorkspaceReadFailureProbe
ON workspace.AccessRecords
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE Operation=''listMyWorkspaces'' AND Outcome=''READ'')
        THROW 51000, ''Workspace read-audit persistence probe.'', 1;
END;');
"@
    Assert-True ((Measure-Read $token 'req-workspace-audit-fail-0001' 'corr-workspace-audit-fail-0001').Probe -eq '500|0') 'Audit persistence failure prevents successful disclosure'
    Invoke-SqlCommand 'DROP TRIGGER workspace.TR_AccessRecords_WorkspaceReadFailureProbe;'
    Assert-True ((Measure-Read $token 'req-workspace-recovery-0001' 'corr-workspace-recovery-0001').Probe -eq '200|1') 'Workspace list recovers with one audit after persistence probe removal'
    Assert-True ((Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.SecurityEvents;') -eq [string] $securityEventsBeforeFailures) 'Workspace read evidence remains separate from Identity security events'

    $auditDump = Invoke-SqlScalar "SELECT STRING_AGG(CONCAT(AccessRecordId,'|',Operation,'|',AccountId,'|',ISNULL(RequestId,''),'|',CorrelationId,'|',ISNULL(Outcome,''),'|',ISNULL(WorkspaceId,'')),' ') FROM workspace.AccessRecords WHERE Operation='listMyWorkspaces';"
    foreach ($forbidden in @('Alpha Workspace','Beta Workspace','workspace-read-alpha','workspace-read-beta','AW','BW',$workspaceA,$workspaceB,$membershipA,$membershipB,$memberId,'active','suspended')) {
        Assert-True ($auditDump -notmatch [regex]::Escape($forbidden)) "Read evidence excludes business value $forbidden"
    }

    $endpointSource = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Contracts/WorkspaceEndpoints.cs')
    $handlerSource = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Application/ListMyWorkspaces/Handler.cs')
    $taskSource = $endpointSource + "`n" + $handlerSource + "`n" + (Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Application/ListMyWorkspaces/Query.cs')) + "`n" + (Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Domain/WorkspaceAccessRecord.cs'))
    Assert-True ([regex]::Matches($endpointSource, 'MapGet\("/workspaces"').Count -eq 1) 'Exactly one listMyWorkspaces route remains'
    Assert-True ([regex]::Matches($endpointSource, 'MapGet\(').Count -eq 2) 'Workspace GET route surface unchanged'
    Assert-True ([regex]::Matches($endpointSource, 'Map(Post|Put|Patch|Delete)\(').Count -eq 0) 'No Workspace mutation route introduced'
    Assert-True ($handlerSource -notmatch 'ICurrentWorkspace|X-Workspace-Id|MemberId.*AccessRecord|foreach|ForEach') 'List audit introduces no current-Workspace, member, or per-row append behavior'
    Assert-True ($taskSource -notmatch '\b(IdentityAuth|AccessControl|Products|Quotes|Orders|Invoices|Payments)DbContext\b') 'Workspace read correction adds no foreign DbContext'
    Assert-True ($taskSource -notmatch 'Outbox|Idempotency|Workflow|GenericAudit|IReadAuditService|SharedAuditDbContext') 'Workspace read correction adds no workflow or generic audit infrastructure'
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id
        $hostProcess.WaitForExit(10000) | Out-Null
    }
    $client.Dispose()
    if (-not $KeepDatabase) {
        try {
            Invoke-SqlCommand "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;" 'master'
        }
        catch { }
    }
}

$env:ConnectionStrings__UnicoreCRM = $connection
$pending = & dotnet ef migrations has-pending-model-changes --project (Join-Path $solutionRoot 'src/UnicoreCRM.Platform') --context WorkspaceDbContext --no-build 2>&1
if ($LASTEXITCODE -ne 0) { throw "Workspace model verification failed: $pending" }
$checks.Add('Workspace model pending changes=NONE')

[pscustomobject]@{
    Status = 'PASS'
    Operation = 'listMyWorkspaces'
    Checks = $checks
} | ConvertTo-Json -Depth 5
