<#
.SYNOPSIS
    Verifies Product Configuration type-eligibility enforcement in createProduct and replaceProduct
    against an isolated SQL database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5611,
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
$password = 'Product-Eligibility-Verify!2026'
$email = 'admin@unicorecrm.local'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-product-eligibility-' + [Guid]::NewGuid().ToString('N'))
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

function Assert-True([string] $Name, [bool] $Condition) { Add-Result $Name 'True' $Condition.ToString() }

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

function New-ProductBody([string] $Sku, [string] $Type, [string] $Name = 'Eligibility Verification Product') {
    return @{
        sku = $Sku; name = $Name; type = $Type; status = 'ACTIVE'; category = 'Software License'
        unit = 'item'; unitPrice = @{ amount = '10.00'; currency = 'USD' }; taxRate = '0'
        taxMode = 'none'; billingCycle = 'one_time'; isSubscription = $false; isRenewable = $false; tags = @()
    }
}

function Invoke-ProductApi(
    [string] $Method,
    [string] $Path,
    [object] $Body = $null,
    [string] $IdempotencyKey = $null,
    [string] $IfMatch = $null,
    [string] $Workspace = $null,
    [string] $Token = $null
) {
    $script:Counter++
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    if ([string]::IsNullOrEmpty($Token)) { $Token = $script:Token }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pte-' + $script:Counter.ToString('d6'))
    $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pte-' + $script:Counter.ToString('d6'))
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($Workspace)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $Workspace) }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    if (-not [string]::IsNullOrWhiteSpace($IfMatch)) { $null = $request.Headers.TryAddWithoutValidation('If-Match', $IfMatch) }
    if ($null -ne $Body) {
        $request.Content = [System.Net.Http.StringContent]::new(($Body | ConvertTo-Json -Compress -Depth 8), [Text.Encoding]::UTF8, 'application/json')
    }
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(90)
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { } }
        $etag = $null
        if ($null -ne $response.Headers.ETag) { $etag = $response.Headers.ETag.ToString() }
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Raw = $raw; Body = $payload; ETag = $etag }
    }
    finally { $request.Dispose(); $client.Dispose() }
}

function New-Product([string] $Sku, [string] $Type, [string] $Key = $null, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Key)) { $Key = 'idem-pte-' + [Guid]::NewGuid().ToString('N') }
    return Invoke-ProductApi 'POST' '/products' (New-ProductBody $Sku $Type) $Key $null $Workspace
}

function Set-TypeStatus([string] $Code, [string] $Status, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace' AND ProductTypeCode=N'$Code'; INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$Workspace',N'$Code',N'$Status');"
}

