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
    Assert-Status (Send-Json 'GET' '/products' $null (New-Headers $token $workspaceId)) 200 'listProducts'
    Assert-Status (Send-Json 'GET' "/products/$productId" $null (New-Headers $token $workspaceId)) 200 'getProduct'

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
    $invalidProjectionAuditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='READ' AND (Operation IS NULL OR Operation='' OR ActorId IS NULL OR ActorId='' OR AggregateId IS NULL OR AggregateId='' OR RequestId IS NULL OR RequestId='' OR CorrelationId IS NULL OR CorrelationId='' OR OccurredAt IS NULL);")
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

    $foreignWorkspaceId = Invoke-SqlScalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$foreignWorkspaceKey';"
    Invoke-Sql "UPDATE products.Products SET WorkspaceId='$foreignWorkspaceId' WHERE ProductId='$thirdId';"
    $crossWorkspace = Send-Json 'GET' "/products/$thirdId" $null (New-Headers $token $workspaceId)
    Assert-Status $crossWorkspace 403 'Cross-Workspace Product access'
    Assert-True (($crossWorkspace.Body | ConvertFrom-Json).code -eq 'WORKSPACE_MISMATCH') 'Cross-Workspace stable error'

    $auditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='COMMITTED';")
    $readAuditCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.AuditRecords WHERE WorkspaceId='$workspaceId' AND Outcome='READ';")
    $outboxCount = [int] (Invoke-SqlScalar "SELECT COUNT(*) FROM products.OutboxMessages WHERE WorkspaceId='$workspaceId';")
    Assert-True ($auditCount -eq 13) 'Immutable command audit count'
    Assert-True ($readAuditCount -ge 8) 'Immutable Product READ_AUDIT count'
    Assert-True ($outboxCount -eq 11) 'Atomic Product outbox count'

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
