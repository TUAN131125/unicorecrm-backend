<#
.SYNOPSIS
    Verifies GET /products/configuration/types against an isolated SQL database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5607,
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
$password = 'Product-Config-Verify!2026'
$email = 'admin@unicorecrm.local'
$canonicalOrder = 'physical_product,service,subscription,package,implementation,support_sla,addon,license,maintenance'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-product-configuration-' + [Guid]::NewGuid().ToString('N'))
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

function Invoke-Api(
    [string] $Method,
    [string] $Path,
    [string] $Token,
    [string] $WorkspaceId,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    $script:Counter++
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') {
        if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-product-config-' + $script:Counter.ToString('d6') }
        $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId)
    }
    if ($CorrelationId -ne 'omit') {
        if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-product-config-' + $script:Counter.ToString('d6') }
        $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId)
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(90)
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { } }
        $etag = $null
        if ($null -ne $response.Headers.ETag) { $etag = $response.Headers.ETag.ToString() }
        return [pscustomobject]@{ Status=[int]$response.StatusCode; Raw=$raw; Body=$payload; ETag=$etag }
    }
    finally { $request.Dispose(); $client.Dispose() }
}

function Invoke-Configuration(
    [string] $Workspace = $script:WorkspaceId,
    [string] $Token = $script:Token
) {
    if ($Token -eq 'omit') { $Token = $null }
    return Invoke-Api 'GET' '/products/configuration/types' $Token $Workspace $null $null
}

function Get-ReadEvidenceCount {
    return [long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.AuditRecords WHERE Operation=N'listProductConfigurationTypes'")
}

function Get-ConfigurationRowSnapshot {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM products.ProductConfigurationDocuments'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM products.ProductConfigurationTypeOverrides'),
        (Get-Scalar 'SELECT COALESCE(SUM(Revision),0) FROM products.ProductConfigurationDocuments')
    ))
}

function Get-Codes([object] $Body) {
    return [string]::Join(',', @($Body.data.types | ForEach-Object { [string]$_.code }))
}

function Get-Statuses([object] $Body) {
    return [string]::Join(',', @($Body.data.types | ForEach-Object { [string]$_.status }))
}

function Get-StatusOf([object] $Body, [string] $Code) {
    return [string](@($Body.data.types | Where-Object { $_.code -ceq $Code })[0].status)
}

function Set-Revision([string] $Workspace, [long] $Revision) {
    Invoke-SqlNonQuery "IF EXISTS(SELECT 1 FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace') UPDATE products.ProductConfigurationDocuments SET Revision=$Revision WHERE WorkspaceId=N'$Workspace' ELSE INSERT INTO products.ProductConfigurationDocuments(WorkspaceId,Revision) VALUES(N'$Workspace',$Revision);"
}

function Set-Override([string] $Workspace, [string] $Code, [string] $Status) {
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$Workspace',N'$Code',N'$Status');"
}

