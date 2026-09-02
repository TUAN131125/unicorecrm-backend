<#
.SYNOPSIS
    Verifies Identity-owned READ_ACCESS_LOG conformance for GET /auth/session.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5371,

    [int] $ReadyTimeoutSeconds = 420,

    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$baseUrl = "http://127.0.0.1:$Port"
$connection = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$email = 'identity.read.audit@example.test'
$password = 'Identity-Read-Audit!2026'
$displayName = 'Identity Read Audit Fixture'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$solutionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost')).Path
$identityRoot = Join-Path $solutionRoot 'src/UnicoreCRM.Platform/IdentityAuth'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-identity-read-audit-' + [Guid]::NewGuid().ToString('N')))
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

function New-ReadHeaders([string] $token, [string] $requestId, [string] $correlationId) {
    $headers = @{
        'X-Request-Id' = $requestId
        'X-Correlation-Id' = $correlationId
    }
    if (-not [string]::IsNullOrEmpty($token)) { $headers.Authorization = "Bearer $token" }
    return $headers
}

function Get-ReadAuditCount {
    return [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM iam.AuditRecords WHERE Operation='getCurrentSession' AND Outcome='READ';")
}

function Measure-Read([string] $token, [string] $requestId, [string] $correlationId) {
    $before = Get-ReadAuditCount
    $response = Invoke-Api 'GET' '/auth/session' $null (New-ReadHeaders $token $requestId $correlationId)
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
            $probe = Invoke-Api 'GET' '/auth/session' $null $null
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
            deviceLabel = 'Identity read audit verifier'
        } | ConvertTo-Json -Compress) @{
            'X-Request-Id' = "req-identity-signin-$suffix"
            'X-Correlation-Id' = "corr-identity-signin-$suffix"
            'Idempotency-Key' = "idem-identity-signin-$suffix"
        }
        if ($response.Status -eq 200) { return $response.Body | ConvertFrom-Json }
        Start-Sleep -Milliseconds 250
    }
    throw 'Development Identity account did not become available for sign-in.'
}

