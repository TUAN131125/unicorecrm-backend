<#
.SYNOPSIS
    Verifies PATCH /products/configuration/types/{typeId} against an isolated SQL database and the
    real ApiHost.
.DESCRIPTION
    Covers the frozen updateProductConfigurationType contract: effective-status transitions and their
    revision/ETag consequences, the semantic no-op, transport and domain validation, canonical typeId
    resolution, fail-closed corrupt state, idempotency, Workspace isolation, immutable command audit,
    and both proven Product-command linearization orderings against the real endpoint.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5613,
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
$password = 'Product-Config-Update!2026'
$email = 'admin@unicorecrm.local'
$canonicalOrder = 'physical_product,service,subscription,package,implementation,support_sla,addon,license,maintenance'
$allActive = 'ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-update-product-config-' + [Guid]::NewGuid().ToString('N'))
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

function New-ApiRequest(
    [string] $Method,
    [string] $Path,
    [string] $Token,
    [string] $WorkspaceId,
    [string] $IdempotencyKey,
    [string] $IfMatch,
    [string] $RawBody,
    [string] $BaseUrl
) {
    $script:Counter++
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$BaseUrl$Path")
    $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pcu-' + $script:Counter.ToString('d6'))
    $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pcu-' + $script:Counter.ToString('d6'))
    if ($Token -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($Token)) {
        $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token")
    }
    if ($WorkspaceId -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($WorkspaceId)) {
        $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId)
    }
    if ($IdempotencyKey -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($IdempotencyKey)) {
        $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey)
    }
    if ($IfMatch -ne 'omit' -and $null -ne $IfMatch) {
        $null = $request.Headers.TryAddWithoutValidation('If-Match', $IfMatch)
    }
    if ($RawBody -ne 'omit' -and $null -ne $RawBody) {
        $request.Content = [System.Net.Http.StringContent]::new($RawBody, [Text.Encoding]::UTF8, 'application/json')
    }
    return $request
}

function Complete-Response([object] $Response) {
    $raw = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { $payload = $null } }
    $etag = $null
    if ($null -ne $Response.Headers.ETag) { $etag = $Response.Headers.ETag.ToString() }
    return [pscustomobject]@{ Status = [int]$Response.StatusCode; Raw = $raw; Body = $payload; ETag = $etag }
}

function Invoke-Request([object] $Request) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(120)
    try {
        $response = $client.SendAsync($Request).GetAwaiter().GetResult()
        try { return Complete-Response $response } finally { $response.Dispose() }
    }
    finally { $Request.Dispose(); $client.Dispose() }
}

function Invoke-Patch(
    [string] $TypeId,
    [string] $Status,
    [string] $IfMatch,
    [string] $IdempotencyKey = $null,
    [string] $Workspace = $null,
    [string] $Token = $null,
    [string] $RawBody = $null,
    [string] $BaseUrl = $null
) {
    if ([string]::IsNullOrEmpty($IdempotencyKey)) { $IdempotencyKey = 'idem-pcu-' + [Guid]::NewGuid().ToString('N') }
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    if ([string]::IsNullOrEmpty($Token)) { $Token = $script:Token }
    if ([string]::IsNullOrEmpty($BaseUrl)) { $BaseUrl = $script:BaseUrl }
    if ([string]::IsNullOrEmpty($RawBody)) {
        if ($Status -eq 'omit') { $RawBody = '{}' }
        else { $RawBody = (@{ status = $Status } | ConvertTo-Json -Compress) }
    }
    $request = New-ApiRequest 'PATCH' "/products/configuration/types/$TypeId" $Token $Workspace $IdempotencyKey $IfMatch $RawBody $BaseUrl
    return Invoke-Request $request
}

function Invoke-Configuration([string] $Workspace = $null, [string] $Token = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    if ([string]::IsNullOrEmpty($Token)) { $Token = $script:Token }
    $request = New-ApiRequest 'GET' '/products/configuration/types' $Token $Workspace 'omit' 'omit' 'omit' $script:BaseUrl
    return Invoke-Request $request
}

function New-ProductBody([string] $Sku, [string] $Type) {
    return @{
        sku = $Sku; name = "Update Config $Sku"; type = $Type; status = 'ACTIVE'
        category = 'Software License'; unit = 'item'
        unitPrice = @{ amount = '10.00'; currency = 'USD' }
        taxRate = '0'; taxMode = 'none'; billingCycle = 'one_time'
        isSubscription = $false; isRenewable = $false; tags = @()
    }
}

function New-Product([string] $Sku, [string] $Type) {
    $request = New-ApiRequest 'POST' '/products' $script:Token $script:WorkspaceId ('idem-pcu-prod-' + [Guid]::NewGuid().ToString('N')) 'omit' ((New-ProductBody $Sku $Type) | ConvertTo-Json -Compress -Depth 8) $script:BaseUrl
    return Invoke-Request $request
}

# The emission half of the frozen asymmetry: the server never sends a weak validator, whatever the
# request supplied. A strong ETag here is exactly a quoted decimal with no W/ prefix.
function Assert-StrongETag([string] $Name, [string] $ETag) {
    Assert-True "$Name emits a strong quoted decimal ETag" ($ETag -cmatch '^"[0-9]+"$')
    Assert-True "$Name emits no weak prefix" (-not $ETag.StartsWith('W/'))
}

function Get-Codes([object] $Body) {
    return [string]::Join(',', @($Body.result.data.types | ForEach-Object { [string]$_.code }))
}

function Get-Statuses([object] $Body) {
    return [string]::Join(',', @($Body.result.data.types | ForEach-Object { [string]$_.status }))
}

function Get-StatusOf([object] $Body, [string] $Code) {
    return [string](@($Body.result.data.types | Where-Object { $_.code -ceq $Code })[0].status)
}

function Get-ReadStatusOf([object] $Body, [string] $Code) {
    return [string](@($Body.data.types | Where-Object { $_.code -ceq $Code })[0].status)
}

