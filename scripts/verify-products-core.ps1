param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [switch] $RunConnectedAcceptance
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$baseUrl = 'http://127.0.0.1:5093'
$email = 'products.core@example.test'
$password = 'Products-Core-Smoke!234'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$workspaceKey = 'products-core-main'
$foreignWorkspaceKey = 'products-core-foreign'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-products-core-' + [Guid]::NewGuid().ToString('N')))
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()

$capabilities = @(
    'workspace.context.resolve',
    'products.read', 'products.create', 'products.edit', 'products.delete'
)

function Invoke-SqlScalar([string] $query) {
    $value = & sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Invoke-Sql([string] $query) {
    & sqlcmd -S $server -d $DatabaseName -b -Q $query | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'A SQL verification command failed.' }
}

function Initialize-Database {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated Products verification database.' }
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:Frontend__AllowedOrigins__0 = 'http://127.0.0.1:3000'
    $contexts = @(
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'IdentityAuthDbContext' },
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'WorkspaceDbContext' },
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'AccessControlDbContext' },
        @{ Project = 'src/UnicoreCRM.Sales'; Context = 'ProductsDbContext' }
    )
    foreach ($entry in $contexts) {
        & dotnet ef database update --project (Join-Path $solutionRoot $entry.Project) --context $entry.Context --no-build | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not apply migrations for $($entry.Context)." }
    }
    $checks.Add('Isolated owner schemas migrated=PASS')
}

function Set-HostEnvironment {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:Development__ApplyMigrations = 'false'
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $email
    $env:IdentityAuth__DevelopmentBootstrap__Password = $password
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Products Core Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'true'
    $env:Workspace__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Workspace__DevelopmentBootstrap__IdentityEmail = $email
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Key = $workspaceKey
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Name = 'Products Core Workspace'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__LogoText = 'PC'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__TimeZone = 'Asia/Saigon'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__0 = 'products'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Key = $foreignWorkspaceKey
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Name = 'Products Core Foreign Workspace'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__LogoText = 'PF'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__TimeZone = 'UTC'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'true'
    $env:AccessControl__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:AccessControl__DevelopmentBootstrap__IdentityEmail = $email
    $env:AccessControl__DevelopmentBootstrap__WorkspaceKey = $workspaceKey
    $env:AccessControl__DevelopmentBootstrap__RoleName = 'Products Core Owner'
    for ($index = 0; $index -lt $capabilities.Count; $index++) {
        [Environment]::SetEnvironmentVariable(
            "AccessControl__DevelopmentBootstrap__Capabilities__$index",
            $capabilities[$index],
            'Process')
    }
    $env:Integrations__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'
}

function Start-ApiHost {
    Set-HostEnvironment
    $standardOut = Join-Path $temporaryDirectory 'host.out.log'
    $standardError = Join-Path $temporaryDirectory 'host.err.log'
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOut -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($process.HasExited) {
            throw "ApiHost exited during startup: $((Get-Content -LiteralPath $standardError -Raw)) $((Get-Content -LiteralPath $standardOut -Raw))"
        }
        try {
            $probe = $client.GetAsync("$baseUrl/auth/session").GetAwaiter().GetResult()
            if ([int] $probe.StatusCode -eq 401) { return $process }
        }
        catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'ApiHost did not listen within the Products smoke timeout.'
}

function Stop-ApiHost($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
}

function Send-Json([string] $method, [string] $path, [string] $body, [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$baseUrl$path")
    # An unbound [string] parameter arrives as an empty string, not $null, so `$null -ne $body` was
    # true for every GET and attached an empty JSON body to it. Windows PowerShell 5.1 ships an
    # HttpClient that refuses content on GET, which failed the request before it reached the API.
    # This is a harness defect only: no API semantics are changed to accommodate it.
    if (-not [string]::IsNullOrEmpty($body)) {
        $message.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    }
    foreach ($entry in $headers.GetEnumerator()) {
        $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
    }
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $status = [int] $response.StatusCode
    $response.Dispose()
    $message.Dispose()
    return [pscustomobject] @{ Status = $status; Body = $text }
}

function Assert-Status($response, [int] $expected, [string] $name) {
    if ($response.Status -ne $expected) {
        throw "$name expected HTTP $expected but got $($response.Status): $($response.Body)"
    }
    $checks.Add("$name=$expected")
}

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name failed." }
    $checks.Add("$name=PASS")
}

function Sign-In {
    $suffix = [Guid]::NewGuid().ToString('N')
    $response = Send-Json 'POST' '/auth/sessions' (@{
        email = $email
        password = $password
        deviceLabel = 'Products Core Smoke'
    } | ConvertTo-Json -Compress) @{
        'X-Request-Id' = "req-signin-$suffix"
        'X-Correlation-Id' = "corr-signin-$suffix"
        'Idempotency-Key' = "idem-signin-$suffix"
    }
    Assert-Status $response 200 'Identity sign-in'
    return ($response.Body | ConvertFrom-Json).accessToken
}