try {
    Invoke-SqlCommand "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" 'master'
    $env:ConnectionStrings__UnicoreCRM = $connection
    & dotnet ef database update --project (Join-Path $solutionRoot 'src/UnicoreCRM.Platform') --context IdentityAuthDbContext --no-build | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'IdentityAuth migration application failed.' }

    $hostProcess = Start-Host
    $signIn = Sign-In
    $token = [string] $signIn.accessToken
    $sessionId = [string] $signIn.session.sessionId
    $accountId = [string] $signIn.session.principal.accountId
    Assert-True (-not [string]::IsNullOrEmpty($token) -and -not [string]::IsNullOrEmpty($sessionId) -and -not [string]::IsNullOrEmpty($accountId)) 'Authenticated principal/session established'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM iam.SecurityEvents WHERE EventType='IDENTITY_SESSION_CREATED' AND AccountId='$accountId';") -eq '1') 'Existing sign-in security event preserved'

    $columns = Invoke-SqlScalar "SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='iam' AND TABLE_NAME='AuditRecords';"
    Assert-True ($columns -eq 'AccountId,CorrelationId,Id,OccurredAt,Operation,Outcome,RequestId') 'Identity audit schema exact'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='iam' AND TABLE_NAME='AuditRecords' AND COLUMN_NAME='WorkspaceId';") -eq '0') 'Non-Workspace audit schema fabricates no WorkspaceId'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('iam.AuditRecords');") -eq '0') 'Identity audit store has zero foreign keys'

    $requestId = 'req-identity-read-audit-0001'
    $correlationId = 'corr-identity-read-audit-0001'
    $success = Measure-Read $token $requestId $correlationId
    Assert-True ($success.Probe -eq '200|1') 'Authenticated getCurrentSession writes exactly one READ row'
    $document = $success.Body | ConvertFrom-Json
    Assert-True ($document.sessionId -eq $sessionId -and $document.principal.accountId -eq $accountId) 'Session wire identity unchanged'
    Assert-True ((($document.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'absoluteExpiresAt,assuranceLevel,device,idleExpiresAt,issuedAt,lastSeenAt,principal,refreshCounter,sessionId,status') 'Session top-level wire shape unchanged'
    $evidence = Invoke-SqlScalar "SELECT CONCAT(Id,'|',Operation,'|',Outcome,'|',AccountId,'|',RequestId,'|',CorrelationId,'|',DATEPART(TZOFFSET,OccurredAt)) FROM iam.AuditRecords WHERE RequestId='$requestId';"
    $parts = $evidence.Split('|')
    Assert-True ($parts.Count -eq 7 -and [long]$parts[0] -gt 0) 'Owner-generated audit identity persisted'
    Assert-True ($evidence -eq "$($parts[0])|getCurrentSession|READ|$accountId|$requestId|$correlationId|0") 'Read provenance, discriminator, and UTC time exact'

    $securityEventsBeforeReads = [int] (Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.SecurityEvents;')
    Assert-True ((Measure-Read '' 'req-identity-unauth-0001' 'corr-identity-unauth-0001').Probe -eq '401|0') 'Unauthenticated request writes no owner read evidence'
    Assert-True ((Measure-Read 'invalid.jwt.token' 'req-identity-invalid-0001' 'corr-identity-invalid-0001').Probe -eq '401|0') 'Invalid token writes no owner read evidence'
    Assert-True ((Measure-Read $token 'bad' 'corr-identity-malformed-0001').Probe -eq '422|0') 'Malformed authorized metadata writes no owner read evidence'

    Invoke-SqlCommand "UPDATE iam.Sessions SET IdleExpiresAt=DATEADD(minute,-2,SYSUTCDATETIME()), AbsoluteExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE SessionId='$sessionId';"
    Assert-True ((Measure-Read $token 'req-identity-expired-0001' 'corr-identity-expired-0001').Probe -eq '401|0') 'Expired authoritative session writes no owner read evidence'
    Invoke-SqlCommand "UPDATE iam.Sessions SET IdleExpiresAt=DATEADD(day,1,SYSUTCDATETIME()), AbsoluteExpiresAt=DATEADD(day,2,SYSUTCDATETIME()) WHERE SessionId='$sessionId';"

    $originalStatus = Invoke-SqlScalar "SELECT Status FROM iam.Accounts WHERE AccountId='$accountId';"
    Invoke-SqlCommand "UPDATE iam.Accounts SET Status='CORRUPT_READ_PROJECTION' WHERE AccountId='$accountId';"
    Assert-True ((Measure-Read $token 'req-identity-corrupt-0001' 'corr-identity-corrupt-0001').Probe -eq '500|0') 'Corrupt persisted account projection writes no owner read evidence'
    Invoke-SqlCommand "UPDATE iam.Accounts SET Status='$originalStatus' WHERE AccountId='$accountId';"

    Invoke-SqlCommand @"
EXEC(N'CREATE TRIGGER iam.TR_AuditRecords_IdentityReadFailureProbe
ON iam.AuditRecords
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE Operation=''getCurrentSession'' AND Outcome=''READ'')
        THROW 51000, ''Identity read-audit persistence probe.'', 1;
END;');
"@
    Assert-True ((Measure-Read $token 'req-identity-audit-fail-0001' 'corr-identity-audit-fail-0001').Probe -eq '500|0') 'Audit persistence failure prevents successful disclosure'
    Invoke-SqlCommand 'DROP TRIGGER iam.TR_AuditRecords_IdentityReadFailureProbe;'
    Assert-True ((Measure-Read $token 'req-identity-recovery-0001' 'corr-identity-recovery-0001').Probe -eq '200|1') 'Identity read recovers with one audit after persistence probe removal'

    Assert-True ((Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.SecurityEvents;') -eq [string]$securityEventsBeforeReads) 'Read auditing does not replace or add security events'
    $auditDump = Invoke-SqlScalar "SELECT STRING_AGG(CONCAT(Id,'|',Operation,'|',Outcome,'|',ISNULL(AccountId,''),'|',ISNULL(RequestId,''),'|',CorrelationId),' ') FROM iam.AuditRecords WHERE Operation='getCurrentSession' AND Outcome='READ';"
    Assert-True ($auditDump -notmatch [regex]::Escape($email)) 'Read evidence stores no disclosed email'
    Assert-True ($auditDump -notmatch [regex]::Escape($displayName)) 'Read evidence stores no disclosed profile value'
    Assert-True ($auditDump -notmatch [regex]::Escape($sessionId)) 'Read evidence stores no session identifier or token material'
    Assert-True ($auditDump -notmatch [regex]::Escape($token)) 'Read evidence stores no access token'

    $source = (Get-ChildItem -LiteralPath $identityRoot -Recurse -File -Filter '*.cs' | Get-Content -Raw) -join "`n"
    $endpointSource = Get-Content -Raw -LiteralPath (Join-Path $identityRoot 'Contracts/IdentityAuthEndpoints.cs')
    $handlerSource = Get-Content -Raw -LiteralPath (Join-Path $identityRoot 'Application/GetCurrentSession/Handler.cs')
    Assert-True ([regex]::Matches($endpointSource, 'MapGet\("/auth/session"').Count -eq 1) 'Exactly one Identity GET route remains'
    Assert-True ([regex]::Matches($endpointSource, 'Map(Post|Put|Patch|Delete)\(').Count -eq 6) 'Authentication mutation route surface unchanged'
    Assert-True ($handlerSource -notmatch 'AddSession|Rotate\(|Revoke\(|AddAccount|AddCredential|AddSecurityEvent') 'getCurrentSession introduces no authentication/session mutation'
    Assert-True ($source -notmatch '\b(Workspace|AccessControl|Products|Quotes|Orders|Invoices|Payments)DbContext\b') 'Identity read adds no foreign DbContext'
    Assert-True ($source -notmatch 'GenericAudit|IReadAuditService|SharedAuditDbContext') 'Identity read adds no generic audit infrastructure'
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
$pending = & dotnet ef migrations has-pending-model-changes --project (Join-Path $solutionRoot 'src/UnicoreCRM.Platform') --context IdentityAuthDbContext --no-build 2>&1
if ($LASTEXITCODE -ne 0) { throw "IdentityAuth model verification failed: $pending" }
$checks.Add('IdentityAuth model pending changes=NONE')

[pscustomobject]@{
    Status = 'PASS'
    Operation = 'getCurrentSession'
    Checks = $checks
} | ConvertTo-Json -Depth 5
