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
$script:MemberId = $null
$script:LastRequestId = $null

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
    $script:LastRequestId = 'req-orders-read-{0:d6}' -f $script:RequestCounter
    return $script:LastRequestId
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

function Get-OrderReadAuditCount {
    return [int](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM orders.ReadAuditRecords')
}

function Get-OrderRecordDecisionCount {
    return [int](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions WHERE ResourceKey = 'orders'")
}

function Measure-OrderRead {
    param([string] $Path)
    $before = Get-OrderReadAuditCount
    $response = Invoke-Order -Method 'GET' -Path $Path
    $delta = (Get-OrderReadAuditCount) - $before
    return [pscustomobject]@{
        Status = $response.Status
        Body = $response.Body
        Raw = $response.Raw
        Delta = $delta
        Probe = ('{0}|{1}' -f $response.Status, $delta)
    }
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

function ConvertTo-Base64Url {
    param([string] $Value)
    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Value)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Cursor-ValidationResult {
    param($Response)
    $hasCursorError = $null -ne $Response.Body.fieldErrors `
        -and @($Response.Body.fieldErrors.cursor).Count -gt 0
    return ('{0}|{1}|{2}' -f $Response.Status, $Response.Body.code, $hasCursorError)
}

function ConvertTo-SqlNVarCharLiteral {
    param([AllowNull()][string] $Value)
    if ([string]::IsNullOrEmpty($Value)) { return 'NULL' }
    return "N'$($Value.Replace("'", "''"))'"
}

function Set-CorruptOrderFixture {
    param(
        [string] $OrderId,
        [string] $Marker,
        [string] $LineItemsJson,
        [string] $ActionsJson,
        [AllowNull()][string] $AdjustmentsJson,
        [AllowNull()][string] $ShippingAddressJson,
        [AllowNull()][string] $CreditPolicyEvaluationJson,
        [AllowNull()][string] $CreditApprovalJson
    )
    $lineItemsSql = ConvertTo-SqlNVarCharLiteral $LineItemsJson
    $actionsSql = ConvertTo-SqlNVarCharLiteral $ActionsJson
    $adjustmentsSql = ConvertTo-SqlNVarCharLiteral $AdjustmentsJson
    $shippingSql = ConvertTo-SqlNVarCharLiteral $ShippingAddressJson
    $creditPolicySql = ConvertTo-SqlNVarCharLiteral $CreditPolicyEvaluationJson
    $creditApprovalSql = ConvertTo-SqlNVarCharLiteral $CreditApprovalJson
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM orders.Orders WHERE WorkspaceId = '$($script:WorkspaceId)' AND OrderId = '$OrderId';
INSERT INTO orders.Orders
(WorkspaceId, OrderId, OrderNumber, OrderDate, BuyerType, BuyerId, State, LineItemsJson,
 AdjustmentsJson, SubtotalAmount, SubtotalCurrency, DiscountTotalAmount, DiscountTotalCurrency,
 TaxTotalAmount, TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency, Currency, RecipientName,
 ShippingAddressJson, CreditPolicyEvaluationJson, ActionsJson, ResourceVersion, CreatedAt, UpdatedAt,
 CreditApprovalJson)
VALUES
('$($script:WorkspaceId)', '$OrderId', 'ORD-CORRUPT-NESTED', '2026-08-30', 'CONTACT',
 'contact_corrupt_nested', 'DRAFT', $lineItemsSql, $adjustmentsSql, 1, 'USD', 0, 'USD', 0, 'USD',
 1, 'USD', 'USD', '$Marker', $shippingSql, $creditPolicySql, $actionsSql, 0,
 '2026-08-30T00:00:00Z', '2026-08-30T02:00:00Z', $creditApprovalSql);
"@
}

function Remove-CorruptOrderFixture {
    param([string] $OrderId)
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM orders.Orders WHERE WorkspaceId = '$($script:WorkspaceId)' AND OrderId = '$OrderId'"
}

function Assert-CorruptOrderFailure {
    param([string] $Name, [string] $OrderId, [string] $Marker)
    $response = Measure-OrderRead -Path "/orders/$OrderId"
    Add-Result ("nested JSON: {0} fails closed" -f $Name) '500|INTERNAL_ERROR' `
        ('{0}|{1}' -f $response.Status, $response.Body.code)
    Add-Result ("nested JSON: {0} writes +0 owner audit" -f $Name) '0' ([string]$response.Delta)
    Add-Result ("nested JSON: {0} leaks no partial Order" -f $Name) 'True' `
        (($response.Raw -notmatch [regex]::Escape($OrderId)) `
            -and ($response.Raw -notmatch [regex]::Escape($Marker)) `
            -and ($response.Raw -notmatch 'Persisted Order|JsonException|lineItemsJson|actionsJson')).ToString()
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
$orderCorrupt = 'order_read_core_corrupt'
$secretNote = 'ORDER-A-NOTES-EXCLUDED-SECRET'
$foreignSecret = 'ORDER-FOREIGN-SECRET-VALUE'
$corruptSecret = 'ORDER-CORRUPT-NESTED-SECRET'

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
    $script:MemberId = $callerMemberId
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
    Add-Result 'fresh migration contains Orders read-audit table' '1' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'orders' AND TABLE_NAME = 'ReadAuditRecords'"))
    Add-Result 'fresh migration recorded exactly two Orders migrations' '2' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM orders.__EFMigrationsHistory"))
    Add-Result 'read-audit columns exact' `
        'ActorId,AuditId,CorrelationId,OccurredAt,Operation,Outcome,RecordId,RequestId,ResourceVersion,WorkspaceId' `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'orders' AND TABLE_NAME = 'ReadAuditRecords'
"@))
    Add-Result 'read-audit Workspace-leading index' '1' ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.indexes
WHERE object_id = OBJECT_ID('orders.ReadAuditRecords')
  AND name = 'IX_ReadAuditRecords_WorkspaceId_OccurredAt'
"@))
    Add-Result 'read-audit has zero foreign keys' '0' ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.foreign_keys
WHERE parent_object_id = OBJECT_ID('orders.ReadAuditRecords')
"@))

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
    Add-Result 'missing orders.read denies list with +0 audit' '403|0' `
        (Measure-OrderRead -Path '/orders').Probe
    Add-Result 'missing orders.read denies detail with +0 audit' '403|0' `
        (Measure-OrderRead -Path "/orders/$orderA").Probe
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'orders.read')"

    # Frozen READ_ACCESS_LOG: one owner row per successful invocation/page, after projection.
    $page1 = Measure-OrderRead -Path '/orders?limit=1&sortBy=updatedAt&sortDirection=desc'
    Add-Result 'read audit: first cursor page => 200 and +1' '200|1' $page1.Probe
    Add-Result 'read audit: first cursor page returns one Order' '1' ([string]$page1.Body.items.Count)
    $page1Cursor = $page1.Body.pageInfo.nextCursor
    $page2 = Measure-OrderRead -Path ('/orders?limit=1&sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($page1Cursor))
    Add-Result 'read audit: second cursor page => 200 and +1' '200|1' $page2.Probe
    $page2Cursor = $page2.Body.pageInfo.nextCursor
    $page3 = Measure-OrderRead -Path ('/orders?limit=1&sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($page2Cursor))
    Add-Result 'read audit: third cursor page => 200 and +1' '200|1' $page3.Probe
    Add-Result 'read audit: three keyset pages preserve order' "$orderC,$orderB,$orderA" `
        (($page1.Body.items.id, $page2.Body.items.id, $page3.Body.items.id) -join ',')

    $recordDecisionsBeforeList = Get-OrderRecordDecisionCount
    $multiRowPage = Measure-OrderRead -Path '/orders?limit=3'
    Add-Result 'read audit: multi-row page => 200 and +1, not per-row' '200|1' $multiRowPage.Probe
    Add-Result 'read audit: multi-row page contains three Orders' '3' ([string]$multiRowPage.Body.items.Count)
    Add-Result 'read audit: list writes zero per-row record decisions' '0' `
        ([string]((Get-OrderRecordDecisionCount) - $recordDecisionsBeforeList))
    $emptyPage = Measure-OrderRead -Path '/orders?search=order_read_audit_no_match'
    Add-Result 'read audit: empty successful page => 200 and +1' '200|1' $emptyPage.Probe
    Add-Result 'read audit: empty successful page contains zero Orders' '0' ([string]$emptyPage.Body.items.Count)
    Add-Result 'read audit: every list row has null recordId and resourceVersion' '0' `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM orders.ReadAuditRecords
WHERE Operation = 'listOrders' AND (RecordId IS NOT NULL OR ResourceVersion IS NOT NULL)
"@))

    $recordDecisionsBeforeDetail = Get-OrderRecordDecisionCount
    $detailAudit = Measure-OrderRead -Path "/orders/$orderA"
    $detailAuditRequestId = $script:LastRequestId
    Add-Result 'read audit: getOrder => 200 and +1' '200|1' $detailAudit.Probe
    Add-Result 'read audit: detail writes canonical record decision' '1' `
        ([string]((Get-OrderRecordDecisionCount) - $recordDecisionsBeforeDetail))
    Add-Result 'read audit: detail recordId and version exact' "$orderA|7" `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT CONCAT(RecordId, '|', ResourceVersion)
FROM orders.ReadAuditRecords WHERE RequestId = '$detailAuditRequestId'
"@))
    Add-Result 'read audit: detail version matches response' '7' ([string]$detailAudit.Body.resourceVersion)
    Add-Result 'read audit: provenance and exact operationId' `
        "getOrder|$($script:WorkspaceId)|$($script:MemberId)|$detailAuditRequestId|corr-orders-read-core-0001|READ|$orderA|7" `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT CONCAT(Operation, '|', WorkspaceId, '|', ActorId, '|', RequestId, '|', CorrelationId, '|', Outcome, '|', RecordId, '|', ResourceVersion)
FROM orders.ReadAuditRecords WHERE RequestId = '$detailAuditRequestId'
"@))
    Add-Result 'read audit: every row outcome is READ' '0' `
        ([string](Get-Scalar -Database $DatabaseName `
            -Query "SELECT COUNT(*) FROM orders.ReadAuditRecords WHERE Outcome <> 'READ'"))
    Add-Result 'read audit: only admitted operationIds are stored' '0' `
        ([string](Get-Scalar -Database $DatabaseName `
            -Query "SELECT COUNT(*) FROM orders.ReadAuditRecords WHERE Operation NOT IN ('listOrders', 'getOrder')"))

    Add-Result 'read audit: malformed authorized query => 422 and +0' '422|0' `
        (Measure-OrderRead -Path '/orders?limit=abc').Probe
    Add-Result 'read audit: malformed cursor => 422 and +0' '422|0' `
        (Measure-OrderRead -Path '/orders?cursor=not-a-cursor').Probe
    Add-Result 'read audit: cross-query cursor reuse => 422 and +0' '422|0' `
        (Measure-OrderRead -Path ('/orders?limit=1&search=different&sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($page1Cursor))).Probe
    Add-Result 'read audit: malformed orderId => 404 and +0' '404|0' `
        (Measure-OrderRead -Path '/orders/%20bad').Probe
    Add-Result 'read audit: unknown order => 404 and +0' '404|0' `
        (Measure-OrderRead -Path "/orders/$orderUnknown").Probe
    Add-Result 'read audit: foreign Workspace order => 404 and +0' '404|0' `
        (Measure-OrderRead -Path "/orders/$orderForeign").Probe

    Set-OrderScope -RoleId $roleId -Scope 'Own'
    Add-Result 'read audit: record-access denied detail => 404 and +0' '404|0' `
        (Measure-OrderRead -Path "/orders/$orderA").Probe
    Add-Result 'read audit: OWN empty list is successful and +1' '200|1' `
        (Measure-OrderRead -Path '/orders').Probe
    Set-OrderScope -RoleId $roleId -Scope 'Workspace'

    Clear-OrderFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_orders_read_audit_required', '$roleId', 'orders', 'state', 'Hidden');
