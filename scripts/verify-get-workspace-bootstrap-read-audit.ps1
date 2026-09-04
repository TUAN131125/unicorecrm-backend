<#
.SYNOPSIS
    Verifies the conservative READ_ACCESS_LOG floor for getWorkspaceBootstrap without defining
    WORKSPACE_CONTEXT_ACCESS_LOG semantics.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5374,

    [int] $ReadyTimeoutSeconds = 420,

    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$baseUrl = "http://127.0.0.1:$Port"
$connection = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$email = 'workspace.bootstrap.read.audit@example.test'
$password = 'Workspace-Bootstrap-Read-Audit!2026'
$displayName = 'Workspace Bootstrap Read Audit Fixture'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$workspaceA = 'ws_bootstrap_read_a'
$workspaceForeign = 'ws_bootstrap_read_foreign'
$workspaceSuspended = 'ws_bootstrap_read_suspended'
$workspaceDenied = 'ws_bootstrap_read_denied'
$membershipA = 'wsm_bootstrap_read_a'
$membershipSuspended = 'wsm_bootstrap_read_suspended'
$membershipDenied = 'wsm_bootstrap_read_denied'
$roleA = 'role_bootstrap_read_a'
$assignmentA = 'assignment_bootstrap_read_a'
$solutionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost')).Path
$workspaceRoot = Join-Path $solutionRoot 'src/UnicoreCRM.Platform/Workspace'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-workspace-bootstrap-read-audit-' + [Guid]::NewGuid().ToString('N')))
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
    if (-not [string]::IsNullOrEmpty($body)) {
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

function New-ReadHeaders(
    [string] $token,
    [string] $requestId,
    [string] $correlationId,
    [string] $untrustedWorkspaceHeader = $null) {
    $headers = @{
        'X-Request-Id' = $requestId
        'X-Correlation-Id' = $correlationId
    }
    if (-not [string]::IsNullOrEmpty($token)) { $headers.Authorization = "Bearer $token" }
    if (-not [string]::IsNullOrEmpty($untrustedWorkspaceHeader)) { $headers['X-Workspace-Id'] = $untrustedWorkspaceHeader }
    return $headers
}

function Get-BootstrapAuditCount {
    return [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE Operation='getWorkspaceBootstrap';")
}

function Measure-BootstrapRead(
    [string] $path,
    [string] $token,
    [string] $requestId,
    [string] $correlationId,
    [string] $untrustedWorkspaceHeader = $null) {
    $before = Get-BootstrapAuditCount
    $response = Invoke-Api 'GET' $path $null (New-ReadHeaders $token $requestId $correlationId $untrustedWorkspaceHeader)
    $delta = (Get-BootstrapAuditCount) - $before
    return [pscustomobject] @{
        Status = $response.Status
        Body = $response.Body
        Delta = $delta
        Probe = "$($response.Status)|$delta"
    }
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
    $lastProbe = 'no response'
    for ($attempt = 0; $attempt -lt $ReadyTimeoutSeconds; $attempt++) {
        if ($process.HasExited) { throw "ApiHost exited during startup. See $standardOutput and $standardError" }
        try {
            $probe = Invoke-Api 'GET' '/workspaces' $null $null
            $lastProbe = "HTTP $($probe.Status)"
            if ($probe.Status -eq 401) { return $process }
        }
        catch { $lastProbe = $_.Exception.Message }
        Start-Sleep -Seconds 1
    }
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(10000) | Out-Null
    }
    throw "ApiHost did not become ready. Last probe: $lastProbe. See $standardOutput and $standardError"
}

function Sign-In {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $suffix = [Guid]::NewGuid().ToString('N')
        $response = Invoke-Api 'POST' '/auth/sessions' (@{
            email = $email
            password = $password
            deviceLabel = 'Workspace bootstrap read audit verifier'
        } | ConvertTo-Json -Compress) @{
            'X-Request-Id' = "req-bootstrap-signin-$suffix"
            'X-Correlation-Id' = "corr-bootstrap-signin-$suffix"
            'Idempotency-Key' = "idem-bootstrap-signin-$suffix"
        }
        if ($response.Status -eq 200) { return $response.Body | ConvertFrom-Json }
        Start-Sleep -Milliseconds 250
    }
    throw 'Development Identity account did not become available for sign-in.'
}