function New-Headers([string] $token, [string] $workspaceId, [string] $idempotencyKey = '', [long] $version = -1) {
    $headers = @{
        Authorization = "Bearer $token"
        'X-Workspace-Id' = $workspaceId
        'X-Request-Id' = 'req-' + [Guid]::NewGuid().ToString('N')
        'X-Correlation-Id' = 'corr-' + [Guid]::NewGuid().ToString('N')
    }
    if ($idempotencyKey.Length -gt 0) { $headers['Idempotency-Key'] = $idempotencyKey }
    if ($version -ge 0) { $headers['If-Match'] = '"' + $version + '"' }
    return $headers
}

function Get-ProductReadAuditCount {
    return [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE Outcome='READ';")
}

function Get-ProductRecordDecisionCount {
    return [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM access.RecordAccessDecisions WHERE ResourceKey='products';")
}

function Measure-ProductRead([string] $path, [hashtable] $headers) {
    $before = Get-ProductReadAuditCount
    $response = Send-Json 'GET' $path $null $headers
    $delta = (Get-ProductReadAuditCount) - $before
    $body = $null
    if (-not [string]::IsNullOrWhiteSpace($response.Body)) {
        try { $body = $response.Body | ConvertFrom-Json } catch { $body = $null }
    }
    return [pscustomobject] @{ Status = $response.Status; Body = $body; Raw = $response.Body; Delta = $delta; Probe = "$($response.Status)|$delta" }
}

function Set-ProductScope([string] $roleId, [string] $scope) {
    Invoke-Sql @"
DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_products_canonical_read_audit';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_products_canonical_read_audit', '$roleId', 'products', '$scope', '[]');
"@
}

function Clear-ProductReadFields {
    Invoke-Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_products_canonical_read_audit_%';"
}

function Product-Body(
    [string] $sku,
    [string] $name,
    [string] $status = 'ACTIVE',
    [string] $amount = '10.125',
    [string] $taxMode = 'exclusive',
    [string] $taxRate = '10') {
    return @{
        sku = $sku
        name = $name
        type = 'service'
        status = $status
        category = 'Professional Services'
        description = 'Authoritative Product fixture'
        unit = 'hour'
        unitPrice = @{ amount = $amount; currency = 'USD' }
        costPrice = @{ amount = '4.25'; currency = 'USD' }
        taxRate = $taxRate
        taxMode = $taxMode
        billingCycle = 'one_time'
        isSubscription = $false
        isRenewable = $false
        tags = @('verified', 'core')
    } | ConvertTo-Json -Compress -Depth 6
}

function Invoke-ConnectedBrowserAcceptance(
    [string] $accessToken,
    [string] $trustedWorkspaceId,
    [string] $targetProductId) {
    $frontendRoot = (Resolve-Path "$PSScriptRoot/../../frontend/unicorecrm-web").Path
    $env:UNICORECRM_TEST_API_BASE_URL = $baseUrl
    $env:UNICORECRM_TEST_ACCESS_TOKEN = $accessToken
    $env:UNICORECRM_TEST_WORKSPACE_ID = $trustedWorkspaceId
    $env:UNICORECRM_TEST_WORKSPACE_KEY = $workspaceKey
    $env:UNICORECRM_TEST_EMAIL = $email
    $env:UNICORECRM_TEST_PASSWORD = $password
    $env:UNICORECRM_TEST_PRODUCT_ID = $targetProductId
    $env:UNICORECRM_TEST_PRODUCT_NAME = 'Core Product Replaced'
    $env:PLAYWRIGHT_DISABLE_VIDEO = '1'
    Push-Location $frontendRoot
    try {
        & npm run e2e:connected -- --grep 'connected Product UI preserves version-bound projections across mutation'
        if ($LASTEXITCODE -ne 0) { throw 'Connected Product browser acceptance failed.' }
    }
    finally {
        Pop-Location
    }
    $checks.Add('Real backend/frontend Product browser acceptance=PASS')
}

$process = $null
try {
    Initialize-Database
    $process = Start-ApiHost
    $token = Sign-In
    $workspaceResponse = Send-Json 'GET' '/workspaces' $null @{
        Authorization = "Bearer $token"
        'X-Request-Id' = 'req-workspaces-' + [Guid]::NewGuid().ToString('N')
        'X-Correlation-Id' = 'corr-workspaces-' + [Guid]::NewGuid().ToString('N')
    }
    Assert-Status $workspaceResponse 200 'Workspace listing'
    $workspaceId = (($workspaceResponse.Body | ConvertFrom-Json).items | Where-Object { $_.workspaceKey -eq $workspaceKey }).workspaceId
    Assert-True (-not [string]::IsNullOrWhiteSpace($workspaceId)) 'Trusted Workspace resolved'
    $memberId = Invoke-SqlScalar "SELECT MemberId FROM workspace.Memberships WHERE WorkspaceId='$workspaceId';"
    $roleId = Invoke-SqlScalar "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$workspaceId' AND Name='Products Core Owner';"
    Assert-True (-not [string]::IsNullOrWhiteSpace($memberId) -and -not [string]::IsNullOrWhiteSpace($roleId)) 'Trusted Product member and role resolved'

    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='products' AND TABLE_NAME='AuditRecords';") -eq '1') 'Products-owned audit store reused'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM products.__EFMigrationsHistory;") -eq '1') 'No Product audit migration required'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('products.AuditRecords');") -eq '0') 'Product audit store has zero foreign keys'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='products' AND TABLE_NAME='AuditRecords' AND COLUMN_NAME IN ('AuditId','Operation','WorkspaceId','ActorId','AggregateId','RequestId','CorrelationId','OccurredAt','Outcome','NewVersion');") -eq '10') 'Product audit store represents frozen read evidence'

    $emptyHeaders = New-Headers $token $workspaceId
    $emptyRequestId = $emptyHeaders['X-Request-Id']
    $emptyList = Measure-ProductRead '/products' $emptyHeaders
    Assert-True ($emptyList.Probe -eq '200|1') 'Canonical empty list writes exactly one owner audit'
    Assert-True ((@($emptyList.Body).Count) -eq 0) 'Canonical empty list remains empty'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE RequestId='$emptyRequestId' AND Operation='listProducts' AND WorkspaceId='$workspaceId' AND ActorId='$memberId' AND Outcome='READ' AND AggregateId IS NULL AND PriorVersion IS NULL AND NewVersion IS NULL;") -eq '1') 'Canonical empty list audit evidence exact'

    $createKey = 'idem-product-create-0001'
    $createBody = Product-Body 'SKU-CORE-001' 'Core Product'
    $create = Send-Json 'POST' '/products' $createBody (New-Headers $token $workspaceId $createKey)
    Assert-Status $create 201 'createProduct'
    $created = $create.Body | ConvertFrom-Json
    $productId = $created.result.product.id
    Assert-True ($created.version -eq 0 -and $created.result.product.unitPrice.amount -eq '10.125') 'Server identity and decimal-string Money'

    Invoke-Sql "UPDATE workspace.BootstrapProjections SET BaseCurrency='EUR', ConfigurationVersion=ConfigurationVersion+1 WHERE WorkspaceId='$workspaceId';"
    $replay = Send-Json 'POST' '/products' $createBody (New-Headers $token $workspaceId $createKey)
    Assert-Status $replay 201 'createProduct replay'
    Assert-True (($replay.Body | ConvertFrom-Json).outcome -eq 'REPLAYED') 'Create replay ignores later effective-currency change'
    $reuse = Send-Json 'POST' '/products' (Product-Body 'SKU-CORE-CHANGED' 'Changed Product') (New-Headers $token $workspaceId $createKey)
    Assert-Status $reuse 409 'createProduct changed-payload reuse'
    Assert-True (($reuse.Body | ConvertFrom-Json).code -eq 'IDEMPOTENCY_KEY_REUSED') 'Changed-payload stable error'
    Invoke-Sql "UPDATE workspace.BootstrapProjections SET BaseCurrency='USD', ConfigurationVersion=ConfigurationVersion+1 WHERE WorkspaceId='$workspaceId';"

    $skuConflict = Send-Json 'POST' '/products' (Product-Body 'sku-core-001' 'Duplicate SKU') (New-Headers $token $workspaceId 'idem-product-sku-conflict')
    Assert-Status $skuConflict 409 'Workspace case-insensitive SKU uniqueness'
    Assert-True (($skuConflict.Body | ConvertFrom-Json).code -eq 'PRODUCT_SKU_CONFLICT') 'SKU conflict stable error'

    $massAssignment = $createBody.TrimEnd('}') + ',"id":"client-owned","version":91}'
    Assert-Status (Send-Json 'POST' '/products' $massAssignment (New-Headers $token $workspaceId 'idem-product-mass-assignment')) 400 'Mass assignment rejection'
    $recordDecisionsBeforeList = Get-ProductRecordDecisionCount
    $singleList = Measure-ProductRead '/products' (New-Headers $token $workspaceId)
    Assert-True ($singleList.Probe -eq '200|1' -and @($singleList.Body).Count -eq 1) 'Canonical one-row list writes one audit'
    Assert-True (((Get-ProductRecordDecisionCount) - $recordDecisionsBeforeList) -eq 0) 'Canonical list writes zero per-row record decisions'

    $detailHeaders = New-Headers $token $workspaceId
    $detailRequestId = $detailHeaders['X-Request-Id']
    $detailCorrelationId = $detailHeaders['X-Correlation-Id']
    $recordDecisionsBeforeDetail = Get-ProductRecordDecisionCount
    $detailRead = Measure-ProductRead "/products/$productId" $detailHeaders
    $detailDocument = $detailRead.Body
    Assert-True ($detailRead.Probe -eq '200|1') 'Canonical detail writes exactly one owner audit'
    Assert-True (((Get-ProductRecordDecisionCount) - $recordDecisionsBeforeDetail) -eq 1) 'Canonical detail record-access behavior unchanged'
    Assert-True ((Invoke-SqlScalar "SELECT CONCAT(Operation,'|',WorkspaceId,'|',ActorId,'|',RequestId,'|',CorrelationId,'|',Outcome,'|',AggregateId,'|',NewVersion) FROM products.AuditRecords WHERE RequestId='$detailRequestId';") -eq "getProduct|$workspaceId|$memberId|$detailRequestId|$detailCorrelationId|READ|$productId|$($detailDocument.version)") 'Canonical detail provenance and disclosed version exact'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE Operation='listProducts' AND (AggregateId IS NOT NULL OR PriorVersion IS NOT NULL OR NewVersion IS NOT NULL);") -eq '0') 'Canonical list record and resource version remain null'

    Invoke-Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='products.read';"
    Assert-True ((Measure-ProductRead '/products' (New-Headers $token $workspaceId)).Probe -eq '403|0') 'Capability-denied list writes no owner audit'
    Assert-True ((Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)).Probe -eq '403|0') 'Capability-denied detail writes no owner audit'
    Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'products.read');"

    $malformedHeaders = New-Headers $token $workspaceId
    $malformedHeaders['X-Request-Id'] = 'bad'
    $malformedRead = Measure-ProductRead '/products' $malformedHeaders
    Assert-True ($malformedRead.Probe -eq '422|0') 'Malformed authorized metadata writes no owner audit'
    Assert-True ((Measure-ProductRead '/products/bad%20product' (New-Headers $token $workspaceId)).Probe -eq '404|0') 'Malformed Product path writes no owner audit'
    Assert-True ((Measure-ProductRead '/products/product_does_not_exist_0001' (New-Headers $token $workspaceId)).Probe -eq '404|0') 'Unknown Product writes no owner audit'

    $projectionAuditBefore = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='READ';")
    $projectionOutboxBefore = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.OutboxMessages WHERE WorkspaceId='$workspaceId';")
    $authorizationAuditBefore = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM access.AuthorizationDecisions WHERE WorkspaceId='$workspaceId' AND RequiredCapability='products.read';")
    Assert-Status (Send-Json 'GET' "/products/$productId/availability" $null (New-Headers $token $workspaceId '' 0)) 200 'getProductAvailability'
    Assert-Status (Send-Json 'GET' "/products/$productId/availability" $null (New-Headers $token $workspaceId)) 400 'Availability required If-Match'
    $price = Send-Json 'GET' "/products/$productId/price-projection?quantity=2.5" $null (New-Headers $token $workspaceId '' 0)
    Assert-Status $price 200 'getProductPriceProjection'
    $priceBody = $price.Body | ConvertFrom-Json
    Assert-True (
        $priceBody.subtotal.amount -eq '25.3125' -and
        $priceBody.taxAmount.amount -eq '2.53125' -and
        $priceBody.total.amount -eq '27.84375') 'Exact authoritative price arithmetic'
    $availabilityAuditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Operation='getProductAvailability' AND AggregateId='$productId' AND Outcome='READ';")
    $priceAuditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Operation='getProductPriceProjection' AND AggregateId='$productId' AND Outcome='READ';")
    $projectionAuditAfter = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='READ';")
    $projectionOutboxAfter = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.OutboxMessages WHERE WorkspaceId='$workspaceId';")
    $authorizationAuditAfter = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM access.AuthorizationDecisions WHERE WorkspaceId='$workspaceId' AND RequiredCapability='products.read';")
    $invalidProjectionAuditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='READ' AND Operation IN ('getProductAvailability','getProductPriceProjection') AND (ActorId IS NULL OR ActorId='' OR AggregateId IS NULL OR AggregateId='' OR RequestId IS NULL OR RequestId='' OR CorrelationId IS NULL OR CorrelationId='' OR OccurredAt IS NULL);")
    $productAfterRead = Send-Json 'GET' "/products/$productId" $null (New-Headers $token $workspaceId)
    Assert-True ($availabilityAuditCount -eq 1 -and $priceAuditCount -eq 1 -and $projectionAuditAfter -eq ($projectionAuditBefore + 2)) 'Product-owned projection READ_AUDIT evidence'
    Assert-True ($invalidProjectionAuditCount -eq 0) 'Product READ_AUDIT authoritative fields'
    Assert-True ($authorizationAuditAfter -ge ($authorizationAuditBefore + 2)) 'AccessControl authorization audit remains separate from Product READ_AUDIT'
    Assert-True ($projectionOutboxAfter -eq $projectionOutboxBefore) 'Projection reads emit no Product outbox event'
    Assert-True ((($productAfterRead.Body | ConvertFrom-Json).version -eq 0)) 'Projection READ_AUDIT preserves Product version'
    $wrongCurrency = Send-Json 'POST' '/products' ((Product-Body 'SKU-EUR' 'Wrong Currency').Replace('"currency":"USD"', '"currency":"EUR"')) (New-Headers $token $workspaceId 'idem-product-wrong-currency')
    Assert-Status $wrongCurrency 422 'Workspace currency authority'
    Assert-True (($wrongCurrency.Body | ConvertFrom-Json).code -eq 'PRODUCT_PRICING_INVALID') 'Pricing error code'

    $replace = Send-Json 'PUT' "/products/$productId" (Product-Body 'SKU-CORE-001' 'Core Product Replaced' 'DRAFT') (New-Headers $token $workspaceId 'idem-product-replace' 0)
    Assert-Status $replace 200 'replaceProduct'
    Assert-True (($replace.Body | ConvertFrom-Json).version -eq 1) 'Replace increments version'
    Invoke-Sql "UPDATE workspace.BootstrapProjections SET BaseCurrency='EUR', ConfigurationVersion=ConfigurationVersion+1 WHERE WorkspaceId='$workspaceId';"
    $replaceReplay = Send-Json 'PUT' "/products/$productId" (Product-Body 'SKU-CORE-001' 'Core Product Replaced' 'DRAFT') (New-Headers $token $workspaceId 'idem-product-replace' 0)
    Assert-Status $replaceReplay 200 'replaceProduct replay after effective-currency change'
    Assert-True (($replaceReplay.Body | ConvertFrom-Json).outcome -eq 'REPLAYED') 'Replace replay depends only on stable client intent'
    Invoke-Sql "UPDATE workspace.BootstrapProjections SET BaseCurrency='USD', ConfigurationVersion=ConfigurationVersion+1 WHERE WorkspaceId='$workspaceId';"
    $stale = Send-Json 'PUT' "/products/$productId" (Product-Body 'SKU-CORE-001' 'Stale Product') (New-Headers $token $workspaceId 'idem-product-stale' 0)
    Assert-Status $stale 412 'replaceProduct stale version'
    Assert-True (($stale.Body | ConvertFrom-Json).code -eq 'VERSION_CONFLICT') 'Version conflict stable error'

    $archive = Send-Json 'POST' "/products/$productId/archive" '{"reason":"Catalog retirement"}' (New-Headers $token $workspaceId 'idem-product-archive' 1)
    Assert-Status $archive 200 'archiveProduct'
    $archiveBody = $archive.Body | ConvertFrom-Json
    Assert-True ($archiveBody.result.product.status -eq 'ARCHIVED' -and $archiveBody.result.product.version -eq 2) 'Archive evidence and version'
    $archivedAvailability = Send-Json 'GET' "/products/$productId/availability" $null (New-Headers $token $workspaceId '' 2)
    Assert-Status $archivedAvailability 200 'Archived Product availability'
    Assert-True (-not ($archivedAvailability.Body | ConvertFrom-Json).sellable) 'Archived Product is not sellable'
    $replaceArchived = Send-Json 'PUT' "/products/$productId" (Product-Body 'SKU-CORE-001' 'Archived Replacement') (New-Headers $token $workspaceId 'idem-product-replace-archived' 2)
    Assert-Status $replaceArchived 409 'Archived Product replacement blocked'
    Assert-True (($replaceArchived.Body | ConvertFrom-Json).code -eq 'PRODUCT_ARCHIVED') 'Archived replacement stable error'
    $restore = Send-Json 'POST' "/products/$productId/restore" '{}' (New-Headers $token $workspaceId 'idem-product-restore' 2)
    Assert-Status $restore 200 'restoreProduct'
    Assert-True ((($restore.Body | ConvertFrom-Json).result.product.status -eq 'ACTIVE')) 'Restore returns Product to ACTIVE'

    $second = Send-Json 'POST' '/products' (Product-Body 'SKU-CORE-002' 'Batch Product Two' 'ACTIVE' '110' 'inclusive' '10') (New-Headers $token $workspaceId 'idem-product-create-0002')
    $third = Send-Json 'POST' '/products' (Product-Body 'SKU-CORE-003' 'Batch Product Three' 'ACTIVE' '0.000001' 'exclusive' '50') (New-Headers $token $workspaceId 'idem-product-create-0003')
    Assert-Status $second 201 'Second Product create'
    Assert-Status $third 201 'Third Product create'
    $secondId = ($second.Body | ConvertFrom-Json).aggregateId
    $thirdId = ($third.Body | ConvertFrom-Json).aggregateId
    $recordDecisionsBeforeMultiList = Get-ProductRecordDecisionCount
    $multiList = Measure-ProductRead '/products' (New-Headers $token $workspaceId)
    Assert-True ($multiList.Probe -eq '200|1' -and @($multiList.Body).Count -eq 3) 'Canonical multi-row list writes one audit, never per row'
    Assert-True (((Get-ProductRecordDecisionCount) - $recordDecisionsBeforeMultiList) -eq 0) 'Canonical multi-row list has no record-decision fan-out'
    $inclusivePrice = Send-Json 'GET' "/products/$secondId/price-projection?quantity=1" $null (New-Headers $token $workspaceId '' 0)
    Assert-Status $inclusivePrice 200 'Inclusive tax projection'
    $inclusivePriceBody = $inclusivePrice.Body | ConvertFrom-Json
    Assert-True ($inclusivePriceBody.taxAmount.amount -eq '10' -and $inclusivePriceBody.total.amount -eq '110') 'Inclusive tax extraction'
    $halfUpPrice = Send-Json 'GET' "/products/$thirdId/price-projection?quantity=1" $null (New-Headers $token $workspaceId '' 0)
    Assert-Status $halfUpPrice 200 'HALF_UP projection'
    $halfUpPriceBody = $halfUpPrice.Body | ConvertFrom-Json
    Assert-True ($halfUpPriceBody.taxAmount.amount -eq '0.000001' -and $halfUpPriceBody.total.amount -eq '0.000002') 'HALF_UP maximum-scale rounding'

    $below = Send-Json 'POST' '/products' (Product-Body 'SKU-ROUND-BELOW' 'Round Below' 'ACTIVE' '1.234567' 'exclusive' '50.000001') (New-Headers $token $workspaceId 'idem-product-round-below')
    $midpoint = Send-Json 'POST' '/products' (Product-Body 'SKU-ROUND-MID' 'Round Midpoint' 'ACTIVE' '0.000001' 'inclusive' '33.333333') (New-Headers $token $workspaceId 'idem-product-round-mid')
    $above = Send-Json 'POST' '/products' (Product-Body 'SKU-ROUND-ABOVE' 'Round Above' 'ACTIVE' '0.000001' 'none' '99.999999') (New-Headers $token $workspaceId 'idem-product-round-above')
    Assert-Status $below 201 'Rounding below-boundary Product create'
    Assert-Status $midpoint 201 'Rounding midpoint Product create'
    Assert-Status $above 201 'Rounding above-boundary Product create'
    $belowProjection = Send-Json 'GET' "/products/$(($below.Body | ConvertFrom-Json).aggregateId)/price-projection?quantity=0.000001" $null (New-Headers $token $workspaceId '' 0)
    $midpointProjection = Send-Json 'GET' "/products/$(($midpoint.Body | ConvertFrom-Json).aggregateId)/price-projection?quantity=0.5" $null (New-Headers $token $workspaceId '' 0)
    $aboveProjection = Send-Json 'GET' "/products/$(($above.Body | ConvertFrom-Json).aggregateId)/price-projection?quantity=0.6" $null (New-Headers $token $workspaceId '' 0)
    Assert-Status $belowProjection 200 'Exclusive below-5 boundary projection'
    Assert-Status $midpointProjection 200 'Inclusive exactly-5 boundary projection'
    Assert-Status $aboveProjection 200 'None above-5 boundary projection'
    $belowBody = $belowProjection.Body | ConvertFrom-Json
    $midpointBody = $midpointProjection.Body | ConvertFrom-Json
    $aboveBody = $aboveProjection.Body | ConvertFrom-Json
    Assert-True ($belowBody.subtotal.amount -eq '0.000001' -and $belowBody.taxAmount.amount -eq '0.000001' -and $belowBody.total.amount -eq '0.000002') 'Exclusive exact-first calculation and explicit rounding'
    Assert-True ($midpointBody.subtotal.amount -eq '0.000001' -and $midpointBody.taxAmount.amount -eq '0' -and $midpointBody.total.amount -eq '0.000001') 'Inclusive HALF_UP exactly-5 boundary'
    Assert-True ($aboveBody.subtotal.amount -eq '0.000001' -and $aboveBody.taxAmount.amount -eq '0' -and $aboveBody.total.amount -eq '0.000001') 'No-tax HALF_UP above-5 boundary'
    $staleBatchBody = @{ items = @(@{ productId = $secondId; expectedVersion = 0 }, @{ productId = $thirdId; expectedVersion = 99 }); reason = 'Atomic stale check' } | ConvertTo-Json -Compress -Depth 5
    Assert-Status (Send-Json 'POST' '/products/archive-batch' $staleBatchBody (New-Headers $token $workspaceId 'idem-product-batch-stale')) 412 'Batch stale version'
    $secondAfterFailure = Send-Json 'GET' "/products/$secondId" $null (New-Headers $token $workspaceId)
    Assert-True ((($secondAfterFailure.Body | ConvertFrom-Json).status -eq 'ACTIVE')) 'Batch rollback preserved all Products'

    $archiveBatchBody = @{ items = @(@{ productId = $secondId; expectedVersion = 0 }, @{ productId = $thirdId; expectedVersion = 0 }); reason = 'Atomic batch archive' } | ConvertTo-Json -Compress -Depth 5
    $archiveBatch = Send-Json 'POST' '/products/archive-batch' $archiveBatchBody (New-Headers $token $workspaceId 'idem-product-batch-archive')
    Assert-Status $archiveBatch 200 'archiveProductsBatch'
    Assert-True ((($archiveBatch.Body | ConvertFrom-Json).result.products | Where-Object { $_.status -ne 'ARCHIVED' }).Count -eq 0) 'Atomic batch archive outcome'
    $archiveBatchReplay = Send-Json 'POST' '/products/archive-batch' $archiveBatchBody (New-Headers $token $workspaceId 'idem-product-batch-archive')
    Assert-True ((($archiveBatchReplay.Body | ConvertFrom-Json).outcome -eq 'REPLAYED')) 'Batch idempotent replay'
    $restoreBatchBody = @{ items = @(@{ productId = $secondId; expectedVersion = 1 }, @{ productId = $thirdId; expectedVersion = 1 }); reason = 'Restore batch' } | ConvertTo-Json -Compress -Depth 5
    Assert-Status (Send-Json 'POST' '/products/restore-batch' $restoreBatchBody (New-Headers $token $workspaceId 'idem-product-batch-restore')) 200 'restoreProductsBatch'

    # A real Product moved into another Workspace must be indistinguishable from one that never
    # existed. The previous assertions pinned the opposite - 403 WORKSPACE_MISMATCH for a real
    # foreign Product against 404 for an unknown one - which was an existence oracle: a caller who
    # could guess an identifier could tell a real foreign Product from a non-existent one. The
    # lookup is now Workspace-scoped in SQL, so both collapse. This is a stronger assertion, not a
    # relaxed one.
    $foreignWorkspaceId = Invoke-SqlScalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$foreignWorkspaceKey';"
    Invoke-Sql "UPDATE products.Products SET WorkspaceId='$foreignWorkspaceId' WHERE ProductId='$thirdId';"
    $crossWorkspaceMeasured = Measure-ProductRead "/products/$thirdId" (New-Headers $token $workspaceId)
    $unknownMeasured = Measure-ProductRead '/products/product_does_not_exist_0001' (New-Headers $token $workspaceId)
    $crossWorkspace = [pscustomobject] @{ Status = $crossWorkspaceMeasured.Status; Body = $crossWorkspaceMeasured.Raw }
    $unknownProduct = [pscustomobject] @{ Status = $unknownMeasured.Status; Body = $unknownMeasured.Raw }
    Assert-Status $crossWorkspace 404 'Cross-Workspace Product access collapses to not found'
    Assert-Status $unknownProduct 404 'Unknown Product access'
    Assert-True ($crossWorkspaceMeasured.Delta -eq 0 -and $unknownMeasured.Delta -eq 0) 'Foreign and unknown Product write no owner audit'
    $foreignNormalised = ($crossWorkspace.Body -replace '"correlationId":"[^"]*"', '"correlationId":"<c>"')
    $unknownNormalised = ($unknownProduct.Body -replace '"correlationId":"[^"]*"', '"correlationId":"<c>"')
    Assert-True ($foreignNormalised -eq $unknownNormalised) 'Foreign Product is byte-indistinguishable from an unknown Product'
    Assert-True ($crossWorkspace.Body -notmatch 'SKU-CORE-003') 'Foreign Product leaks no business value'
    $workspaceList = Measure-ProductRead '/products' (New-Headers $token $workspaceId)
    Assert-True ($workspaceList.Probe -eq '200|1' -and @($workspaceList.Body).Count -eq 5) 'Workspace list discloses only current Workspace Products with one audit'
    Assert-True ($workspaceList.Raw -notmatch 'SKU-CORE-003') 'Workspace list leaks no foreign Product value'

    Set-ProductScope $roleId 'Own'
    Assert-True ((Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)).Probe -eq '404|0') 'Record-access denied detail writes no owner audit'
    Assert-True ((Measure-ProductRead '/products' (New-Headers $token $workspaceId)).Probe -eq '200|1') 'OWN fail-closed empty list remains a successful audited disclosure'
    foreach ($scope in @('Team', 'Custom')) {
        Set-ProductScope $roleId $scope
        Assert-True ((Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)).Probe -eq '404|0') "$scope record denial writes no owner audit"
        $scopedList = Measure-ProductRead '/products' (New-Headers $token $workspaceId)
        Assert-True ($scopedList.Probe -eq '200|1' -and @($scopedList.Body).Count -eq 0) "$scope list fails closed with one invocation audit"
    }
    Set-ProductScope $roleId 'Workspace'

    Clear-ProductReadFields
    Invoke-Sql "INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES ('field_products_canonical_read_audit_required', '$roleId', 'products', 'name', 'Hidden');"
    Assert-True ((Measure-ProductRead '/products' (New-Headers $token $workspaceId)).Probe -eq '403|0') 'Required hidden Product field list writes no owner audit'
    Assert-True ((Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)).Probe -eq '403|0') 'Required hidden Product field detail writes no owner audit'
    Clear-ProductReadFields
    Invoke-Sql "INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES ('field_products_canonical_read_audit_optional', '$roleId', 'products', 'description', 'Hidden');"
    $optionalHidden = Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)
    Assert-True ($optionalHidden.Probe -eq '200|1' -and $optionalHidden.Raw -notmatch 'description|Authoritative Product fixture') 'Optional hidden Product field is withheld before one detail audit'
    Clear-ProductReadFields

    $corruptProductId = 'product_corrupt_read_audit_0001'
    Invoke-Sql @"