"@
    Add-Result 'read audit: required hidden field list => 403 and +0' '403|0' `
        (Measure-OrderRead -Path '/orders').Probe
    Add-Result 'read audit: required hidden field detail => 403 and +0' '403|0' `
        (Measure-OrderRead -Path "/orders/$orderA").Probe
    Clear-OrderFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_orders_read_audit_filter', '$roleId', 'orders', 'sourceQuoteId', 'Hidden');
"@
    Add-Result 'read audit: hidden-filter rejection => 403 and +0' '403|0' `
        (Measure-OrderRead -Path '/orders?sourceQuoteId=quote_source_a').Probe
    Clear-OrderFields

    $auditDump = Get-Scalar -Database $DatabaseName -Query @"
SELECT STRING_AGG(CONCAT(AuditId, '|', Operation, '|', WorkspaceId, '|', ActorId, '|',
    ISNULL(RecordId, ''), '|', RequestId, '|', CorrelationId, '|', Outcome, '|',
    ISNULL(CAST(ResourceVersion AS varchar(32)), '')), ' ')
FROM orders.ReadAuditRecords
"@
    Add-Result 'read audit: no Order business values stored' 'True' `
        ([string]($auditDump -notmatch 'ORD-2026|ORD-FOREIGN|Acme Delivery|Beta Recipient|Gamma Recipient|ORDER-A-NOTES|ORDER-FOREIGN-SECRET|contact_order|organization_order|product_scalar|quote_source|deal_order|USD|VND|1357\.924678|1000000'))
    Add-Result 'read audit: no foreign Workspace owner evidence' '0' `
        ([string](Get-Scalar -Database $DatabaseName `
            -Query "SELECT COUNT(*) FROM orders.ReadAuditRecords WHERE WorkspaceId = '$foreignWorkspaceId'"))

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

    foreach ($sortField in @('updatedAt','createdAt','orderDate','grandTotal','orderNumber')) {
        foreach ($sortDirection in @('asc','desc')) {
            $cursorIds = New-Object System.Collections.ArrayList
            $cursor = $null
            do {
                $path = "/orders?limit=1&sortBy=$sortField&sortDirection=$sortDirection"
                if (-not [string]::IsNullOrWhiteSpace($cursor)) { $path += '&cursor=' + [Uri]::EscapeDataString($cursor) }
                $page = Invoke-Order -Method 'GET' -Path $path
                Add-Result ("keyset page succeeds: {0} {1}" -f $sortField, $sortDirection) '200' $page.Status
                if ($page.Body.items.Count -eq 1) { [void]$cursorIds.Add($page.Body.items[0].id) }
                $cursor = $page.Body.pageInfo.nextCursor
            } while ($page.Body.pageInfo.hasNextPage)
            $expectedCursorIds = if ($sortDirection -eq 'asc') {
                "$orderA,$orderB,$orderC"
            } else {
                "$orderC,$orderB,$orderA"
            }
            Add-Result ("keyset has no skips: {0} {1}" -f $sortField, $sortDirection) `
                $expectedCursorIds ($cursorIds -join ',')
            Add-Result ("keyset has no duplicates: {0} {1}" -f $sortField, $sortDirection) `
                '3' ([string](@($cursorIds | Select-Object -Unique).Count))
        }
    }

    $firstCursorPage = Invoke-Order -Method 'GET' `
        -Path '/orders?limit=1&sortBy=updatedAt&sortDirection=desc'
    $baseCursor = $firstCursorPage.Body.pageInfo.nextCursor
    Add-Result 'first keyset page returns continuation' 'True' `
        (-not [string]::IsNullOrWhiteSpace($baseCursor)).ToString()
    $changedLimitPage = Invoke-Order -Method 'GET' `
        -Path ('/orders?limit=2&sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($baseCursor))
    Add-Result 'same cursor accepts a different valid page size' '200' $changedLimitPage.Status
    Add-Result 'changed page size continues after last returned row' "$orderB,$orderA" `
        (($changedLimitPage.Body.items.id) -join ',')
    Add-Result 'changed page size computes lookahead correctly' 'False' `
        ([string]$changedLimitPage.Body.pageInfo.hasNextPage)

    $cursorReusePaths = [ordered]@{
        'different search' = '/orders?search=Delivery'
        'different state' = '/orders?state=DRAFT'
        'different sourceQuoteId' = '/orders?sourceQuoteId=quote_source_a'
        'different sourceDealId' = '/orders?sourceDealId=deal_order_c'
        'different buyer filter' = '/orders?buyerType=CONTACT&buyerId=contact_order_buyer_b'
        'different sortBy' = '/orders?sortBy=orderNumber&sortDirection=desc'
        'different direction' = '/orders?sortBy=updatedAt&sortDirection=asc'
    }
    foreach ($reuseCase in $cursorReusePaths.GetEnumerator()) {
        $separator = if ($reuseCase.Value.Contains('?')) { '&' } else { '?' }
        $reuse = Invoke-Order -Method 'GET' `
            -Path ($reuseCase.Value + $separator + 'cursor=' + [Uri]::EscapeDataString($baseCursor))
        Add-Result ("query-bound cursor rejects {0}" -f $reuseCase.Key) `
            '422|VALIDATION_FAILED|True' (Cursor-ValidationResult $reuse)
    }

    $unsupportedCursor = ConvertTo-Base64Url `
        '{"v":2,"sortBy":"updatedAt","sortDirection":"desc","lastPrimary":"2026-08-30T00:00:00.0000000+00:00","lastOrderId":"order_read_core_c","queryFingerprint":"0000000000000000000000000000000000000000000000000000000000000000"}'
    Add-Result 'unsupported cursor version is rejected on cursor field' '422|VALIDATION_FAILED|True' `
        (Cursor-ValidationResult (Invoke-Order -Method 'GET' `
            -Path ('/orders?cursor=' + [Uri]::EscapeDataString($unsupportedCursor))))
    Add-Result 'malformed cursor is rejected on cursor field' '422|VALIDATION_FAILED|True' `
        (Cursor-ValidationResult (Invoke-Order -Method 'GET' -Path '/orders?cursor=not-a-cursor'))

    $foreignCursor = Invoke-Api -Method 'GET' `
        -Path ('/orders?sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($baseCursor)) `
        -Token $script:Token -WorkspaceId $foreignWorkspaceId
    Add-Result 'foreign Workspace cannot use a valid cursor' '403' $foreignCursor.Status
    Add-Result 'foreign Workspace cursor response leaks no value' 'True' `
        ($foreignCursor.Raw -notmatch [regex]::Escape($foreignSecret)).ToString()

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'orders.read'"
    Add-Result 'orders.read is re-evaluated on a continuation request' '403' `
        (Invoke-Order -Method 'GET' `
            -Path ('/orders?sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($baseCursor))).Status
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'orders.read')"

    Set-OrderScope -RoleId $roleId -Scope 'Own'
    $restrictedContinuation = Invoke-Order -Method 'GET' `
        -Path ('/orders?sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($baseCursor))
    Add-Result 'record access is re-evaluated on a continuation request' '200|0|0|False' `
        ('{0}|{1}|{2}|{3}' -f $restrictedContinuation.Status, $restrictedContinuation.Body.items.Count, `
            $restrictedContinuation.Body.pageInfo.totalCount, $restrictedContinuation.Body.pageInfo.hasNextPage)
    Set-OrderScope -RoleId $roleId -Scope 'Workspace'

    Clear-OrderFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_orders_read_cursor_recipient', '$roleId', 'orders', 'recipientName', 'Hidden');
"@
    Add-Result 'cursor rejects changed effective searchable-field set' '422|VALIDATION_FAILED|True' `
        (Cursor-ValidationResult (Invoke-Order -Method 'GET' `
            -Path ('/orders?sortBy=updatedAt&sortDirection=desc&cursor=' + [Uri]::EscapeDataString($baseCursor))))
    Clear-OrderFields

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
    Add-Result 'valid optional line snapshots remain accepted' 'SKU-ORDER-EXCLUDED|SERVICE|MONTHLY' `
        ("{0}|{1}|{2}" -f $detail.Body.lineItems[0].skuSnapshot, $detail.Body.lineItems[0].productTypeSnapshot, $detail.Body.lineItems[0].billingCycleSnapshot)
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
('field_orders_read_filter_quote', '$roleId', 'orders', 'sourceQuoteId', 'Hidden'),
('field_orders_read_filter_deal', '$roleId', 'orders', 'sourceDealId', 'Masked');
"@
    $hiddenQuoteKnown = Invoke-Order -Method 'GET' -Path '/orders?sourceQuoteId=quote_source_a'
    $hiddenQuoteUnknown = Invoke-Order -Method 'GET' -Path '/orders?sourceQuoteId=quote_source_unknown'
    Add-Result 'hidden sourceQuoteId filter is denied before list query' '403' $hiddenQuoteKnown.Status
    Add-Result 'hidden sourceQuoteId filter does not reveal existence' 'True' `
        (Same-Problem $hiddenQuoteKnown $hiddenQuoteUnknown).ToString()
    Add-Result 'hidden sourceQuoteId denial exposes no list counts/pageInfo' 'True' `
        ($hiddenQuoteKnown.Raw -notmatch 'items|totalCount|hasNextPage|nextCursor').ToString()
    $hiddenDealKnown = Invoke-Order -Method 'GET' -Path '/orders?sourceDealId=deal_order_c'
    $hiddenDealUnknown = Invoke-Order -Method 'GET' -Path '/orders?sourceDealId=deal_order_unknown'
    Add-Result 'masked sourceDealId filter is denied before list query' '403' $hiddenDealKnown.Status
    Add-Result 'masked sourceDealId filter does not reveal existence' 'True' `
        (Same-Problem $hiddenDealKnown $hiddenDealUnknown).ToString()
    Add-Result 'masked sourceDealId denial exposes no list counts/pageInfo' 'True' `
        ($hiddenDealKnown.Raw -notmatch 'items|totalCount|hasNextPage|nextCursor').ToString()
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

    $validCorruptLineItems = '[{"id":"order_line_corrupt","productId":"product_corrupt_snapshot","productNameSnapshot":"__MARKER__","quantity":"1","unitPrice":{"amount":"1","currency":"USD"},"discountRate":"0","taxMode":"NONE","lineSubtotal":{"amount":"1","currency":"USD"},"lineDiscountAmount":{"amount":"0","currency":"USD"},"lineTaxAmount":{"amount":"0","currency":"USD"},"lineTotal":{"amount":"1","currency":"USD"}}]'.Replace('__MARKER__', $corruptSecret)
    $validCorruptActions = '{"confirm":{"allowed":true,"blockerCodes":[]},"cancel":{"allowed":true,"blockerCodes":[]}}'

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query 'ALTER TABLE orders.Orders NOCHECK CONSTRAINT CK_Orders_LineItemsJson'
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson '[{"id":' -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'malformed lineItems JSON syntax' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query 'ALTER TABLE orders.Orders WITH CHECK CHECK CONSTRAINT CK_Orders_LineItemsJson'

    $missingLineField = '[{"id":"order_line_corrupt","productNameSnapshot":"__MARKER__","quantity":"1","unitPrice":{"amount":"1","currency":"USD"},"discountRate":"0","taxMode":"NONE","lineSubtotal":{"amount":"1","currency":"USD"},"lineDiscountAmount":{"amount":"0","currency":"USD"},"lineTaxAmount":{"amount":"0","currency":"USD"},"lineTotal":{"amount":"1","currency":"USD"}}]'.Replace('__MARKER__', $corruptSecret)
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $missingLineField -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'missing required line productId' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $nullLineString = $validCorruptLineItems.Replace('"productNameSnapshot":"' + $corruptSecret + '"', '"productNameSnapshot":null')
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $nullLineString -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'null required line string' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $missingMoneyField = $validCorruptLineItems.Replace('{"amount":"1","currency":"USD"}', '{"amount":"1"}')
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $missingMoneyField -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'required nested money field missing' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $invalidTaxMode = $validCorruptLineItems.Replace('"taxMode":"NONE"', '"taxMode":"UNKNOWN"')
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $invalidTaxMode -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'invalid line taxMode vocabulary' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $invalidQuantity = $validCorruptLineItems.Replace('"quantity":"1"', '"quantity":"1e2"')
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $invalidQuantity -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'invalid DecimalAmount string' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $invalidMoneyAmount = $validCorruptLineItems.Replace('"amount":"1"', '"amount":"NaN"')
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $invalidMoneyAmount -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'invalid Money amount string' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $invalidPercentage = $validCorruptLineItems.Replace('"discountRate":"0"', '"discountRate":"100.000001"')
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $invalidPercentage -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'invalid PercentageRate string' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $unknownLineProperty = $validCorruptLineItems.Replace('"quantity":"1"', '"quantity":"1","unknown":"forbidden"')
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $unknownLineProperty -ActionsJson $validCorruptActions
    Assert-CorruptOrderFailure 'line additional property' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson '[]'
    Assert-CorruptOrderFailure 'malformed actions structure' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $missingActionAllowed = '{"confirm":{"blockerCodes":[]},"cancel":{"allowed":true,"blockerCodes":[]}}'
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $missingActionAllowed
    Assert-CorruptOrderFailure 'missing required action member' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $invalidBlockerCode = '{"confirm":{"allowed":false,"blockerCodes":["NOT_AN_ERROR_CODE"]},"cancel":{"allowed":true,"blockerCodes":[]}}'
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $invalidBlockerCode
    Assert-CorruptOrderFailure 'invalid blockerCode vocabulary' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions -AdjustmentsJson '{}'
    Assert-CorruptOrderFailure 'malformed adjustments structure' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $invalidAdjustmentEnum = '[{"id":"adjustment_corrupt","label":"Corrupt adjustment","type":"UNKNOWN","calculation":"FIXED_AMOUNT","value":"1","amount":{"amount":"1","currency":"USD"}}]'
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions -AdjustmentsJson $invalidAdjustmentEnum
    Assert-CorruptOrderFailure 'invalid adjustment type vocabulary' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions `
        -ShippingAddressJson '{"line1":"Address without city"}'
    Assert-CorruptOrderFailure 'malformed shippingAddress structure' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions `
        -CreditPolicyEvaluationJson '{"status":"NOT_REQUIRED","blockerCodes":"not-an-array"}'
    Assert-CorruptOrderFailure 'malformed creditPolicyEvaluation structure' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions `
        -CreditPolicyEvaluationJson '{"status":"UNKNOWN","blockerCodes":[]}'
    Assert-CorruptOrderFailure 'invalid credit-policy status vocabulary' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions `
        -CreditPolicyEvaluationJson '{"status":"NOT_REQUIRED","blockerCodes":[],"evaluatedAt":"2026-08-30T00:00:00+07:00"}'
    Assert-CorruptOrderFailure 'non-UTC credit-policy timestamp' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $missingCreditApprovalField = '{"id":"credit_approval_corrupt","state":"APPROVED","policyVersion":"policy-v1","orderResourceVersion":0,"paymentPlanResourceVersion":0,"resourceVersion":0}'
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions `
        -CreditApprovalJson $missingCreditApprovalField
    Assert-CorruptOrderFailure 'malformed creditApproval structure' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $invalidCreditApprovalState = '{"id":"credit_approval_corrupt","state":"UNKNOWN","amount":{"amount":"1","currency":"USD"},"policyVersion":"policy-v1","orderResourceVersion":0,"paymentPlanResourceVersion":0,"resourceVersion":0}'
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions `
        -CreditApprovalJson $invalidCreditApprovalState
    Assert-CorruptOrderFailure 'invalid credit-approval state vocabulary' $orderCorrupt $corruptSecret
    Remove-CorruptOrderFixture $orderCorrupt

    $negativeCreditApprovalVersion = '{"id":"credit_approval_corrupt","state":"APPROVED","amount":{"amount":"1","currency":"USD"},"policyVersion":"policy-v1","orderResourceVersion":-1,"paymentPlanResourceVersion":0,"resourceVersion":0}'
    Set-CorruptOrderFixture -OrderId $orderCorrupt -Marker $corruptSecret `
        -LineItemsJson $validCorruptLineItems -ActionsJson $validCorruptActions `
        -CreditApprovalJson $negativeCreditApprovalVersion
    Assert-CorruptOrderFailure 'negative nested resource version' $orderCorrupt $corruptSecret
    $corruptList = Measure-OrderRead -Path '/orders'
    Add-Result 'corrupt row fails the whole list closed' '500|INTERNAL_ERROR' `
        ('{0}|{1}' -f $corruptList.Status, $corruptList.Body.code)
    Add-Result 'corrupt list writes +0 owner audit' '0' ([string]$corruptList.Delta)
    Add-Result 'corrupt list emits no partial Order data' 'True' `
        (($corruptList.Raw -notmatch [regex]::Escape($orderCorrupt)) `
            -and ($corruptList.Raw -notmatch [regex]::Escape($corruptSecret)) `
            -and ($corruptList.Raw -notmatch '"items"')).ToString()
    Remove-CorruptOrderFixture $orderCorrupt

    $healthyAfterCorruption = Invoke-Order -Method 'GET' -Path '/orders'
    Add-Result 'healthy Orders list after corrupt-fixture removal' '200|3' `
        ('{0}|{1}' -f $healthyAfterCorruption.Status, $healthyAfterCorruption.Body.items.Count)
    Add-Result 'healthy Order detail after corrupt-fixture removal' '200' `
        (Invoke-Order -Method 'GET' -Path "/orders/$orderA").Status

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
    Add-Result 'Orders has owner-local durable read-audit runtime' 'True' `
        (($ordersSource -match 'OrderReadAuditRecord') `
            -and ($ordersSource -match 'ReadAuditRecords') `
            -and ($ordersSource -match 'AddReadAudit')).ToString()
    Add-Result 'Orders has no generic cross-module audit framework' 'True' `
        ($ordersSource -notmatch 'IAuditFramework|IReadAuditService|GenericAudit').ToString()
    # SaveChanges is admitted only for Orders read-evidence append persistence. No Order business
    # mutation path may acquire it while this read-only surface remains the only admitted surface.
    $saveChangesFiles = (Get-ChildItem -LiteralPath $ordersRoot -Recurse -File -Filter '*.cs' |
        Where-Object { (Get-Content -Raw -LiteralPath $_.FullName) -match 'SaveChangesAsync' } |
        ForEach-Object Name | Sort-Object) -join ','
    Add-Result 'SaveChanges confined to read-audit append' `
        'EfOrdersPersistence.cs,OrderReadAudit.cs,OrdersApplication.cs' $saveChangesFiles

    $logText = ''
    if (Test-Path -LiteralPath $logPath) { $logText += Get-Content -Raw -LiteralPath $logPath }
    if (Test-Path -LiteralPath "$logPath.err") { $logText += Get-Content -Raw -LiteralPath "$logPath.err" }
    Add-Result 'foreign Order value absent from host logs' 'True' ($logText -notmatch [regex]::Escape($foreignSecret)).ToString()
    Add-Result 'corrupt persisted JSON value absent from host logs' 'True' `
        ($logText -notmatch [regex]::Escape($corruptSecret)).ToString()
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
