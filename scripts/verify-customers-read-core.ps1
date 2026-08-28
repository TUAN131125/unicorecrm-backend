<#
.SYNOPSIS
    Reproducible Customers Read Core security verification against an isolated database and real ApiHost.

.DESCRIPTION
    Customers has no admitted mutation API. This harness therefore seeds owner-local read state with
    controlled SQL after applying the real Customers migration, and exercises the public list/detail
    routes plus the canonical AccessControl evaluator. It never creates a hidden production write path.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5331,

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
    return ('req-customers-read-{0:d6}' -f $script:RequestCounter)
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
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-customers-read-core-0001')
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

function Invoke-Customer {
    param([string] $Method, [string] $Path, [string] $Body)
    return Invoke-Api -Method $Method -Path $Path -Body $Body -Token $script:Token -WorkspaceId $script:WorkspaceId
}

function Set-CustomerScope {
    param([string] $RoleId, [string] $Scope)
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_customers_read_core';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_customers_read_core', '$RoleId', 'customers', '$Scope', '[]');
"@
}

function Clear-CustomerFields {
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_customers_read_%'"
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
$crmProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Crm/UnicoreCRM.Crm.csproj'
$demoEmail = 'customers.view.provisioned@example.test'
$demoPassword = 'Customers-Read-Core!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-customers-read-$([Guid]::NewGuid().ToString('N')).log")
$customerA = 'customer_read_core_a'
$customerB = 'customer_read_core_b'
$customerC = 'customer_read_core_c'
$customerUnknown = 'customer_read_core_unknown'
$secretA = 'CUSTOMER-A-EXTERNAL-PRIVATE-VALUE'
$secretB = 'CUSTOMER-B-HIDDEN-BUSINESS-VALUE'
$secretC = 'CUSTOMER-C-FOREIGN-BUSINESS-VALUE'
$contactRelationshipId = 'contact_relationship_ref_a'
$organizationRelationshipId = 'organization_relationship_ref_b'

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
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Customers Provisioning Fixture'
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

    $anonymousList = Invoke-Api -Method 'GET' -Path '/customers' -WorkspaceId 'ws_unknown'
    $anonymousDetail = Invoke-Api -Method 'GET' -Path "/customers/$customerA" -WorkspaceId 'ws_unknown'
    Add-Result 'unauthenticated list rejected' '401' $anonymousList.Status
    Add-Result 'unauthenticated detail rejected' '401' $anonymousDetail.Status

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' `
        -IdempotencyKey 'idem-customers-read-signin-0001' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    $callerMemberId = $session.Body.principal.memberId
    $provisioning = Invoke-Api -Method 'POST' -Path '/workspaces/initial-provisioning' `
        -Token $script:Token -IdempotencyKey 'idem-customers-read-provisioning-0001' `
        -Body '{"name":"Customers Read Workspace"}'
    Add-Result 'initial Workspace provisioning succeeds' '201' $provisioning.Status
    $script:WorkspaceId = $provisioning.Body.workspaceId
    $foreignWorkspaceId = 'ws_customers_read_foreign'
    $roleId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)' AND Name = 'Workspace Owner'"
    if ([string]::IsNullOrWhiteSpace($script:WorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($foreignWorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($callerMemberId) `
        -or [string]::IsNullOrWhiteSpace($roleId)) {
        throw 'The Development identity/workspace/access fixture was not provisioned.'
    }
    $provisionedCustomersRead = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'customers.view'"
    Add-Result 'initial Workspace provisioning does not invent customers.view default authority' '0' ([string]$provisionedCustomersRead)
    $provisionedBootstrap = Invoke-Api -Method 'GET' -Path "/workspaces/$($script:WorkspaceId)/bootstrap" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'provisioned Workspace bootstrap succeeds' '200' $provisionedBootstrap.Status
    Add-Result 'initial Workspace provisioning preserves the exact existing module defaults' `
        'contacts,leads,deals,tasks' `
        ((@($provisionedBootstrap.Body.configuration.enabledModuleKeys)) -join ',')

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'customers.view')"
    Add-Result 'controlled fixture grants one canonical customers.view' '1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'customers.view'"))

    $customersTable = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'customers' AND TABLE_NAME = 'Customers'"
    Add-Result 'Customers migration created owner table' '1' ([string]$customersTable)
    $readAuditTable = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'customers' AND TABLE_NAME = 'ReadAuditRecords'"
    Add-Result 'Customers migration created read-audit table' '1' ([string]$readAuditTable)
    $indexCount = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'customers' AND t.name = 'Customers'
  AND i.name = 'IX_Customers_WorkspaceId_CreatedAt_CustomerId'
"@
    Add-Result 'Customers Workspace list index applied' '1' ([string]$indexCount)
    $reverseIndexCount = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'customers' AND t.name = 'Customers'
  AND i.name = 'IX_Customers_WorkspaceId_RelationshipType_RelationshipId'
  AND i.is_unique = 1
"@
    Add-Result 'Customers Workspace relationship reverse key is unique' '1' ([string]$reverseIndexCount)

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Workspaces WHERE WorkspaceId = '$foreignWorkspaceId')
INSERT INTO workspace.Workspaces (WorkspaceId, [Key], Name, LogoText, CreatedAt)
VALUES ('$foreignWorkspaceId', 'customers-read-foreign', 'Customers Read Foreign Workspace', 'CF', SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM workspace.Memberships WHERE MemberId = 'mem-customers-read-other')
INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, Status, CreatedAt)
VALUES ('wsm-customers-read-other', '$($script:WorkspaceId)', 'acc-customers-read-other', 'mem-customers-read-other', 'Active', SYSUTCDATETIME());

INSERT INTO customers.Customers
(WorkspaceId, CustomerId, CustomerCode, Type, RelationshipType, RelationshipId, Status, Health,
 FirstPurchaseAt, LastPurchaseAt, Version, CreatedAt, UpdatedAt, Profile)
VALUES
('$($script:WorkspaceId)', '$customerA', 'CUS-A-001', 'B2C', 'CONTACT', '$contactRelationshipId', 'ACTIVE', 'GOOD',
 DATEADD(day, -30, SYSUTCDATETIME()), DATEADD(day, -2, SYSUTCDATETIME()), 4,
 DATEADD(minute, -30, SYSUTCDATETIME()), DATEADD(minute, -5, SYSUTCDATETIME()),
 N'{"calculatedHealth":"GOOD","onboardingStatus":"PENDING","sourceSystem":"verified-fixture","externalCustomerRef":"$secretA","careOwnerId":"$callerMemberId","segment":"READ_ONLY-CUSTOMER-SEGMENT","tags":["priority"]}'),
('$($script:WorkspaceId)', '$customerB', 'CUS-B-001', 'B2B', 'ORGANIZATION_ACCOUNT', '$organizationRelationshipId', 'AT_RISK', 'WATCH',
 DATEADD(day, -60, SYSUTCDATETIME()), DATEADD(day, -3, SYSUTCDATETIME()), 2,
 DATEADD(minute, -20, SYSUTCDATETIME()), DATEADD(minute, -4, SYSUTCDATETIME()),
 N'{"calculatedHealth":"WATCH","onboardingStatus":"COMPLETED","sourceSystem":"verified-fixture","externalCustomerRef":"beta-external","careOwnerId":"mem-customers-read-other","segment":"$secretB"}'),
('$foreignWorkspaceId', '$customerC', 'CUS-C-001', 'B2C', 'CONTACT', '$contactRelationshipId', 'ACTIVE', 'GOOD',
 DATEADD(day, -10, SYSUTCDATETIME()), DATEADD(day, -1, SYSUTCDATETIME()), 1,
 DATEADD(minute, -10, SYSUTCDATETIME()), DATEADD(minute, -3, SYSUTCDATETIME()),
 N'{"sourceSystem":"foreign-fixture","externalCustomerRef":"$secretC","careOwnerId":"mem-customers-read-other"}');
"@

    $sameWorkspaceDuplicateRejected = $false
    try {
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO customers.Customers
(WorkspaceId, CustomerId, CustomerCode, Type, RelationshipType, RelationshipId, Status, Health,
 FirstPurchaseAt, LastPurchaseAt, Version, CreatedAt, UpdatedAt, Profile)
VALUES
('$($script:WorkspaceId)', 'customer_duplicate_relationship', 'CUS-DUP', 'B2C', 'CONTACT', '$contactRelationshipId', 'NEW', 'GOOD',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 0, SYSUTCDATETIME(), SYSUTCDATETIME(), N'{}');
"@
    }
    catch { $sameWorkspaceDuplicateRejected = $true }
    Add-Result 'same relationshipRef cannot exist twice in one Workspace' 'True' $sameWorkspaceDuplicateRejected.ToString()
    Add-Result 'same relationshipRef may exist in another Workspace' '2' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.Customers WHERE RelationshipType = 'CONTACT' AND RelationshipId = '$contactRelationshipId'"))

    Set-CustomerScope -RoleId $roleId -Scope 'Workspace'

    $provisionedList = Invoke-Customer -Method 'GET' -Path '/customers'
    Add-Result 'controlled customers.view permits the first Customers list' '200' $provisionedList.Status
    Add-Result 'first Customers list contains only trusted Workspace rows' '2' ([string]$provisionedList.Body.Count)

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'customers.view'"
    Add-Result 'no Customers read capability denies list' '403' (Invoke-Customer -Method 'GET' -Path '/customers').Status
    Add-Result 'no Customers read capability denies detail' '403' (Invoke-Customer -Method 'GET' -Path "/customers/$customerA").Status
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'customers.view')"
    Add-Result 'negative capability test restores one canonical customers.view' '1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'customers.view'"))

    $workspaceList = Invoke-Customer -Method 'GET' -Path '/customers'
    Add-Result 'WORKSPACE list succeeds' '200' $workspaceList.Status
    Add-Result 'list is the admitted plain array representation' '2' ([string]$workspaceList.Body.Count)
    Add-Result 'list includes trusted Customer A' 'True' ($workspaceList.Body.id -contains $customerA).ToString()
    Add-Result 'list includes trusted Customer B' 'True' ($workspaceList.Body.id -contains $customerB).ToString()
    Add-Result 'foreign Workspace Customer absent from list' 'False' ($workspaceList.Body.id -contains $customerC).ToString()
    Add-Result 'foreign business value absent from list bytes' 'True' ($workspaceList.Raw -notmatch [regex]::Escape($secretC)).ToString()
    Add-Result 'unadmitted page metadata absent' 'True' ($workspaceList.Raw -notmatch 'pageInfo|totalCount|nextCursor').ToString()

    $customerDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerA"
    Add-Result 'own Workspace detail succeeds' '200' $customerDetail.Status
    Add-Result 'detail identity is Customer-owned ID' $customerA $customerDetail.Body.id
    Add-Result 'detail carries trusted Workspace' $script:WorkspaceId $customerDetail.Body.workspaceId
    Add-Result 'detail required customerCode present' 'CUS-A-001' $customerDetail.Body.customerCode
    Add-Result 'detail required version present' '4' ([string]$customerDetail.Body.version)
    Add-Result 'RelationshipRef CONTACT projects exactly' 'CONTACT|contact_relationship_ref_a' `
        ("{0}|{1}" -f $customerDetail.Body.relationshipRef.type, $customerDetail.Body.relationshipRef.id)
    Add-Result 'customerId remains independent from relationshipRef.id' 'True' `
        ($customerDetail.Body.id -ne $customerDetail.Body.relationshipRef.id).ToString()
    $organizationDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerB"
    Add-Result 'RelationshipRef ORGANIZATION_ACCOUNT projects exactly' 'ORGANIZATION_ACCOUNT|organization_relationship_ref_b' `
        ("{0}|{1}" -f $organizationDetail.Body.relationshipRef.type, $organizationDetail.Body.relationshipRef.id)
    $malformedDetail = Invoke-Customer -Method 'GET' -Path '/customers/bad%20customer%20id'
    Add-Result 'malformed Customer EntityId is indistinguishable from unknown' '404' $malformedDetail.Status

    $foreignDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerC"
    $unknownDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerUnknown"
    Add-Result 'foreign detail is not found' '404' $foreignDetail.Status
    Add-Result 'unknown detail is not found' '404' $unknownDetail.Status
    Add-Result 'foreign and unknown problem behavior match' 'True' (Same-Problem $foreignDetail $unknownDetail).ToString()
    Add-Result 'foreign detail leaks no business value' 'True' `
        (($foreignDetail.Raw -notmatch [regex]::Escape($secretC)) -and ($foreignDetail.Raw -notmatch 'Customer Foreign')).ToString()

    Set-CustomerScope -RoleId $roleId -Scope 'Own'
    $hiddenCustomerAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM customers.ReadAuditRecords WHERE Operation = 'getCustomer' AND WorkspaceId = '$($script:WorkspaceId)' AND CustomerId = '$customerB'"
    $ownDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerA"
    $hiddenDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerB"
    $ownUnknown = Invoke-Customer -Method 'GET' -Path "/customers/$customerUnknown"
    Add-Result 'unresolved OWN fails closed even when careOwnerId matches caller' '404' $ownDetail.Status
    Add-Result 'unresolved OWN hides same-Workspace Customer' '404' $hiddenDetail.Status
    Add-Result 'scope-hidden and unknown problem behavior match' 'True' (Same-Problem $hiddenDetail $ownUnknown).ToString()
    Add-Result 'scope-hidden response leaks no business value' 'True' `
        (($hiddenDetail.Raw -notmatch [regex]::Escape($secretB)) -and ($hiddenDetail.Raw -notmatch 'Customer Beta')).ToString()
    $hiddenCustomerAuditAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM customers.ReadAuditRecords WHERE Operation = 'getCustomer' AND WorkspaceId = '$($script:WorkspaceId)' AND CustomerId = '$customerB'"
    Add-Result 'scope-hidden detail adds no Customers successful-read audit' '0' `
        ([string]([int]$hiddenCustomerAuditAfter - [int]$hiddenCustomerAuditBefore))
    $ownList = Invoke-Customer -Method 'GET' -Path '/customers'
    Add-Result 'unresolved OWN list fails closed before materialization' '0' ([string]$ownList.Body.Count)

    foreach ($unsupported in @('Team', 'Custom')) {
        Set-CustomerScope -RoleId $roleId -Scope $unsupported
        Add-Result ("{0} detail fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Customer -Method 'GET' -Path "/customers/$customerA").Status
        Add-Result ("{0} list fails closed" -f $unsupported.ToUpperInvariant()) '0' `
            ([string](Invoke-Customer -Method 'GET' -Path '/customers').Body.Count)
    }

    Set-CustomerScope -RoleId $roleId -Scope 'Workspace'
    Clear-CustomerFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_customers_read_external', '$roleId', 'customers', 'externalCustomerRef', 'Hidden'),
('field_customers_read_segment_mask', '$roleId', 'customers', 'segment', 'Masked'),
('field_customers_read_onboarding', '$roleId', 'customers', 'onboardingStatus', 'ReadOnly'),
('field_customers_read_source', '$roleId', 'customers', 'sourceSystem', 'ReadWrite'),
('field_customers_read_unknown', '$roleId', 'customers', 'ghostField', 'ReadWrite');
"@
    $fieldDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerA"
    Add-Result 'optional HIDDEN field omitted' 'True' ($fieldDetail.Raw -notmatch '"externalCustomerRef"').ToString()
    Add-Result 'hidden business value absent from raw bytes' 'True' ($fieldDetail.Raw -notmatch [regex]::Escape($secretA)).ToString()
    Add-Result 'MASKED value withheld safely' 'True' ($fieldDetail.Raw -notmatch '"segment"|READ_ONLY-CUSTOMER-SEGMENT').ToString()
    Add-Result 'READ_ONLY field remains readable' 'True' ($fieldDetail.Raw -match '"onboardingStatus":"PENDING"').ToString()
    Add-Result 'READ_WRITE field remains readable' 'True' ($fieldDetail.Raw -match 'verified-fixture').ToString()

    $unknownField = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'customers'; recordId = $customerA; requestedFields = @('ghostField') } | ConvertTo-Json -Compress)
    Add-Result 'unknown field evaluation succeeds safely' '200' $unknownField.Status
    Add-Result 'unknown field cannot widen read access' 'HIDDEN' $unknownField.Body.fieldAccess.ghostField
    Add-Result 'unknown field has no projected value' 'True' ($fieldDetail.Raw -notmatch 'ghostField').ToString()

    $spoofOwner = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'customers'; recordId = $customerB; ownerId = $callerMemberId } | ConvertTo-Json -Compress)
    $spoofWorkspace = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'customers'; recordId = $customerA; workspaceId = $foreignWorkspaceId } | ConvertTo-Json -Compress)
    $spoofTeam = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'customers'; recordId = $customerA; teamId = 'team_spoof' } | ConvertTo-Json -Compress)
    Add-Result 'caller-supplied owner fact rejected' '422' $spoofOwner.Status
    Add-Result 'caller-supplied Workspace fact rejected' '422' $spoofWorkspace.Status
    Add-Result 'caller-supplied team fact rejected' '422' $spoofTeam.Status

    Clear-CustomerFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_customers_read_required', '$roleId', 'customers', 'customerCode', 'Hidden');