function Add-WorkspaceFixtures([string] $accountId, [string] $memberId) {
    Invoke-SqlCommand @"
DELETE FROM access.MembershipRoleAssignments WHERE WorkspaceId IN ('$workspaceA','$workspaceForeign','$workspaceSuspended','$workspaceDenied');
DELETE FROM access.RoleCapabilities WHERE RoleId='$roleA';
DELETE FROM access.Roles WHERE WorkspaceId IN ('$workspaceA','$workspaceForeign','$workspaceSuspended','$workspaceDenied');
DELETE FROM workspace.Memberships WHERE WorkspaceId IN ('$workspaceA','$workspaceForeign','$workspaceSuspended','$workspaceDenied');
DELETE FROM workspace.BootstrapProjections WHERE WorkspaceId IN ('$workspaceA','$workspaceForeign','$workspaceSuspended','$workspaceDenied');
DELETE FROM workspace.Workspaces WHERE WorkspaceId IN ('$workspaceA','$workspaceForeign','$workspaceSuspended','$workspaceDenied');

INSERT INTO workspace.Workspaces(WorkspaceId,[Key],[Name],LogoText,CreatedAt) VALUES
('$workspaceA','bootstrap-read-alpha','Bootstrap Alpha Secret','BA',SYSUTCDATETIME()),
('$workspaceForeign','bootstrap-read-foreign','Bootstrap Foreign Secret','BF',SYSUTCDATETIME()),
('$workspaceSuspended','bootstrap-read-suspended','Bootstrap Suspended Secret','BS',SYSUTCDATETIME()),
('$workspaceDenied','bootstrap-read-denied','Bootstrap Denied Secret','BD',SYSUTCDATETIME());

INSERT INTO workspace.BootstrapProjections(WorkspaceId,ContextVersion,ConfigurationVersion,Locale,TimeZone,BaseCurrency,CapabilitiesJson,EnabledModuleKeysJson,AvailableProductSpacesJson) VALUES
('$workspaceA',7,11,'vi','Asia/Saigon','VND','[]',CONCAT('[',CHAR(34),'contacts',CHAR(34),',',CHAR(34),'leads',CHAR(34),']'),CONCAT('[',CHAR(34),'crm',CHAR(34),']')),
('$workspaceForeign',3,4,'en','UTC','USD','[]','[]',CONCAT('[',CHAR(34),'crm',CHAR(34),']')),
('$workspaceSuspended',5,6,'en','UTC','USD','[]','[]',CONCAT('[',CHAR(34),'crm',CHAR(34),']')),
('$workspaceDenied',8,9,'en','UTC','USD','[]','[]',CONCAT('[',CHAR(34),'crm',CHAR(34),']'));

INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,[Status],CreatedAt) VALUES
('$membershipA','$workspaceA','$accountId','$memberId','Active',SYSUTCDATETIME()),
('$membershipSuspended','$workspaceSuspended','$accountId','$memberId','Suspended',SYSUTCDATETIME()),
('$membershipDenied','$workspaceDenied','$accountId','$memberId','Active',SYSUTCDATETIME());

INSERT INTO access.Roles(RoleId,WorkspaceId,[Name],NormalizedName,[Description],SourceTemplateId,IsActive,[Version],CreatedAt,UpdatedAt)
VALUES('$roleA','$workspaceA','Bootstrap Reader','BOOTSTRAP READER',NULL,NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME());
INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('$roleA','workspace.context.resolve');
INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
VALUES('$assignmentA','$workspaceA','$membershipA','$roleA',SYSUTCDATETIME());
"@
}