INSERT INTO products.Products
(ProductId, WorkspaceId, Profile, NormalizedSku, ArchivedAt, ArchiveReason, CreatedAt, UpdatedAt, Version)
VALUES ('$corruptProductId', '$workspaceId', N'{', 'SKU-CORRUPT-READ-AUDIT', NULL, NULL, '2026-08-31T00:00:00Z', '2026-08-31T00:00:00Z', 0);
"@
    Assert-True ((Measure-ProductRead "/products/$corruptProductId" (New-Headers $token $workspaceId)).Probe -eq '500|0') 'Corrupt persisted Product detail fails before owner audit'
    Assert-True ((Measure-ProductRead '/products' (New-Headers $token $workspaceId)).Probe -eq '500|0') 'Corrupt persisted Product list fails before owner audit'
    Invoke-Sql "DELETE FROM products.Products WHERE ProductId='$corruptProductId';"
    Assert-True ((Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)).Probe -eq '200|1') 'Healthy Product detail recovers with one audit'

    Invoke-Sql @"
CREATE TRIGGER products.TR_AuditRecords_CanonicalReadFailureProbe
ON products.AuditRecords
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE Operation IN ('listProducts', 'getProduct'))
        THROW 51000, 'Canonical Product read-audit persistence probe.', 1;
END;
"@
    Assert-True ((Measure-ProductRead '/products' (New-Headers $token $workspaceId)).Probe -eq '500|0') 'List returns no success when owner audit persistence fails'
    Assert-True ((Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)).Probe -eq '500|0') 'Detail returns no success when owner audit persistence fails'
    Invoke-Sql 'DROP TRIGGER products.TR_AuditRecords_CanonicalReadFailureProbe;'
    Assert-True ((Measure-ProductRead "/products/$productId" (New-Headers $token $workspaceId)).Probe -eq '200|1') 'Canonical detail recovers after audit persistence probe removal'

    $canonicalAuditDump = Invoke-SqlScalar "SELECT STRING_AGG(CONCAT(AuditId,'|',Operation,'|',WorkspaceId,'|',ActorId,'|',ISNULL(AggregateId,''),'|',RequestId,'|',CorrelationId,'|',Outcome,'|',ISNULL(CAST(NewVersion AS varchar(32)),'')), ' ') FROM products.AuditRecords WHERE Outcome='READ' AND Operation IN ('listProducts','getProduct');"
    Assert-True ($canonicalAuditDump -notmatch 'SKU-CORE|Core Product|Professional Services|Authoritative Product fixture|10\.125|verified|core') 'Canonical read evidence stores no Product business values'
    Assert-True ((Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$foreignWorkspaceId' AND Outcome='READ' AND Operation IN ('listProducts','getProduct');") -eq '0') 'No foreign Workspace canonical owner evidence'

    $auditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='COMMITTED';")
    $readAuditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='READ';")
    $outboxCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.OutboxMessages WHERE WorkspaceId='$workspaceId';")
    Assert-True ($auditCount -eq 13) 'Immutable command audit count'
    Assert-True ($readAuditCount -ge 8) 'Immutable Product READ_AUDIT count'
    Assert-True ($outboxCount -eq 11) 'Atomic Product outbox count'

    $productsRoot = Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.Sales/Products"
    $productsSource = (Get-ChildItem -LiteralPath $productsRoot -Recurse -File -Filter '*.cs' | Get-Content -Raw) -join "`n"
    $endpointSource = Get-Content -Raw -LiteralPath (Join-Path $productsRoot 'Contracts/ProductsEndpoints.cs')
    Assert-True ([regex]::Matches($endpointSource, 'MapGet\(endpoints,').Count -eq 4) 'Product route surface remains unchanged'
    Assert-True ([regex]::Matches($endpointSource, 'Map(Post|Put)\(endpoints,').Count -eq 6) 'Existing Product mutation route count remains unchanged'
    Assert-True ($productsSource -notmatch '\b(Quotes|Orders|Invoices|Payments|Shipping)DbContext\b') 'Products adds no foreign DbContext'
    Assert-True ($productsSource -notmatch 'IReadAuditService|GenericAudit|READ_ACCESS_LOG framework') 'Products adds no generic audit framework'
    Assert-True ($productsSource -notmatch 'WF-16|WF-22|QuoteToOrder|ProductWorkflow') 'Products adds no workflow implementation'

    if ($RunConnectedAcceptance) {
        Invoke-ConnectedBrowserAcceptance $token $workspaceId $productId
    }

    Invoke-Sql "DELETE rc FROM access.RoleCapabilities rc INNER JOIN access.Roles r ON r.RoleId=rc.RoleId WHERE r.WorkspaceId='$workspaceId' AND rc.Capability='products.create';"
    $beforeDenied = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.Products WHERE WorkspaceId='$workspaceId';")
    $denied = Send-Json 'POST' '/products' (Product-Body 'SKU-DENIED' 'Denied Product') (New-Headers $token $workspaceId 'idem-product-denied')
    Assert-Status $denied 403 'Application-boundary Product capability denial'
    $afterDenied = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.Products WHERE WorkspaceId='$workspaceId';")
    Assert-True ($beforeDenied -eq $afterDenied) 'Authorization denial persisted no Product'
}
finally {
    Stop-ApiHost $process
    $client.Dispose()
}

$env:ConnectionStrings__UnicoreCRM = $connection
$pending = & dotnet ef migrations has-pending-model-changes --project (Join-Path $solutionRoot 'src/UnicoreCRM.Sales') --context ProductsDbContext --no-build 2>&1
if ($LASTEXITCODE -ne 0) { throw "Products model verification failed: $pending" }
$checks.Add('Products model pending changes=NONE')

[pscustomobject]@{
    Status = 'PASS'
    Database = $DatabaseName
    Checks = $checks
} | ConvertTo-Json -Depth 5
