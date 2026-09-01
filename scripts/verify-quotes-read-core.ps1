<#
.SYNOPSIS
    Reproducible Quote Owner-Local Read Core verification against an isolated database and real ApiHost.

.DESCRIPTION
    Quotes has no admitted mutation API in this slice. This harness applies the real Quotes migration,
    seeds owner-local Quote read state directly with controlled SQL, and exercises only GET /quotes and
    GET /quotes/{quoteId}. Direct fixture seeding does not create a production Quote command.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5336,

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
    $script:LastRequestId = 'req-quotes-read-{0:d6}' -f $script:RequestCounter
    return $script:LastRequestId
}

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Path,
        [string] $Body,
        [string] $Token,
        [string] $WorkspaceId,
        [string] $IdempotencyKey,
        [string] $RequestId
    )
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ([string]::IsNullOrWhiteSpace($RequestId)) {
        [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId))
    }
    elseif ($RequestId -ne 'omit') {
        [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId)
    }
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-quotes-read-core-0001')
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

function Invoke-Quote {
    param([string] $Method, [string] $Path, [string] $Body)
    return Invoke-Api -Method $Method -Path $Path -Body $Body -Token $script:Token -WorkspaceId $script:WorkspaceId
}

function Get-QuoteReadAuditCount {
    return [int](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM quotes.ReadAuditRecords')
}

function Get-QuoteRecordDecisionCount {
    return [int](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions WHERE ResourceKey = 'quotes'")
}

function Measure-QuoteRead {
    param([string] $Path)
    $before = Get-QuoteReadAuditCount
    $response = Invoke-Quote -Method 'GET' -Path $Path
    $delta = (Get-QuoteReadAuditCount) - $before
    return [pscustomobject]@{
        Status = $response.Status
        Body = $response.Body
        Raw = $response.Raw
        Delta = $delta
        Probe = ('{0}|{1}' -f $response.Status, $delta)
    }
}

function Set-QuoteScope {
    param([string] $RoleId, [string] $Scope)
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_quotes_read_core';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_quotes_read_core', '$RoleId', 'quotes', '$Scope', '[]');
"@
}

function Clear-QuoteFields {
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_quotes_read_%'"
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
$demoEmail = 'quotes.read.provisioned@example.test'
$demoPassword = 'Quotes-Read-Core!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-quotes-read-$([Guid]::NewGuid().ToString('N')).log")
$quoteA = 'quote_read_core_a'
$quoteB = 'quote_read_core_b'
$quoteC = 'quote_read_core_foreign'
$quoteUnknown = 'quote_read_core_unknown'
$secretA = 'QUOTE-A-OPTIONAL-PRIVATE-NOTE'
$secretC = 'QUOTE-C-FOREIGN-BUSINESS-VALUE'

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
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Quotes Provisioning Fixture'
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

    Add-Result 'unauthenticated list rejected' '401' (Invoke-Api -Method 'GET' -Path '/quotes' -WorkspaceId 'ws_unknown').Status
    Add-Result 'unauthenticated detail rejected' '401' (Invoke-Api -Method 'GET' -Path "/quotes/$quoteA" -WorkspaceId 'ws_unknown').Status

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' `
        -IdempotencyKey 'idem-quotes-read-signin-0001' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    $callerMemberId = $session.Body.principal.memberId
    $script:MemberId = $callerMemberId
    $provisioning = Invoke-Api -Method 'POST' -Path '/workspaces/initial-provisioning' `
        -Token $script:Token -IdempotencyKey 'idem-quotes-read-provisioning-0001' `
        -Body '{"name":"Quotes Read Workspace"}'
    Add-Result 'initial Workspace provisioning succeeds' '201' $provisioning.Status
    $script:WorkspaceId = $provisioning.Body.workspaceId
    $foreignWorkspaceId = 'ws_quotes_read_foreign'
    $roleId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)' AND Name = 'Workspace Owner'"
    if ([string]::IsNullOrWhiteSpace($script:WorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($callerMemberId) `
        -or [string]::IsNullOrWhiteSpace($roleId)) {
        throw 'The Development identity/workspace/access fixture was not provisioned.'
    }

    $defaultCapability = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'quotes.read'"
    Add-Result 'initial provisioning does not invent quotes.read' '0' ([string]$defaultCapability)
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'quotes.read')"
    Add-Result 'controlled fixture grants canonical quotes.read' '1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'quotes.read'"))

    Add-Result 'fresh migration created Quotes table' '1' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'quotes' AND TABLE_NAME = 'Quotes'"))
    Add-Result 'fresh migration contains Quote read-audit table' '1' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'quotes' AND TABLE_NAME = 'ReadAuditRecords'"))
    Add-Result 'fresh migration recorded exactly two Quotes migrations' '2' ([string](Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM quotes.__EFMigrationsHistory"))
    Add-Result 'read-audit columns exact' `
        'ActorId,AuditId,CorrelationId,OccurredAt,Operation,Outcome,RecordId,RequestId,ResourceVersion,WorkspaceId' `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'quotes' AND TABLE_NAME = 'ReadAuditRecords'
"@))
    Add-Result 'read-audit Workspace-leading index' '1' ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.indexes
WHERE object_id = OBJECT_ID('quotes.ReadAuditRecords')
  AND name = 'IX_ReadAuditRecords_WorkspaceId_OccurredAt'
"@))
    Add-Result 'read-audit has zero foreign keys' '0' ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.foreign_keys
WHERE parent_object_id = OBJECT_ID('quotes.ReadAuditRecords')
"@))

    $constraintCount = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.check_constraints c
JOIN sys.tables t ON t.object_id = c.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'quotes' AND t.name = 'Quotes'
  AND c.name IN ('CK_Quotes_BuyerType','CK_Quotes_SourcePath','CK_Quotes_Status','CK_Quotes_ApprovalStatus',
                 'CK_Quotes_QuoteRevision','CK_Quotes_ResourceVersion','CK_Quotes_LineItemsJson','CK_Quotes_ActionsJson')
  AND c.is_disabled = 0 AND c.is_not_trusted = 0
"@
    Add-Result 'Quote enum/version/JSON constraints are enabled and trusted' '8' ([string]$constraintCount)

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Workspaces WHERE WorkspaceId = '$foreignWorkspaceId')
INSERT INTO workspace.Workspaces (WorkspaceId, [Key], Name, LogoText, CreatedAt)
VALUES ('$foreignWorkspaceId', 'quotes-read-foreign', 'Quotes Read Foreign Workspace', 'QF', SYSUTCDATETIME());

INSERT INTO quotes.Quotes
(WorkspaceId, QuoteId, QuoteNumber, QuoteRevision, RootQuoteId, RevisionOfQuoteId,
 BuyerType, BuyerId, SourcePath, SourceDealId, ContactId, SourceLeadId, Status, Title, Currency,
 OwnerId, RecipientEmail, LineItemsJson, AdjustmentsJson,
 SubtotalAmount, SubtotalCurrency, DiscountTotalAmount, DiscountTotalCurrency,
 TaxTotalAmount, TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency, ValidUntil,
 ReviewRequestedAt, SentAt, AcceptedAt, RejectedAt, ExpiredAt, Notes, ArchivedAt, ArchiveReason,
 ActionsJson, ApprovalStatus, ApprovalRequired, ApprovalReasonsJson, ApprovalRequestedAt,
 ApprovalRequestedBy, ApprovedAt, ApprovedBy, ApprovalDecisionNote, ApprovalContentFingerprint,
 ApprovalPolicyVersion, PaymentAgreementJson, DeliveryHistoryJson, SenderName, SenderAddress,
 SenderEmail, SenderTaxId, ResourceVersion, CreatedAt, UpdatedAt)
VALUES
('$($script:WorkspaceId)', '$quoteA', 'Q-2026-0001', 2, 'quote_root_a', 'quote_revision_parent_a',
 'ORGANIZATION_ACCOUNT', 'organization_buyer_a', 'DEAL', 'deal_source_a', 'contact_a', 'lead_a',
 'SENT', 'Verified Enterprise Quote', 'USD', '$callerMemberId', 'buyer@example.test',
 N'[{"id":"quote_line_a","productId":"product_scalar_reference","skuSnapshot":"SKU-SNAPSHOT","productNameSnapshot":"Historical Product Snapshot","productTypeSnapshot":"SERVICE","descriptionSnapshot":"Owner-local immutable line","quantity":"2.5","unitPrice":{"amount":"493.827156","currency":"USD"},"discountRate":"10.25","taxRate":"8.5","taxMode":"EXCLUSIVE","billingCycleSnapshot":"MONTHLY","lineSubtotal":{"amount":"1234.56789","currency":"USD"},"lineDiscountAmount":{"amount":"126.543209","currency":"USD"},"lineTaxAmount":{"amount":"249.9","currency":"USD"},"lineTotal":{"amount":"1357.924678","currency":"USD"}}]',
 N'[{"id":"adjustment_a","label":"Verified discount","type":"DISCOUNT","calculation":"FIXED_AMOUNT","value":"126.543209","amount":{"amount":"126.543209","currency":"USD"}}]',
 1234.567890, 'USD', 126.543209, 'USD', 249.900000, 'USD', 1357.924678, 'USD', '2026-09-30',
 '2026-08-28T01:02:03+07:00', '2026-08-29T02:03:04+07:00', NULL, NULL, NULL, '$secretA', NULL, NULL,
 N'{"accept":{"allowed":false,"blockerCodes":["QUOTE_APPROVAL_REQUIRED"]}}',
 'PENDING', 1,
 N'[{"code":"MANUAL_REVIEW","label":"Manual verification","actual":"1357.924678","limit":"1000"}]',
 '2026-08-28T01:02:03+07:00', '$callerMemberId', NULL, NULL, NULL, 'sha256:approval-a', 'policy-v1',
 N'{"version":3,"kind":"DEPOSIT_AND_BALANCE","currency":"USD","lines":[{"id":"payment_line_a","sequence":1,"label":"Deposit","purpose":"DEPOSIT","amountRule":{"type":"PERCENTAGE","percentage":"30"},"previewAmount":{"amount":"407.377403","currency":"USD"},"dueRule":{"type":"FIXED_DATE","date":"2026-09-01"},"allowedMethodCodes":["BANK_TRANSFER"],"preferredMethodCode":"BANK_TRANSFER","channel":"BANK","fulfillmentGate":"BEFORE_BOOKING","invoicePolicyCode":"STANDARD"}],"acceptedAt":"2026-08-29T00:00:00Z","sourceQuoteId":"$quoteA","policyVersion":"payment-v1"}',
 N'[{"id":"delivery_a","channel":"EMAIL","evidenceType":"PROVIDER_ACCEPTED","recipientEmail":"buyer@example.test","recipient":"buyer@example.test","note":"accepted","sentAt":"2026-08-29T00:00:00Z","sentBy":"$callerMemberId","fileName":"quote.pdf","contentFingerprint":"sha256:delivery-a"}]',
 'Verified Seller', '1 Verified Street', 'seller@example.test', 'TAX-VERIFIED', 7,
 '2026-08-27T08:00:00+07:00', '2026-08-29T09:10:11+07:00');

INSERT INTO quotes.Quotes
(WorkspaceId, QuoteId, QuoteNumber, QuoteRevision, RootQuoteId, BuyerType, BuyerId, SourcePath,
 Status, Title, Currency, LineItemsJson, SubtotalAmount, SubtotalCurrency, DiscountTotalAmount,
 DiscountTotalCurrency, TaxTotalAmount, TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency,
 ActionsJson, ResourceVersion, CreatedAt, UpdatedAt)
VALUES
('$($script:WorkspaceId)', '$quoteB', 'Q-2026-0002', 1, '$quoteB', 'CONTACT', 'contact_buyer_b', 'DIRECT_SALE',
 'DRAFT', 'Minimal Direct Quote', 'VND',
 N'[{"id":"quote_line_b","productNameSnapshot":"Standalone Snapshot","quantity":"1","unitPrice":{"amount":"1000000","currency":"VND"},"discountRate":"0","taxMode":"NONE","lineSubtotal":{"amount":"1000000","currency":"VND"},"lineDiscountAmount":{"amount":"0","currency":"VND"},"lineTaxAmount":{"amount":"0","currency":"VND"},"lineTotal":{"amount":"1000000","currency":"VND"}}]',
 1000000, 'VND', 0, 'VND', 0, 'VND', 1000000, 'VND',
 N'{"accept":{"allowed":false,"blockerCodes":[]}}', 0, '2026-08-29T00:00:00Z', '2026-08-30T00:00:00Z');

INSERT INTO quotes.Quotes
(WorkspaceId, QuoteId, QuoteNumber, QuoteRevision, RootQuoteId, BuyerType, BuyerId, SourcePath,
 Status, Title, Currency, LineItemsJson, SubtotalAmount, SubtotalCurrency, DiscountTotalAmount,
 DiscountTotalCurrency, TaxTotalAmount, TaxTotalCurrency, GrandTotalAmount, GrandTotalCurrency,
 Notes, ActionsJson, ResourceVersion, CreatedAt, UpdatedAt)
VALUES
('$foreignWorkspaceId', '$quoteC', 'Q-FOREIGN-SECRET', 1, '$quoteC', 'CONTACT', 'contact_foreign', 'DIRECT_SALE',
 'DRAFT', '$secretC', 'USD',
 N'[{"id":"quote_line_c","productNameSnapshot":"$secretC","quantity":"1","unitPrice":{"amount":"99.99","currency":"USD"},"discountRate":"0","taxMode":"NONE","lineSubtotal":{"amount":"99.99","currency":"USD"},"lineDiscountAmount":{"amount":"0","currency":"USD"},"lineTaxAmount":{"amount":"0","currency":"USD"},"lineTotal":{"amount":"99.99","currency":"USD"}}]',
 99.99, 'USD', 0, 'USD', 0, 'USD', 99.99, 'USD', '$secretC',
 N'{"accept":{"allowed":false,"blockerCodes":[]}}', 0, '2026-08-29T00:00:00Z', '2026-08-30T01:00:00Z');
"@

    Set-QuoteScope -RoleId $roleId -Scope 'Workspace'

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'quotes.read'"
    Add-Result 'missing quotes.read denies list with +0 audit' '403|0' `
        (Measure-QuoteRead -Path '/quotes').Probe
    Add-Result 'missing quotes.read denies detail with +0 audit' '403|0' `
        (Measure-QuoteRead -Path "/quotes/$quoteA").Probe
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'quotes.read')"

    # Frozen READ_ACCESS_LOG owner evidence: one row per successful invocation after projection.
    $recordDecisionsBeforeList = Get-QuoteRecordDecisionCount
    $multiRowAudit = Measure-QuoteRead -Path '/quotes'
    Add-Result 'read audit: multi-row listQuotes => 200 and +1, not per-row' '200|1' $multiRowAudit.Probe
    Add-Result 'read audit: multi-row list returns two Quotes' '2' ([string]$multiRowAudit.Body.items.Count)
    Add-Result 'read audit: list writes zero per-row record decisions' '0' `
        ([string]((Get-QuoteRecordDecisionCount) - $recordDecisionsBeforeList))
    $emptyAudit = Measure-QuoteRead -Path '/quotes?search=quote_read_audit_no_match'
    Add-Result 'read audit: empty successful list => 200 and +1' '200|1' $emptyAudit.Probe
    Add-Result 'read audit: empty successful list contains zero Quotes' '0' ([string]$emptyAudit.Body.items.Count)
    Add-Result 'read audit: every list row has null recordId and resourceVersion' '0' `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM quotes.ReadAuditRecords
WHERE Operation = 'listQuotes' AND (RecordId IS NOT NULL OR ResourceVersion IS NOT NULL)
"@))

    $recordDecisionsBeforeDetail = Get-QuoteRecordDecisionCount
    $detailAudit = Measure-QuoteRead -Path "/quotes/$quoteA"
    $detailAuditRequestId = $script:LastRequestId
    Add-Result 'read audit: getQuote => 200 and +1' '200|1' $detailAudit.Probe
    Add-Result 'read audit: detail writes canonical record decision' '1' `
        ([string]((Get-QuoteRecordDecisionCount) - $recordDecisionsBeforeDetail))
    Add-Result 'read audit: detail recordId and resourceVersion exact' "$quoteA|7" `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT CONCAT(RecordId, '|', ResourceVersion)
FROM quotes.ReadAuditRecords WHERE RequestId = '$detailAuditRequestId'
"@))
    Add-Result 'read audit: detail version matches response' '7' ([string]$detailAudit.Body.resourceVersion)
    Add-Result 'read audit: provenance and exact operationId' `
        "getQuote|$($script:WorkspaceId)|$($script:MemberId)|$detailAuditRequestId|corr-quotes-read-core-0001|READ|$quoteA|7" `
        ([string](Get-Scalar -Database $DatabaseName -Query @"
SELECT CONCAT(Operation, '|', WorkspaceId, '|', ActorId, '|', RequestId, '|', CorrelationId, '|', Outcome, '|', RecordId, '|', ResourceVersion)
FROM quotes.ReadAuditRecords WHERE RequestId = '$detailAuditRequestId'
"@))
    Add-Result 'read audit: every row outcome is READ' '0' `
        ([string](Get-Scalar -Database $DatabaseName `
            -Query "SELECT COUNT(*) FROM quotes.ReadAuditRecords WHERE Outcome <> 'READ'"))
    Add-Result 'read audit: only admitted operationIds are stored' '0' `
        ([string](Get-Scalar -Database $DatabaseName `
            -Query "SELECT COUNT(*) FROM quotes.ReadAuditRecords WHERE Operation NOT IN ('listQuotes', 'getQuote')"))

    Add-Result 'read audit: malformed authorized query => 422 and +0' '422|0' `
        (Measure-QuoteRead -Path '/quotes?status=draft').Probe
    Add-Result 'read audit: malformed cursor => 422 and +0' '422|0' `
        (Measure-QuoteRead -Path '/quotes?cursor=not-a-cursor').Probe
    Add-Result 'read audit: malformed limit type => 422 and +0' '422|0' `
        (Measure-QuoteRead -Path '/quotes?limit=abc').Probe
    Add-Result 'read audit: malformed authorized path => 404 and +0' '404|0' `
        (Measure-QuoteRead -Path '/quotes/bad%20quote').Probe
    Add-Result 'read audit: unknown Quote => 404 and +0' '404|0' `
        (Measure-QuoteRead -Path "/quotes/$quoteUnknown").Probe
    Add-Result 'read audit: foreign Workspace Quote => 404 and +0' '404|0' `
        (Measure-QuoteRead -Path "/quotes/$quoteC").Probe

    Set-QuoteScope -RoleId $roleId -Scope 'Own'
    Add-Result 'read audit: record-access denied detail => 404 and +0' '404|0' `
        (Measure-QuoteRead -Path "/quotes/$quoteA").Probe
    Add-Result 'read audit: OWN empty list is successful and +1' '200|1' `
        (Measure-QuoteRead -Path '/quotes').Probe
    Set-QuoteScope -RoleId $roleId -Scope 'Workspace'

    Clear-QuoteFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_quotes_read_audit_required', '$roleId', 'quotes', 'title', 'Hidden');
"@
    Add-Result 'read audit: required hidden field list => 403 and +0' '403|0' `
        (Measure-QuoteRead -Path '/quotes').Probe
    Add-Result 'read audit: required hidden field detail => 403 and +0' '403|0' `
        (Measure-QuoteRead -Path "/quotes/$quoteA").Probe
    Clear-QuoteFields

    $auditDump = Get-Scalar -Database $DatabaseName -Query @"
SELECT STRING_AGG(CONCAT(AuditId, '|', Operation, '|', WorkspaceId, '|', ActorId, '|',
    ISNULL(RecordId, ''), '|', RequestId, '|', CorrelationId, '|', Outcome, '|',
    ISNULL(CAST(ResourceVersion AS varchar(32)), '')), ' ')
FROM quotes.ReadAuditRecords
"@
    Add-Result 'read audit: no Quote business values stored' 'True' `
        ([string]($auditDump -notmatch 'Q-2026|Q-FOREIGN|Verified Enterprise|Minimal Direct|buyer@example|organization_buyer|contact_buyer|deal_source|Historical Product|SKU-SNAPSHOT|QUOTE-A-OPTIONAL|QUOTE-C-FOREIGN|USD|VND|1357\.924678|1000000'))
    Add-Result 'read audit: no foreign Workspace owner evidence' '0' `
        ([string](Get-Scalar -Database $DatabaseName `
            -Query "SELECT COUNT(*) FROM quotes.ReadAuditRecords WHERE WorkspaceId = '$foreignWorkspaceId'"))

    $workspaceList = Invoke-Quote -Method 'GET' -Path '/quotes'
    Add-Result 'WORKSPACE list succeeds' '200' $workspaceList.Status
    Add-Result 'list returns only current Workspace Quotes' '2' ([string]$workspaceList.Body.items.Count)
    Add-Result 'foreign Quote absent from list' 'False' ($workspaceList.Body.items.id -contains $quoteC).ToString()
    Add-Result 'foreign Quote value absent from list bytes' 'True' ($workspaceList.Raw -notmatch [regex]::Escape($secretC)).ToString()
    Add-Result 'list wrapper has exact top-level fields' 'items,pageInfo' `
        ((@($workspaceList.Body.PSObject.Properties.Name | Sort-Object)) -join ',')
    Add-Result 'pageInfo required hasNextPage is present' 'True' `
        ($workspaceList.Body.pageInfo.PSObject.Properties.Name -ccontains 'hasNextPage').ToString()
    Add-Result 'list totalCount is Workspace/access filtered' '2' ([string]$workspaceList.Body.pageInfo.totalCount)

    $firstPage = Invoke-Quote -Method 'GET' -Path '/quotes?limit=1&sortBy=quoteNumber&sortDirection=asc'
    Add-Result 'first cursor page succeeds' '200' $firstPage.Status
    Add-Result 'first cursor page contains one item' '1' ([string]$firstPage.Body.items.Count)
    Add-Result 'first cursor page advertises continuation' 'True' ([string]$firstPage.Body.pageInfo.hasNextPage)
    Add-Result 'continuation cursor is present' 'True' (-not [string]::IsNullOrWhiteSpace($firstPage.Body.pageInfo.nextCursor)).ToString()
    $secondPage = Invoke-Quote -Method 'GET' -Path ("/quotes?limit=1&sortBy=quoteNumber&sortDirection=asc&cursor={0}" -f $firstPage.Body.pageInfo.nextCursor)
    Add-Result 'second cursor page succeeds' '200' $secondPage.Status
    Add-Result 'cursor pages do not duplicate Quote' 'True' ($firstPage.Body.items[0].id -ne $secondPage.Body.items[0].id).ToString()
    Add-Result 'second cursor page ends collection' 'False' ([string]$secondPage.Body.pageInfo.hasNextPage)

    Add-Result 'status filter is exact' '1' ([string](Invoke-Quote -Method 'GET' -Path '/quotes?status=SENT').Body.items.Count)
    Add-Result 'sourceDealId filter is exact' $quoteA `
        (Invoke-Quote -Method 'GET' -Path '/quotes?sourceDealId=deal_source_a').Body.items[0].id
    Add-Result 'buyer filter is exact' $quoteB `
        (Invoke-Quote -Method 'GET' -Path '/quotes?buyerType=CONTACT&buyerId=contact_buyer_b').Body.items[0].id

    $quoteNumberExact = Invoke-Quote -Method 'GET' -Path '/quotes?search=Q-2026-0001'
    Add-Result 'search: quoteNumber exact match succeeds' '200' $quoteNumberExact.Status
    Add-Result 'search: quoteNumber exact match returns the Quote' $quoteA $quoteNumberExact.Body.items[0].id
    Add-Result 'search: quoteNumber partial substring returns the Quote' $quoteA `
        (Invoke-Quote -Method 'GET' -Path '/quotes?search=2026-0001').Body.items[0].id
    Add-Result 'search: quoteNumber matching is case-insensitive' $quoteA `
        (Invoke-Quote -Method 'GET' -Path '/quotes?search=q-2026-0001').Body.items[0].id

    $titleExactPath = '/quotes?search=' + [Uri]::EscapeDataString('Verified Enterprise Quote')
    Add-Result 'search: title exact match returns the Quote' $quoteA `
        (Invoke-Quote -Method 'GET' -Path $titleExactPath).Body.items[0].id
    Add-Result 'search: title partial substring returns the Quote' $quoteA `
        (Invoke-Quote -Method 'GET' -Path '/quotes?search=Enterprise').Body.items[0].id
    $titleDifferentCasePath = '/quotes?search=' + [Uri]::EscapeDataString('verified enterprise quote')
    Add-Result 'search: title matching is case-insensitive' $quoteA `
        (Invoke-Quote -Method 'GET' -Path $titleDifferentCasePath).Body.items[0].id
    $trimmedSearchPath = '/quotes?search=' + [Uri]::EscapeDataString('  Enterprise  ')
    Add-Result 'search: leading and trailing whitespace is trimmed' $quoteA `
        (Invoke-Quote -Method 'GET' -Path $trimmedSearchPath).Body.items[0].id
    Add-Result 'search: empty value applies no filter' '2' `
        ([string](Invoke-Quote -Method 'GET' -Path '/quotes?search=').Body.items.Count)
    Add-Result 'search: whitespace-only value applies no filter' '2' `
        ([string](Invoke-Quote -Method 'GET' -Path '/quotes?search=%20%20%20').Body.items.Count)
    Add-Result 'search: percent is a literal substring character, not a SQL wildcard' '0' `
        ([string](Invoke-Quote -Method 'GET' -Path '/quotes?search=%25').Body.items.Count)
    Add-Result 'search: underscore is a literal substring character, not a SQL wildcard' '0' `
        ([string](Invoke-Quote -Method 'GET' -Path '/quotes?search=_').Body.items.Count)

    $tooLongSearchPath = '/quotes?search=' + ('a' * 241)
    $tooLongSearch = Invoke-Quote -Method 'GET' -Path $tooLongSearchPath
    Add-Result 'search: value over 240 characters is rejected' '422' $tooLongSearch.Status
    Add-Result 'search: overlength validation identifies search' 'True' `
        ($tooLongSearch.Raw -match 'search').ToString()

    $excludedSearchValues = @(
        $secretA,
        'buyer@example.test',
        'organization_buyer_a',
        'deal_source_a',
        'Historical Product Snapshot',
        'SKU-SNAPSHOT',
        'Verified discount',
        'Manual verification',
        'DEPOSIT_AND_BALANCE',
        'sha256:delivery-a',
        'QUOTE_APPROVAL_REQUIRED'
    )
    foreach ($excludedSearchValue in $excludedSearchValues) {
        $excludedSearchPath = '/quotes?search=' + [Uri]::EscapeDataString($excludedSearchValue)
        Add-Result ("search: excluded corpus value is not matched ({0})" -f $excludedSearchValue) '0' `
            ([string](Invoke-Quote -Method 'GET' -Path $excludedSearchPath).Body.items.Count)
    }

    $foreignSearch = Invoke-Quote -Method 'GET' -Path '/quotes?search=Q-FOREIGN-SECRET'
    Add-Result 'search: foreign Workspace Quote is absent' '0' ([string]$foreignSearch.Body.items.Count)
    Add-Result 'search: foreign Workspace value is absent from response bytes' 'True' `
        ($foreignSearch.Raw -notmatch [regex]::Escape($secretC)).ToString()

    $filteredPage = Invoke-Quote -Method 'GET' -Path '/quotes?search=Enterprise&limit=1&sortBy=quoteNumber&sortDirection=asc'
    Add-Result 'search: filtered page succeeds' '200' $filteredPage.Status
    Add-Result 'search: page limit applies to filtered results' '1' ([string]$filteredPage.Body.items.Count)
    Add-Result 'search: totalCount reflects the filtered visible result set' '1' `
        ([string]$filteredPage.Body.pageInfo.totalCount)
    Add-Result 'search: pageInfo reflects filtered continuation' 'False' `
        ([string]$filteredPage.Body.pageInfo.hasNextPage)

    Add-Result 'invalid status is rejected' '422' (Invoke-Quote -Method 'GET' -Path '/quotes?status=draft').Status
    Add-Result 'invalid cursor is rejected' '422' (Invoke-Quote -Method 'GET' -Path '/quotes?cursor=not-a-cursor').Status
    Add-Result 'invalid limit type is rejected' '422' (Invoke-Quote -Method 'GET' -Path '/quotes?limit=abc').Status

    $detail = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteA"
    Add-Result 'GET Quote succeeds' '200' $detail.Status
    Add-Result 'detail returns correct Quote' $quoteA $detail.Body.id
    $requiredFields = @(
        'id','quoteNumber','quoteRevision','rootQuoteId','buyerRef','sourcePath','status','title','currency',
        'lineItems','subtotal','discountTotal','taxTotal','grandTotal','actions','resourceVersion','createdAt','updatedAt'
    )
    $allowedFields = @($requiredFields + @(
        'revisionOfQuoteId','sourceDealId','contactId','sourceLeadId','ownerId','recipientEmail','adjustments',
        'validUntil','reviewRequestedAt','sentAt','acceptedAt','rejectedAt','expiredAt','notes','archivedAt',
        'archiveReason','approvalStatus','approvalRequired','approvalReasons','approvalRequestedAt',
        'approvalRequestedBy','approvedAt','approvedBy','approvalDecisionNote','approvalContentFingerprint',
        'approvalPolicyVersion','paymentAgreement','deliveryHistory','senderName','senderAddress','senderEmail','senderTaxId'
    ))
    $actualFields = @($detail.Body.PSObject.Properties.Name)
    Add-Result 'detail contains every exact required wire field' '0' `
        ([string](@($requiredFields | Where-Object { $actualFields -cnotcontains $_ }).Count))
    Add-Result 'detail contains no field outside exact wire' '0' `
        ([string](@($actualFields | Where-Object { $allowedFields -cnotcontains $_ }).Count))
    Add-Result 'buyerRef projects exact enum and ID' 'ORGANIZATION_ACCOUNT|organization_buyer_a' `
        ("{0}|{1}" -f $detail.Body.buyerRef.type, $detail.Body.buyerRef.id)
    Add-Result 'Quote status enum persisted exactly' 'SENT' $detail.Body.status
    Add-Result 'root money decimal is a JSON string' 'String' $detail.Body.grandTotal.amount.GetType().Name
    Add-Result 'root money preserves six-scale decimal value' '1357.924678|USD' `
        ("{0}|{1}" -f $detail.Body.grandTotal.amount, $detail.Body.grandTotal.currency)
    Add-Result 'line money and tax enum persist exactly' '493.827156|USD|EXCLUSIVE' `
        ("{0}|{1}|{2}" -f $detail.Body.lineItems[0].unitPrice.amount, $detail.Body.lineItems[0].unitPrice.currency, $detail.Body.lineItems[0].taxMode)
    Add-Result 'timestamp persistence projects canonical UTC Z' '2026-08-29T02:10:11.0000000Z' `
        $detail.Body.updatedAt.ToUniversalTime().ToString('O')
    Add-Result 'business date persistence projects exact date' '2026-09-30' $detail.Body.validUntil
    Add-Result 'optional approval/payment/delivery documents round-trip' 'PENDING|DEPOSIT_AND_BALANCE|EMAIL' `
        ("{0}|{1}|{2}" -f $detail.Body.approvalStatus, $detail.Body.paymentAgreement.kind, $detail.Body.deliveryHistory[0].channel)

    $minimal = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteB"
    Add-Result 'minimal required-only Quote succeeds' '200' $minimal.Status
    $minimalActual = @($minimal.Body.PSObject.Properties.Name)
    Add-Result 'required-only Quote still contains all required fields' '0' `
        ([string](@($requiredFields | Where-Object { $minimalActual -cnotcontains $_ }).Count))
    Add-Result 'absent optional Quote fields are omitted' 'True' `
        (($minimal.Raw -notmatch 'recipientEmail|adjustments|validUntil|approvalStatus|paymentAgreement|deliveryHistory')).ToString()
    Add-Result 'absent optional line fields are omitted' 'True' `
        (($minimal.Raw -notmatch 'productId|skuSnapshot|taxRate|billingCycleSnapshot')).ToString()

    $unknown = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteUnknown"
    $foreign = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteC"
    Add-Result 'unknown Quote follows nondisclosure status' '404' $unknown.Status
    Add-Result 'foreign Workspace Quote follows nondisclosure status' '404' $foreign.Status
    Add-Result 'unknown and foreign problem behavior match' 'True' (Same-Problem $unknown $foreign).ToString()
    Add-Result 'foreign Quote response leaks no value' 'True' ($foreign.Raw -notmatch [regex]::Escape($secretC)).ToString()
    Add-Result 'malformed Quote ID is nondisclosing' '404' (Invoke-Quote -Method 'GET' -Path '/quotes/bad%20quote').Status

    Set-QuoteScope -RoleId $roleId -Scope 'Own'
    $ownDetail = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteA"
    $ownUnknown = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteUnknown"
    Add-Result 'OWN fails closed despite matching ownerId wire field' '404' $ownDetail.Status
    Add-Result 'OWN access denial does not leak existence' 'True' (Same-Problem $ownDetail $ownUnknown).ToString()
    Add-Result 'OWN list fails closed before Quote query' '0' ([string](Invoke-Quote -Method 'GET' -Path '/quotes').Body.items.Count)
    $ownSearch = Invoke-Quote -Method 'GET' -Path '/quotes?search=Enterprise'
    Add-Result 'search: OWN record-access scope still fails closed' '0' ([string]$ownSearch.Body.items.Count)
    Add-Result 'search: OWN record-access totalCount still fails closed' '0' `
        ([string]$ownSearch.Body.pageInfo.totalCount)
    foreach ($scope in @('Team','Custom')) {
        Set-QuoteScope -RoleId $roleId -Scope $scope
        Add-Result ("{0} detail fails closed" -f $scope.ToUpperInvariant()) '404' `
            (Invoke-Quote -Method 'GET' -Path "/quotes/$quoteA").Status
        Add-Result ("{0} list fails closed" -f $scope.ToUpperInvariant()) '0' `
            ([string](Invoke-Quote -Method 'GET' -Path '/quotes').Body.items.Count)
    }

    Set-QuoteScope -RoleId $roleId -Scope 'Workspace'
    Clear-QuoteFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_quotes_read_recipient', '$roleId', 'quotes', 'recipientEmail', 'Hidden'),
('field_quotes_read_notes', '$roleId', 'quotes', 'notes', 'Masked');
"@
    $fieldDetail = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteA"
    Add-Result 'optional HIDDEN Quote field is omitted' 'True' `
        ($fieldDetail.Body.PSObject.Properties.Name -cnotcontains 'recipientEmail').ToString()
    Add-Result 'MASKED Quote field is withheld safely' 'True' `
        (($fieldDetail.Raw -notmatch '"notes"') -and ($fieldDetail.Raw -notmatch [regex]::Escape($secretA))).ToString()
    Clear-QuoteFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_quotes_read_required', '$roleId', 'quotes', 'title', 'Hidden');
"@
    $requiredHidden = Invoke-Quote -Method 'GET' -Path "/quotes/$quoteA"
    Add-Result 'required Quote field restriction fails operation closed' '403' $requiredHidden.Status
    Add-Result 'required restricted Quote value is absent' 'True' ($requiredHidden.Raw -notmatch 'Verified Enterprise Quote').ToString()
    Clear-QuoteFields

    # Simulate legacy/corrupt persisted JSON after disabling only the physical JSON constraint.
    # Projection must fail before owner evidence is appended.
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
ALTER TABLE quotes.Quotes NOCHECK CONSTRAINT CK_Quotes_ActionsJson;
UPDATE quotes.Quotes SET ActionsJson = N'{'
WHERE WorkspaceId = '$($script:WorkspaceId)' AND QuoteId = '$quoteA';
"@
    Add-Result 'read audit: corrupt persisted detail => 500 and +0' '500|0' `
        (Measure-QuoteRead -Path "/quotes/$quoteA").Probe
    Add-Result 'read audit: corrupt persisted list => 500 and +0' '500|0' `
        (Measure-QuoteRead -Path '/quotes').Probe
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
UPDATE quotes.Quotes
SET ActionsJson = N'{"accept":{"allowed":false,"blockerCodes":["QUOTE_APPROVAL_REQUIRED"]}}'
WHERE WorkspaceId = '$($script:WorkspaceId)' AND QuoteId = '$quoteA';
ALTER TABLE quotes.Quotes WITH CHECK CHECK CONSTRAINT CK_Quotes_ActionsJson;
"@
    Add-Result 'restored Quote fixture reads cleanly with +1 audit' '200|1' `
        (Measure-QuoteRead -Path "/quotes/$quoteA").Probe

    $wrongWorkspace = Invoke-Api -Method 'GET' -Path "/quotes/$quoteA" `
        -Token $script:Token -WorkspaceId $foreignWorkspaceId
    Add-Result 'untrusted foreign Workspace header is denied' '403' $wrongWorkspace.Status

    $countBeforeMutationProbes = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM quotes.Quotes'
    Add-Result 'POST /quotes mutation is not mapped' '405' (Invoke-Quote -Method 'POST' -Path '/quotes' -Body '{}').Status
    Add-Result 'PUT Quote mutation is not mapped' '405' (Invoke-Quote -Method 'PUT' -Path "/quotes/$quoteA" -Body '{}').Status
    Add-Result 'PATCH Quote mutation is not mapped' '405' (Invoke-Quote -Method 'PATCH' -Path "/quotes/$quoteA" -Body '{}').Status
    Add-Result 'DELETE Quote mutation is not mapped' '405' (Invoke-Quote -Method 'DELETE' -Path "/quotes/$quoteA").Status
    Add-Result 'Quote acceptance route is absent' '404' (Invoke-Quote -Method 'POST' -Path "/quotes/$quoteA/accept" -Body '{}').Status
    Add-Result 'mutation probes change no Quote state' ([string]$countBeforeMutationProbes) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM quotes.Quotes'))

    $quoteRoot = Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Quotes'
    $quoteSource = (Get-ChildItem -LiteralPath $quoteRoot -Recurse -File -Filter '*.cs' | Get-Content -Raw) -join "`n"
    $endpointSource = Get-Content -Raw -LiteralPath (Join-Path $quoteRoot 'Contracts/QuotesEndpoints.cs')
    Add-Result 'Quotes maps exactly the two admitted GET routes' '2' `
        ([string]([regex]::Matches($endpointSource, 'endpoints\.MapGet\(').Count))
    Add-Result 'Quotes maps no mutation method' '0' `
        ([string]([regex]::Matches($endpointSource, 'endpoints\.Map(Post|Put|Patch|Delete)\(').Count))
    Add-Result 'listQuotes operationId is exact' 'True' ($endpointSource -cmatch '\.WithName\("listQuotes"\)').ToString()
    Add-Result 'getQuote operationId is exact' 'True' ($endpointSource -cmatch '\.WithName\("getQuote"\)').ToString()
    Add-Result 'Quotes has no Product runtime lookup or DbContext' 'True' `
        (($quoteSource -notmatch 'ProductsDbContext') -and ($quoteSource -notmatch 'UnicoreCRM\.Sales\.Products') -and ($quoteSource -notmatch '\bIProduct[A-Za-z]*Reader\b')).ToString()
    Add-Result 'Quotes has no foreign owner DbContext' 'True' `
        ($quoteSource -notmatch '\b(Deals|Orders|Customers|Contacts|Organizations|Products)DbContext\b').ToString()
    Add-Result 'Quotes has owner-local durable read-audit runtime' 'True' `
        (($quoteSource -match 'QuoteReadAuditRecord') `
            -and ($quoteSource -match 'ReadAuditRecords') `
            -and ($quoteSource -match 'AddReadAudit')).ToString()
    Add-Result 'Quotes has no generic cross-module audit framework' 'True' `
        ($quoteSource -notmatch 'IAuditFramework|IReadAuditService|GenericAudit').ToString()
    # SaveChanges is admitted only for owner read-evidence append persistence. The Quote surface
    # remains read-only and no business mutation path may acquire it.
    $saveChangesFiles = (Get-ChildItem -LiteralPath $quoteRoot -Recurse -File -Filter '*.cs' |
        Where-Object { (Get-Content -Raw -LiteralPath $_.FullName) -match 'SaveChangesAsync' } |
        ForEach-Object Name | Sort-Object) -join ','
    Add-Result 'SaveChanges confined to read-audit append' `
        'EfQuotesPersistence.cs,QuoteReadAudit.cs,QuotesApplication.cs' $saveChangesFiles
    Add-Result 'Quotes adds neither WF-16 nor WF-22 runtime' 'True' `
        (($quoteSource -notmatch 'WF-16|WF-22|acceptQuoteAndCloseDeal|convertAcceptedQuoteToOrderDraft')).ToString()

    $logText = ''
    if (Test-Path -LiteralPath $logPath) { $logText += Get-Content -Raw -LiteralPath $logPath }
    if (Test-Path -LiteralPath "$logPath.err") { $logText += Get-Content -Raw -LiteralPath "$logPath.err" }
    Add-Result 'foreign Quote business value absent from host logs' 'True' ($logText -notmatch [regex]::Escape($secretC)).ToString()
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit(10000) | Out-Null
    }

    Push-Location $repositoryRoot
    try {
        & dotnet ef migrations has-pending-model-changes --project $salesProject --context QuotesDbContext --no-build
        Add-Result 'no pending Quotes EF model changes' '0' ([string]$LASTEXITCODE)
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
Write-Host ("Quotes Read Core verification: passed={0} failed={1}" -f $script:Passed, $script:Failed)
if ($script:Failed -ne 0) { throw 'Quotes Read Core verification failed.' }
Write-Host 'QUOTE SEARCH CORPUS: quoteNumber + title'
Write-Host 'QUOTE SEARCH MATCH: CASE_INSENSITIVE_SUBSTRING'
Write-Host 'QUOTE SEARCH SEMANTICS: PASS'
Write-Host 'QUOTE OWNER-LOCAL READ CORE: IMPLEMENTED AND VERIFIED'