try {
    Invoke-SqlCommand "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" 'master'
    $env:ConnectionStrings__UnicoreCRM = $connection
    foreach ($context in @('IdentityAuthDbContext', 'WorkspaceDbContext', 'AccessControlDbContext')) {
        & dotnet ef database update --project (Join-Path $solutionRoot 'src/UnicoreCRM.Platform') --context $context --no-build | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$context migration application failed." }
    }

    $hostProcess = Start-Host
    $signIn = Sign-In
    $token = [string] $signIn.accessToken
    $sessionId = [string] $signIn.session.sessionId
    $accountId = [string] $signIn.session.principal.accountId
    $memberId = [string] $signIn.session.principal.memberId
    Assert-True (-not [string]::IsNullOrEmpty($token) -and -not [string]::IsNullOrEmpty($sessionId) -and -not [string]::IsNullOrEmpty($accountId) -and -not [string]::IsNullOrEmpty($memberId)) 'Trusted authenticated account principal established'

    $columns = Invoke-SqlScalar "SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='workspace' AND TABLE_NAME='AccessRecords';"
    Assert-True ($columns -eq 'AccessRecordId,AccountId,CorrelationId,OccurredAt,Operation,Outcome,RequestId,WorkspaceId') 'Existing Workspace access evidence schema supplies the exact floor fields'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='workspace' AND TABLE_NAME='AccessRecords' AND COLUMN_NAME IN ('MemberId','RecordId','ResourceVersion','ResultCount','Cardinality','WorkspaceName','WorkspaceKey','LogoText','Configuration');") -eq '0') 'Evidence schema has no member, record, version, cardinality, or bootstrap payload fields'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('workspace.AccessRecords');") -eq '0') 'Workspace owner evidence has no foreign keys'

    Add-WorkspaceFixtures $accountId $memberId

    $requestId = 'req-bootstrap-read-audit-0001'
    $correlationId = 'corr-bootstrap-read-audit-0001'
    $success = Measure-BootstrapRead "/workspaces/$workspaceA/bootstrap" $token $requestId $correlationId $workspaceForeign
    Assert-True ($success.Probe -eq '200|1') 'Successful bootstrap writes exactly one Workspace owner disclosure row'
    $document = $success.Body | ConvertFrom-Json
    Assert-True ((($document.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'capabilities,configuration,contextVersion,resolvedAt,workspace') 'Bootstrap top-level response contract unchanged'
    Assert-True ((($document.workspace.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'logoText,membershipId,name,status,workspaceId,workspaceKey') 'Bootstrap Workspace response contract unchanged'
    Assert-True ((($document.configuration.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'availableProductSpaces,baseCurrency,configurationVersion,enabledModuleKeys,locale,timeZone') 'Bootstrap configuration response contract unchanged'
    Assert-True ($document.workspace.workspaceId -eq $workspaceA -and $document.workspace.membershipId -eq $membershipA -and $document.workspace.status -eq 'active') 'Current route-selected Workspace and active membership behavior unchanged'
    Assert-True ($document.workspace.name -eq 'Bootstrap Alpha Secret' -and $document.workspace.workspaceKey -eq 'bootstrap-read-alpha' -and $document.workspace.logoText -eq 'BA') 'Authoritative Workspace projection values unchanged'
    Assert-True ([long] $document.contextVersion -eq 7 -and [long] $document.configuration.configurationVersion -eq 11) 'Bootstrap versions unchanged'
    Assert-True ($document.configuration.locale -eq 'vi' -and $document.configuration.timeZone -eq 'Asia/Saigon' -and $document.configuration.baseCurrency -eq 'VND') 'Bootstrap configuration composition unchanged'
    Assert-True ((($document.configuration.enabledModuleKeys) -join ',') -eq 'contacts,leads' -and (($document.configuration.availableProductSpaces) -join ',') -eq 'crm') 'Bootstrap array ordering and composition unchanged'
    Assert-True ((($document.capabilities) -join ',') -eq 'workspace.context.resolve') 'Existing capability evaluation behavior unchanged'
    Assert-True ($success.Body -match '"resolvedAt":"[^"]+Z"') 'Bootstrap resolved time remains UTC'

    $evidence = Invoke-SqlScalar "SELECT CONCAT(AccessRecordId,'|',Operation,'|',AccountId,'|',RequestId,'|',CorrelationId,'|',Outcome,'|',WorkspaceId,'|',DATEPART(TZOFFSET,OccurredAt)) FROM workspace.AccessRecords WHERE RequestId='$requestId';"
    $parts = $evidence.Split('|')
    Assert-True ($parts.Count -eq 8 -and $parts[0].StartsWith('wsa_')) 'Owner-generated audit identity persisted'
    Assert-True ($evidence -eq "$($parts[0])|getWorkspaceBootstrap|$accountId|$requestId|$correlationId|READ|$workspaceA|0") 'Operation, trusted actor, request, correlation, READ, trusted Workspace, and UTC evidence exact'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE RequestId='$requestId';") -eq '1') 'Successful request has no duplicate or per-component evidence'
    Assert-True ($document.workspace.workspaceId -ne $workspaceForeign) 'Untrusted Workspace header cannot replace route-selected trusted Workspace'

    Assert-True ((Measure-BootstrapRead "/workspaces/$workspaceA/bootstrap" '' 'req-bootstrap-unauth-0001' 'corr-bootstrap-unauth-0001').Probe -eq '401|0') 'Unauthenticated request writes no successful-disclosure owner evidence'
    Assert-True ((Measure-BootstrapRead '/workspaces/!/bootstrap' $token 'req-bootstrap-invalid-context-0001' 'corr-bootstrap-invalid-context-0001').Probe -eq '403|0') 'Invalid Workspace context writes no successful-disclosure owner evidence'
    Assert-True ((Measure-BootstrapRead '/workspaces/ws_bootstrap_read_unknown/bootstrap' $token 'req-bootstrap-unknown-0001' 'corr-bootstrap-unknown-0001').Probe -eq '403|0') 'Unknown Workspace writes no successful-disclosure owner evidence'
    Assert-True ((Measure-BootstrapRead "/workspaces/$workspaceForeign/bootstrap" $token 'req-bootstrap-foreign-0001' 'corr-bootstrap-foreign-0001').Probe -eq '403|0') 'Foreign Workspace writes no successful-disclosure owner evidence'
    Assert-True ((Measure-BootstrapRead "/workspaces/$workspaceSuspended/bootstrap" $token 'req-bootstrap-suspended-0001' 'corr-bootstrap-suspended-0001').Probe -eq '403|0') 'Suspended membership writes no successful-disclosure owner evidence'
    Assert-True ((Measure-BootstrapRead "/workspaces/$workspaceDenied/bootstrap" $token 'req-bootstrap-denied-0001' 'corr-bootstrap-denied-0001').Probe -eq '403|0') 'Capability denial writes no successful-disclosure owner evidence'
    Assert-True ((Measure-BootstrapRead "/workspaces/$workspaceA/bootstrap" $token 'bad' 'corr-bootstrap-malformed-0001').Probe -eq '422|0') 'Malformed authorized request metadata writes no successful-disclosure owner evidence'

    Invoke-SqlCommand "UPDATE workspace.BootstrapProjections SET EnabledModuleKeysJson='{' WHERE WorkspaceId='$workspaceA';"
    $corrupt = Measure-BootstrapRead "/workspaces/$workspaceA/bootstrap" $token 'req-bootstrap-corrupt-0001' 'corr-bootstrap-corrupt-0001'
    Assert-True ($corrupt.Probe -eq '500|0') 'Corrupt projection writes no successful-disclosure owner evidence'
    Assert-True ($corrupt.Body -notmatch 'Bootstrap Alpha Secret|bootstrap-read-alpha|Asia/Saigon|contacts') 'Corrupt projection discloses no partial bootstrap payload'
    Invoke-SqlCommand @"
UPDATE workspace.BootstrapProjections SET EnabledModuleKeysJson=CONCAT('[',CHAR(34),'contacts',CHAR(34),',',CHAR(34),'leads',CHAR(34),']') WHERE WorkspaceId='$workspaceA';
"@

    Invoke-SqlCommand @"
EXEC(N'CREATE TRIGGER workspace.TR_AccessRecords_BootstrapReadFailureProbe
ON workspace.AccessRecords
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE Operation=''getWorkspaceBootstrap'' AND Outcome=''READ'')
        THROW 51000, ''Workspace bootstrap read-audit persistence probe.'', 1;
END;');
"@
    Assert-True ((Measure-BootstrapRead "/workspaces/$workspaceA/bootstrap" $token 'req-bootstrap-audit-fail-0001' 'corr-bootstrap-audit-fail-0001').Probe -eq '500|0') 'Audit persistence failure prevents successful disclosure and rolls back owner evidence'
    Invoke-SqlCommand 'DROP TRIGGER workspace.TR_AccessRecords_BootstrapReadFailureProbe;'
    Assert-True ((Measure-BootstrapRead "/workspaces/$workspaceA/bootstrap" $token 'req-bootstrap-recovery-0001' 'corr-bootstrap-recovery-0001').Probe -eq '200|1') 'Bootstrap recovers with exactly one owner evidence row after persistence probe removal'

    $listRequestId = 'req-list-regression-0001'
    $listBefore = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE Operation='listMyWorkspaces';")
    $list = Invoke-Api 'GET' '/workspaces' $null (New-ReadHeaders $token $listRequestId 'corr-list-regression-0001')
    $listDelta = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE Operation='listMyWorkspaces';") - $listBefore
    Assert-True ($list.Status -eq 200 -and $listDelta -eq 1) 'listMyWorkspaces still writes exactly one row per successful invocation'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM workspace.AccessRecords WHERE RequestId='$listRequestId' AND Operation='listMyWorkspaces' AND WorkspaceId IS NULL AND Outcome='READ' AND CorrelationId='corr-list-regression-0001';") -eq '1') 'listMyWorkspaces keeps null Workspace provenance with request and READ evidence'

    $auditDump = Invoke-SqlScalar "SELECT STRING_AGG(CONCAT(AccessRecordId,'|',Operation,'|',AccountId,'|',ISNULL(RequestId,''),'|',CorrelationId,'|',ISNULL(Outcome,''),'|',ISNULL(WorkspaceId,'')),' ') FROM workspace.AccessRecords WHERE Operation='getWorkspaceBootstrap';"
    foreach ($forbidden in @('Bootstrap Alpha Secret','bootstrap-read-alpha','Asia/Saigon','contacts','leads','workspace.context.resolve',$membershipA,$memberId)) {
        Assert-True ($auditDump -notmatch [regex]::Escape($forbidden)) "Bootstrap evidence excludes business value $forbidden"
    }

    $endpointSource = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Contracts/WorkspaceEndpoints.cs')
    $handlerSource = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Application/GetWorkspaceBootstrap/Handler.cs')
    $querySource = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Application/GetWorkspaceBootstrap/Query.cs')
    $recordSource = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'Domain/WorkspaceAccessRecord.cs')
    $taskSource = $endpointSource + "`n" + $handlerSource + "`n" + $querySource + "`n" + $recordSource
    $projectionIndex = $handlerSource.IndexOf('new WorkspaceBootstrapDocument', [StringComparison]::Ordinal)
    $appendIndex = $handlerSource.IndexOf('persistence.AddAccessRecord', [StringComparison]::Ordinal)
    $saveIndex = $handlerSource.IndexOf('await persistence.SaveChangesAsync', [StringComparison]::Ordinal)
    $returnIndex = $handlerSource.IndexOf('return WorkspaceOperationResult<WorkspaceBootstrapDocument>.Success', [StringComparison]::Ordinal)
    Assert-True ($projectionIndex -ge 0 -and $projectionIndex -lt $appendIndex -and $appendIndex -lt $saveIndex -and $saveIndex -lt $returnIndex) 'Projection, single append, persistence, and response timing is exact'
    Assert-True ([regex]::Matches($handlerSource, 'WorkspaceAccessRecord\.SuccessfulRead\(').Count -eq 1 -and $handlerSource -notmatch 'new WorkspaceAccessRecord\(') 'Legacy constructor is removed only from bootstrap successful disclosure'
    Assert-True ($handlerSource -match 'trustedWorkspace\.AccountId' -and $handlerSource -match 'trustedWorkspace\.WorkspaceId') 'Bootstrap audit provenance is taken from Trusted Workspace context'
    Assert-True ($querySource -match 'string RequestId' -and $endpointSource -match 'request!?\.RequestId, request\.CorrelationId') 'Validated request metadata reaches the bootstrap handler'
    Assert-True ([regex]::Matches($endpointSource, 'MapGet\("/workspaces/\{workspaceId\}/bootstrap"').Count -eq 1) 'Exactly one getWorkspaceBootstrap route remains'
    Assert-True ([regex]::Matches($endpointSource, 'MapGet\(').Count -eq 2) 'Workspace GET route surface unchanged'
    Assert-True ([regex]::Matches($endpointSource, 'Map(Post|Put|Patch|Delete)\(').Count -eq 0) 'No Workspace mutation route introduced'
    Assert-True ($handlerSource -notmatch 'foreach|ForEach') 'No per-component or per-field bootstrap audit append exists'
    Assert-True ($taskSource -notmatch '\b(IdentityAuth|AccessControl|Products|Quotes|Orders|Invoices|Payments)DbContext\b') 'Bootstrap correction adds no foreign DbContext'
    Assert-True ($taskSource -notmatch 'Outbox|Idempotency|Workflow|GenericAudit|IReadAuditService|SharedAuditDbContext') 'Bootstrap correction adds no generic audit infrastructure'
    Assert-True ($taskSource -notmatch 'WORKSPACE_CONTEXT_ACCESS_LOG|READ_ACCESS_LOG') 'Runtime correction does not define or retoken audit admission semantics'
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
    Operation = 'getWorkspaceBootstrap'
    AuditFloor = 'READ_ACCESS_LOG_CONFORMANT_MINIMUM'
    SpecialAuditToken = 'UNCHANGED_AND_UNDEFINED'
    Checks = $checks
} | ConvertTo-Json -Depth 5
