<#
.SYNOPSIS
    Verifies bounded AccessControl administrative request bodies and authorization precedence.
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
$script:HostProcess = $null
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost')).Path
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$password = 'Access-Body-Limit-Verify!2026'
$email = 'admin@unicorecrm.local'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-access-body-limit-' + [Guid]::NewGuid().ToString('N'))
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
    $connection = New-Connection
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        return $command.ExecuteScalar()
    }
    finally { $connection.Dispose() }
}

function New-Client {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    return $client
}

function Invoke-Api(
    [string] $Method,
    [string] $Path,
    [AllowNull()][string] $Body,
    [AllowNull()][string] $Token,
    [AllowNull()][string] $WorkspaceId,
    [AllowNull()][string] $IdempotencyKey,
    [AllowNull()][string] $IfMatch = $null,
    [AllowNull()][string] $RequestId = $null,
    [AllowNull()][string] $CorrelationId = $null,
    [switch] $Chunked
) {
    $script:Counter++
    if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-access-body-' + $script:Counter.ToString('d6') }
    if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-access-body-' + $script:Counter.ToString('d6') }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId) }
    if ($CorrelationId -ne 'omit') { $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId) }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if ($IdempotencyKey -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    if ($IfMatch -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($IfMatch)) { $null = $request.Headers.TryAddWithoutValidation('If-Match', $IfMatch) }
    if (-not [string]::IsNullOrEmpty($Body)) {
        $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json')
        if ($Chunked) {
            $request.Headers.TransferEncodingChunked = $true
            $request.Content.Headers.ContentLength = $null
        }
    }
    $client = New-Client
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            try { $payload = $raw | ConvertFrom-Json } catch { }
        }
        return [pscustomobject] @{
            Status = [int] $response.StatusCode
            Raw = $raw
            Body = $payload
            ContentType = [string] $response.Content.Headers.ContentType
        }
    }
    finally { $request.Dispose(); $client.Dispose() }
}

function Get-MutationSnapshot {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.Roles'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleCapabilities'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleDataScopes'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.RoleFieldSecurity'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments'),
        (Get-Scalar 'SELECT COALESCE(SUM(Revision),0) FROM access.WorkspaceDirectoryRevisions'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.AccessRoleCommandIdempotencyRecords'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.MemberAccessCommandIdempotencyRecords'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.GovernanceCommandAudits'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.OutboxEvents')
    ))
}

