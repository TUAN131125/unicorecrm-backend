<#
.SYNOPSIS
    Reproducible Order Owner-Local Read Core verification against an isolated database and real ApiHost.

.DESCRIPTION
    Orders has no admitted mutation API in this slice. This harness applies the real Orders migration,
    seeds Orders-owned read state directly with controlled SQL, and exercises only GET /orders and
    GET /orders/{orderId}. Direct fixture seeding does not create a production Order command.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5344,

    [int] $ReadyTimeoutSeconds = 420,

    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$script:Passed = 0
$script:Failed = 0
$script:Results = New-Object System.Collections.ArrayList
$script:RequestCounter = 0
$script:BaseUrl = "http://127.0.0.1:$Port"
$script:Token = $null
$script:WorkspaceId = $null

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
    return "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}

function Invoke-Sql {
    param([string] $Query, [string] $Database = 'master')
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString -Database $Database)
    $command = $null
    $reader = $null
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        $reader = $command.ExecuteReader()
        $rows = New-Object System.Collections.ArrayList
        while ($reader.Read()) {
            $row = @{}
            for ($index = 0; $index -lt $reader.FieldCount; $index++) {
                $name = $reader.GetName($index)
                if ([string]::IsNullOrWhiteSpace($name)) { $name = "Value$index" }
                $row[$name] = $reader.GetValue($index)
            }
            [void]$rows.Add([pscustomobject]$row)
        }
        return $rows
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $command) { $command.Dispose() }
        $connection.Close()
        $connection.Dispose()
    }
}

function Invoke-SqlNonQuery {
    param([string] $Query, [string] $Database = 'master')
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString -Database $Database)
    $command = $null
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        [void]$command.ExecuteNonQuery()
    }
    finally {
        if ($null -ne $command) { $command.Dispose() }
        $connection.Close()
        $connection.Dispose()
    }
}

function Get-Scalar {
    param([string] $Query, [string] $Database)
    $rows = Invoke-Sql -Query $Query -Database $Database
    if ($rows.Count -eq 0) { return $null }
    $property = ($rows[0].PSObject.Properties | Select-Object -First 1).Name
    return $rows[0].$property
}

function New-RequestId {
    $script:RequestCounter++
    return ('req-orders-read-{0:d6}' -f $script:RequestCounter)
}

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Path,
        [string] $Body,
        [string] $Token,
        [string] $WorkspaceId,
        [string] $IdempotencyKey
    )
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId))
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-orders-read-core-0001')
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        [void]$request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token")
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) {
        [void]$request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId)
    }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) {
        [void]$request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey)
    }
    if (-not [string]::IsNullOrEmpty($Body)) {
        $request.Content = New-Object System.Net.Http.StringContent ($Body, [Text.Encoding]::UTF8, 'application/json')
    }

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient ($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    $response = $null
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $client.Dispose()
        $request.Dispose()
    }
    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        try { $payload = $raw | ConvertFrom-Json } catch { $payload = $null }
    }
    return [pscustomobject]@{ Status = $status; Body = $payload; Raw = $raw }
}

function Invoke-Order {
    param([string] $Method, [string] $Path, [string] $Body)
    return Invoke-Api -Method $Method -Path $Path -Body $Body -Token $script:Token -WorkspaceId $script:WorkspaceId
}

function Set-OrderScope {
    param([string] $RoleId, [string] $Scope)
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_orders_read_core';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_orders_read_core', '$RoleId', 'orders', '$Scope', '[]');
"@
}

function Clear-OrderFields {
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_orders_read_%'"
}