function Clear-Configuration([string] $Workspace) {
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace'; DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace';"
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
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'studio.read'
    # Ordinary Product capabilities are granted so the regression below exercises a real Product
    # mutation rather than an authorization failure that would prove nothing about revision isolation.
    $env:AccessControl__DevelopmentBootstrap__Capabilities__1 = 'products.create'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__2 = 'products.read'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $stdout = Join-Path $logRoot 'host.out.log'
    $stderr = Join-Path $logRoot 'host.err.log'
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Api 'GET' '/auth/session' $null $null 'omit' 'omit'
            if ($probe.Status -eq 401) { $ready=$true; break }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.AggregateException] { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }

    $signInRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/auth/sessions")
    $signInRequest.Headers.TryAddWithoutValidation('X-Request-Id','req-product-config-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-product-config-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('Idempotency-Key','idem-product-config-signin') | Out-Null
    $signInRequest.Content = [System.Net.Http.StringContent]::new((@{email=$email;password=$password}|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json')
    $signInClient = [System.Net.Http.HttpClient]::new()
    $signInResponse = $signInClient.SendAsync($signInRequest).GetAwaiter().GetResult()
    $signInBody = $signInResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Add-Result 'authentication fixture sign-in' 200 ([int]$signInResponse.StatusCode)
    $script:Token = [string]$signInBody.accessToken
    $signInRequest.Dispose();$signInResponse.Dispose();$signInClient.Dispose()

    $script:WorkspaceId = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo'")
    $foreignWorkspace = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo-isolated'")
    $accountId = [string](Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail=N'$($email.ToUpperInvariant())'")
    $membershipId = [string](Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId=N'$script:WorkspaceId' AND AccountId=N'$accountId'")
    $roleId = [string](Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.WorkspaceId=N'$script:WorkspaceId' AND a.MembershipId=N'$membershipId' AND c.Capability=N'studio.read'")
    Assert-True 'trusted studio.read fixture exists' (-not [string]::IsNullOrWhiteSpace($roleId))
    Assert-True 'foreign Workspace fixture exists' (-not [string]::IsNullOrWhiteSpace($foreignWorkspace))

    # -- 35.12 / 35.13 authorization and Workspace trust ------------------------------------------
    $unauthenticated = Invoke-Configuration $script:WorkspaceId 'omit'
    Add-Result 'unauthenticated rejected' 401 $unauthenticated.Status
    $unknownWorkspace = Invoke-Configuration 'ws_unknown_product_configuration'
    Add-Result 'unknown Workspace rejected' 403 $unknownWorkspace.Status
    $foreign = Invoke-Configuration $foreignWorkspace
    Add-Result 'non-member Workspace rejected' 403 $foreign.Status
    $missingWorkspaceHeader = Invoke-Api 'GET' '/products/configuration/types' $script:Token $null $null $null
    Assert-True 'missing Workspace context denied' ($missingWorkspaceHeader.Status -eq 400 -or $missingWorkspaceHeader.Status -eq 403)

    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Suspended' WHERE MembershipId=N'$membershipId'"
    $suspended = Invoke-Configuration
    Add-Result 'suspended membership rejected' 403 $suspended.Status
    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Active' WHERE MembershipId=N'$membershipId'"

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE Capability=N'studio.read'"
    $withoutCapability = Invoke-Configuration
    Add-Result 'missing studio.read rejected' 403 $withoutCapability.Status
    Add-Result 'missing studio.read emits no ETag' '' ([string]$withoutCapability.ETag)
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES(N'$roleId',N'studio.read')"
    $restored = Invoke-Configuration
    Add-Result 'capability restored' 200 $restored.Status

    # -- 35.1 sparse empty state ------------------------------------------------------------------
    Clear-Configuration $script:WorkspaceId
    $rowsBefore = Get-ConfigurationRowSnapshot
    $evidenceBefore = Get-ReadEvidenceCount
    $empty = Invoke-Configuration
    Add-Result 'no persisted configuration succeeds' 200 $empty.Status
    Add-Result 'no persisted configuration revision' 0 ([long]$empty.Body.revision)
    Add-Result 'no persisted configuration ETag' '"0"' ([string]$empty.ETag)
    Add-Result 'no persisted configuration entry count' 9 (@($empty.Body.data.types).Count)
    Add-Result 'no persisted configuration canonical order' $canonicalOrder (Get-Codes $empty.Body)
    Add-Result 'no persisted configuration all ACTIVE' 'ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE' (Get-Statuses $empty.Body)
    Add-Result 'GET writes no configuration rows' $rowsBefore (Get-ConfigurationRowSnapshot)
    Add-Result 'GET records read evidence' ($evidenceBefore + 1) (Get-ReadEvidenceCount)
    Add-Result 'data carries only the types key' 'types' (($empty.Body.data.PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'row carries only code and status' 'code,status' (((@($empty.Body.data.types)[0]).PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'envelope carries only revision and data' 'data,revision' (($empty.Body.PSObject.Properties.Name | Sort-Object) -join ',')
    $repeat = Invoke-Configuration
    Add-Result 'unchanged document repeats the same ETag' ([string]$empty.ETag) ([string]$repeat.ETag)
    Add-Result 'unchanged document repeats the same body' ($empty.Body | ConvertTo-Json -Compress -Depth 10) ($repeat.Body | ConvertTo-Json -Compress -Depth 10)

    # -- 35.2 explicit INACTIVE override ----------------------------------------------------------
    Set-Override $script:WorkspaceId 'service' 'INACTIVE'
    Set-Revision $script:WorkspaceId 1
    $inactive = Invoke-Configuration
    Add-Result 'INACTIVE override succeeds' 200 $inactive.Status
    Add-Result 'INACTIVE override entry count' 9 (@($inactive.Body.data.types).Count)
    Add-Result 'INACTIVE override canonical order preserved' $canonicalOrder (Get-Codes $inactive.Body)
    Add-Result 'INACTIVE override applied' 'INACTIVE' (Get-StatusOf $inactive.Body 'service')
    Add-Result 'INACTIVE override leaves others ACTIVE' 8 (@($inactive.Body.data.types | Where-Object { $_.status -ceq 'ACTIVE' }).Count)
    Add-Result 'INACTIVE override revision' 1 ([long]$inactive.Body.revision)
    Add-Result 'INACTIVE override ETag' '"1"' ([string]$inactive.ETag)

    # -- 35.3 explicit ACTIVE override is representationally identical to a missing override -------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'service' 'ACTIVE'
    $explicitActive = Invoke-Configuration
    Add-Result 'explicit ACTIVE override succeeds' 200 $explicitActive.Status
    Add-Result 'explicit ACTIVE override entry count' 9 (@($explicitActive.Body.data.types).Count)
    Add-Result 'explicit ACTIVE matches missing override representation' ($empty.Body | ConvertTo-Json -Compress -Depth 10) ($explicitActive.Body | ConvertTo-Json -Compress -Depth 10)
    Add-Result 'explicit ACTIVE emits no duplicate entry' 1 (@($explicitActive.Body.data.types | Where-Object { $_.code -ceq 'service' }).Count)

    # -- 35.4 multiple sparse overrides -----------------------------------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'service' 'INACTIVE'
    Set-Override $script:WorkspaceId 'license' 'INACTIVE'
    Set-Override $script:WorkspaceId 'addon' 'ACTIVE'
    Set-Revision $script:WorkspaceId 3
    $merged = Invoke-Configuration
    Add-Result 'sparse merge succeeds' 200 $merged.Status
    Add-Result 'sparse merge canonical order preserved' $canonicalOrder (Get-Codes $merged.Body)
    Add-Result 'sparse merge statuses' 'ACTIVE,INACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,INACTIVE,ACTIVE' (Get-Statuses $merged.Body)
    Add-Result 'sparse merge revision' 3 ([long]$merged.Body.revision)
    Add-Result 'sparse merge ETag' '"3"' ([string]$merged.ETag)
    Add-Result 'revision and ETag describe one snapshot' ('"' + [string]$merged.Body.revision + '"') ([string]$merged.ETag)

    # -- 25 revision survives deletion of every override ------------------------------------------
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId'"
    $afterDelete = Invoke-Configuration
    Add-Result 'revision survives zero override rows' 3 ([long]$afterDelete.Body.revision)
    Add-Result 'zero override rows report all ACTIVE' 'ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE' (Get-Statuses $afterDelete.Body)
    Add-Result 'revision does not reset to zero' '"3"' ([string]$afterDelete.ETag)

    # -- 35.5 cross-Workspace isolation -----------------------------------------------------------
    Clear-Configuration $script:WorkspaceId
    Clear-Configuration $foreignWorkspace
    Set-Override $foreignWorkspace 'service' 'INACTIVE'
    Set-Revision $foreignWorkspace 7
    $isolated = Invoke-Configuration
    Add-Result 'foreign override does not leak status' 'ACTIVE' (Get-StatusOf $isolated.Body 'service')
    Add-Result 'foreign revision does not leak' 0 ([long]$isolated.Body.revision)
    Add-Result 'foreign revision does not leak into ETag' '"0"' ([string]$isolated.ETag)
    Clear-Configuration $foreignWorkspace

    # -- 18 ordinary Product mutations do not touch configuration revision ------------------------
    Set-Revision $script:WorkspaceId 4
    $revisionBeforeProduct = [long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'")
    $createRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/products")
    $createRequest.Headers.TryAddWithoutValidation('X-Request-Id','req-product-config-createproduct') | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-product-config-createproduct') | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('Idempotency-Key','idem-product-config-createproduct') | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('Authorization',"Bearer $script:Token") | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('X-Workspace-Id',$script:WorkspaceId) | Out-Null
    $createBody = @{ sku='PC-VERIFY-0001'; name='Product Configuration Verification'; type='service'; status='ACTIVE'; category='Software License'; unit='item'; unitPrice=@{amount='10.00';currency='USD'}; taxRate='0'; taxMode='none'; billingCycle='one_time'; isSubscription=$false; isRenewable=$false; tags=@() }
    $createRequest.Content = [System.Net.Http.StringContent]::new(($createBody|ConvertTo-Json -Compress -Depth 6),[Text.Encoding]::UTF8,'application/json')
    $createClient = [System.Net.Http.HttpClient]::new()
    $createResponse = $createClient.SendAsync($createRequest).GetAwaiter().GetResult()
    $createStatus = [int]$createResponse.StatusCode
    $createRequest.Dispose();$createResponse.Dispose();$createClient.Dispose()
    Add-Result 'ordinary Product create still succeeds' 201 $createStatus
    Add-Result 'ordinary Product create is unaffected by an INACTIVE overlay' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.Products WHERE NormalizedSku=N'PC-VERIFY-0001'"))
    Add-Result 'ordinary Product mutation leaves configuration revision unchanged' $revisionBeforeProduct ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    $afterProduct = Invoke-Configuration
    Add-Result 'ordinary Product mutation leaves configuration ETag unchanged' '"4"' ([string]$afterProduct.ETag)

    # -- 35.6 / 35.7 / 35.8 corrupt state fails closed --------------------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'my_custom_type' 'ACTIVE'
    $evidenceBeforeCorrupt = Get-ReadEvidenceCount
    $rowsBeforeCorrupt = Get-ConfigurationRowSnapshot
    $unknownCode = Invoke-Configuration
    Add-Result 'unknown ProductType code fails closed' 500 $unknownCode.Status
    Add-Result 'unknown ProductType code returns INTERNAL_ERROR' 'INTERNAL_ERROR' ([string]$unknownCode.Body.code)
    Add-Result 'unknown ProductType code returns no document' '' ([string]$unknownCode.Body.data)
    Add-Result 'unknown ProductType code emits no ETag' '' ([string]$unknownCode.ETag)
    Add-Result 'corrupt read writes no success evidence' $evidenceBeforeCorrupt (Get-ReadEvidenceCount)
    Add-Result 'corrupt read repairs nothing' $rowsBeforeCorrupt (Get-ConfigurationRowSnapshot)
    Clear-Configuration $script:WorkspaceId

    Set-Override $script:WorkspaceId 'service' 'DISABLED'
    $invalidStatus = Invoke-Configuration
    Add-Result 'invalid status fails closed' 500 $invalidStatus.Status
    Add-Result 'invalid status never falls back to ACTIVE' '' ([string]$invalidStatus.Body.data)
    Add-Result 'invalid status row is not rewritten' 'DISABLED' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
    Clear-Configuration $script:WorkspaceId

    Set-Override $script:WorkspaceId 'Service' 'INACTIVE'
    $caseVariant = Invoke-Configuration
    Add-Result 'case-variant ProductType code fails closed' 500 $caseVariant.Status
    Add-Result 'case-variant code is not normalised' 'Service' ([string](Get-Scalar "SELECT ProductTypeCode FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Add-Result 'case-variant code count' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'Service' COLLATE Latin1_General_100_BIN2"))
    Clear-Configuration $script:WorkspaceId

    Set-Revision $script:WorkspaceId 2
    Invoke-SqlNonQuery "ALTER TABLE products.ProductConfigurationDocuments NOCHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision; UPDATE products.ProductConfigurationDocuments SET Revision=-4 WHERE WorkspaceId=N'$script:WorkspaceId';"
    $negativeRevision = Invoke-Configuration
    Add-Result 'negative revision fails closed' 500 $negativeRevision.Status
    Add-Result 'negative revision emits no ETag' '' ([string]$negativeRevision.ETag)
    Add-Result 'negative revision is not repaired' -4 ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'; ALTER TABLE products.ProductConfigurationDocuments WITH CHECK CHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision;"

    # -- 35.9 duplicate override is structurally impossible ---------------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'service' 'INACTIVE'
    $duplicateRejected = $false
    try { Set-Override $script:WorkspaceId 'service' 'ACTIVE' }
    catch { $duplicateRejected = $true }
    Assert-True 'duplicate override rejected by the database' $duplicateRejected
    Add-Result 'duplicate override left exactly one row' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
    Clear-Configuration $script:WorkspaceId

    # -- structural / scope assertions -------------------------------------------------------------
    Add-Result 'revision check constraint present' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM sys.check_constraints WHERE name=N'CK_ProductConfigurationDocuments_Revision'"))
    Add-Result 'override primary key is Workspace plus code' 'ProductTypeCode,WorkspaceId' ((@(Invoke-Sql "SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=i.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'products.ProductConfigurationTypeOverrides') AND i.is_primary_key=1") | ForEach-Object { [string]$_.name } | Sort-Object) -join ',')
    Add-Result 'configuration tables have no foreign key' 0 ([int](Get-Scalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'products.ProductConfigurationDocuments'),OBJECT_ID(N'products.ProductConfigurationTypeOverrides'))"))
    Add-Result 'migration seeds no override rows' 0 ([int](Get-Scalar 'SELECT COUNT(*) FROM products.ProductConfigurationTypeOverrides'))

    $handlerSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/ListProductConfigurationTypes/Handler.cs')
    $catalogSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/Common/ProductConfigurationCatalog.cs')
    $persistenceSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Infrastructure/Persistence/EfProductsPersistence.cs')
    $endpointSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Contracts/ProductsEndpoints.cs')
    $validationSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/Common/ProductValidation.cs')
    Assert-True 'no foreign DbContext in the configuration read' (($handlerSource+$catalogSource+$persistenceSource) -notmatch '\b(Workspace|AccessControl|IdentityAuth)DbContext\b')
    Assert-True 'no cross-owner SQL in Products persistence' ($persistenceSource -notmatch '\[(workspace|iam|access)\]|\b(workspace|iam|access)\.')
    # Prose in the doc comments names the header, so assert on what the code can actually touch:
    # neither type can reach transport state at all.
    Assert-True 'configuration read cannot reach transport state' (($handlerSource+$catalogSource) -notmatch 'HttpContext|Request\.Headers|Headers\[')
    Assert-True 'trusted Workspace comes from the authorization context' ($handlerSource -match 'context\.WorkspaceId')
    Assert-True 'configuration read writes no idempotency or outbox state' ($handlerSource -notmatch 'Idempotency|Outbox|AddProduct')
    Assert-True 'ProductValidation is unchanged by this operation' ($validationSource -notmatch 'ProductConfiguration')
    Add-Result 'exactly one configuration route' 1 ([regex]::Matches($endpointSource,'"/products/configuration/types"').Count)
    Add-Result 'no configuration mutation route' 0 ([regex]::Matches($endpointSource,'MapPost\(endpoints, "/products/configuration|MapPatch|MapDelete').Count)
    Add-Result 'strong quoted ETag encoding in the endpoint' 1 ([regex]::Matches($endpointSource,'Headers\.ETag').Count)
}
finally {
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit()
    }
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
if ($script:Failed -ne 0) { throw "listProductConfigurationTypes verification failed: $script:Failed check(s)." }