function Get-Revision([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $value = Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace'"
    if ($null -eq $value) { return [long]0 }
    return [long]$value
}

# Reads the anchor while a mutation is deliberately blocked. A short lock timeout turns an
# unexpected lock hold into a fast, legible failure instead of a stall.
function Get-RevisionNoWait([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $connection = New-Connection
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = 'SET LOCK_TIMEOUT 3000; SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=@ws'
        $null = $command.Parameters.AddWithValue('@ws', $Workspace)
        $command.CommandTimeout = 30
        $value = $command.ExecuteScalar()
        if ($null -eq $value -or $value -is [DBNull]) { return [long]0 }
        return [long]$value
    }
    finally { $connection.Dispose() }
}

function Get-TrustedRevision([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $value = Get-Scalar "SELECT GreatestTrustedRevision FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$Workspace'"
    if ($null -eq $value) { return [long]0 }
    return [long]$value
}

function Get-OverrideStatus([string] $Code, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $value = Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace' AND ProductTypeCode=N'$Code' COLLATE Latin1_General_100_BIN2"
    if ($null -eq $value) { return '' }
    return [string]$value
}

function Get-OverrideCount([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    return [long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace'")
}

function Get-CommandAuditCount {
    return [long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.AuditRecords WHERE Operation=N'updateProductConfigurationType'")
}

function Get-IdempotencyCount {
    return [long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.IdempotencyRecords WHERE Operation=N'updateProductConfigurationType'")
}

function Get-OutboxCount {
    return [long](Get-Scalar 'SELECT COUNT_BIG(*) FROM products.OutboxMessages')
}

function Get-ConfigurationRowSnapshot([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    return [string]::Join('|', @(
        (Get-Scalar "SELECT COUNT_BIG(*) FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace'"),
        (Get-Scalar "SELECT COUNT_BIG(*) FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace'"),
        (Get-Scalar "SELECT COALESCE(SUM(Revision),0) FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace'")
    ))
}

function Set-Revision([string] $Workspace, [long] $Revision) {
    Invoke-SqlNonQuery "IF EXISTS(SELECT 1 FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace') UPDATE products.ProductConfigurationDocuments SET Revision=$Revision WHERE WorkspaceId=N'$Workspace' ELSE INSERT INTO products.ProductConfigurationDocuments(WorkspaceId,Revision) VALUES(N'$Workspace',$Revision);"
}

function Set-Override([string] $Workspace, [string] $Code, [string] $Status) {
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$Workspace',N'$Code',N'$Status');"
}

function Clear-Configuration([string] $Workspace) {
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace'; DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace'; DELETE FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$Workspace';"
}

function Start-ApiHost {
    $stdout = Join-Path $logRoot ('host.out.' + [Guid]::NewGuid().ToString('N') + '.log')
    $stderr = Join-Path $logRoot ('host.err.' + [Guid]::NewGuid().ToString('N') + '.log')
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probeRequest = New-ApiRequest 'GET' '/auth/session' 'omit' 'omit' 'omit' 'omit' 'omit' $script:BaseUrl
            $probe = Invoke-Request $probeRequest
            if ($probe.Status -eq 401) { $ready = $true; break }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.AggregateException] { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }
}

function Stop-ApiHost {
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit()
    }
    $script:HostProcess = $null
}

# Freezes an in-flight Product command mid-transaction with no production test hook: an independent
# session holds an exclusive lock on products.OutboxMessages, a table the command touches only after
# it has read the configuration and passed eligibility. The command is therefore provably holding an
# ACTIVE snapshot inside its open serializable transaction while it stalls there.
function Start-CommandFreeze {
    $connection = New-Connection
    $transaction = $connection.BeginTransaction()
    $command = $connection.CreateCommand()
    $command.Transaction = $transaction
    $command.CommandText = 'SELECT TOP 0 * FROM products.OutboxMessages WITH (TABLOCKX, HOLDLOCK)'
    $command.ExecuteReader().Close()
    return [pscustomobject]@{ Connection = $connection; Transaction = $transaction }
}

function Start-Request([object] $Request) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(180)
    return [pscustomobject]@{ Client = $client; Request = $Request; Task = $client.SendAsync($Request) }
}

function Complete-PendingRequest([object] $Pending) {
    $response = $Pending.Task.GetAwaiter().GetResult()
    try { $result = Complete-Response $response } finally { $response.Dispose() }
    $Pending.Request.Dispose(); $Pending.Client.Dispose()
    return $result
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
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'products.configure'
    # studio.read backs the public GET, used only to cross-check that the mutation and the read agree
    # on the same effective document. products.create/read back the linearization regressions.
    $env:AccessControl__DevelopmentBootstrap__Capabilities__1 = 'studio.read'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__2 = 'products.create'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__3 = 'products.read'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    Start-ApiHost

    $signInRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/auth/sessions")
    $null = $signInRequest.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pcu-signin')
    $null = $signInRequest.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pcu-signin')
    $null = $signInRequest.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-pcu-signin')
    $signInRequest.Content = [System.Net.Http.StringContent]::new((@{ email = $email; password = $password } | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
    $signInClient = [System.Net.Http.HttpClient]::new()
    $signInResponse = $signInClient.SendAsync($signInRequest).GetAwaiter().GetResult()
    $signInBody = $signInResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Add-Result 'authentication fixture sign-in' 200 ([int]$signInResponse.StatusCode)
    $script:Token = [string]$signInBody.accessToken
    $signInRequest.Dispose(); $signInResponse.Dispose(); $signInClient.Dispose()

    $script:WorkspaceId = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo'")
    $foreignWorkspace = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo-isolated'")
    $accountId = [string](Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail=N'$($email.ToUpperInvariant())'")
    $membershipId = [string](Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId=N'$script:WorkspaceId' AND AccountId=N'$accountId'")
    $roleId = [string](Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.WorkspaceId=N'$script:WorkspaceId' AND a.MembershipId=N'$membershipId' AND c.Capability=N'products.configure'")
    Assert-True 'trusted products.configure fixture exists' (-not [string]::IsNullOrWhiteSpace($roleId))
    Assert-True 'foreign Workspace fixture exists' (-not [string]::IsNullOrWhiteSpace($foreignWorkspace))

    # =============================================================================================
    # 1. Authorization and trusted Workspace context
    # =============================================================================================
    Clear-Configuration $script:WorkspaceId
    $unauthenticated = Invoke-Patch 'service' 'INACTIVE' '"0"' $null $script:WorkspaceId 'omit'
    Add-Result 'unauthenticated rejected' 401 $unauthenticated.Status
    Add-Result 'unauthenticated emits no ETag' '' ([string]$unauthenticated.ETag)
    $unknownWorkspace = Invoke-Patch 'service' 'INACTIVE' '"0"' $null 'ws_unknown_product_configuration'
    Add-Result 'unknown Workspace rejected' 403 $unknownWorkspace.Status
    $foreign = Invoke-Patch 'service' 'INACTIVE' '"0"' $null $foreignWorkspace
    Add-Result 'non-member Workspace rejected' 403 $foreign.Status
    Add-Result 'denied Workspace mutation writes nothing' 0 (Get-OverrideCount $foreignWorkspace)

    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Suspended' WHERE MembershipId=N'$membershipId'"
    $suspended = Invoke-Patch 'service' 'INACTIVE' '"0"'
    Add-Result 'suspended membership rejected' 403 $suspended.Status
    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Active' WHERE MembershipId=N'$membershipId'"

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE Capability=N'products.configure'"
    $withoutCapability = Invoke-Patch 'service' 'INACTIVE' '"0"'
    Add-Result 'missing products.configure rejected' 403 $withoutCapability.Status
    Add-Result 'missing products.configure returns ACCESS_DENIED' 'ACCESS_DENIED' ([string]$withoutCapability.Body.code)
    Add-Result 'missing products.configure emits no ETag' '' ([string]$withoutCapability.ETag)
    Add-Result 'missing products.configure writes nothing' 0 (Get-OverrideCount)
    # studio.read is the READ capability and must not authorize the mutation.
    $readOnlyAttempt = Invoke-Patch 'service' 'INACTIVE' '"0"'
    Add-Result 'read capability alone does not authorize the mutation' 403 $readOnlyAttempt.Status
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES(N'$roleId',N'products.configure')"

    # =============================================================================================
    # 2. Transport validation: If-Match and Idempotency-Key are required, and are 400
    # =============================================================================================
    Clear-Configuration $script:WorkspaceId
    $missingIfMatch = Invoke-Patch 'service' 'INACTIVE' 'omit'
    Add-Result 'missing If-Match rejected' 400 $missingIfMatch.Status
    Add-Result 'missing If-Match returns VALIDATION_FAILED' 'VALIDATION_FAILED' ([string]$missingIfMatch.Body.code)
    Add-Result 'missing If-Match names the header' 1 (@($missingIfMatch.Body.fieldErrors.PSObject.Properties.Name | Where-Object { $_ -ceq 'If-Match' }).Count)
    foreach ($malformed in @('3', '""', '"-1"', '"+3"', '" 3"', '"3a"', 'W/3', 'W/""', 'W/"-1"', 'W/"3a"', 'w/"3"', '*')) {
        $result = Invoke-Patch 'service' 'INACTIVE' $malformed
        Add-Result "malformed If-Match $malformed rejected" 400 $result.Status
    }
    Add-Result 'transport rejection writes nothing' 0 (Get-OverrideCount)
    Add-Result 'transport rejection records no command evidence' 0 (Get-CommandAuditCount)
    $missingIdempotency = Invoke-Patch 'service' 'INACTIVE' '"0"' 'omit'
    Add-Result 'missing Idempotency-Key rejected' 400 $missingIdempotency.Status

    # =============================================================================================
    # 3. typeId resolution: contract-global vocabulary, ordinal match
    # =============================================================================================
    $unknownType = Invoke-Patch 'not_a_product_type' 'INACTIVE' '"0"'
    Add-Result 'unknown typeId rejected' 404 $unknownType.Status
    Add-Result 'unknown typeId returns RESOURCE_NOT_FOUND' 'RESOURCE_NOT_FOUND' ([string]$unknownType.Body.code)
    $caseVariant = Invoke-Patch 'Service' 'INACTIVE' '"0"'
    Add-Result 'case-variant typeId rejected' 404 $caseVariant.Status
    Add-Result 'case-variant typeId returns RESOURCE_NOT_FOUND' 'RESOURCE_NOT_FOUND' ([string]$caseVariant.Body.code)
    $upperVariant = Invoke-Patch 'SERVICE' 'INACTIVE' '"0"'
    Add-Result 'upper-case typeId rejected' 404 $upperVariant.Status
    Add-Result 'unknown typeId writes nothing' 0 (Get-OverrideCount)
    # A canonical code carrying no override row is an existing resource, never a 404.
    Add-Result 'unconfigured canonical code precondition' 0 (Get-OverrideCount)
    $unconfigured = Invoke-Patch 'maintenance' 'ACTIVE' '"0"'
    Add-Result 'canonical code with no override row is not 404' 200 $unconfigured.Status

    # =============================================================================================
    # 4. Domain validation is 422
    # =============================================================================================
    Clear-Configuration $script:WorkspaceId
    $auditBeforeDomain = Get-CommandAuditCount
    $idempotencyBeforeDomain = Get-IdempotencyCount
    $missingStatus = Invoke-Patch 'service' 'omit' '"0"'
    Add-Result 'missing status rejected' 422 $missingStatus.Status
    Add-Result 'missing status returns FIELD_VALIDATION_FAILED' 'FIELD_VALIDATION_FAILED' ([string]$missingStatus.Body.code)
    Add-Result 'missing status names the field' 1 (@($missingStatus.Body.fieldErrors.PSObject.Properties.Name | Where-Object { $_ -ceq 'status' }).Count)
    foreach ($invalid in @('active', 'Inactive', 'DISABLED', 'ARCHIVED', '')) {
        $result = Invoke-Patch 'service' $invalid '"0"'
        Add-Result "invalid status '$invalid' rejected" 422 $result.Status
        Add-Result "invalid status '$invalid' uses the field error" 'FIELD_VALIDATION_FAILED' ([string]$result.Body.code)
    }
    $nullStatus = Invoke-Patch 'service' $null '"0"' $null $null $null '{"status":null}'
    Add-Result 'null status rejected' 422 $nullStatus.Status
    $extraProperty = Invoke-Patch 'service' $null '"0"' $null $null $null '{"status":"INACTIVE","label":"x"}'
    Add-Result 'additional property rejected' 422 $extraProperty.Status
    Add-Result 'additional property returns VALIDATION_FAILED' 'VALIDATION_FAILED' ([string]$extraProperty.Body.code)
    $malformedJson = Invoke-Patch 'service' $null '"0"' $null $null $null '{"status":'
    Add-Result 'malformed JSON rejected' 422 $malformedJson.Status
    Add-Result 'domain rejection writes nothing' 0 (Get-OverrideCount)
    Add-Result 'domain rejection records no command evidence' $auditBeforeDomain (Get-CommandAuditCount)
    Add-Result 'domain rejection completes no idempotency' $idempotencyBeforeDomain (Get-IdempotencyCount)
    Add-Result 'domain rejection advances no revision' 0 (Get-Revision)

    # =============================================================================================
    # 5. ACTIVE -> INACTIVE, revision +1
    # =============================================================================================
    Clear-Configuration $script:WorkspaceId
    $auditBefore = Get-CommandAuditCount
    $idempotencyBefore = Get-IdempotencyCount
    $outboxBefore = Get-OutboxCount
    $deactivate = Invoke-Patch 'service' 'INACTIVE' '"0"' 'idem-pcu-deactivate-0001'
    Add-Result 'ACTIVE to INACTIVE succeeds' 200 $deactivate.Status
    Add-Result 'ACTIVE to INACTIVE outcome' 'COMMITTED' ([string]$deactivate.Body.outcome)
    Add-Result 'ACTIVE to INACTIVE advances the revision by one' 1 ([long]$deactivate.Body.version)
    Add-Result 'ACTIVE to INACTIVE ETag reflects the new revision' '"1"' ([string]$deactivate.ETag)
    Assert-StrongETag 'committed transition' ([string]$deactivate.ETag)
    Add-Result 'ACTIVE to INACTIVE result revision matches the envelope' 1 ([long]$deactivate.Body.result.revision)
    Add-Result 'ACTIVE to INACTIVE aggregateId is the canonical typeId' 'service' ([string]$deactivate.Body.aggregateId)
    Add-Result 'ACTIVE to INACTIVE aggregateType' 'PRODUCT_CONFIGURATION_TYPE' ([string]$deactivate.Body.aggregateType)
    Add-Result 'ACTIVE to INACTIVE returns all nine entries' 9 (@($deactivate.Body.result.data.types).Count)
    Add-Result 'ACTIVE to INACTIVE preserves canonical order' $canonicalOrder (Get-Codes $deactivate.Body)
    Add-Result 'ACTIVE to INACTIVE applies the status' 'INACTIVE' (Get-StatusOf $deactivate.Body 'service')
    Add-Result 'ACTIVE to INACTIVE leaves the other eight ACTIVE' 8 (@($deactivate.Body.result.data.types | Where-Object { $_.status -ceq 'ACTIVE' }).Count)
    Add-Result 'ACTIVE to INACTIVE emits no events' 0 (@($deactivate.Body.emittedEventIds).Count)
    # The canonical UtcDateTime wire contract: an ISO-8601 instant with the literal Z designator,
    # emitted through the same shared Products helper every other response uses.
    Assert-True 'occurredAt is a canonical UTC instant' ([string]$deactivate.Body.occurredAt -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$')
    # The frozen envelope, field for field, with no additional business property.
    Add-Result 'response envelope carries exactly the frozen fields' 'aggregateId,aggregateType,auditEvidenceIds,commandId,correlationId,emittedEventIds,occurredAt,outcome,result,version,warnings' (($deactivate.Body.PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'result reuses the existing document shape' 'data,revision' (($deactivate.Body.result.PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'result data carries only the types key' 'types' (($deactivate.Body.result.data.PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'entry carries only code and status' 'code,status' (((@($deactivate.Body.result.data.types)[0]).PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'no override-existence signal is exposed' 0 (@($deactivate.Body.result.data.types | Where-Object { $null -ne $_.overrideId -or $null -ne $_.persisted }).Count)
    Add-Result 'ACTIVE to INACTIVE writes no outbox message' $outboxBefore (Get-OutboxCount)
    Add-Result 'ACTIVE to INACTIVE records one audit record' ($auditBefore + 1) (Get-CommandAuditCount)
    Add-Result 'ACTIVE to INACTIVE records one idempotency record' ($idempotencyBefore + 1) (Get-IdempotencyCount)
    Add-Result 'ACTIVE to INACTIVE cites its audit evidence' 1 (@($deactivate.Body.auditEvidenceIds).Count)
    Add-Result 'audit evidence id matches a persisted audit record' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.AuditRecords WHERE AuditId=N'$([string]@($deactivate.Body.auditEvidenceIds)[0])' AND Operation=N'updateProductConfigurationType' AND Outcome=N'COMMITTED'"))
    Add-Result 'command audit records the prior revision' 0 ([long](Get-Scalar "SELECT PriorVersion FROM products.AuditRecords WHERE AuditId=N'$([string]@($deactivate.Body.auditEvidenceIds)[0])'"))
    Add-Result 'command audit records the new revision' 1 ([long](Get-Scalar "SELECT NewVersion FROM products.AuditRecords WHERE AuditId=N'$([string]@($deactivate.Body.auditEvidenceIds)[0])'"))
    Add-Result 'command audit targets the canonical typeId' 'service' ([string](Get-Scalar "SELECT AggregateId FROM products.AuditRecords WHERE AuditId=N'$([string]@($deactivate.Body.auditEvidenceIds)[0])'"))
    # Model B persistence consequence: INACTIVE persists the deviation row.
    Add-Result 'INACTIVE persists exactly one override row' 1 (Get-OverrideCount)
    Add-Result 'INACTIVE override row carries INACTIVE' 'INACTIVE' (Get-OverrideStatus 'service')
    Add-Result 'document anchor advanced' 1 (Get-Revision)
    Add-Result 'served revision became trusted' 1 (Get-TrustedRevision)
    # The mutation and the public read must agree on the same effective document.
    $readBack = Invoke-Configuration
    Add-Result 'GET agrees on the revision' 1 ([long]$readBack.Body.revision)
    Add-Result 'GET agrees on the ETag' '"1"' ([string]$readBack.ETag)
    Assert-StrongETag 'GET cross-check' ([string]$readBack.ETag)
    Add-Result 'GET agrees on the status' 'INACTIVE' (Get-ReadStatusOf $readBack.Body 'service')

    # =============================================================================================
    # 6. INACTIVE -> ACTIVE, revision +1, override removed internally
    # =============================================================================================
    $auditBefore = Get-CommandAuditCount
    $reactivate = Invoke-Patch 'service' 'ACTIVE' '"1"' 'idem-pcu-reactivate-0001'
    Add-Result 'INACTIVE to ACTIVE succeeds' 200 $reactivate.Status
    Add-Result 'INACTIVE to ACTIVE outcome' 'COMMITTED' ([string]$reactivate.Body.outcome)
    Add-Result 'INACTIVE to ACTIVE advances the revision by one' 2 ([long]$reactivate.Body.version)
    Add-Result 'INACTIVE to ACTIVE ETag reflects the new revision' '"2"' ([string]$reactivate.ETag)
    Assert-StrongETag 'reverse transition' ([string]$reactivate.ETag)
    Add-Result 'INACTIVE to ACTIVE applies the status' 'ACTIVE' (Get-StatusOf $reactivate.Body 'service')
    Add-Result 'INACTIVE to ACTIVE reports all nine ACTIVE' $allActive (Get-Statuses $reactivate.Body)
    Add-Result 'INACTIVE to ACTIVE preserves canonical order' $canonicalOrder (Get-Codes $reactivate.Body)
    # Model B persistence consequence: ACTIVE restores the canonical default internally.
    Add-Result 'ACTIVE removes the override row' 0 (Get-OverrideCount)
    Add-Result 'document anchor advanced again' 2 (Get-Revision)
    Add-Result 'INACTIVE to ACTIVE records one audit record' ($auditBefore + 1) (Get-CommandAuditCount)
    $readBack = Invoke-Configuration
    Add-Result 'GET agrees after reactivation' 2 ([long]$readBack.Body.revision)
    Add-Result 'GET reports the restored default' 'ACTIVE' (Get-ReadStatusOf $readBack.Body 'service')

    # =============================================================================================
    # 7. ACTIVE -> ACTIVE is a successful semantic no-op
    # =============================================================================================
    $auditBefore = Get-CommandAuditCount
    $idempotencyBefore = Get-IdempotencyCount
    $rowsBefore = Get-ConfigurationRowSnapshot
    $noopActive = Invoke-Patch 'service' 'ACTIVE' '"2"' 'idem-pcu-noop-active-0001'
    Add-Result 'ACTIVE to ACTIVE succeeds' 200 $noopActive.Status
    Add-Result 'ACTIVE to ACTIVE is COMMITTED, not REPLAYED' 'COMMITTED' ([string]$noopActive.Body.outcome)
    Add-Result 'ACTIVE to ACTIVE leaves the revision unchanged' 2 ([long]$noopActive.Body.version)
    Add-Result 'ACTIVE to ACTIVE leaves the ETag unchanged' '"2"' ([string]$noopActive.ETag)
    Assert-StrongETag 'semantic no-op' ([string]$noopActive.ETag)
    Add-Result 'ACTIVE to ACTIVE returns the unchanged document' $allActive (Get-Statuses $noopActive.Body)
    Add-Result 'ACTIVE to ACTIVE advances no anchor' 2 (Get-Revision)
    Add-Result 'ACTIVE to ACTIVE writes no configuration row' $rowsBefore (Get-ConfigurationRowSnapshot)
    Add-Result 'ACTIVE to ACTIVE still records command audit' ($auditBefore + 1) (Get-CommandAuditCount)
    Add-Result 'ACTIVE to ACTIVE still completes idempotency' ($idempotencyBefore + 1) (Get-IdempotencyCount)
    Add-Result 'ACTIVE to ACTIVE audit records an unchanged revision' 2 ([long](Get-Scalar "SELECT NewVersion FROM products.AuditRecords WHERE AuditId=N'$([string]@($noopActive.Body.auditEvidenceIds)[0])'"))

    # =============================================================================================
    # 8. INACTIVE -> INACTIVE is a successful semantic no-op
    # =============================================================================================
    $toInactive = Invoke-Patch 'license' 'INACTIVE' '"2"' 'idem-pcu-license-0001'
    Add-Result 'no-op fixture deactivation succeeds' 200 $toInactive.Status
    Add-Result 'no-op fixture revision' 3 ([long]$toInactive.Body.version)
    $rowsBefore = Get-ConfigurationRowSnapshot
    $auditBefore = Get-CommandAuditCount
    $noopInactive = Invoke-Patch 'license' 'INACTIVE' '"3"' 'idem-pcu-noop-inactive-0001'
    Add-Result 'INACTIVE to INACTIVE succeeds' 200 $noopInactive.Status
    Add-Result 'INACTIVE to INACTIVE is COMMITTED' 'COMMITTED' ([string]$noopInactive.Body.outcome)
    Add-Result 'INACTIVE to INACTIVE leaves the revision unchanged' 3 ([long]$noopInactive.Body.version)
    Add-Result 'INACTIVE to INACTIVE leaves the ETag unchanged' '"3"' ([string]$noopInactive.ETag)
    Add-Result 'INACTIVE to INACTIVE still reports INACTIVE' 'INACTIVE' (Get-StatusOf $noopInactive.Body 'license')
    Add-Result 'INACTIVE to INACTIVE writes no configuration row' $rowsBefore (Get-ConfigurationRowSnapshot)
    Add-Result 'INACTIVE to INACTIVE keeps exactly one override row' 1 (Get-OverrideCount)
    Add-Result 'INACTIVE to INACTIVE still records command audit' ($auditBefore + 1) (Get-CommandAuditCount)
    # The strong-validator rule: a byte-identical representation keeps a byte-identical ETag.
    $readNoop = Invoke-Configuration
    Add-Result 'no-op leaves the GET ETag byte-identical' '"3"' ([string]$readNoop.ETag)

    # =============================================================================================
    # 9. Stale If-Match
    # =============================================================================================
    $rowsBefore = Get-ConfigurationRowSnapshot
    $auditBefore = Get-CommandAuditCount
    $idempotencyBefore = Get-IdempotencyCount
    $stale = Invoke-Patch 'service' 'INACTIVE' '"1"' 'idem-pcu-stale-0001'
    Add-Result 'stale If-Match rejected' 412 $stale.Status
    Add-Result 'stale If-Match returns VERSION_CONFLICT' 'VERSION_CONFLICT' ([string]$stale.Body.code)
    Add-Result 'stale If-Match reports the expected version' 1 ([long]$stale.Body.expectedVersion)
    Add-Result 'stale If-Match reports the current version' 3 ([long]$stale.Body.currentVersion)
    Add-Result 'stale If-Match emits no ETag' '' ([string]$stale.ETag)
    Add-Result 'stale If-Match writes nothing' $rowsBefore (Get-ConfigurationRowSnapshot)
    Add-Result 'stale If-Match records no command audit' $auditBefore (Get-CommandAuditCount)
    Add-Result 'stale If-Match completes no idempotency' $idempotencyBefore (Get-IdempotencyCount)
    $ahead = Invoke-Patch 'service' 'INACTIVE' '"9"' 'idem-pcu-ahead-0001'
    Add-Result 'If-Match ahead of the document also conflicts' 412 $ahead.Status
    # W/ is tolerated and stripped on the REQUEST, exactly as every other Products command accepts it.
    # The response validator stays strong: the two directions are deliberately asymmetric.
    $weakStale = Invoke-Patch 'addon' 'INACTIVE' 'W/"1"' 'idem-pcu-weak-stale-0001'
    Add-Result 'weak-prefixed stale If-Match still conflicts' 412 $weakStale.Status
    Add-Result 'weak prefix is stripped before comparison, not bypassed' 1 ([long]$weakStale.Body.expectedVersion)
    $weak = Invoke-Patch 'addon' 'INACTIVE' 'W/"3"' 'idem-pcu-weak-0001'
    Add-Result 'weak-prefixed If-Match accepted' 200 $weak.Status
    Add-Result 'weak-prefixed If-Match commits normally' 4 ([long]$weak.Body.version)
    Add-Result 'weak-prefixed request yields the same expected revision as the strong form' '"4"' ([string]$weak.ETag)
    Assert-StrongETag 'weak-prefixed request response' ([string]$weak.ETag)
    $restoreAddon = Invoke-Patch 'addon' 'ACTIVE' '"4"' 'idem-pcu-weak-restore-0001'
    Add-Result 'weak-prefixed fixture restored' 5 ([long]$restoreAddon.Body.version)

    # =============================================================================================
    # 10. Idempotency
    # =============================================================================================
    Clear-Configuration $script:WorkspaceId
    $first = Invoke-Patch 'subscription' 'INACTIVE' '"0"' 'idem-pcu-replay-0001'
    Add-Result 'idempotency fixture committed' 200 $first.Status
    Add-Result 'idempotency fixture revision' 1 ([long]$first.Body.version)
    $auditBefore = Get-CommandAuditCount
    $idempotencyBefore = Get-IdempotencyCount
    $replay = Invoke-Patch 'subscription' 'INACTIVE' '"0"' 'idem-pcu-replay-0001'
    Add-Result 'replay succeeds' 200 $replay.Status
    Add-Result 'replay reports REPLAYED' 'REPLAYED' ([string]$replay.Body.outcome)
    Add-Result 'replay returns the original revision' 1 ([long]$replay.Body.version)
    Add-Result 'replay returns the original ETag' '"1"' ([string]$replay.ETag)
    Assert-StrongETag 'idempotency replay' ([string]$replay.ETag)
    Add-Result 'replay returns the original commandId' ([string]$first.Body.commandId) ([string]$replay.Body.commandId)
    Add-Result 'replay returns the original occurredAt' ([string]$first.Body.occurredAt) ([string]$replay.Body.occurredAt)
    # The replay round-trips through stored JSON, so the UTC encoding has to survive verbatim.
    Assert-True 'replayed occurredAt is still a canonical UTC instant' ([string]$replay.Body.occurredAt -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$')
    Add-Result 'replay returns the original document' (($first.Body.result | ConvertTo-Json -Compress -Depth 10)) (($replay.Body.result | ConvertTo-Json -Compress -Depth 10))
    Add-Result 'replay writes no new audit record' $auditBefore (Get-CommandAuditCount)
    Add-Result 'replay writes no new idempotency record' $idempotencyBefore (Get-IdempotencyCount)
    Add-Result 'replay advances no revision' 1 (Get-Revision)
    # A stale If-Match must not defeat a replay: the command already committed.
    $replayStale = Invoke-Patch 'subscription' 'INACTIVE' '"9"' 'idem-pcu-replay-0001'
    Add-Result 'replay is answered from stored evidence regardless of If-Match' 200 $replayStale.Status
    Add-Result 'replay under a stale validator still reports REPLAYED' 'REPLAYED' ([string]$replayStale.Body.outcome)
    # A committed mutation stays replayable after the configuration has moved on.
    $moveOn = Invoke-Patch 'package' 'INACTIVE' '"1"' 'idem-pcu-moveon-0001'
    Add-Result 'configuration moved on' 2 ([long]$moveOn.Body.version)
    $replayAfterMove = Invoke-Patch 'subscription' 'INACTIVE' '"0"' 'idem-pcu-replay-0001'
    Add-Result 'replay after the configuration changed still succeeds' 200 $replayAfterMove.Status
    Add-Result 'replay after the configuration changed returns the original revision' 1 ([long]$replayAfterMove.Body.version)
    Add-Result 'replay after the configuration changed returns the original ETag' '"1"' ([string]$replayAfterMove.ETag)
    Add-Result 'replay after the configuration changed returns the original document' (($first.Body.result | ConvertTo-Json -Compress -Depth 10)) (($replayAfterMove.Body.result | ConvertTo-Json -Compress -Depth 10))

    # Same key, different request fingerprint.
    $rowsBefore = Get-ConfigurationRowSnapshot
    $reused = Invoke-Patch 'subscription' 'ACTIVE' '"2"' 'idem-pcu-replay-0001'
    Add-Result 'same key with a different request conflicts' 409 $reused.Status
    Add-Result 'same key with a different request returns IDEMPOTENCY_KEY_REUSED' 'IDEMPOTENCY_KEY_REUSED' ([string]$reused.Body.code)
    Add-Result 'idempotency conflict emits no ETag' '' ([string]$reused.ETag)
    Add-Result 'idempotency conflict writes nothing' $rowsBefore (Get-ConfigurationRowSnapshot)
    # The idempotency scope is per target, so the same key against another typeId is a distinct command.
    $otherTarget = Invoke-Patch 'implementation' 'INACTIVE' '"2"' 'idem-pcu-replay-0001'
    Add-Result 'same key against another typeId is a distinct command' 200 $otherTarget.Status
    Add-Result 'distinct command commits its own revision' 3 ([long]$otherTarget.Body.version)

    # =============================================================================================
    # 11. Corrupt configuration fails closed, repairs nothing
    # =============================================================================================
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'my_custom_type' 'ACTIVE'
    Set-Revision $script:WorkspaceId 1
    $rowsBefore = Get-ConfigurationRowSnapshot
    $auditBefore = Get-CommandAuditCount
    $idempotencyBefore = Get-IdempotencyCount
    $corruptCode = Invoke-Patch 'service' 'INACTIVE' '"1"' 'idem-pcu-corrupt-0001'
    Add-Result 'unknown persisted code fails the mutation closed' 500 $corruptCode.Status
    Add-Result 'unknown persisted code returns INTERNAL_ERROR' 'INTERNAL_ERROR' ([string]$corruptCode.Body.code)
    Add-Result 'unknown persisted code returns no document' '' ([string]$corruptCode.Body.result)
    Add-Result 'unknown persisted code emits no ETag' '' ([string]$corruptCode.ETag)
    Add-Result 'corrupt mutation writes nothing' $rowsBefore (Get-ConfigurationRowSnapshot)
    Add-Result 'corrupt mutation repairs nothing' 'ACTIVE' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'my_custom_type'"))
    Add-Result 'corrupt mutation records no command audit' $auditBefore (Get-CommandAuditCount)
    Add-Result 'corrupt mutation completes no idempotency' $idempotencyBefore (Get-IdempotencyCount)
    Clear-Configuration $script:WorkspaceId

    Set-Override $script:WorkspaceId 'service' 'DISABLED'
    Set-Revision $script:WorkspaceId 1
    $corruptStatus = Invoke-Patch 'service' 'ACTIVE' '"1"' 'idem-pcu-corrupt-0002'
    Add-Result 'invalid persisted status fails the mutation closed' 500 $corruptStatus.Status
    Add-Result 'invalid persisted status is not normalised' 'DISABLED' (Get-OverrideStatus 'service')
    Add-Result 'invalid persisted status advances no revision' 1 (Get-Revision)
    Clear-Configuration $script:WorkspaceId

    Set-Override $script:WorkspaceId 'Service' 'INACTIVE'
    Set-Revision $script:WorkspaceId 1
    $corruptCase = Invoke-Patch 'service' 'INACTIVE' '"1"' 'idem-pcu-corrupt-0003'
    Add-Result 'case-variant persisted code fails the mutation closed' 500 $corruptCase.Status
    Add-Result 'case-variant persisted code is not rewritten' 'INACTIVE' (Get-OverrideStatus 'Service')
    Add-Result 'case-variant persisted code creates no canonical row' '' (Get-OverrideStatus 'service')
    Clear-Configuration $script:WorkspaceId

    Set-Revision $script:WorkspaceId 2
    Invoke-SqlNonQuery "ALTER TABLE products.ProductConfigurationDocuments NOCHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision; UPDATE products.ProductConfigurationDocuments SET Revision=-2 WHERE WorkspaceId=N'$script:WorkspaceId';"
    $negative = Invoke-Patch 'service' 'INACTIVE' '"0"' 'idem-pcu-corrupt-0004'
    Add-Result 'negative revision fails the mutation closed' 500 $negative.Status
    Add-Result 'negative revision is not repaired' -2 (Get-Revision)
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'; ALTER TABLE products.ProductConfigurationDocuments WITH CHECK CHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision;"
    Clear-Configuration $script:WorkspaceId

    # The trusted-revision rollback rule governs the mutation exactly as it governs the read.
    Set-Revision $script:WorkspaceId 5
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTrustedRevisions(WorkspaceId,GreatestTrustedRevision) VALUES(N'$script:WorkspaceId',5)"
    Invoke-SqlNonQuery "UPDATE products.ProductConfigurationDocuments SET Revision=3 WHERE WorkspaceId=N'$script:WorkspaceId'"
    $rolledBack = Invoke-Patch 'service' 'INACTIVE' '"3"' 'idem-pcu-rollback-0001'
    Add-Result 'rollback below trusted revision fails the mutation closed' 500 $rolledBack.Status
    Add-Result 'rollback leaves the anchor untouched' 3 (Get-Revision)
    Add-Result 'rollback leaves the trusted mark intact' 5 (Get-TrustedRevision)
    Clear-Configuration $script:WorkspaceId

    # =============================================================================================
    # 12. Atomicity: a persistence failure commits nothing
    # =============================================================================================
    Set-Revision $script:WorkspaceId 1
    $auditBefore = Get-CommandAuditCount
    $idempotencyBefore = Get-IdempotencyCount
    Invoke-SqlNonQuery "CREATE TRIGGER products.TR_VerifyConfigAuditFailure ON products.AuditRecords INSTEAD OF INSERT AS THROW 51000, 'forced audit persistence failure', 1"
    try {
        $atomic = Invoke-Patch 'service' 'INACTIVE' '"1"' 'idem-pcu-atomic-0001'
        Add-Result 'audit persistence failure returns no success' 500 $atomic.Status
        Add-Result 'audit persistence failure emits no ETag' '' ([string]$atomic.ETag)
        Add-Result 'audit persistence failure advances no revision' 1 (Get-Revision)
        Add-Result 'audit persistence failure writes no override row' 0 (Get-OverrideCount)
        Add-Result 'audit persistence failure completes no idempotency' $idempotencyBefore (Get-IdempotencyCount)
        Add-Result 'audit persistence failure rolls back the trust raise' 0 (Get-TrustedRevision)
        Add-Result 'audit persistence failure leaves no audit record' $auditBefore (Get-CommandAuditCount)
    }
    finally { Invoke-SqlNonQuery 'DROP TRIGGER products.TR_VerifyConfigAuditFailure' }
    # The same key is still usable, because nothing was committed under it.
    $retryAfterFailure = Invoke-Patch 'service' 'INACTIVE' '"1"' 'idem-pcu-atomic-0001'
    Add-Result 'the key is reusable after a fully rolled back attempt' 200 $retryAfterFailure.Status
    Add-Result 'the retry commits the intended transition' 2 ([long]$retryAfterFailure.Body.version)
    Clear-Configuration $script:WorkspaceId

    # =============================================================================================
    # 13. Workspace isolation
    # =============================================================================================
    Clear-Configuration $script:WorkspaceId
    Clear-Configuration $foreignWorkspace
    Set-Override $foreignWorkspace 'service' 'INACTIVE'
    Set-Revision $foreignWorkspace 7
    $isolated = Invoke-Patch 'service' 'INACTIVE' '"0"' 'idem-pcu-isolation-0001'
    Add-Result 'foreign revision does not govern this If-Match' 200 $isolated.Status
    Add-Result 'this Workspace commits its own revision' 1 ([long]$isolated.Body.version)
    Add-Result 'foreign Workspace anchor unchanged' 7 (Get-Revision $foreignWorkspace)
    Add-Result 'foreign Workspace override unchanged' 'INACTIVE' (Get-OverrideStatus 'service' $foreignWorkspace)
    Add-Result 'foreign Workspace override count unchanged' 1 (Get-OverrideCount $foreignWorkspace)
    # The same idempotency key in a different Workspace is a different command, and it is refused
    # here only because membership is refused - never by leaking the other Workspace's state.
    $crossWorkspace = Invoke-Patch 'service' 'ACTIVE' '"7"' 'idem-pcu-isolation-0001' $foreignWorkspace
    Add-Result 'foreign Workspace mutation denied' 403 $crossWorkspace.Status
    Add-Result 'foreign Workspace state survives the denied mutation' 'INACTIVE' (Get-OverrideStatus 'service' $foreignWorkspace)
    Add-Result 'foreign Workspace revision survives the denied mutation' 7 (Get-Revision $foreignWorkspace)
    Clear-Configuration $foreignWorkspace
    Clear-Configuration $script:WorkspaceId

    # =============================================================================================
    # 14. Product-command linearization, both proven orderings, through the real endpoint
    # =============================================================================================

    # -- L1: Product command reads ACTIVE first -> mutation waits -> command commits -> mutation runs
    Clear-Configuration $script:WorkspaceId
    Set-Revision $script:WorkspaceId 1
    $freeze = Start-CommandFreeze
    $mutationPending = $null
    try {
        $createRequest = New-ApiRequest 'POST' '/products' $script:Token $script:WorkspaceId ('idem-pcu-lin-' + [Guid]::NewGuid().ToString('N')) 'omit' ((New-ProductBody 'PCU-LIN-L1' 'service') | ConvertTo-Json -Compress -Depth 8) $script:BaseUrl
        $createPending = Start-Request $createRequest
        Start-Sleep -Seconds 4
        Assert-True 'L1 Product command is in flight and uncommitted' (-not $createPending.Task.IsCompleted)
        Add-Result 'L1 no Product committed while the command is frozen' 0 ([int](Get-Scalar "SELECT COUNT(*) FROM products.Products WHERE NormalizedSku=N'PCU-LIN-L1'"))
        Add-Result 'L1 configuration is ACTIVE at the moment of the command read' 0 (Get-OverrideCount)

        # The command has already read the configuration as ACTIVE and holds its serializable
        # transaction open. The mutation must now wait for it rather than commit inside its window.
        $patchRequest = New-ApiRequest 'PATCH' '/products/configuration/types/service' $script:Token $script:WorkspaceId 'idem-pcu-lin-l1-mutation' '"1"' (@{ status = 'INACTIVE' } | ConvertTo-Json -Compress) $script:BaseUrl
        $mutationPending = Start-Request $patchRequest
        Start-Sleep -Seconds 5
        Assert-True 'L1 mutation blocks on the in-flight Product command' (-not $mutationPending.Task.IsCompleted)
        Add-Result 'L1 mutation has committed no override row while blocked' 0 (Get-OverrideCount)
        Add-Result 'L1 mutation has advanced no revision while blocked' 1 (Get-RevisionNoWait)
        $blocking = @(Invoke-Sql 'SELECT session_id, blocking_session_id, wait_type FROM sys.dm_exec_requests WHERE blocking_session_id <> 0')
        Assert-True 'L1 a blocked session is observable in sys.dm_exec_requests' ($blocking.Count -ge 1)

        $freeze.Transaction.Rollback()
        $freeze.Connection.Dispose()
        $freeze = $null
        $createResult = Complete-PendingRequest $createPending
        $productCommittedAt = [DateTime]::UtcNow
        Add-Result 'L1 Product command commits under its ACTIVE snapshot' 201 $createResult.Status
        Add-Result 'L1 Product persisted with the type it read' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.Products WHERE NormalizedSku=N'PCU-LIN-L1'"))

        $mutationResult = Complete-PendingRequest $mutationPending
        $mutationCompletedAt = [DateTime]::UtcNow
        $mutationPending = $null
        Assert-True 'L1 mutation completed only after the Product command committed' ($mutationCompletedAt -ge $productCommittedAt)
        Add-Result 'L1 mutation then proceeds' 200 $mutationResult.Status
        Add-Result 'L1 mutation commits the advanced revision' 2 ([long]$mutationResult.Body.version)
        Add-Result 'L1 mutation ETag reflects the advanced revision' '"2"' ([string]$mutationResult.ETag)
        Add-Result 'L1 final configuration is INACTIVE' 'INACTIVE' (Get-OverrideStatus 'service')
        # The forbidden interleaving would have produced 422 here: the command would have observed the
        # mutation's INACTIVE. A 201 proves the mutation could not commit inside the command window.
        Add-Result 'L1 forbidden interleaving not observed' 201 $createResult.Status
        Assert-True 'L1 trust relation remains valid (revision >= trusted)' ((Get-Revision) -ge (Get-TrustedRevision))
    }
    finally {
        if ($null -ne $mutationPending) { try { $null = Complete-PendingRequest $mutationPending } catch { } }
        if ($null -ne $freeze) { try { $freeze.Transaction.Rollback() } catch { }; $freeze.Connection.Dispose() }
    }

    # -- L2: mutation commits INACTIVE first -> a later Product command newly selecting it is rejected
    Clear-Configuration $script:WorkspaceId
    $mutationFirst = Invoke-Patch 'service' 'INACTIVE' '"0"' 'idem-pcu-lin-l2-mutation'
    Add-Result 'L2 mutation commits INACTIVE first' 200 $mutationFirst.Status
    Add-Result 'L2 mutation revision' 1 ([long]$mutationFirst.Body.version)
    $laterCreate = New-Product 'PCU-LIN-L2' 'service'
    Add-Result 'L2 later createProduct newly selecting the type is rejected' 422 $laterCreate.Status
    Add-Result 'L2 rejection uses the admitted field error' 'FIELD_VALIDATION_FAILED' ([string]$laterCreate.Body.code)
    Add-Result 'L2 no Product written' 0 ([int](Get-Scalar "SELECT COUNT(*) FROM products.Products WHERE NormalizedSku=N'PCU-LIN-L2'"))
    # A different, still-ACTIVE type is unaffected.
    $unaffected = New-Product 'PCU-LIN-L2B' 'package'
    Add-Result 'L2 an ACTIVE type is unaffected' 201 $unaffected.Status
    # replaceProduct changing TO the inactive type is rejected; preserving it is allowed.
    $replaceTarget = [string]$unaffected.Body.result.product.id
    $replaceVersion = [string]$unaffected.Body.version
    $replaceRequest = New-ApiRequest 'PUT' "/products/$replaceTarget" $script:Token $script:WorkspaceId ('idem-pcu-l2-replace-' + [Guid]::NewGuid().ToString('N')) ('"' + $replaceVersion + '"') ((New-ProductBody 'PCU-LIN-L2B' 'service') | ConvertTo-Json -Compress -Depth 8) $script:BaseUrl
    $replaceResult = Invoke-Request $replaceRequest
    Add-Result 'L2 replaceProduct changing to the inactive type is rejected' 422 $replaceResult.Status
    # Reactivation through the mutation restores selectability.
    $reactivateForCreate = Invoke-Patch 'service' 'ACTIVE' '"1"' 'idem-pcu-lin-l2-restore'
    Add-Result 'L2 reactivation commits' 200 $reactivateForCreate.Status
    $afterReactivation = New-Product 'PCU-LIN-L2C' 'service'
    Add-Result 'L2 createProduct succeeds again after reactivation' 201 $afterReactivation.Status
    Clear-Configuration $script:WorkspaceId

    # =============================================================================================
    # 15. createProductConfigurationType and deleteProductConfigurationType remain BLOCKED
    # =============================================================================================
    $createBlocked = Invoke-Request (New-ApiRequest 'POST' '/products/configuration/types' $script:Token $script:WorkspaceId 'idem-pcu-blocked-0001' 'omit' '{"code":"service","status":"ACTIVE"}' $script:BaseUrl)
    Assert-True 'createProductConfigurationType is unavailable' ($createBlocked.Status -eq 404 -or $createBlocked.Status -eq 405)
    Assert-True 'createProductConfigurationType never succeeds' ($createBlocked.Status -lt 200 -or $createBlocked.Status -ge 300)
    $deleteBlocked = Invoke-Request (New-ApiRequest 'DELETE' '/products/configuration/types/service' $script:Token $script:WorkspaceId 'idem-pcu-blocked-0002' '"0"' 'omit' $script:BaseUrl)
    Assert-True 'deleteProductConfigurationType is unavailable' ($deleteBlocked.Status -eq 404 -or $deleteBlocked.Status -eq 405)
    Assert-True 'deleteProductConfigurationType never succeeds' ($deleteBlocked.Status -lt 200 -or $deleteBlocked.Status -ge 300)
    Add-Result 'blocked operations wrote no configuration state' 0 (Get-OverrideCount)

    # =============================================================================================
    # 16. Structural and scope assertions
    # =============================================================================================
    $handlerSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/UpdateProductConfigurationType/Handler.cs')
    $listHandlerSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/ListProductConfigurationTypes/Handler.cs')
    $persistenceSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Infrastructure/Persistence/EfProductsPersistence.cs')
    $endpointSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Contracts/ProductsEndpoints.cs')
    $dbContextSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Infrastructure/Persistence/ProductsDbContext.cs')
    Assert-True 'the command does not call the public configuration read handler' ($handlerSource -notmatch 'ListProductConfigurationTypes')
    Assert-True 'the command does not require the read capability' ($handlerSource -notmatch 'StudioRead')
    Assert-True 'the command requires products.configure' ($handlerSource -match 'ProductConfigurationCapabilities\.Configure')
    Assert-True 'the command cannot reach transport state' ($handlerSource -notmatch 'HttpContext|Request\.Headers|Headers\[')
    Assert-True 'the command reads no foreign DbContext' ($handlerSource -notmatch '\b(Workspace|AccessControl|IdentityAuth)DbContext\b')
    Assert-True 'the trusted Workspace comes from the authorization context' ($handlerSource -match 'context\.WorkspaceId')
    Assert-True 'the command reuses the shared catalog projection' ($handlerSource -match 'ProductConfigurationCatalog\.Project')
    Assert-True 'the command reuses the shared canonical vocabulary' ($handlerSource -match 'ProductConfigurationCatalog\.IsCanonicalTypeCode')
    Assert-True 'the mutation reads inside the command transaction under an update lock' ($persistenceSource -match 'ProductConfigurationDocuments\] WITH \(UPDLOCK, HOLDLOCK\)')
    Assert-True 'the override read is update-locked too' ($persistenceSource -match 'ProductConfigurationTypeOverrides\] WITH \(UPDLOCK, HOLDLOCK\)')
    Assert-True 'the mutation opens exactly one serializable transaction' ([regex]::Matches($handlerSource, 'BeginSerializableAsync').Count -eq 1)
    Assert-True 'the mutation saves changes exactly once' ([regex]::Matches($handlerSource, 'SaveChangesAsync').Count -eq 1)
    Assert-True 'the mutation emits no outbox message' ($handlerSource -notmatch 'AddOutbox')
    # The anchor moves only through the domain Advance operation, and exactly once, so no persistence
    # path can assign an arbitrary revision or skip the concurrency token guarding it.
    Assert-True 'the anchor advances through the domain operation' ($persistenceSource -match 'anchor\.Advance\(\)')
    Assert-True 'exactly one anchor advance path exists' ([regex]::Matches($persistenceSource, '\.Advance\(\)').Count -eq 1)
    Assert-True 'no persistence path assigns a revision directly' ($persistenceSource -notmatch 'Revision = |Revision=')
    Assert-True 'the revision is a concurrency token' ($dbContextSource -match 'item\.Revision\)\.IsConcurrencyToken\(\)')
    Assert-True 'the list read is unchanged by this operation' ($listHandlerSource -notmatch 'products\.configure|UpdateProductConfigurationType')
    Add-Result 'exactly one configuration mutation route' 1 ([regex]::Matches($endpointSource, 'MapPatch\(endpoints, "/products/configuration/types/\{typeId\}"').Count)
    Add-Result 'no configuration create route' 0 ([regex]::Matches($endpointSource, 'MapPost\(endpoints, "/products/configuration').Count)
    Add-Result 'no configuration delete route' 0 ([regex]::Matches($endpointSource, 'MapDelete').Count)
    # The global Products If-Match helper is untouched: one implementation, still 400 for headers.
    Add-Result 'one shared If-Match parser' 1 ([regex]::Matches($endpointSource, 'private static bool TryExpectedVersion').Count)
    Assert-True 'header validation still reports 400' ($endpointSource -match 'ProductErrors\.Validation\(fields, StatusCodes\.Status400BadRequest\)')
    Assert-True 'the body reader still defaults to the established 400' ($endpointSource -match 'int errorStatus = StatusCodes\.Status400BadRequest')
}
finally {
    Stop-ApiHost
    if (-not $KeepDatabase) {
        try { Invoke-SqlNonQuery "IF DB_ID(N'$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END" 'master' } catch { }
        Remove-Item -LiteralPath $logRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "Verification logs retained at $logRoot"
    }
}

$script:Results | ForEach-Object { Write-Host $_ }
Write-Host "RESULT | passed=$script:Passed failed=$script:Failed"
if ($script:Failed -ne 0) { throw "updateProductConfigurationType verification failed: $script:Failed check(s)." }