function Clear-Config([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace'; DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace'; DELETE FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$Workspace';"
}

function Set-Revision([string] $Revision, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    Invoke-SqlNonQuery "IF EXISTS(SELECT 1 FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace') UPDATE products.ProductConfigurationDocuments SET Revision=$Revision WHERE WorkspaceId=N'$Workspace' ELSE INSERT INTO products.ProductConfigurationDocuments(WorkspaceId,Revision) VALUES(N'$Workspace',$Revision);"
}

function Get-TrustedRevision([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $value = Get-Scalar "SELECT GreatestTrustedRevision FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$Workspace'"
    if ($null -eq $value) { return [long]0 }
    return [long]$value
}

function Get-ProductCount([string] $Sku) {
    return [int](Get-Scalar "SELECT COUNT(*) FROM products.Products WHERE NormalizedSku=N'$Sku'")
}

function Get-CommandEvidence {
    return [string]::Join('|', @(
        (Get-Scalar "SELECT COUNT_BIG(*) FROM products.AuditRecords WHERE Outcome=N'COMMITTED'"),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM products.OutboxMessages'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM products.IdempotencyRecords')
    ))
}

function Get-FieldErrorKeys([object] $Body) {
    if ($null -eq $Body.fieldErrors) { return '' }
    return (($Body.fieldErrors.PSObject.Properties.Name | Sort-Object) -join ',')
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
    # Deliberately NO studio.read. Product commands must work without it; that is the capability
    # separation this task has to prove.
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'products.create'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__1 = 'products.edit'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__2 = 'products.read'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $stdout = Join-Path $logRoot 'host.out.log'
    $stderr = Join-Path $logRoot 'host.err.log'
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-ProductApi 'GET' '/auth/session' $null $null $null '' ''
            if ($probe.Status -eq 401) { $ready = $true; break }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.AggregateException] { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }

    $signInRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/auth/sessions")
    $signInRequest.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pte-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pte-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-pte-signin') | Out-Null
    $signInRequest.Content = [System.Net.Http.StringContent]::new((@{ email = $email; password = $password } | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
    $signInClient = [System.Net.Http.HttpClient]::new()
    $signInResponse = $signInClient.SendAsync($signInRequest).GetAwaiter().GetResult()
    $signInBody = $signInResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Add-Result 'authentication fixture sign-in' 200 ([int]$signInResponse.StatusCode)
    $script:Token = [string]$signInBody.accessToken
    $signInRequest.Dispose(); $signInResponse.Dispose(); $signInClient.Dispose()

    $script:WorkspaceId = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo'")
    $foreignWorkspace = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo-isolated'")
    Assert-True 'trusted Workspace fixture exists' (-not [string]::IsNullOrWhiteSpace($script:WorkspaceId))

    # -- N. Capability separation ------------------------------------------------------------------
    Add-Result 'caller holds no studio.read' 0 ([int](Get-Scalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE Capability=N'studio.read'"))
    $configGet = Invoke-ProductApi 'GET' '/products/configuration/types'
    Add-Result 'configuration GET denied without studio.read' 403 $configGet.Status

    # -- C1 no configuration at all ----------------------------------------------------------------
    Clear-Config
    $c1 = New-Product 'PTE-C1' 'service'
    Add-Result 'C1 create with no configuration succeeds' 201 $c1.Status
    Add-Result 'C1 Product persisted' 1 (Get-ProductCount 'PTE-C1')
    Add-Result 'C1 no trust row materialised at revision 0' 0 (Get-TrustedRevision)

    # -- C2 missing override for the requested type ------------------------------------------------
    Set-TypeStatus 'license' 'INACTIVE'
    $c2 = New-Product 'PTE-C2' 'service'
    Add-Result 'C2 missing override treated as ACTIVE' 201 $c2.Status
    Add-Result 'C2 Product persisted' 1 (Get-ProductCount 'PTE-C2')

    # -- C3 explicit ACTIVE override ---------------------------------------------------------------
    Set-TypeStatus 'service' 'ACTIVE'
    $c3 = New-Product 'PTE-C3' 'service'
    Add-Result 'C3 explicit ACTIVE succeeds' 201 $c3.Status

    # -- C4 explicit INACTIVE override -------------------------------------------------------------
    Set-TypeStatus 'service' 'INACTIVE'
    $evidenceBeforeC4 = Get-CommandEvidence
    $c4 = New-Product 'PTE-C4' 'service'
    Add-Result 'C4 INACTIVE create rejected' 422 $c4.Status
    Add-Result 'C4 rejection uses the admitted field error' 'FIELD_VALIDATION_FAILED' ([string]$c4.Body.code)
    Add-Result 'C4 rejection names the type field' 'type' (Get-FieldErrorKeys $c4.Body)
    Add-Result 'C4 writes no Product' 0 (Get-ProductCount 'PTE-C4')
    Add-Result 'C4 writes no command evidence' $evidenceBeforeC4 (Get-CommandEvidence)

    # -- C5 non-canonical type still handled by canonical validation, no config dependency ----------
    Clear-Config
    $c5 = New-Product 'PTE-C5' 'made_up_type'
    Add-Result 'C5 non-canonical type rejected' 422 $c5.Status
    Add-Result 'C5 non-canonical type reports type field' 'type' (Get-FieldErrorKeys $c5.Body)
    Add-Result 'C5 writes no Product' 0 (Get-ProductCount 'PTE-C5')

    # -- C6 corrupt configuration fails closed -----------------------------------------------------
    foreach ($corruption in @(
        @{ Name = 'unknown code'; Sql = "INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$script:WorkspaceId',N'my_custom_type',N'ACTIVE')"; Sku = 'PTE-C6A' },
        @{ Name = 'invalid status'; Sql = "INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$script:WorkspaceId',N'service',N'DISABLED')"; Sku = 'PTE-C6B' },
        @{ Name = 'case variant'; Sql = "INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$script:WorkspaceId',N'Service',N'ACTIVE')"; Sku = 'PTE-C6C' }
    )) {
        Clear-Config
        Invoke-SqlNonQuery $corruption.Sql
        $evidenceBefore = Get-CommandEvidence
        $corrupt = New-Product $corruption.Sku 'service'
        Add-Result "C6 $($corruption.Name) fails closed" 500 $corrupt.Status
        Add-Result "C6 $($corruption.Name) returns INTERNAL_ERROR" 'INTERNAL_ERROR' ([string]$corrupt.Body.code)
        Add-Result "C6 $($corruption.Name) writes no Product" 0 (Get-ProductCount $corruption.Sku)
        Add-Result "C6 $($corruption.Name) writes no command evidence" $evidenceBefore (Get-CommandEvidence)
    }

    Clear-Config
    Invoke-SqlNonQuery "ALTER TABLE products.ProductConfigurationDocuments NOCHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision;"
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationDocuments(WorkspaceId,Revision) VALUES(N'$script:WorkspaceId',-4);"
    $c6d = New-Product 'PTE-C6D' 'service'
    Add-Result 'C6 negative revision fails closed' 500 $c6d.Status
    Add-Result 'C6 negative revision writes no Product' 0 (Get-ProductCount 'PTE-C6D')
    Add-Result 'C6 negative revision not repaired' -4 ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'; ALTER TABLE products.ProductConfigurationDocuments WITH CHECK CHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision;"

    Clear-Config
    Set-Revision 5
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTrustedRevisions(WorkspaceId,GreatestTrustedRevision) VALUES(N'$script:WorkspaceId',9)"
    $c6e = New-Product 'PTE-C6E' 'service'
    Add-Result 'C6 revision below trusted fails closed' 500 $c6e.Status
    Add-Result 'C6 revision below trusted writes no Product' 0 (Get-ProductCount 'PTE-C6E')
    Add-Result 'C6 revision below trusted leaves watermark intact' 9 (Get-TrustedRevision)
    Clear-Config

    # -- C7 / O. Workspace isolation ---------------------------------------------------------------
    Clear-Config
    Clear-Config $foreignWorkspace
    Set-TypeStatus 'service' 'INACTIVE' $foreignWorkspace
    $c7 = New-Product 'PTE-C7' 'service'
    Add-Result 'C7 foreign Workspace INACTIVE does not block this Workspace' 201 $c7.Status
    Set-TypeStatus 'service' 'INACTIVE'
    Set-TypeStatus 'service' 'ACTIVE' $foreignWorkspace
    $c7b = New-Product 'PTE-C7B' 'service'
    Add-Result 'C7 foreign Workspace ACTIVE does not unblock this Workspace' 422 $c7b.Status
    Add-Result 'C7 no cross-Workspace configuration leak' 0 (Get-ProductCount 'PTE-C7B')
    Clear-Config $foreignWorkspace
    Clear-Config

    # -- Trust advancement by a committed command --------------------------------------------------
    Set-Revision 4
    $trustCreate = New-Product 'PTE-TRUST' 'service'
    Add-Result 'committed command succeeds under a valid revision' 201 $trustCreate.Status
    Add-Result 'committed command advances the trusted watermark' 4 (Get-TrustedRevision)
    Set-Revision 3
    $afterRollback = New-Product 'PTE-TRUST2' 'service'
    Add-Result 'command relying on a rolled-back revision fails closed' 500 $afterRollback.Status
    Add-Result 'rolled-back command writes no Product' 0 (Get-ProductCount 'PTE-TRUST2')
    Clear-Config

    # -- C8 idempotent replay survives later deactivation ------------------------------------------
    Set-TypeStatus 'service' 'ACTIVE'
    $replayKey = 'idem-pte-replay-0001'
    $c8First = New-Product 'PTE-C8' 'service' $replayKey
    Add-Result 'C8 original create succeeds while ACTIVE' 201 $c8First.Status
    Set-TypeStatus 'service' 'INACTIVE'
    $c8Replay = New-Product 'PTE-C8' 'service' $replayKey
    Add-Result 'C8 replay after deactivation still succeeds' 201 $c8Replay.Status
    Add-Result 'C8 replay returns the same Product' ([string]$c8First.Body.result.product.id) ([string]$c8Replay.Body.result.product.id)
    Add-Result 'C8 replay creates no second Product' 1 (Get-ProductCount 'PTE-C8')

    # -- C9 idempotency conflict precedence is unchanged by an inactive type -----------------------
    $c9 = Invoke-ProductApi 'POST' '/products' (New-ProductBody 'PTE-C9-DIFFERENT' 'service') $replayKey
    Add-Result 'C9 different request on a used key keeps conflict precedence' 409 $c9.Status
    Add-Result 'C9 conflict is not converted into field validation' 'IDEMPOTENCY_KEY_REUSED' ([string]$c9.Body.code)

    # -- C10 a rejected inactive create leaves no replayable success -------------------------------
    $rejectedKey = 'idem-pte-rejected-0001'
    $c10 = New-Product 'PTE-C10' 'service' $rejectedKey
    Add-Result 'C10 inactive create rejected' 422 $c10.Status
    Add-Result 'C10 no idempotency record stored for the rejected command' 0 ([int](Get-Scalar "SELECT COUNT(*) FROM products.IdempotencyRecords WHERE IdempotencyKey=N'$rejectedKey'"))
    Set-TypeStatus 'service' 'ACTIVE'
    $c10Retry = New-Product 'PTE-C10' 'service' $rejectedKey
    Add-Result 'C10 the same key succeeds once the type is active again' 201 $c10Retry.Status

    # -- Replace matrix ----------------------------------------------------------------------------
    Clear-Config
    $rplSeed = New-Product 'PTE-RPL' 'service'
    Add-Result 'replace fixture created' 201 $rplSeed.Status
    $rplProductId = [string]$rplSeed.Body.result.product.id
    $rplVersion = [string]$rplSeed.Body.version

    function Invoke-Replace([string] $Type, [string] $Version, [string] $Sku = 'PTE-RPL') {
        return Invoke-ProductApi 'PUT' "/products/$rplProductId" (New-ProductBody $Sku $Type) ('idem-pte-' + [Guid]::NewGuid().ToString('N')) ('"' + $Version + '"')
    }

    $rpl1 = Invoke-Replace 'service' $rplVersion
    Add-Result 'RPL1 same ACTIVE type allowed' 200 $rpl1.Status
    $rplVersion = [string]$rpl1.Body.version

    $rpl2 = Invoke-Replace 'subscription' $rplVersion
    Add-Result 'RPL2 change to a different ACTIVE type allowed' 200 $rpl2.Status
    $rplVersion = [string]$rpl2.Body.version

    Set-TypeStatus 'package' 'INACTIVE'
    $rpl3 = Invoke-Replace 'package' $rplVersion
    Add-Result 'RPL3 change to an INACTIVE type rejected' 422 $rpl3.Status
    Add-Result 'RPL3 uses the admitted field error' 'FIELD_VALIDATION_FAILED' ([string]$rpl3.Body.code)
    Add-Result 'RPL3 names the type field' 'type' (Get-FieldErrorKeys $rpl3.Body)
    Add-Result 'RPL3 leaves the Product version untouched' $rplVersion ([string](Get-Scalar "SELECT Version FROM products.Products WHERE ProductId=N'$rplProductId'"))

    # Product currently holds subscription; deactivate it and prove preservation is still allowed.
    Set-TypeStatus 'subscription' 'INACTIVE'
    $rpl4 = Invoke-Replace 'subscription' $rplVersion 'PTE-RPL-RENAMED'
    Add-Result 'RPL4 preserving an existing INACTIVE type allowed' 200 $rpl4.Status
    Add-Result 'RPL4 other fields still replaced' 'PTE-RPL-RENAMED' ([string]$rpl4.Body.result.product.sku)
    $rplVersion = [string]$rpl4.Body.version

    $rpl5 = Invoke-Replace 'service' $rplVersion 'PTE-RPL-RENAMED'
    Add-Result 'RPL5 INACTIVE existing to a different ACTIVE type allowed' 200 $rpl5.Status
    $rplVersion = [string]$rpl5.Body.version

    Set-TypeStatus 'service' 'INACTIVE'
    $rpl6 = Invoke-Replace 'package' $rplVersion 'PTE-RPL-RENAMED'
    Add-Result 'RPL6 INACTIVE existing to a different INACTIVE type rejected' 422 $rpl6.Status

    # -- RPL9 stale If-Match precedence is preserved -----------------------------------------------
    $rpl9 = Invoke-Replace 'package' '999' 'PTE-RPL-RENAMED'
    Add-Result 'RPL9 stale If-Match beats inactive target' 412 $rpl9.Status
    Add-Result 'RPL9 reports version conflict not field validation' 'VERSION_CONFLICT' ([string]$rpl9.Body.code)

    # -- RPL7 corrupt configuration on an otherwise valid replacement ------------------------------
    Clear-Config
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$script:WorkspaceId',N'my_custom_type',N'ACTIVE')"
    $rpl7 = Invoke-Replace 'service' $rplVersion 'PTE-RPL-RENAMED'
    Add-Result 'RPL7 corrupt configuration fails closed' 500 $rpl7.Status
    Add-Result 'RPL7 corrupt configuration returns INTERNAL_ERROR' 'INTERNAL_ERROR' ([string]$rpl7.Body.code)
    Add-Result 'RPL7 leaves the Product version untouched' $rplVersion ([string](Get-Scalar "SELECT Version FROM products.Products WHERE ProductId=N'$rplProductId'"))
    Clear-Config

    # -- RPL8 archived Product precedence ----------------------------------------------------------
    $archiveSeed = New-Product 'PTE-ARCH' 'service'
    $archiveId = [string]$archiveSeed.Body.result.product.id
    $archiveVersion = [string]$archiveSeed.Body.version
    $archived = Invoke-ProductApi 'POST' "/products/$archiveId/archive" @{ reason = 'eligibility precedence fixture' } ('idem-pte-' + [Guid]::NewGuid().ToString('N')) ('"' + $archiveVersion + '"')
    Add-Result 'RPL8 archive fixture succeeded' 200 $archived.Status
    $archivedVersion = [string]$archived.Body.version
    Set-TypeStatus 'package' 'INACTIVE'
    $rpl8 = Invoke-ProductApi 'PUT' "/products/$archiveId" (New-ProductBody 'PTE-ARCH' 'package') ('idem-pte-' + [Guid]::NewGuid().ToString('N')) ('"' + $archivedVersion + '"')
    Add-Result 'RPL8 archived Product beats inactive target' 409 $rpl8.Status
    Add-Result 'RPL8 reports PRODUCT_ARCHIVED not field validation' 'PRODUCT_ARCHIVED' ([string]$rpl8.Body.code)
    Clear-Config

    # -- Replace idempotency replay after deactivation ---------------------------------------------
    $rplReplayKey = 'idem-pte-rplreplay-0001'
    $rplVersionNow = [string](Get-Scalar "SELECT Version FROM products.Products WHERE ProductId=N'$rplProductId'")
    $rplReplayFirst = Invoke-ProductApi 'PUT' "/products/$rplProductId" (New-ProductBody 'PTE-RPL-REPLAY' 'service') $rplReplayKey ('"' + $rplVersionNow + '"')
    Add-Result 'replace idempotency original succeeds while ACTIVE' 200 $rplReplayFirst.Status
    Set-TypeStatus 'service' 'INACTIVE'
    $rplReplayAgain = Invoke-ProductApi 'PUT' "/products/$rplProductId" (New-ProductBody 'PTE-RPL-REPLAY' 'service') $rplReplayKey ('"' + $rplVersionNow + '"')
    Add-Result 'replace replay after deactivation still succeeds' 200 $rplReplayAgain.Status
    Add-Result 'replace replay returns the same version' ([string]$rplReplayFirst.Body.version) ([string]$rplReplayAgain.Body.version)
    Clear-Config

    # -- M. Existing Product with a now-inactive type: the four-way regression ---------------------
    Clear-Config
    $legacy = New-Product 'PTE-LEGACY' 'service'
    Add-Result 'legacy Product created while ACTIVE' 201 $legacy.Status
    $legacyId = [string]$legacy.Body.result.product.id
    $legacyVersion = [string]$legacy.Body.version
    Set-TypeStatus 'service' 'INACTIVE'
    $legacyGet = Invoke-ProductApi 'GET' "/products/$legacyId"
    Add-Result 'legacy GET unchanged after deactivation' 200 $legacyGet.Status
    Add-Result 'legacy GET still reports its type' 'service' ([string]$legacyGet.Body.type)
    $legacyList = Invoke-ProductApi 'GET' '/products'
    Add-Result 'legacy LIST unchanged after deactivation' 200 $legacyList.Status
    Add-Result 'legacy Product still listed' 1 (@($legacyList.Body | Where-Object { $_.id -eq $legacyId }).Count)
    $legacyReplace = Invoke-ProductApi 'PUT' "/products/$legacyId" (New-ProductBody 'PTE-LEGACY' 'service' 'Legacy Renamed') ('idem-pte-' + [Guid]::NewGuid().ToString('N')) ('"' + $legacyVersion + '"')
    Add-Result 'legacy replace preserving the inactive type allowed' 200 $legacyReplace.Status
    $legacyNew = New-Product 'PTE-LEGACY-NEW' 'service'
    Add-Result 'new create with the same inactive type rejected' 422 $legacyNew.Status
    Clear-Config

    # -- K. Configuration / command race -----------------------------------------------------------
    # R1: deactivation commits before the command reads.
    Clear-Config
    Set-TypeStatus 'service' 'ACTIVE'
    Invoke-SqlNonQuery "UPDATE products.ProductConfigurationTypeOverrides SET Status=N'INACTIVE' WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"
    $r1 = New-Product 'PTE-R1' 'service'
    Add-Result 'W-A deactivation committed first rejects the command' 422 $r1.Status
    Add-Result 'W-A writes no Product' 0 (Get-ProductCount 'PTE-R1')

    # R2: a configuration writer holds the override row; the command must block on it rather than
    # having already completed an independent pre-check, then observe the committed state.
    Clear-Config
    Set-TypeStatus 'service' 'ACTIVE'
    $lockConnection = New-Connection
    try {
        $lockTransaction = $lockConnection.BeginTransaction()
        $lockCommand = $lockConnection.CreateCommand()
        $lockCommand.Transaction = $lockTransaction
        $lockCommand.CommandText = "UPDATE products.ProductConfigurationTypeOverrides SET Status=N'INACTIVE' WHERE WorkspaceId=@ws AND ProductTypeCode=N'service'"
        $null = $lockCommand.Parameters.AddWithValue('@ws', $script:WorkspaceId)
        $null = $lockCommand.ExecuteNonQuery()

        $raceRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/products")
        $null = $raceRequest.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pte-race-000001')
        $null = $raceRequest.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pte-race-000001')
        $null = $raceRequest.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $raceRequest.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $raceRequest.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-pte-race-0001')
        $raceRequest.Content = [System.Net.Http.StringContent]::new(((New-ProductBody 'PTE-R2' 'service') | ConvertTo-Json -Compress -Depth 8), [Text.Encoding]::UTF8, 'application/json')
        $raceClient = [System.Net.Http.HttpClient]::new()
        $raceClient.Timeout = [TimeSpan]::FromSeconds(90)
        $raceTask = $raceClient.SendAsync($raceRequest)
        Start-Sleep -Seconds 3
        # An independent pre-check that had already committed would have let this finish by now.
        $blocked = -not $raceTask.IsCompleted
        Assert-True 'W-B command blocks on the uncommitted configuration write' $blocked
        Add-Result 'W-B no Product written while the writer holds the row' 0 (Get-ProductCount 'PTE-R2')
        $lockTransaction.Commit()
        $raceResponse = $raceTask.GetAwaiter().GetResult()
        $raceStatus = [int]$raceResponse.StatusCode
        $raceBody = $raceResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $raceResponse.Dispose(); $raceRequest.Dispose(); $raceClient.Dispose()
        Add-Result 'W-B command observes the committed deactivation' 422 $raceStatus
        Add-Result 'W-B rejection uses the admitted field error' 'FIELD_VALIDATION_FAILED' ([string]$raceBody.code)
        Add-Result 'W-B writes no Product after serialisation' 0 (Get-ProductCount 'PTE-R2')
    }
    finally { $lockConnection.Dispose() }
    Clear-Config

    # -- P. Failure atomicity: eligibility passes, then the Product write fails --------------------
    Clear-Config
    Set-Revision 6
    $evidenceBeforeFailure = Get-CommandEvidence
    Invoke-SqlNonQuery "CREATE TRIGGER products.TR_VerifyProductInsertFailure ON products.Products INSTEAD OF INSERT AS THROW 51000, 'forced Product persistence failure', 1"
    try {
        $atomic = New-Product 'PTE-ATOMIC' 'service'
        Add-Result 'persistence failure after eligibility returns no success' 500 $atomic.Status
        Add-Result 'persistence failure writes no Product' 0 (Get-ProductCount 'PTE-ATOMIC')
        Add-Result 'persistence failure writes no command evidence' $evidenceBeforeFailure (Get-CommandEvidence)
        Add-Result 'persistence failure rolls back the trust raise' 0 (Get-TrustedRevision)
    }
    finally { Invoke-SqlNonQuery 'DROP TRIGGER products.TR_VerifyProductInsertFailure' }
    Clear-Config

    # =============================================================================================
    # Command-first linearization.
    #
    # The decisive ordering is: the command reads ACTIVE, THEN a configuration writer tries to
    # deactivate. The writer-first direction proves nothing about this one.
    #
    # The command is frozen mid-transaction without any production test hook: an independent session
    # takes an exclusive lock on products.OutboxMessages, a table the command touches only in
    # RecordCommit - after the configuration read and after eligibility. The command therefore
    # necessarily has already read the configuration as ACTIVE and is holding its serializable
    # transaction open when it stalls there. The configuration writer is then started and must block
    # on the command's range locks.
    # =============================================================================================

    function Start-CommandFreeze {
        $connection = New-Connection
        $transaction = $connection.BeginTransaction()
        $command = $connection.CreateCommand()
        $command.Transaction = $transaction
        # Exclusive table lock held for the life of this transaction. The command's outbox insert,
        # which happens after eligibility, cannot proceed past it.
        $command.CommandText = 'SELECT TOP 0 * FROM products.OutboxMessages WITH (TABLOCKX, HOLDLOCK)'
        $null = $command.ExecuteReader().Close()
        return [pscustomobject]@{ Connection = $connection; Transaction = $transaction }
    }

    function Start-ConfigDeactivation([string] $Code) {
        $connection = New-Connection
        $spidCommand = $connection.CreateCommand()
        $spidCommand.CommandText = 'SELECT @@SPID'
        $spid = [int]$spidCommand.ExecuteScalar()
        $transaction = $connection.BeginTransaction()
        $command = $connection.CreateCommand()
        $command.Transaction = $transaction
        # A real transaction modelling a future Products-owned configuration mutation: the effective
        # document changes, so the aggregate revision advances with it in the same transaction.
        $command.CommandText = @"
UPDATE products.ProductConfigurationTypeOverrides SET Status=N'INACTIVE'
    WHERE WorkspaceId=@ws AND ProductTypeCode=@code;
UPDATE products.ProductConfigurationDocuments SET Revision=Revision+1 WHERE WorkspaceId=@ws;
"@
        $null = $command.Parameters.AddWithValue('@ws', $script:WorkspaceId)
        $null = $command.Parameters.AddWithValue('@code', $Code)
        $command.CommandTimeout = 120
        $pending = $command.BeginExecuteNonQuery()
        return [pscustomobject]@{
            Connection = $connection; Transaction = $transaction; Command = $command
            Pending = $pending; Spid = $spid
        }
    }

    function Get-BlockingChain {
        return @(Invoke-Sql "SELECT session_id, blocking_session_id, wait_type, wait_resource FROM sys.dm_exec_requests WHERE blocking_session_id <> 0")
    }

    function Start-ProductRequest([string] $Method, [string] $Path, [object] $Body) {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
        $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pte-lin-' + [Guid]::NewGuid().ToString('N'))
        $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pte-lin-' + [Guid]::NewGuid().ToString('N'))
        $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-pte-lin-' + [Guid]::NewGuid().ToString('N'))
        $request.Content = [System.Net.Http.StringContent]::new(($Body | ConvertTo-Json -Compress -Depth 8), [Text.Encoding]::UTF8, 'application/json')
        $client = [System.Net.Http.HttpClient]::new()
        $client.Timeout = [TimeSpan]::FromSeconds(120)
        return [pscustomobject]@{ Client = $client; Request = $request; Task = $client.SendAsync($request) }
    }

    function Complete-ProductRequest([object] $Pending) {
        $response = $Pending.Task.GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { } }
        $status = [int]$response.StatusCode
        $response.Dispose(); $Pending.Request.Dispose(); $Pending.Client.Dispose()
        return [pscustomobject]@{ Status = $status; Body = $payload }
    }

    # -- C1: createProduct command first -----------------------------------------------------------
    Clear-Config
    Set-TypeStatus 'service' 'ACTIVE'
    Set-Revision 1
    $freeze = Start-CommandFreeze
    $writer = $null
    try {
        $createPending = Start-ProductRequest 'POST' '/products' (New-ProductBody 'PTE-LIN-C1' 'service')
        Start-Sleep -Seconds 4
        Assert-True 'C1 command is in flight and uncommitted' (-not $createPending.Task.IsCompleted)
        Add-Result 'C1 no Product committed while the command is frozen' 0 (Get-ProductCount 'PTE-LIN-C1')
        # The command is stalled on the outbox lock, which it can only have reached after reading the
        # configuration and passing eligibility.
        Add-Result 'C1 configuration still ACTIVE at the moment of the read' 'ACTIVE' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))

        $writer = Start-ConfigDeactivation 'service'
        Assert-True 'C1 configuration writer attempted' ($null -ne $writer.Pending)
        Start-Sleep -Seconds 4
        Assert-True 'C1 configuration writer is blocked' (-not $writer.Pending.IsCompleted)
        Add-Result 'C1 configuration not yet deactivated while the writer is blocked' 'ACTIVE' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
        $blocking = Get-BlockingChain
        $writerWait = @($blocking | Where-Object { [int]$_.session_id -eq $writer.Spid })
        Assert-True 'C1 the writer session is recorded as blocked in sys.dm_exec_requests' ($writerWait.Count -ge 1)
        # Record what the writer is actually waiting on, so the lock evidence is observed rather than
        # asserted from theory.
        Add-Result 'C1 writer wait type is a lock wait' 'LCK' ([string]$writerWait[0].wait_type).Substring(0, 3)
        Assert-True 'C1 writer is blocked by a session other than itself' ([int]$writerWait[0].blocking_session_id -ne $writer.Spid -and [int]$writerWait[0].blocking_session_id -ne 0)
        $script:C1WaitType = [string]$writerWait[0].wait_type
        $script:C1WaitResource = [string]$writerWait[0].wait_resource

        # Release the command; it must commit before the writer can proceed.
        $freeze.Transaction.Rollback()
        $freeze.Connection.Dispose()
        $freeze = $null
        $createResult = Complete-ProductRequest $createPending
        $productCommittedAt = [DateTime]::UtcNow
        Add-Result 'C1 command commits successfully under its ACTIVE snapshot' 201 $createResult.Status
        Add-Result 'C1 Product persisted with the type it read' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.Products WHERE NormalizedSku=N'PTE-LIN-C1' AND Profile LIKE N'%service%'"))

        $null = $writer.Command.EndExecuteNonQuery($writer.Pending)
        $writer.Transaction.Commit()
        $writerCompletedAt = [DateTime]::UtcNow
        $writer.Connection.Dispose()
        $writer = $null
        Assert-True 'C1 configuration writer completed only after the Product commit' ($writerCompletedAt -ge $productCommittedAt)
        Add-Result 'C1 final configuration status' 'INACTIVE' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
        Add-Result 'C1 writer left a valid advanced revision' 2 ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
        Add-Result 'C1 trust relation remains valid (revision >= trusted)' 'True' ([string]([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'") -ge (Get-TrustedRevision)))
        # The forbidden ordering would have produced a 422 here: the command would have observed the
        # writer's INACTIVE. A 201 proves the writer could not commit inside the command's window.
        Add-Result 'C1 forbidden ordering (writer commits inside the command window) not observed' 201 $createResult.Status
    }
    finally {
        if ($null -ne $writer) { try { $writer.Transaction.Rollback() } catch { }; $writer.Connection.Dispose() }
        if ($null -ne $freeze) { try { $freeze.Transaction.Rollback() } catch { }; $freeze.Connection.Dispose() }
    }

    # -- C2: createProduct writer first (opposite ordering, must still reject) ---------------------
    Clear-Config
    Set-TypeStatus 'service' 'ACTIVE'
    Set-Revision 1
    $writerFirst = Start-ConfigDeactivation 'service'
    try {
        $null = $writerFirst.Command.EndExecuteNonQuery($writerFirst.Pending)
        $writerFirst.Transaction.Commit()
    }
    finally { $writerFirst.Connection.Dispose() }
    $c2 = New-Product 'PTE-LIN-C2' 'service'
    Add-Result 'C2 writer-first create rejected' 422 $c2.Status
    Add-Result 'C2 writer-first uses the admitted field error' 'FIELD_VALIDATION_FAILED' ([string]$c2.Body.code)
    Add-Result 'C2 writer-first writes no Product' 0 (Get-ProductCount 'PTE-LIN-C2')

    # -- R1: replaceProduct command first ----------------------------------------------------------
    Clear-Config
    $linSeed = New-Product 'PTE-LIN-R1' 'physical_product'
    Add-Result 'R1 replace fixture created' 201 $linSeed.Status
    $linProductId = [string]$linSeed.Body.result.product.id
    $linVersion = [string]$linSeed.Body.version
    Set-TypeStatus 'service' 'ACTIVE'
    Set-Revision 1
    $freeze = Start-CommandFreeze
    $writer = $null
    try {
        # replaceProduct requires If-Match, so the request is built explicitly here rather than
        # through the create-shaped helper.
        $replaceRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "$script:BaseUrl/products/$linProductId")
        $null = $replaceRequest.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pte-lin-r1')
        $null = $replaceRequest.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pte-lin-r1')
        $null = $replaceRequest.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
        $null = $replaceRequest.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
        $null = $replaceRequest.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-pte-lin-r1')
        $null = $replaceRequest.Headers.TryAddWithoutValidation('If-Match', '"' + $linVersion + '"')
        $replaceRequest.Content = [System.Net.Http.StringContent]::new(((New-ProductBody 'PTE-LIN-R1' 'service') | ConvertTo-Json -Compress -Depth 8), [Text.Encoding]::UTF8, 'application/json')
        $replaceClient = [System.Net.Http.HttpClient]::new()
        $replaceClient.Timeout = [TimeSpan]::FromSeconds(120)
        $replaceTask = $replaceClient.SendAsync($replaceRequest)
        Start-Sleep -Seconds 4
        Assert-True 'R1 replace command is in flight and uncommitted' (-not $replaceTask.IsCompleted)
        Add-Result 'R1 Product type unchanged while the command is frozen' 'physical_product' ([string](Get-Scalar "SELECT CASE WHEN Profile LIKE N'%physical_product%' THEN N'physical_product' ELSE N'other' END FROM products.Products WHERE ProductId=N'$linProductId'"))

        $writer = Start-ConfigDeactivation 'service'
        Assert-True 'R1 configuration writer attempted' ($null -ne $writer.Pending)
        Start-Sleep -Seconds 4
        Assert-True 'R1 configuration writer is blocked' (-not $writer.Pending.IsCompleted)
        Add-Result 'R1 configuration still ACTIVE while the writer is blocked' 'ACTIVE' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
        $replaceBlocking = Get-BlockingChain
        Assert-True 'R1 the writer session is recorded as blocked in sys.dm_exec_requests' (@($replaceBlocking | Where-Object { [int]$_.session_id -eq $writer.Spid }).Count -ge 1)

        $freeze.Transaction.Rollback()
        $freeze.Connection.Dispose()
        $freeze = $null
        $replaceResponse = $replaceTask.GetAwaiter().GetResult()
        $replaceStatus = [int]$replaceResponse.StatusCode
        $replaceCommittedAt = [DateTime]::UtcNow
        $replaceResponse.Dispose(); $replaceRequest.Dispose(); $replaceClient.Dispose()
        Add-Result 'R1 replace commits successfully under its ACTIVE snapshot' 200 $replaceStatus
        Add-Result 'R1 Product type is the newly selected type' 'service' ([string](Get-Scalar "SELECT CASE WHEN Profile LIKE N'%service%' AND Profile NOT LIKE N'%physical_product%' THEN N'service' ELSE N'other' END FROM products.Products WHERE ProductId=N'$linProductId'"))

        $null = $writer.Command.EndExecuteNonQuery($writer.Pending)
        $writer.Transaction.Commit()
        $replaceWriterCompletedAt = [DateTime]::UtcNow
        $writer.Connection.Dispose()
        $writer = $null
        Assert-True 'R1 configuration writer completed only after the replace commit' ($replaceWriterCompletedAt -ge $replaceCommittedAt)
        Add-Result 'R1 final configuration status' 'INACTIVE' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
        Add-Result 'R1 forbidden ordering not observed' 200 $replaceStatus
    }
    finally {
        if ($null -ne $writer) { try { $writer.Transaction.Rollback() } catch { }; $writer.Connection.Dispose() }
        if ($null -ne $freeze) { try { $freeze.Transaction.Rollback() } catch { }; $freeze.Connection.Dispose() }
    }

    # -- R2: replaceProduct writer first -----------------------------------------------------------
    Clear-Config
    $r2Seed = New-Product 'PTE-LIN-R2' 'physical_product'
    $r2ProductId = [string]$r2Seed.Body.result.product.id
    $r2Version = [string]$r2Seed.Body.version
    Set-TypeStatus 'service' 'ACTIVE'
    Set-Revision 1
    $r2Writer = Start-ConfigDeactivation 'service'
    try {
        $null = $r2Writer.Command.EndExecuteNonQuery($r2Writer.Pending)
        $r2Writer.Transaction.Commit()
    }
    finally { $r2Writer.Connection.Dispose() }
    $r2 = Invoke-ProductApi 'PUT' "/products/$r2ProductId" (New-ProductBody 'PTE-LIN-R2' 'service') 'idem-pte-lin-r2' ('"' + $r2Version + '"')
    Add-Result 'R2 writer-first replace rejected' 422 $r2.Status
    Add-Result 'R2 writer-first uses the admitted field error' 'FIELD_VALIDATION_FAILED' ([string]$r2.Body.code)
    Add-Result 'R2 Product type unchanged after rejection' 'physical_product' ([string](Get-Scalar "SELECT CASE WHEN Profile LIKE N'%physical_product%' THEN N'physical_product' ELSE N'other' END FROM products.Products WHERE ProductId=N'$r2ProductId'"))
    Clear-Config

    # -- Source-level scope assertions -------------------------------------------------------------
    $createSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/CreateProduct/Handler.cs')
    $replaceSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/ReplaceProduct/Handler.cs')
    $validationSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/Common/ProductValidation.cs')
    $endpointSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Contracts/ProductsEndpoints.cs')
    Assert-True 'commands never invoke the public configuration read handler' (($createSource + $replaceSource) -notmatch 'ListProductConfigurationTypes')
    Assert-True 'commands never require studio.read' (($createSource + $replaceSource) -notmatch 'ProductConfigurationCapabilities|StudioRead')
    Assert-True 'commands use the transaction-participating configuration load' (($createSource + $replaceSource) -match 'LoadProductConfigurationForCommandAsync')
    Assert-True 'commands never open their own configuration transaction' (($createSource + $replaceSource) -notmatch 'ReadProductConfigurationAsync')
    Assert-True 'ProductValidation stays free of configuration and persistence' ($validationSource -notmatch 'ProductConfiguration|DbContext|IProductsPersistence')
    Assert-True 'commands reuse the shared monotonic trust statement' (($createSource + $replaceSource) -match 'RaiseProductConfigurationTrustAsync')
    Assert-True 'commands contain no duplicated MERGE statement' (($createSource + $replaceSource) -notmatch 'MERGE')
    Assert-True 'commands reach no HttpContext' (($createSource + $replaceSource) -notmatch 'HttpContext|Request\.Headers')
    # createProductConfigurationType and deleteProductConfigurationType remain BLOCKED; the admitted
    # updateProductConfigurationType PATCH is the only configuration mutation route that may exist,
    # and it changes no Product command semantics.
    Add-Result 'no configuration create or delete route exists' 0 ([regex]::Matches($endpointSource, 'MapPost\(endpoints, "/products/configuration|MapDelete').Count)
    Add-Result 'exactly one configuration mutation route exists' 1 ([regex]::Matches($endpointSource, 'MapPatch\(endpoints, "/products/configuration/types/\{typeId\}"').Count)
    Add-Result 'exactly one configuration route' 1 ([regex]::Matches($endpointSource, '"/products/configuration/types"').Count)
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
    else { Write-Host "Verification logs retained at $logRoot" }
}

if (-not [string]::IsNullOrEmpty($script:C1WaitType)) {
    Write-Host "OBSERVED | C1 writer wait_type=$($script:C1WaitType) wait_resource=$($script:C1WaitResource)"
}
$script:Results | ForEach-Object { Write-Host $_ }
Write-Host "RESULT | passed=$script:Passed failed=$script:Failed"
if ($script:Failed -ne 0) { throw "Product type eligibility verification failed: $script:Failed check(s)." }