function Assert-Response([string] $Name, [object] $Response, [int] $Status, [string] $Code) {
    Add-Result "$Name status" $Status $Response.Status
    Add-Result "$Name code" $Code $Response.Body.code
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
    $stdout = Join-Path $logRoot 'host.out.log'
    $stderr = Join-Path $logRoot 'host.err.log'
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 480; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Api 'GET' '/auth/session' $null $null $null $null
            if ($probe.Status -eq 401) { $ready = $true; break }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }

    $signInBody = @{ email = $email; password = $password } | ConvertTo-Json -Compress
    $signIn = Invoke-Api 'POST' '/auth/sessions' $signInBody $null $null 'idem-access-body-signin-0001'
    Add-Result 'authentication fixture sign-in' 200 $signIn.Status
    $token = [string] $signIn.Body.accessToken
    $workspaceId = [string] (Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo'")
    $accountId = [string] (Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail='$($email.ToUpperInvariant())'")
    $membershipId = [string] (Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId='$workspaceId' AND AccountId='$accountId'")
    $adminRoleId = [string] (Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.MembershipId='$membershipId' AND c.Capability='access.configure'")
    Assert-True 'trusted fixture exists' (-not [string]::IsNullOrWhiteSpace($workspaceId) -and -not [string]::IsNullOrWhiteSpace($adminRoleId))

    $validBody = @{ name = 'Body Limit Control'; capabilities = @('tasks.read'); dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress
    $normal = Invoke-Api 'POST' '/access/roles' $validBody $token $workspaceId 'idem-access-body-normal-0001'
    Assert-Response 'normal allowed create' $normal 200 $null

    $atLimit = '{' + (' ' * 65535)
    $overLimit = '{' + (' ' * 65536)
    Add-Result 'at-limit UTF-8 byte count' 65536 ([Text.Encoding]::UTF8.GetByteCount($atLimit))
    Add-Result 'over-limit UTF-8 byte count' 65537 ([Text.Encoding]::UTF8.GetByteCount($overLimit))

    $atLimitResponse = Invoke-Api 'POST' '/access/roles' $atLimit $token $workspaceId 'idem-access-body-at-limit-0001'
    Assert-Response 'exact-limit body follows malformed contract' $atLimitResponse 422 'VALIDATION_FAILED'

    $operations = @(
        [pscustomobject] @{ Name = 'createAccessRole'; Method = 'POST'; Path = '/access/roles'; IfMatch = $null },
        [pscustomobject] @{ Name = 'replaceAccessRole'; Method = 'PUT'; Path = "/access/roles/$adminRoleId"; IfMatch = '"0"' },
        [pscustomobject] @{ Name = 'archiveAccessRole'; Method = 'POST'; Path = "/access/roles/$adminRoleId/archive"; IfMatch = '"0"' },
        [pscustomobject] @{ Name = 'replaceWorkspaceMemberAccess'; Method = 'POST'; Path = "/access/members/$membershipId/access"; IfMatch = '"0"' }
    )

    $effectsBefore = Get-MutationSnapshot
    $decisionBefore = [long] (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.AuthorizationDecisions')
    $firstOversized = Invoke-Api $operations[0].Method $operations[0].Path $overLimit $token $workspaceId 'idem-access-body-over-0001' $operations[0].IfMatch
    Assert-Response 'createAccessRole oversized' $firstOversized 413 'PAYLOAD_TOO_LARGE'
    Add-Result 'oversized problem content type' 'application/problem+json' $firstOversized.ContentType
    Add-Result 'one capability decision per oversized request' ($decisionBefore + 1) (Get-Scalar 'SELECT COUNT_BIG(*) FROM access.AuthorizationDecisions')

    for ($index = 1; $index -lt $operations.Count; $index++) {
        $operation = $operations[$index]
        $response = Invoke-Api $operation.Method $operation.Path $overLimit $token $workspaceId ("idem-access-body-over-{0:d4}" -f ($index + 1)) $operation.IfMatch
        Assert-Response "$($operation.Name) oversized" $response 413 'PAYLOAD_TOO_LARGE'
    }

    $chunked = Invoke-Api 'POST' '/access/roles' $overLimit $token $workspaceId 'idem-access-body-chunked-0001' $null $null $null -Chunked
    Assert-Response 'chunked oversized body' $chunked 413 'PAYLOAD_TOO_LARGE'

    $unicodeBody = '{"name":"' + [string]::new([char]0x4E00, 22000) + '"}'
    Assert-True 'multibyte body has fewer than limit characters' ($unicodeBody.Length -lt 65536)
    Assert-True 'multibyte body exceeds byte limit' ([Text.Encoding]::UTF8.GetByteCount($unicodeBody) -gt 65536)
    $unicode = Invoke-Api 'POST' '/access/roles' $unicodeBody $token $workspaceId 'idem-access-body-unicode-0001'
    Assert-Response 'multibyte oversized body' $unicode 413 'PAYLOAD_TOO_LARGE'

    foreach ($operation in $operations) {
        $malformed = Invoke-Api $operation.Method $operation.Path '{' $token $workspaceId ("idem-access-body-malformed-$($operation.Name)") $operation.IfMatch
        Assert-Response "$($operation.Name) malformed within limit" $malformed 422 'VALIDATION_FAILED'

        $missingMetadata = Invoke-Api $operation.Method $operation.Path $overLimit $token $workspaceId 'omit' $operation.IfMatch
        Assert-Response "$($operation.Name) metadata precedes size" $missingMetadata 422 'VALIDATION_FAILED'
    }

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE RoleId='$adminRoleId' AND Capability='access.configure';"
    try {
        foreach ($operation in $operations) {
            $oversizedDenied = Invoke-Api $operation.Method $operation.Path $overLimit $token $workspaceId ("idem-access-body-denied-large-$($operation.Name)") $operation.IfMatch
            $malformedDenied = Invoke-Api $operation.Method $operation.Path '{' $token $workspaceId ("idem-access-body-denied-small-$($operation.Name)") $operation.IfMatch
            Assert-Response "$($operation.Name) unauthorized oversized" $oversizedDenied 403 'ACCESS_DENIED'
            Assert-Response "$($operation.Name) unauthorized malformed" $malformedDenied 403 'ACCESS_DENIED'
            Add-Result "$($operation.Name) denial independent of body size" $malformedDenied.Status $oversizedDenied.Status
        }
    }
    finally {
        Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('$adminRoleId','access.configure');"
    }

    Add-Result 'all rejected bodies create no mutation effects' $effectsBefore (Get-MutationSnapshot)

    $endpointSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Contracts/AccessControlEndpoints.cs')
    Add-Result 'effective source byte limit' 65536 ([regex]::Match($endpointSource, 'MaximumAdministrativeRequestBodyBytes\s*=\s*([\d_]+)').Groups[1].Value -replace '_','')
    Add-Result 'administrative direct request-stream reads removed' 0 ([regex]::Matches($endpointSource, 'new StreamReader\(context\.Request\.Body\)').Count)
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