"@
    $requiredRestricted = Invoke-Customer -Method 'GET' -Path "/customers/$customerA"
    Add-Result 'required-field restriction fails operation closed' '403' $requiredRestricted.Status
    Add-Result 'required restricted value absent' 'True' ($requiredRestricted.Raw -notmatch 'CUS-A-001').ToString()
    Clear-CustomerFields

    $wrongWorkspace = Invoke-Api -Method 'GET' -Path "/customers/$customerA" `
        -Token $script:Token -WorkspaceId $foreignWorkspaceId
    Add-Result 'wrong Workspace header cannot become authority' '403' $wrongWorkspace.Status

    $recordDecisionsBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions"
    $authorizationsBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.AuthorizationDecisions"
    $listReadAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM customers.ReadAuditRecords WHERE Operation = 'listCustomers'"
    [void](Invoke-Customer -Method 'GET' -Path '/customers')
    $recordDecisionsAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions"
    $authorizationsAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.AuthorizationDecisions"
    $listReadAuditAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM customers.ReadAuditRecords WHERE Operation = 'listCustomers'"
    Add-Result 'list performs no per-row record evaluations' '0' `
        ([string]([int]$recordDecisionsAfter - [int]$recordDecisionsBefore))
    Add-Result 'list performs exactly one resource authorization' '1' `
        ([string]([int]$authorizationsAfter - [int]$authorizationsBefore))
    Add-Result 'successful list writes one Customers read audit' '1' `
        ([string]([int]$listReadAuditAfter - [int]$listReadAuditBefore))

    $detailReadAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM customers.ReadAuditRecords WHERE Operation = 'getCustomer' AND WorkspaceId = '$($script:WorkspaceId)' AND CustomerId = '$customerA'"
    [void](Invoke-Customer -Method 'GET' -Path "/customers/$customerA")
    $detailReadAuditAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM customers.ReadAuditRecords WHERE Operation = 'getCustomer' AND WorkspaceId = '$($script:WorkspaceId)' AND CustomerId = '$customerA'"
    Add-Result 'successful detail writes one Customers read audit' '1' `
        ([string]([int]$detailReadAuditAfter - [int]$detailReadAuditBefore))
    $completeReadAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM customers.ReadAuditRecords
WHERE Operation = 'getCustomer' AND WorkspaceId = '$($script:WorkspaceId)'
  AND CustomerId = '$customerA' AND ActorId = '$callerMemberId'
  AND CustomerVersion = 4 AND RequestId <> '' AND CorrelationId <> ''
"@
    Add-Result 'Customers read audit carries trusted actor and request evidence' 'True' `
        ([int]$completeReadAudit -gt 0).ToString()

    $foreignAuditRows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM access.RecordAccessDecisions