function Same-Problem {
    param($Left, $Right)
    return $Left.Status -eq $Right.Status `
        -and $Left.Body.code -eq $Right.Body.code `
        -and $Left.Body.type -eq $Right.Body.type `
        -and $Left.Body.title -eq $Right.Body.title `
        -and $Left.Body.status -eq $Right.Body.status
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostProject = Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
$salesProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/UnicoreCRM.Sales.csproj'
$demoEmail = 'orders.read.provisioned@example.test'
$demoPassword = 'Orders-Read-Core!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-orders-read-$([Guid]::NewGuid().ToString('N')).log")
$orderA = 'order_read_core_a'
$orderB = 'order_read_core_b'
$orderC = 'order_read_core_c'
$orderForeign = 'order_read_core_foreign'
$orderUnknown = 'order_read_core_unknown'
$secretNote = 'ORDER-A-NOTES-EXCLUDED-SECRET'
$foreignSecret = 'ORDER-FOREIGN-SECRET-VALUE'

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

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = New-ConnectionString -Database $DatabaseName
    $env:Development__ApplyMigrations = 'true'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $env:UNICORE_DEV_SEED_ENABLED = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $demoEmail
    $env:IdentityAuth__DevelopmentBootstrap__Password = $demoPassword
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Orders Provisioning Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'

    $hostProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--no-build', '--no-launch-profile', '--project', $hostProject) `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

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
        catch { }
    }
    if (-not $ready) { throw "ApiHost did not become ready within $ReadyTimeoutSeconds seconds. See $logPath" }

    Add-Result 'unauthenticated list rejected' '401' (Invoke-Api -Method 'GET' -Path '/orders' -WorkspaceId 'ws_unknown').Status
    Add-Result 'unauthenticated detail rejected' '401' (Invoke-Api -Method 'GET' -Path "/orders/$orderA" -WorkspaceId 'ws_unknown').Status

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' `
        -IdempotencyKey 'idem-orders-read-signin-0001' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    $callerMemberId = $session.Body.principal.memberId
    $provisioning = Invoke-Api -Method 'POST' -Path '/workspaces/initial-provisioning' `
        -Token $script:Token -IdempotencyKey 'idem-orders-read-provisioning-0001' `
        -Body '{"name":"Orders Read Workspace"}'
    Add-Result 'initial Workspace provisioning succeeds' '201' $provisioning.Status
    $script:WorkspaceId = $provisioning.Body.workspaceId
    $foreignWorkspaceId = 'ws_orders_read_foreign'
    $roleId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)' AND Name = 'Workspace Owner'"
    if ([string]::IsNullOrWhiteSpace($script:WorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($callerMemberId) `
        -or [string]::IsNullOrWhiteSpace($roleId)) {
        throw 'The Development identity/workspace/access fixture was not provisioned.'
    }

    $defaultCapability = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'orders.read'"
    Add-Result 'initial provisioning does not invent orders.read' '0' ([string]$defaultCapability)
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'orders.read')"

    Add-Result 'fresh migration created Orders table' '1' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'orders' AND TABLE_NAME = 'Orders'"))
    Add-Result 'fresh migration contains no Orders read-audit table' '0' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'orders' AND TABLE_NAME = 'ReadAuditRecords'"))
    Add-Result 'fresh migration recorded exactly one Orders migration' '1' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM orders.__EFMigrationsHistory"))

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Workspaces WHERE WorkspaceId = '$foreignWorkspaceId')
INSERT INTO workspace.Workspaces (WorkspaceId, [Key], Name, LogoText, CreatedAt)
VALUES ('$foreignWorkspaceId', 'orders-read-foreign', 'Orders Read Foreign Workspace', 'OF', SYSUTCDATETIME());

INSERT INTO orders.Orders
(WorkspaceId, OrderId, OrderNumber, OrderDate, BuyerType, BuyerId, ContactId, SourceLeadId,
 SourceQuoteId, SourceQuoteNumber, SourceDealId, State, LineItemsJson, AdjustmentsJson,
 SubtotalAmount, SubtotalCurrency, DiscountTotalAmount, DiscountTotalCurrency,
 TaxTotalAmount, TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency, Currency,
 ConfirmedAt, CompletedAt, CancelledAt, ExpectedDeliveryDate, RecipientName, RecipientPhone,
 RecipientEmail, ShippingAddressJson, OwnerId, Notes, CreditPolicyEvaluationJson, ActionsJson,
 ArchivedAt, ArchiveReason, ResourceVersion, CreatedAt, UpdatedAt, CreditApprovalJson)
VALUES
('$($script:WorkspaceId)', '$orderA', 'ORD-2026-0001', '2026-08-27', 'ORGANIZATION_ACCOUNT', 'organization_order_buyer_a',
 'contact_order_a', 'lead_order_a', 'quote_source_a', 'Q-2026-0001', 'deal_order_a', 'CONFIRMED',
 N'[{"id":"order_line_a","productId":"product_scalar_a","skuSnapshot":"SKU-ORDER-EXCLUDED","productNameSnapshot":"Historical Product Order Snapshot","productTypeSnapshot":"SERVICE","descriptionSnapshot":"Line JSON excluded search value","quantity":"2.5","unitPrice":{"amount":"493.827156","currency":"USD"},"discountRate":"10.25","taxRate":"8.5","taxMode":"EXCLUSIVE","billingCycleSnapshot":"MONTHLY","lineSubtotal":{"amount":"1234.56789","currency":"USD"},"lineDiscountAmount":{"amount":"126.543209","currency":"USD"},"lineTaxAmount":{"amount":"249.9","currency":"USD"},"lineTotal":{"amount":"1357.924678","currency":"USD"}}]',
 N'[{"id":"order_adjustment_a","label":"Order adjustment excluded","type":"DISCOUNT","calculation":"FIXED_AMOUNT","value":"126.543209","amount":{"amount":"126.543209","currency":"USD"}}]',
 1234.567890, 'USD', 126.543209, 'USD', 249.900000, 'USD', 1357.924678, 'USD', 'USD',
 '2026-08-28T01:02:03+07:00', NULL, NULL, '2026-09-30', 'Acme Delivery Recipient', '+84-000-ORDER',
 'order-recipient@example.test', N'{"line1":"Shipping excluded avenue","line2":"Suite 8","ward":"Ward A","district":"District A","city":"Ho Chi Minh City","country":"VN","postalCode":"700000"}',
 '$callerMemberId', '$secretNote',
 N'{"status":"APPROVAL_REQUIRED","blockerCodes":["CREDIT_APPROVAL_REQUIRED"],"policyVersion":"credit-policy-excluded","evaluatedAt":"2026-08-28T00:00:00Z"}',
 N'{"confirm":{"allowed":false,"blockerCodes":["ORDER_CONFIRMATION_BLOCKED"]},"cancel":{"allowed":true,"blockerCodes":[]}}',
 NULL, NULL, 7, '2026-08-27T08:00:00+07:00', '2026-08-29T09:10:11+07:00',
 N'{"id":"credit_approval_excluded","state":"APPROVED","amount":{"amount":"1357.924678","currency":"USD"},"policyVersion":"approval-policy-excluded","orderResourceVersion":7,"paymentPlanResourceVersion":3,"resourceVersion":2}');

INSERT INTO orders.Orders
(WorkspaceId, OrderId, OrderNumber, OrderDate, BuyerType, BuyerId, State, LineItemsJson,
 SubtotalAmount, SubtotalCurrency, DiscountTotalAmount, DiscountTotalCurrency, TaxTotalAmount,
 TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency, Currency, RecipientName, ActionsJson,
 ResourceVersion, CreatedAt, UpdatedAt)
VALUES
('$($script:WorkspaceId)', '$orderB', 'ORD-2026-0002', '2026-08-29', 'CONTACT', 'contact_order_buyer_b', 'DRAFT',
 N'[{"id":"order_line_b","productId":"product_scalar_b","productNameSnapshot":"Standalone Order Snapshot","quantity":"1","unitPrice":{"amount":"1000000","currency":"VND"},"discountRate":"0","taxMode":"NONE","lineSubtotal":{"amount":"1000000","currency":"VND"},"lineDiscountAmount":{"amount":"0","currency":"VND"},"lineTaxAmount":{"amount":"0","currency":"VND"},"lineTotal":{"amount":"1000000","currency":"VND"}}]',
 1000000, 'VND', 0, 'VND', 0, 'VND', 1000000, 'VND', 'VND', 'Beta Recipient',
 N'{"confirm":{"allowed":true,"blockerCodes":[]},"cancel":{"allowed":true,"blockerCodes":[]}}',
 0, '2026-08-29T00:00:00Z', '2026-08-30T00:00:00Z');

INSERT INTO orders.Orders
(WorkspaceId, OrderId, OrderNumber, OrderDate, BuyerType, BuyerId, SourceDealId, State, LineItemsJson,
 SubtotalAmount, SubtotalCurrency, DiscountTotalAmount, DiscountTotalCurrency, TaxTotalAmount,
 TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency, Currency, CompletedAt, RecipientName,
 ActionsJson, ResourceVersion, CreatedAt, UpdatedAt)
VALUES
('$($script:WorkspaceId)', '$orderC', 'ORD-2026-0003', '2026-08-29', 'CONTACT', 'contact_order_buyer_c', 'deal_order_c', 'COMPLETED',
 N'[{"id":"order_line_c","productId":"product_scalar_c","productNameSnapshot":"Third Order Snapshot","quantity":"1","unitPrice":{"amount":"1000000","currency":"VND"},"discountRate":"0","taxMode":"NONE","lineSubtotal":{"amount":"1000000","currency":"VND"},"lineDiscountAmount":{"amount":"0","currency":"VND"},"lineTaxAmount":{"amount":"0","currency":"VND"},"lineTotal":{"amount":"1000000","currency":"VND"}}]',
 1000000, 'VND', 0, 'VND', 0, 'VND', 1000000, 'VND', 'VND', '2026-08-30T00:00:00Z', 'Gamma Recipient',
 N'{"confirm":{"allowed":false,"blockerCodes":["ORDER_LIFECYCLE_CONFLICT"]},"cancel":{"allowed":false,"blockerCodes":["ORDER_CANCELLATION_BLOCKED"]}}',
 1, '2026-08-29T00:00:00Z', '2026-08-30T00:00:00Z');

INSERT INTO orders.Orders
(WorkspaceId, OrderId, OrderNumber, OrderDate, BuyerType, BuyerId, State, LineItemsJson,
 SubtotalAmount, SubtotalCurrency, DiscountTotalAmount, DiscountTotalCurrency, TaxTotalAmount,
 TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency, Currency, RecipientName, Notes, ActionsJson,
 ResourceVersion, CreatedAt, UpdatedAt)
VALUES
('$foreignWorkspaceId', '$orderForeign', 'ORD-FOREIGN-SECRET', '2026-08-30', 'CONTACT', 'contact_foreign', 'DRAFT',
 N'[{"id":"order_line_foreign","productId":"product_foreign","productNameSnapshot":"$foreignSecret","quantity":"1","unitPrice":{"amount":"99.99","currency":"USD"},"discountRate":"0","taxMode":"NONE","lineSubtotal":{"amount":"99.99","currency":"USD"},"lineDiscountAmount":{"amount":"0","currency":"USD"},"lineTaxAmount":{"amount":"0","currency":"USD"},"lineTotal":{"amount":"99.99","currency":"USD"}}]',
 99.99, 'USD', 0, 'USD', 0, 'USD', 99.99, 'USD', 'USD', '$foreignSecret', '$foreignSecret',
 N'{"confirm":{"allowed":true,"blockerCodes":[]},"cancel":{"allowed":true,"blockerCodes":[]}}',
 0, '2026-08-30T00:00:00Z', '2026-08-30T01:00:00Z');
"@

    Set-OrderScope -RoleId $roleId -Scope 'Workspace'

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'orders.read'"
    Add-Result 'missing orders.read denies list' '403' (Invoke-Order -Method 'GET' -Path '/orders').Status
    Add-Result 'missing orders.read denies detail' '403' (Invoke-Order -Method 'GET' -Path "/orders/$orderA").Status
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'orders.read')"

    $workspaceList = Invoke-Order -Method 'GET' -Path '/orders'
    Add-Result 'WORKSPACE list succeeds' '200' $workspaceList.Status
    Add-Result 'list returns only current Workspace Orders' '3' ([string]$workspaceList.Body.items.Count)
    Add-Result 'foreign Order absent from list' 'False' ($workspaceList.Body.items.id -contains $orderForeign).ToString()
    Add-Result 'foreign Order value absent from list bytes' 'True' ($workspaceList.Raw -notmatch [regex]::Escape($foreignSecret)).ToString()
    Add-Result 'list wrapper has exact top-level fields' 'items,pageInfo' `
        ((@($workspaceList.Body.PSObject.Properties.Name | Sort-Object)) -join ',')
    Add-Result 'default updatedAt DESC uses OrderId DESC tie-breaker' "$orderC,$orderB,$orderA" `
        (($workspaceList.Body.items.id) -join ',')
    Add-Result 'default list totalCount is Workspace/access filtered' '3' ([string]$workspaceList.Body.pageInfo.totalCount)

    foreach ($sortField in @('updatedAt','createdAt','orderDate','grandTotal','orderNumber')) {
        $sorted = Invoke-Order -Method 'GET' -Path "/orders?sortBy=$sortField&sortDirection=asc"
        Add-Result ("sort: {0} asc succeeds" -f $sortField) '200' $sorted.Status
        Add-Result ("sort: {0} asc uses same-direction OrderId tie-breaker" -f $sortField) `
            "$orderA,$orderB,$orderC" (($sorted.Body.items.id) -join ',')
    }
    Add-Result 'partial sort: sortBy alone defaults direction DESC' $orderC `
        (Invoke-Order -Method 'GET' -Path '/orders?sortBy=orderNumber').Body.items[0].id
    Add-Result 'partial sort: direction alone uses updatedAt ASC' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?sortDirection=asc').Body.items[0].id
    Add-Result 'explicit sort uses supplied field and direction' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?sortBy=orderNumber&sortDirection=asc').Body.items[0].id

    $cursorIds = New-Object System.Collections.ArrayList
    $cursor = $null
    do {
        $path = '/orders?limit=1&sortBy=updatedAt&sortDirection=desc'
        if (-not [string]::IsNullOrWhiteSpace($cursor)) { $path += '&cursor=' + [Uri]::EscapeDataString($cursor) }
        $page = Invoke-Order -Method 'GET' -Path $path
        Add-Result 'cursor page succeeds' '200' $page.Status
        if ($page.Body.items.Count -eq 1) { [void]$cursorIds.Add($page.Body.items[0].id) }
        $cursor = $page.Body.pageInfo.nextCursor
    } while ($page.Body.pageInfo.hasNextPage)
    Add-Result 'cursor continuation has no duplicates or skips' "$orderC,$orderB,$orderA" ($cursorIds -join ',')
    Add-Result 'cursor continuation returns every visible Order once' '3' ([string](@($cursorIds | Select-Object -Unique).Count))

    Add-Result 'state filter is exact' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?state=CONFIRMED').Body.items[0].id
    Add-Result 'sourceQuoteId filter is exact' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?sourceQuoteId=quote_source_a').Body.items[0].id
    Add-Result 'sourceDealId filter is exact' $orderC `
        (Invoke-Order -Method 'GET' -Path '/orders?sourceDealId=deal_order_c').Body.items[0].id
    Add-Result 'buyer filter is exact' $orderB `
        (Invoke-Order -Method 'GET' -Path '/orders?buyerType=CONTACT&buyerId=contact_order_buyer_b').Body.items[0].id

    Add-Result 'search: orderNumber exact match' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?search=ORD-2026-0001').Body.items[0].id
    Add-Result 'search: orderNumber partial substring' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?search=2026-0001').Body.items[0].id
    Add-Result 'search: orderNumber case-insensitive substring' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?search=ord-2026-0001').Body.items[0].id
    Add-Result 'search: recipientName exact match' $orderA `
        (Invoke-Order -Method 'GET' -Path ('/orders?search=' + [Uri]::EscapeDataString('Acme Delivery Recipient'))).Body.items[0].id
    Add-Result 'search: recipientName partial substring' $orderA `
        (Invoke-Order -Method 'GET' -Path '/orders?search=Delivery').Body.items[0].id
    Add-Result 'search: recipientName case-insensitive substring' $orderA `
        (Invoke-Order -Method 'GET' -Path ('/orders?search=' + [Uri]::EscapeDataString('acme delivery recipient'))).Body.items[0].id
    Add-Result 'search: leading/trailing whitespace is trimmed' $orderA `
        (Invoke-Order -Method 'GET' -Path ('/orders?search=' + [Uri]::EscapeDataString('  Delivery  '))).Body.items[0].id
    Add-Result 'search: empty applies no filter' '3' ([string](Invoke-Order -Method 'GET' -Path '/orders?search=').Body.items.Count)
    Add-Result 'search: whitespace applies no filter' '3' ([string](Invoke-Order -Method 'GET' -Path '/orders?search=%20%20').Body.items.Count)
    Add-Result 'search: percent is literal, not a SQL wildcard' '0' `
        ([string](Invoke-Order -Method 'GET' -Path '/orders?search=%25').Body.items.Count)
    Add-Result 'search: underscore is literal, not a SQL wildcard' '0' `
        ([string](Invoke-Order -Method 'GET' -Path '/orders?search=_').Body.items.Count)
    Add-Result 'search: over 240 characters is rejected' '422' `
        (Invoke-Order -Method 'GET' -Path ('/orders?search=' + ('a' * 241))).Status

    $excludedSearchValues = @(
        'organization_order_buyer_a', 'quote_source_a', 'deal_order_a', $secretNote,
        'Historical Product Order Snapshot', 'SKU-ORDER-EXCLUDED', 'Order adjustment excluded',
        'Shipping excluded avenue', 'credit-policy-excluded', 'credit_approval_excluded',
        'payment-data-does-not-exist'
    )
    foreach ($excludedSearchValue in $excludedSearchValues) {
        $excludedPath = '/orders?search=' + [Uri]::EscapeDataString($excludedSearchValue)
        Add-Result ("search: excluded value is not matched ({0})" -f $excludedSearchValue) '0' `
            ([string](Invoke-Order -Method 'GET' -Path $excludedPath).Body.items.Count)
    }
    Add-Result 'search: foreign Workspace row is absent' '0' `
        ([string](Invoke-Order -Method 'GET' -Path '/orders?search=ORD-FOREIGN-SECRET').Body.items.Count)
    $filtered = Invoke-Order -Method 'GET' -Path '/orders?search=Delivery&limit=1'
    Add-Result 'search: filtered totalCount is exact' '1' ([string]$filtered.Body.pageInfo.totalCount)
    Add-Result 'search: filtered pageInfo has no false continuation' 'False' ([string]$filtered.Body.pageInfo.hasNextPage)

    Add-Result 'invalid state is rejected' '422' (Invoke-Order -Method 'GET' -Path '/orders?state=confirmed').Status
    Add-Result 'invalid cursor is rejected' '422' (Invoke-Order -Method 'GET' -Path '/orders?cursor=not-a-cursor').Status
    Add-Result 'invalid limit type is rejected' '422' (Invoke-Order -Method 'GET' -Path '/orders?limit=abc').Status

    $detail = Invoke-Order -Method 'GET' -Path "/orders/$orderA"
    Add-Result 'GET Order succeeds' '200' $detail.Status
    Add-Result 'detail returns correct Order' $orderA $detail.Body.id
    $requiredFields = @(
        'id','orderNumber','orderDate','buyerRef','state','lineItems','subtotal','discountTotal',
        'taxTotal','grandTotal','currency','actions','resourceVersion','createdAt','updatedAt'
    )
    $allowedFields = @($requiredFields + @(
        'contactId','sourceLeadId','sourceQuoteId','sourceQuoteNumber','sourceDealId','adjustments',
        'confirmedAt','completedAt','cancelledAt','expectedDeliveryDate','recipientName','recipientPhone',
        'recipientEmail','shippingAddress','ownerId','notes','creditPolicyEvaluation','archivedAt',
        'archiveReason','creditApproval'
    ))
    $actualFields = @($detail.Body.PSObject.Properties.Name)
    Add-Result 'detail contains every exact required wire field' '0' `
        ([string](@($requiredFields | Where-Object { $actualFields -cnotcontains $_ }).Count))
    Add-Result 'detail contains no field outside exact wire' '0' `
        ([string](@($actualFields | Where-Object { $allowedFields -cnotcontains $_ }).Count))
    Add-Result 'buyerRef enum and ID persist exactly' 'ORGANIZATION_ACCOUNT|organization_order_buyer_a' `
        ("{0}|{1}" -f $detail.Body.buyerRef.type, $detail.Body.buyerRef.id)
    Add-Result 'Order state enum persists exactly' 'CONFIRMED' $detail.Body.state
    Add-Result 'money decimal is a JSON string with scale-six value' 'String|1357.924678|USD' `
        ("{0}|{1}|{2}" -f $detail.Body.grandTotal.amount.GetType().Name, $detail.Body.grandTotal.amount, $detail.Body.grandTotal.currency)
    Add-Result 'line snapshot enums/money persist exactly' 'product_scalar_a|493.827156|EXCLUSIVE' `
        ("{0}|{1}|{2}" -f $detail.Body.lineItems[0].productId, $detail.Body.lineItems[0].unitPrice.amount, $detail.Body.lineItems[0].taxMode)
    Add-Result 'timestamp projects canonical UTC Z' '2026-08-29T02:10:11.0000000Z' `
        $detail.Body.updatedAt.ToUniversalTime().ToString('O')
    Add-Result 'business date projects exact date' '2026-08-27|2026-09-30' `
        ("{0}|{1}" -f $detail.Body.orderDate, $detail.Body.expectedDeliveryDate)
    Add-Result 'optional nested documents round-trip' 'Ho Chi Minh City|APPROVAL_REQUIRED|APPROVED' `
        ("{0}|{1}|{2}" -f $detail.Body.shippingAddress.city, $detail.Body.creditPolicyEvaluation.status, $detail.Body.creditApproval.state)

    $minimal = Invoke-Order -Method 'GET' -Path "/orders/$orderB"
    Add-Result 'required-only Order succeeds' '200' $minimal.Status
    Add-Result 'absent optional Order fields are omitted' 'True' `
        (($minimal.Raw -notmatch 'sourceQuoteId|adjustments|shippingAddress|creditApproval|confirmedAt')).ToString()

    $unknown = Invoke-Order -Method 'GET' -Path "/orders/$orderUnknown"
    $foreign = Invoke-Order -Method 'GET' -Path "/orders/$orderForeign"
    Add-Result 'unknown Order follows nondisclosure status' '404' $unknown.Status
    Add-Result 'foreign Workspace Order follows nondisclosure status' '404' $foreign.Status
    Add-Result 'unknown and foreign problem behavior match' 'True' (Same-Problem $unknown $foreign).ToString()
    Add-Result 'foreign Order response leaks no value' 'True' ($foreign.Raw -notmatch [regex]::Escape($foreignSecret)).ToString()

    Set-OrderScope -RoleId $roleId -Scope 'Own'
    $ownDetail = Invoke-Order -Method 'GET' -Path "/orders/$orderA"
    Add-Result 'OWN fails closed despite matching ownerId wire field' '404' $ownDetail.Status
    Add-Result 'OWN denial does not leak existence' 'True' (Same-Problem $ownDetail $unknown).ToString()
    Add-Result 'OWN list fails closed' '0' ([string](Invoke-Order -Method 'GET' -Path '/orders').Body.items.Count)
    foreach ($scope in @('Team','Custom')) {
        Set-OrderScope -RoleId $roleId -Scope $scope
        Add-Result ("{0} detail fails closed" -f $scope.ToUpperInvariant()) '404' `
            (Invoke-Order -Method 'GET' -Path "/orders/$orderA").Status
        Add-Result ("{0} list fails closed" -f $scope.ToUpperInvariant()) '0' `
            ([string](Invoke-Order -Method 'GET' -Path '/orders').Body.items.Count)
    }

    Set-OrderScope -RoleId $roleId -Scope 'Workspace'
    Clear-OrderFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_orders_read_recipient', '$roleId', 'orders', 'recipientName', 'Hidden'),
('field_orders_read_notes', '$roleId', 'orders', 'notes', 'Masked');
"@
    $fieldDetail = Invoke-Order -Method 'GET' -Path "/orders/$orderA"
    Add-Result 'optional HIDDEN Order field is omitted' 'True' `
        ($fieldDetail.Body.PSObject.Properties.Name -cnotcontains 'recipientName').ToString()
    Add-Result 'MASKED Order field is withheld safely' 'True' `
        (($fieldDetail.Raw -notmatch '"notes"') -and ($fieldDetail.Raw -notmatch [regex]::Escape($secretNote))).ToString()
    Add-Result 'hidden recipientName does not participate in search' '0' `
        ([string](Invoke-Order -Method 'GET' -Path '/orders?search=Delivery').Body.items.Count)
    Clear-OrderFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_orders_read_required', '$roleId', 'orders', 'orderNumber', 'Hidden');
"@
    Add-Result 'required Order field restriction fails operation closed' '403' `
        (Invoke-Order -Method 'GET' -Path "/orders/$orderA").Status
    Clear-OrderFields

    $wrongWorkspace = Invoke-Api -Method 'GET' -Path "/orders/$orderA" `
        -Token $script:Token -WorkspaceId $foreignWorkspaceId
    Add-Result 'untrusted foreign Workspace header is denied' '403' $wrongWorkspace.Status

    $countBeforeMutationProbes = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM orders.Orders'
    Add-Result 'POST /orders mutation is not mapped' '405' (Invoke-Order -Method 'POST' -Path '/orders' -Body '{}').Status
    Add-Result 'PUT Order mutation is not mapped' '405' (Invoke-Order -Method 'PUT' -Path "/orders/$orderA" -Body '{}').Status
    Add-Result 'PATCH Order mutation is not mapped' '405' (Invoke-Order -Method 'PATCH' -Path "/orders/$orderA" -Body '{}').Status
    Add-Result 'DELETE Order mutation is not mapped' '405' (Invoke-Order -Method 'DELETE' -Path "/orders/$orderA").Status
    Add-Result 'Order confirmation route is absent' '404' (Invoke-Order -Method 'POST' -Path "/orders/$orderA/confirm" -Body '{}').Status
    Add-Result 'mutation probes change no Order state' ([string]$countBeforeMutationProbes) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM orders.Orders'))

    $ordersRoot = Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Orders'
    $ordersSource = (Get-ChildItem -LiteralPath $ordersRoot -Recurse -File -Filter '*.cs' | Get-Content -Raw) -join "`n"
    $endpointSource = Get-Content -Raw -LiteralPath (Join-Path $ordersRoot 'Contracts/OrdersEndpoints.cs')
    Add-Result 'Orders maps exactly the two admitted GET routes' '2' `
        ([string]([regex]::Matches($endpointSource, 'endpoints\.MapGet\(').Count))
    Add-Result 'Orders maps no mutation method' '0' `
        ([string]([regex]::Matches($endpointSource, 'endpoints\.Map(Post|Put|Patch|Delete)\(').Count))
    Add-Result 'Orders has no Product/Quote/Payment/Shipping runtime lookup' 'True' `
        (($ordersSource -notmatch '\b(Products|Quotes|Payments|Shipping)DbContext\b') `
            -and ($ordersSource -notmatch 'UnicoreCRM\.Sales\.(Products|Quotes)') `
            -and ($ordersSource -notmatch '\bI(Product|Quote|Payment|Shipping)[A-Za-z]*Reader\b')).ToString()
    Add-Result 'Orders has no foreign owner DbContext' 'True' `
        ($ordersSource -notmatch '\b(Quotes|Products|Payments|Shipping|CommercialEvidence|Customers)DbContext\b').ToString()
    Add-Result 'Orders adds no WF-12/WF-13/WF-22 or CommercialEvidence wiring' 'True' `
        (($ordersSource -notmatch 'WF-12|WF-13|WF-22|CommercialEvidence|PurchaseEvidence|convertAcceptedQuoteToOrderDraft')).ToString()
    Add-Result 'Orders has no owner-local durable read-audit runtime' 'True' `
        ($ordersSource -notmatch 'ReadAuditRecord|ReadAuditRecords|AddReadAudit').ToString()

    $logText = ''
    if (Test-Path -LiteralPath $logPath) { $logText += Get-Content -Raw -LiteralPath $logPath }
    if (Test-Path -LiteralPath "$logPath.err") { $logText += Get-Content -Raw -LiteralPath "$logPath.err" }
    Add-Result 'foreign Order value absent from host logs' 'True' ($logText -notmatch [regex]::Escape($foreignSecret)).ToString()
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit(10000) | Out-Null
    }

    Push-Location $repositoryRoot
    try {
        & dotnet ef migrations has-pending-model-changes --project $salesProject --context OrdersDbContext --no-build
        Add-Result 'no pending Orders EF model changes' '0' ([string]$LASTEXITCODE)
    }
    finally {
        Pop-Location
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
        catch { }
    }
}

$script:Results | ForEach-Object { Write-Host $_ }
Write-Host ("Orders Read Core verification: passed={0} failed={1}" -f $script:Passed, $script:Failed)
if ($script:Failed -ne 0) { throw 'Orders Read Core verification failed.' }
Write-Host 'ORDER LIST QUERY SEMANTICS: PASS'
Write-Host 'ORDER OWNER-LOCAL READ CORE: IMPLEMENTED AND VERIFIED'