WHERE ResourceKey = 'customers' AND RecordId = '$customerC'
"@
    Add-Result 'foreign Customer never enters record audit' '0' ([string]$foreignAuditRows)
    $foreignOwnerAuditRows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM customers.ReadAuditRecords
WHERE CustomerId IN ('$customerC', '$customerUnknown')
"@
    Add-Result 'foreign and unknown Customers never enter owner read audit' '0' ([string]$foreignOwnerAuditRows)

    $countBeforeMutationProbe = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.Customers'
    $postProbe = Invoke-Customer -Method 'POST' -Path '/customers' -Body '{}'
    $putProbe = Invoke-Customer -Method 'PUT' -Path "/customers/$customerA" -Body '{}'
    $patchProbe = Invoke-Customer -Method 'PATCH' -Path "/customers/$customerA" -Body '{}'
    $deleteProbe = Invoke-Customer -Method 'DELETE' -Path "/customers/$customerA"
    $customer360Probe = Invoke-Customer -Method 'GET' -Path "/customers/$customerA/360"
    Add-Result 'create Customer method is not mapped' '405' $postProbe.Status
    Add-Result 'replace Customer method is not mapped' '405' $putProbe.Status
    Add-Result 'update Customer method is not mapped' '405' $patchProbe.Status
    Add-Result 'delete Customer method is not mapped' '405' $deleteProbe.Status
    Add-Result 'Customer360 route is absent from read core' '404' $customer360Probe.Status
    Add-Result 'mutation probes changed no Customer state' ([string]$countBeforeMutationProbe) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.Customers'))

    $healthy = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    Add-Result 'ApiHost healthy after denied requests' '200' $healthy.Status

    $logText = ''
    if (Test-Path -LiteralPath $logPath) { $logText += Get-Content -Raw -LiteralPath $logPath }
    if (Test-Path -LiteralPath "$logPath.err") { $logText += Get-Content -Raw -LiteralPath "$logPath.err" }
    Add-Result 'foreign Customer value absent from host logs' 'True' ($logText -notmatch [regex]::Escape($secretC)).ToString()
    $customerSource = (Get-ChildItem -Path (Join-Path $repositoryRoot 'src/UnicoreCRM.Crm/Customers') -Recurse -File -Filter '*.cs' |
        Get-Content -Raw) -join "`n"
    Add-Result 'Customers has no Contact or Organization persistence dependency' 'True' `
        (($customerSource -notmatch 'ContactsDbContext') -and ($customerSource -notmatch 'OrganizationsDbContext')).ToString()
    Add-Result 'Customers has no speculative relationship reader' 'True' `
        ($customerSource -notmatch 'ICustomerRelationshipReader|IContactCustomerReader|IOrganizationCustomerReader').ToString()
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit(10000) | Out-Null
    }

    Push-Location $repositoryRoot
    try {
        & dotnet ef migrations has-pending-model-changes --project $crmProject --context CustomersDbContext --no-build
        Add-Result 'no pending Customers EF model changes' '0' ([string]$LASTEXITCODE)
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
Write-Host ("Customers Read Core verification: passed={0} failed={1}" -f $script:Passed, $script:Failed)
if ($script:Failed -ne 0) { throw 'Customers Read Core verification failed.' }
Write-Host 'CUSTOMERS READ CORE: PASS'

