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
        [string] $IfMatch,
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
    if (-not [string]::IsNullOrWhiteSpace($IfMatch)) {
        [void]$request.Headers.TryAddWithoutValidation('If-Match', $IfMatch)
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
    param([string] $Method, [string] $Path, [string] $Body, [string] $IdempotencyKey, [string] $IfMatch)
    return Invoke-Api -Method $Method -Path $Path -Body $Body -Token $script:Token -WorkspaceId $script:WorkspaceId -IdempotencyKey $IdempotencyKey -IfMatch $IfMatch
}

function Start-CustomerCreate {
    param([string] $Body, [string] $IdempotencyKey)
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/customers")
    [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId))
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-customers-create-race-0001')
    [void]$request.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
    [void]$request.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
    [void]$request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey)
    $request.Content = New-Object System.Net.Http.StringContent ($Body, [Text.Encoding]::UTF8, 'application/json')
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.UseProxy = $false
    $client = New-Object System.Net.Http.HttpClient ($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    return [pscustomobject]@{ Client = $client; Request = $request; Task = $client.SendAsync($request) }
}

function Complete-CustomerCreate {
    param($Pending)
    try {
        $response = $Pending.Task.GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Raw = $raw }
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $Pending.Client.Dispose()
        $Pending.Request.Dispose()
    }
}

function Set-CustomerScope {
    param([string] $RoleId, [string] $Scope)
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_customers_read_core';
INSERT INTO access.RoleDataScopes (PolicyId, WorkspaceId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_customers_read_core', '$($script:WorkspaceId)', '$RoleId', 'customers', '$Scope', '[]');
"@
}

function Clear-CustomerFields {
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_customers_read_%'"
}

function Assert-InvalidCustomerEnumRejected {
    param(
        [string] $TestId,
        [string] $FieldName,
        [string] $ExpectedConstraint,
        [string] $Type,
        [string] $RelationshipType,
        [string] $Status,
        [string] $Health
    )

    $customerId = "customer_invalid_$TestId"
    $rejected = $false
    try {
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO customers.Customers
(WorkspaceId, CustomerId, CustomerCode, Type, RelationshipType, RelationshipId, Status, Health,
 FirstPurchaseAt, LastPurchaseAt, Version, CreatedAt, UpdatedAt, Profile)
VALUES
('$($script:WorkspaceId)', '$customerId', 'CUS-INVALID-$TestId', '$Type', '$RelationshipType',
 'relationship_invalid_$TestId', '$Status', '$Health', SYSUTCDATETIME(), SYSUTCDATETIME(), 0,
 SYSUTCDATETIME(), SYSUTCDATETIME(), N'{}');
"@
    }
    catch {
        $exception = $_.Exception
        $sqlException = $null
        while ($null -ne $exception) {
            if ($exception -is [System.Data.SqlClient.SqlException]) {
                $sqlException = $exception
                break
            }
            $exception = $exception.InnerException
        }
        if ($null -eq $sqlException `
            -or $sqlException.Number -ne 547 `
            -or $sqlException.Message -notmatch [regex]::Escape($ExpectedConstraint)) {
            throw
        }
        $rejected = $true
    }

    Add-Result "$FieldName invalid persisted value is rejected" 'True' $rejected.ToString()
    $persisted = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM customers.Customers WHERE WorkspaceId = '$($script:WorkspaceId)' AND CustomerId = '$customerId'"
    Add-Result "$FieldName invalid persisted value leaves no row" '0' ([string]$persisted)
    if ([int]$persisted -ne 0) {
        Invoke-SqlNonQuery -Database $DatabaseName `
            -Query "DELETE FROM customers.Customers WHERE WorkspaceId = '$($script:WorkspaceId)' AND CustomerId = '$customerId'"
    }
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
    $env:Logging__LogLevel__Microsoft = 'Warning'

    & dotnet run --no-build --no-launch-profile --project $hostProject -- --migrate
    if ($LASTEXITCODE -ne 0) { throw "ApiHost migration command failed with exit code $LASTEXITCODE." }
    & dotnet run --no-build --no-launch-profile --project $hostProject -- --seed-demo
    if ($LASTEXITCODE -ne 0) { throw "ApiHost bootstrap command failed with exit code $LASTEXITCODE." }

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

    # The HTTP listener can become reachable before the ordered development migration hosted
    # services finish. Wait for the authentication schema instead of racing the first sign-in.
    $schemaReady = $false
    for ($attempt = 0; $attempt -lt $ReadyTimeoutSeconds; $attempt++) {
        $identityTables = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'iam' AND t.name IN ('IdempotencyRecords', 'EmailOutboxMessages')"
        if ([int]$identityTables -eq 2) { $schemaReady = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $schemaReady) { throw "IdentityAuth migrations did not become ready within $ReadyTimeoutSeconds seconds. See $logPath" }

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
    Add-Result 'initial Workspace provisioning grants canonical customers.view to Workspace Owner' '1' ([string]$provisionedCustomersRead)
    $provisionedBootstrap = Invoke-Api -Method 'GET' -Path "/workspaces/$($script:WorkspaceId)/bootstrap" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'provisioned Workspace bootstrap succeeds' '200' $provisionedBootstrap.Status
    Add-Result 'initial Workspace provisioning exposes the admitted CRM module defaults' `
        'leads,customers,contacts,deals,quotes,orders,support,organizations,tasks,payments,invoices,shipping,returns' `
        ((@($provisionedBootstrap.Body.configuration.enabledModuleKeys)) -join ',')

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "IF NOT EXISTS (SELECT 1 FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'customers.view') INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'customers.view')"
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
  AND i.name = 'IX_Customers_WorkspaceId_Status_CreatedAt_CustomerId'
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
    $requiredEnumConstraintCount = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.check_constraints c
JOIN sys.tables t ON t.object_id = c.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'customers' AND t.name = 'Customers'
  AND c.name IN ('CK_Customers_Type', 'CK_Customers_RelationshipType', 'CK_Customers_Status', 'CK_Customers_Health')
  AND c.is_disabled = 0 AND c.is_not_trusted = 0
"@
    Add-Result 'Customers required enum constraints are present, enabled, and trusted' '4' ([string]$requiredEnumConstraintCount)

    Assert-InvalidCustomerEnumRejected -TestId 'type_lower' -FieldName 'Type lowercase' -ExpectedConstraint 'CK_Customers_Type' `
        -Type 'b2c' -RelationshipType 'CONTACT' -Status 'ACTIVE' -Health 'GOOD'
    Assert-InvalidCustomerEnumRejected -TestId 'type_padded' -FieldName 'Type padded' -ExpectedConstraint 'CK_Customers_Type' `
        -Type 'B2C ' -RelationshipType 'CONTACT' -Status 'ACTIVE' -Health 'GOOD'
    Assert-InvalidCustomerEnumRejected -TestId 'relationship_lower' -FieldName 'RelationshipType lowercase' -ExpectedConstraint 'CK_Customers_RelationshipType' `
        -Type 'B2C' -RelationshipType 'contact' -Status 'ACTIVE' -Health 'GOOD'
    Assert-InvalidCustomerEnumRejected -TestId 'relationship_padded' -FieldName 'RelationshipType padded' -ExpectedConstraint 'CK_Customers_RelationshipType' `
        -Type 'B2C' -RelationshipType 'CONTACT ' -Status 'ACTIVE' -Health 'GOOD'
    Assert-InvalidCustomerEnumRejected -TestId 'status_lower' -FieldName 'Status lowercase' -ExpectedConstraint 'CK_Customers_Status' `
        -Type 'B2C' -RelationshipType 'CONTACT' -Status 'active' -Health 'GOOD'
    Assert-InvalidCustomerEnumRejected -TestId 'status_padded' -FieldName 'Status padded' -ExpectedConstraint 'CK_Customers_Status' `
        -Type 'B2C' -RelationshipType 'CONTACT' -Status 'ACTIVE ' -Health 'GOOD'
    Assert-InvalidCustomerEnumRejected -TestId 'health_lower' -FieldName 'Health lowercase' -ExpectedConstraint 'CK_Customers_Health' `
        -Type 'B2C' -RelationshipType 'CONTACT' -Status 'ACTIVE' -Health 'good'
    Assert-InvalidCustomerEnumRejected -TestId 'health_padded' -FieldName 'Health padded' -ExpectedConstraint 'CK_Customers_Health' `
        -Type 'B2C' -RelationshipType 'CONTACT' -Status 'ACTIVE' -Health 'GOOD '

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

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO customers.Customers
(WorkspaceId, CustomerId, CustomerCode, Type, RelationshipType, RelationshipId, Status, Health,
 FirstPurchaseAt, LastPurchaseAt, Version, CreatedAt, UpdatedAt, Profile)
VALUES
('$($script:WorkspaceId)', 'customer_enum_new', 'CUS-ENUM-NEW', 'B2C', 'CONTACT', 'enum_ref_new', 'NEW', 'GOOD',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME(),
 N'{"calculatedHealth":"GOOD","manualHealthOverride":"WATCH","onboardingStatus":"PENDING","tier":"STANDARD","serviceLevel":"STANDARD"}'),
('$($script:WorkspaceId)', 'customer_enum_active', 'CUS-ENUM-ACTIVE', 'B2B', 'ORGANIZATION_ACCOUNT', 'enum_ref_active', 'ACTIVE', 'WATCH',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME(),
 N'{"onboardingStatus":"COMPLETED","tier":"SILVER","serviceLevel":"PRIORITY"}'),
('$($script:WorkspaceId)', 'customer_enum_at_risk', 'CUS-ENUM-AT-RISK', 'B2C', 'CONTACT', 'enum_ref_at_risk', 'AT_RISK', 'RISK',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME(),
 N'{"tier":"GOLD","serviceLevel":"PREMIUM"}'),
('$($script:WorkspaceId)', 'customer_enum_inactive', 'CUS-ENUM-INACTIVE', 'B2B', 'ORGANIZATION_ACCOUNT', 'enum_ref_inactive', 'INACTIVE', 'GOOD',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME(),
 N'{"tier":"PLATINUM","serviceLevel":"ENTERPRISE"}'),
('$($script:WorkspaceId)', 'customer_enum_churned', 'CUS-ENUM-CHURNED', 'B2C', 'CONTACT', 'enum_ref_churned', 'CHURNED', 'WATCH',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME(), N'{"tier":"STRATEGIC"}'),
('$($script:WorkspaceId)', 'customer_enum_do_not_contact', 'CUS-ENUM-DNC', 'B2B', 'ORGANIZATION_ACCOUNT', 'enum_ref_dnc', 'DO_NOT_CONTACT', 'RISK',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME(), N'{}'),
('$($script:WorkspaceId)', 'customer_enum_archived', 'CUS-ENUM-ARCHIVED', 'B2C', 'CONTACT', 'enum_ref_archived', 'ARCHIVED', 'GOOD',
 SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME(), N'{}');
"@

    Set-CustomerScope -RoleId $roleId -Scope 'Workspace'
    $validEnumCustomerIds = @(
        'customer_enum_new',
        'customer_enum_active',
        'customer_enum_at_risk',
        'customer_enum_inactive',
        'customer_enum_churned',
        'customer_enum_do_not_contact',
        'customer_enum_archived'
    )
    $validEnumDocuments = New-Object System.Collections.ArrayList
    foreach ($validEnumCustomerId in $validEnumCustomerIds) {
        $validEnumDetail = Invoke-Customer -Method 'GET' -Path "/customers/$validEnumCustomerId"
        Add-Result "admitted enum fixture $validEnumCustomerId is readable" '200' $validEnumDetail.Status
        [void]$validEnumDocuments.Add($validEnumDetail.Body)
    }
    Add-Result 'all admitted Customer Type values persist and project exactly' 'B2B,B2C' `
        ((@($validEnumDocuments.type | Sort-Object -Unique)) -join ',')
    Add-Result 'all admitted RelationshipType values persist and project exactly' 'CONTACT,ORGANIZATION_ACCOUNT' `
        ((@($validEnumDocuments.relationshipRef.type | Sort-Object -Unique)) -join ',')
    Add-Result 'all admitted Customer Status values persist and project exactly' `
        'ACTIVE,ARCHIVED,AT_RISK,CHURNED,DO_NOT_CONTACT,INACTIVE,NEW' `
        ((@($validEnumDocuments.status | Sort-Object -Unique)) -join ',')
    Add-Result 'all admitted Customer Health values persist and project exactly' 'GOOD,RISK,WATCH' `
        ((@($validEnumDocuments.health | Sort-Object -Unique)) -join ',')
    Add-Result 'controlled fixtures use every admitted OnboardingStatus value' 'COMPLETED,PENDING' `
        ((@($validEnumDocuments.onboardingStatus | Where-Object { $_ } | Sort-Object -Unique)) -join ',')
    Add-Result 'controlled fixtures use every admitted Tier value' 'GOLD,PLATINUM,SILVER,STANDARD,STRATEGIC' `
        ((@($validEnumDocuments.tier | Where-Object { $_ } | Sort-Object -Unique)) -join ',')
    Add-Result 'controlled fixtures use every admitted ServiceLevel value' 'ENTERPRISE,PREMIUM,PRIORITY,STANDARD' `
        ((@($validEnumDocuments.serviceLevel | Where-Object { $_ } | Sort-Object -Unique)) -join ',')
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM customers.Customers WHERE WorkspaceId = '$($script:WorkspaceId)' AND CustomerId LIKE 'customer_enum_%'"
    Add-Result 'admitted enum fixtures are removed before Read Core regression checks' '0' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.Customers WHERE WorkspaceId = '$($script:WorkspaceId)' AND CustomerId LIKE 'customer_enum_%'"))

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
    Add-Result 'first Customers list contains only trusted Workspace rows' '2' ([string]$provisionedList.Body.items.Count)

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
    Add-Result 'list uses the admitted page envelope' '2' ([string]$workspaceList.Body.items.Count)
    Add-Result 'list includes trusted Customer A' 'True' ($workspaceList.Body.items.id -contains $customerA).ToString()
    Add-Result 'list includes trusted Customer B' 'True' ($workspaceList.Body.items.id -contains $customerB).ToString()
    Add-Result 'foreign Workspace Customer absent from list' 'False' ($workspaceList.Body.items.id -contains $customerC).ToString()
    Add-Result 'foreign business value absent from list bytes' 'True' ($workspaceList.Raw -notmatch [regex]::Escape($secretC)).ToString()
    Add-Result 'admitted page metadata present' 'True' ($workspaceList.Raw -match 'pageInfo').ToString()

    $customerDetail = Invoke-Customer -Method 'GET' -Path "/customers/$customerA"
    Add-Result 'own Workspace detail succeeds' '200' $customerDetail.Status
    Add-Result 'detail identity is Customer-owned ID' $customerA $customerDetail.Body.id
    Add-Result 'detail carries trusted Workspace' $script:WorkspaceId $customerDetail.Body.workspaceId
    Add-Result 'detail required customerCode present' 'CUS-A-001' $customerDetail.Body.customerCode
    Add-Result 'detail required version present' '4' ([string]$customerDetail.Body.version)
    $requiredCustomerFields = @(
        'id', 'workspaceId', 'customerCode', 'type', 'relationshipRef', 'status', 'health',
        'firstPurchaseAt', 'lastPurchaseAt', 'ownerId', 'version', 'createdAt', 'updatedAt'
    )
    $allowedCustomerFields = @(
        $requiredCustomerFields + @(
            'calculatedHealth', 'manualHealthOverride', 'onboardingStatus', 'onboardingCompletedAt',
            'createdFromEvidenceId', 'conversionPolicyVersion', 'conversionCorrelationId', 'sourceSystem',
            'externalCustomerRef', 'tier', 'serviceLevel', 'careCadenceDays', 'careOwnerId', 'segment',
            'tags', 'nextCareAt', 'lastCareAt'
        )
    )
    $actualCustomerFields = @($customerDetail.Body.PSObject.Properties.Name)
    Add-Result 'Customer detail contains every exact required wire field' '0' `
        ([string](@($requiredCustomerFields | Where-Object { $actualCustomerFields -cnotcontains $_ }).Count))
    Add-Result 'Customer detail contains no field outside the exact wire' '0' `
        ([string](@($actualCustomerFields | Where-Object { $allowedCustomerFields -cnotcontains $_ }).Count))
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
    Add-Result 'unresolved OWN list fails closed before materialization' '0' ([string]$ownList.Body.items.Count)

    foreach ($unsupported in @('Team', 'Custom')) {
        Set-CustomerScope -RoleId $roleId -Scope $unsupported
        Add-Result ("{0} detail fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Customer -Method 'GET' -Path "/customers/$customerA").Status
        Add-Result ("{0} list fails closed" -f $unsupported.ToUpperInvariant()) '0' `
            ([string](Invoke-Customer -Method 'GET' -Path '/customers').Body.items.Count)
    }

    Set-CustomerScope -RoleId $roleId -Scope 'Workspace'
    Clear-CustomerFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_customers_read_external', '$($script:WorkspaceId)', '$roleId', 'customers', 'externalCustomerRef', 'Hidden'),
('field_customers_read_segment_mask', '$($script:WorkspaceId)', '$roleId', 'customers', 'segment', 'Masked'),
('field_customers_read_onboarding', '$($script:WorkspaceId)', '$roleId', 'customers', 'onboardingStatus', 'ReadOnly'),
('field_customers_read_source', '$($script:WorkspaceId)', '$roleId', 'customers', 'sourceSystem', 'ReadWrite'),
('field_customers_read_unknown', '$($script:WorkspaceId)', '$roleId', 'customers', 'ghostField', 'ReadWrite');
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
INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_customers_read_required', '$($script:WorkspaceId)', '$roleId', 'customers', 'customerCode', 'Hidden');
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

    foreach ($capability in @('customers.onboard_existing', 'customers.edit', 'customers.archive')) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query "IF NOT EXISTS (SELECT 1 FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = '$capability') INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', '$capability')"
    }
    $contactCreate = Invoke-Api -Method 'POST' -Path '/contacts' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-customer-core-contact-0001' -Body '{"fullName":"Customer Core Subject","workEmail":"customer-core@example.test"}'
    Add-Result 'Customer fixture Contact create succeeds through owner API' '201' $contactCreate.Status
    $subjectContactId = $contactCreate.Body.result.contact.id
    $createBody = @{ relationshipRef = @{ type = 'CONTACT'; id = $subjectContactId }; segment = 'enterprise'; tags = @('priority'); tier = 'GOLD'; serviceLevel = 'PREMIUM' } | ConvertTo-Json -Depth 5 -Compress
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'customers.onboard_existing'"
    $deniedCreateEffectsBefore = @(
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.Customers'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.AuditRecords'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.OutboxMessages'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.IdempotencyRecords'
    ) -join '|'
    $missingCreateCapability = Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-no-capability'
    $deniedCreateEffectsAfter = @(
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.Customers'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.AuditRecords'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.OutboxMessages'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.IdempotencyRecords'
    ) -join '|'
    Add-Result 'missing customers.onboard_existing denies createCustomer' '403' $missingCreateCapability.Status
    Add-Result 'missing create capability has zero aggregate/audit/outbox/idempotency effects' $deniedCreateEffectsBefore $deniedCreateEffectsAfter
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'customers.onboard_existing')"
    $createResult = Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-0001'
    Add-Result 'createCustomer commits' '201' $createResult.Status
    Add-Result 'createCustomer starts NEW at version zero' 'NEW|0' ("{0}|{1}" -f $createResult.Body.result.status, $createResult.Body.result.version)
    Add-Result 'createCustomer does not fabricate health or purchase timestamps' 'True' `
        (($null -eq $createResult.Body.result.health) -and ($null -eq $createResult.Body.result.firstPurchaseAt) -and ($null -eq $createResult.Body.result.lastPurchaseAt)).ToString()
    $createdCustomerId = $createResult.Body.result.id
    $raceContactCreate = Invoke-Api -Method 'POST' -Path '/contacts' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-customer-core-contact-race-0001' -Body '{"fullName":"Customer Concurrent Subject"}'
    Add-Result 'concurrent create fixture Contact succeeds' '201' $raceContactCreate.Status
    $raceSubjectId = $raceContactCreate.Body.result.contact.id
    $raceBody = @{ relationshipRef = @{ type = 'CONTACT'; id = $raceSubjectId } } | ConvertTo-Json -Depth 4 -Compress
    $raceOne = Start-CustomerCreate -Body $raceBody -IdempotencyKey 'idem-customer-core-race-a'
    $raceTwo = Start-CustomerCreate -Body $raceBody -IdempotencyKey 'idem-customer-core-race-b'
    [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($raceOne.Task, $raceTwo.Task))
    $raceStatuses = @((Complete-CustomerCreate $raceOne).Status, (Complete-CustomerCreate $raceTwo).Status) | Sort-Object
    Add-Result 'concurrent Customer creates for one subject yield one commit and one deterministic conflict' '201,409' ($raceStatuses -join ',')
    $raceCustomerId = Get-Scalar -Database $DatabaseName -Query "SELECT CustomerId FROM customers.Customers WHERE WorkspaceId = '$($script:WorkspaceId)' AND RelationshipType = 'CONTACT' AND RelationshipId = '$raceSubjectId'"
    Add-Result 'concurrent Customer create leaves exactly one aggregate/audit/outbox/idempotency record' '1|1|1|1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT((SELECT COUNT(*) FROM customers.Customers WHERE WorkspaceId = '$($script:WorkspaceId)' AND RelationshipType = 'CONTACT' AND RelationshipId = '$raceSubjectId'),'|',(SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$raceCustomerId'),'|',(SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$raceCustomerId'),'|',(SELECT COUNT(*) FROM customers.IdempotencyRecords WHERE IdempotencyKey IN ('idem-customer-core-race-a','idem-customer-core-race-b')))"))
    $createReplay = Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-0001'
    Add-Result 'createCustomer replay returns the same aggregate' $createdCustomerId $createReplay.Body.result.id
    Add-Result 'createCustomer replay is marked REPLAYED' 'REPLAYED' $createReplay.Body.outcome
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE contacts.Contacts SET ArchivedAt = SYSUTCDATETIME() WHERE WorkspaceId = '$($script:WorkspaceId)' AND ContactId = '$subjectContactId'"
    $replayAfterSubjectIneligible = Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-0001'
    Add-Result 'durable create replay survives later subject ineligibility' '201|REPLAYED' `
        ("{0}|{1}" -f $replayAfterSubjectIneligible.Status, $replayAfterSubjectIneligible.Body.outcome)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE contacts.Contacts SET ArchivedAt = NULL WHERE WorkspaceId = '$($script:WorkspaceId)' AND ContactId = '$subjectContactId'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access) VALUES ('field_customers_read_replay_segment_hidden', '$($script:WorkspaceId)', '$roleId', 'customers', 'segment', 'Hidden')"
    $redactedReplay = Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-0001'
    Add-Result 'createCustomer replay re-projects response under current field security' 'True' `
        ($null -eq $redactedReplay.Body.result.segment).ToString()
    Clear-CustomerFields
    Set-CustomerScope -RoleId $roleId -Scope 'Team'
    Add-Result 'createCustomer replay fails closed after TEAM scope change' '404' `
        (Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-0001').Status
    Set-CustomerScope -RoleId $roleId -Scope 'Custom'
    Add-Result 'createCustomer replay fails closed after CUSTOM scope change' '404' `
        (Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-0001').Status
    Set-CustomerScope -RoleId $roleId -Scope 'Workspace'
    $createConflict = Invoke-Customer -Method 'POST' -Path '/customers' -Body ($createBody.Replace('enterprise', 'changed')) -IdempotencyKey 'idem-customer-core-create-0001'
    Add-Result 'reused idempotency key with different intent conflicts' '409' $createConflict.Status
    $duplicateEffectsBefore = @(
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.Customers'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.AuditRecords'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.OutboxMessages'
        Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.IdempotencyRecords'
    ) -join '|'
    $duplicateSubject = Invoke-Customer -Method 'POST' -Path '/customers' -Body $createBody -IdempotencyKey 'idem-customer-core-create-distinct-key'
    Add-Result 'same subject with a distinct idempotency key is rejected' '409' $duplicateSubject.Status
    Add-Result 'duplicate subject rejection has zero aggregate/audit/outbox/idempotency effects' $duplicateEffectsBefore `
        (@(
            Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.Customers'
            Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.AuditRecords'
            Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.OutboxMessages'
            Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.IdempotencyRecords'
        ) -join '|')
    foreach ($capability in @('organizations.create', 'organizations.read')) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query "IF NOT EXISTS (SELECT 1 FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = '$capability') INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', '$capability')"
    }
    $organizationCreate = Invoke-Api -Method 'POST' -Path '/organizations' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-customer-core-organization-0001' -Body '{"displayName":"Customer Core Organization","email":"org-customer-core@example.test","phone":"0909000111"}'
    Add-Result 'B2B fixture Organization create succeeds through owner API' '200' $organizationCreate.Status
    $subjectOrganizationId = $organizationCreate.Body.result.id
    $b2bBody = @{ relationshipRef = @{ type = 'ORGANIZATION_ACCOUNT'; id = $subjectOrganizationId }; tier = 'STRATEGIC' } | ConvertTo-Json -Depth 5 -Compress
    $b2bCreate = Invoke-Customer -Method 'POST' -Path '/customers' -Body $b2bBody -IdempotencyKey 'idem-customer-core-create-b2b-0001'
    $b2bCustomerId = $b2bCreate.Body.result.id
    Add-Result 'direct B2B Organization Customer create succeeds' '201|B2B|ORGANIZATION_ACCOUNT' `
        ("{0}|{1}|{2}" -f $b2bCreate.Status, $b2bCreate.Body.result.type, $b2bCreate.Body.result.relationshipRef.type)
    $b2b360 = Invoke-Customer -Method 'GET' -Path "/customers/$b2bCustomerId/360"
    Add-Result 'direct B2B Customer360 resolves authoritative Organization identity' '200|Customer Core Organization' `
        ("{0}|{1}" -f $b2b360.Status, $b2b360.Body.identity.displayName)
    $b2bUpdate = Invoke-Customer -Method 'PATCH' -Path "/customers/$b2bCustomerId" -Body '{"segment":"enterprise","status":"ACTIVE"}' -IdempotencyKey 'idem-customer-core-b2b-update-0001' -IfMatch '"0"'
    Add-Result 'direct B2B update advances authoritative version' '200|ACTIVE|1' `
        ("{0}|{1}|{2}" -f $b2bUpdate.Status, $b2bUpdate.Body.result.status, $b2bUpdate.Body.result.version)
    $b2bArchive = Invoke-Customer -Method 'POST' -Path "/customers/$b2bCustomerId/archive" -Body '{}' -IdempotencyKey 'idem-customer-core-b2b-archive-0001' -IfMatch '"1"'
    Add-Result 'direct B2B archive retains authoritative aggregate' '200|ARCHIVED|2' `
        ("{0}|{1}|{2}" -f $b2bArchive.Status, $b2bArchive.Body.result.status, $b2bArchive.Body.result.version)
    $b2bDefaultList = Invoke-Customer -Method 'GET' -Path '/customers'
    Add-Result 'default list excludes archived B2B Customer' 'False' `
        ($b2bDefaultList.Body.items.id -contains $b2bCustomerId).ToString()
    $b2bArchivedDetail = Invoke-Customer -Method 'GET' -Path "/customers/$b2bCustomerId"
    Add-Result 'detail retains archived B2B Customer' '200|ARCHIVED|2' `
        ("{0}|{1}|{2}" -f $b2bArchivedDetail.Status, $b2bArchivedDetail.Body.status, $b2bArchivedDetail.Body.version)
    Add-Result 'B2B account subject is not manufactured as a stakeholder Contact' '0' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.CustomerRelationships WHERE CustomerId = '$b2bCustomerId'"))
    Add-Result 'B2C account subject is not manufactured as a stakeholder Contact' '0' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.CustomerRelationships WHERE CustomerId = '$createdCustomerId'"))
    $secondEmail = 'customers.read.second@example.test'
    $secondPassword = 'Customers-Second!2026'
    $secondRegistration = Invoke-Api -Method 'POST' -Path '/auth/accounts' -IdempotencyKey 'idem-customers-second-register-0001' `
        -Body (@{ email = $secondEmail; password = $secondPassword; displayName = 'Customers Second User' } | ConvertTo-Json -Compress)
    Add-Result 'separate second identity registers' '201' $secondRegistration.Status
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE iam.Accounts SET Status = 'Active', EmailVerifiedAt = SYSUTCDATETIME() WHERE NormalizedEmail = UPPER('$secondEmail')"
    $secondAccountId = Get-Scalar -Database $DatabaseName -Query "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail = UPPER('$secondEmail')"
    $secondMemberId = Get-Scalar -Database $DatabaseName -Query "SELECT MemberId FROM iam.Accounts WHERE AccountId = '$secondAccountId'"
    $secondMembershipId = 'wsm-customers-read-second-real'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, Status, CreatedAt)
VALUES ('$secondMembershipId', '$($script:WorkspaceId)', '$secondAccountId', '$secondMemberId', 'Active', SYSUTCDATETIME());
INSERT INTO access.MembershipRoleAssignments (AssignmentId, WorkspaceId, MembershipId, RoleId, AssignedAt)
VALUES ('assignment-customers-second-real', '$($script:WorkspaceId)', '$secondMembershipId', '$roleId', SYSUTCDATETIME());
"@
    $secondSignIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' -IdempotencyKey 'idem-customers-second-signin-0001' `
        -Body (@{ email = $secondEmail; password = $secondPassword } | ConvertTo-Json -Compress)
    Add-Result 'separate second identity signs in' '200' $secondSignIn.Status
    $secondToken = $secondSignIn.Body.accessToken
    Set-CustomerScope -RoleId $roleId -Scope 'Own'
    Add-Result 'OWN scope permits the Customer created by the caller' '200' `
        (Invoke-Customer -Method 'GET' -Path "/customers/$createdCustomerId").Status
    $secondOwnRead = Invoke-Api -Method 'GET' -Path "/customers/$createdCustomerId" -Token $secondToken -WorkspaceId $script:WorkspaceId
    Add-Result 'genuine second member under OWN cannot see creator-owned Customer' '404' $secondOwnRead.Status
    Set-CustomerScope -RoleId $roleId -Scope 'Workspace'
    $deniedAuditBefore = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId'"
    $deniedOutboxBefore = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$createdCustomerId'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access) VALUES ('field_customers_read_write_segment_readonly', '$($script:WorkspaceId)', '$roleId', 'customers', 'segment', 'ReadOnly')"
    $writeDenied = Invoke-Customer -Method 'PATCH' -Path "/customers/$createdCustomerId" -Body '{"segment":"denied"}' -IdempotencyKey 'idem-customer-core-update-denied' -IfMatch '"0"'
    Add-Result 'updateCustomer refuses a READ_ONLY field' '403' $writeDenied.Status
    Add-Result 'field-write denial produces no audit or outbox side effect' 'True' `
        (([int](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId'") -eq [int]$deniedAuditBefore) -and `
         ([int](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$createdCustomerId'") -eq [int]$deniedOutboxBefore)).ToString()
    Clear-CustomerFields
    $updateResult = Invoke-Customer -Method 'PATCH' -Path "/customers/$createdCustomerId" -Body '{"segment":"strategic","status":"ACTIVE"}' -IdempotencyKey 'idem-customer-core-update-0001' -IfMatch '"0"'
    Add-Result 'updateCustomer commits with matching If-Match' '200' $updateResult.Status
    Add-Result 'updateCustomer advances version and lifecycle' 'ACTIVE|1' ("{0}|{1}" -f $updateResult.Body.result.status, $updateResult.Body.result.version)
    $staleUpdateStateBefore = Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(Version, '|', Segment, '|', (SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.IdempotencyRecords WHERE IdempotencyKey = 'idem-customer-core-update-stale')) FROM customers.Customers WHERE CustomerId = '$createdCustomerId'"
    $staleUpdate = Invoke-Customer -Method 'PATCH' -Path "/customers/$createdCustomerId" -Body '{"segment":"stale"}' -IdempotencyKey 'idem-customer-core-update-stale' -IfMatch '"0"'
    Add-Result 'stale updateCustomer is rejected' '412' $staleUpdate.Status
    Add-Result 'stale updateCustomer has zero aggregate/audit/outbox/idempotency effects' ([string]$staleUpdateStateBefore) `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(Version, '|', Segment, '|', (SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.IdempotencyRecords WHERE IdempotencyKey = 'idem-customer-core-update-stale')) FROM customers.Customers WHERE CustomerId = '$createdCustomerId'"))
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access) VALUES ('field_contacts_customer360_email_hidden', '$($script:WorkspaceId)', '$roleId', 'contacts', 'workEmail', 'Hidden')"
    $projected360 = Invoke-Customer -Method 'GET' -Path "/customers/$createdCustomerId/360"
    Add-Result 'Customer360 applies Contact-owned field security to identity email' 'True' `
        (($projected360.Status -eq '200') -and ($projected360.Raw -notmatch 'customer-core@example.test|"email"')).ToString()
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId = 'field_contacts_customer360_email_hidden'"
    $staleArchiveStateBefore = Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(Version, '|', Status, '|', (SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.IdempotencyRecords WHERE IdempotencyKey = 'idem-customer-core-archive-stale')) FROM customers.Customers WHERE CustomerId = '$createdCustomerId'"
    $staleArchive = Invoke-Customer -Method 'POST' -Path "/customers/$createdCustomerId/archive" -Body '{}' -IdempotencyKey 'idem-customer-core-archive-stale' -IfMatch '"0"'
    Add-Result 'stale archiveCustomer is rejected' '412' $staleArchive.Status
    Add-Result 'stale archiveCustomer has zero aggregate/audit/outbox/idempotency effects' ([string]$staleArchiveStateBefore) `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(Version, '|', Status, '|', (SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$createdCustomerId'), '|', (SELECT COUNT(*) FROM customers.IdempotencyRecords WHERE IdempotencyKey = 'idem-customer-core-archive-stale')) FROM customers.Customers WHERE CustomerId = '$createdCustomerId'"))
    $archiveResult = Invoke-Customer -Method 'POST' -Path "/customers/$createdCustomerId/archive" -Body '{}' -IdempotencyKey 'idem-customer-core-archive-0001' -IfMatch '"1"'
    Add-Result 'archiveCustomer commits with matching If-Match' '200' $archiveResult.Status
    Add-Result 'archiveCustomer advances version and retains the record' 'ARCHIVED|2' ("{0}|{1}" -f $archiveResult.Body.result.status, $archiveResult.Body.result.version)
    $defaultListAfterArchive = Invoke-Customer -Method 'GET' -Path '/customers'
    Add-Result 'default list excludes archived Customer' 'False' ($defaultListAfterArchive.Body.items.id -contains $createdCustomerId).ToString()
    $archiveList = Invoke-Customer -Method 'GET' -Path '/customers?status=ARCHIVED'
    Add-Result 'explicit archived filter returns retained Customer' 'True' ($archiveList.Body.items.id -contains $createdCustomerId).ToString()
    $commandAuditCount = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId'"
    $commandAuditVersions = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.AuditRecords WHERE AggregateId = '$createdCustomerId' AND NewVersion IN (0, 1, 2)"
    $commandOutboxDelta = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM customers.OutboxMessages WHERE AggregateId = '$createdCustomerId'"
    Add-Result 'create/update/archive each write immutable command audit' '3' ([string]$commandAuditCount)
    Add-Result 'command audit persists each resulting aggregate version' '3' ([string]$commandAuditVersions)
    Add-Result 'commands emit create, profile, lifecycle, and archive owner events' '4' ([string]$commandOutboxDelta)

    $countBeforeMutationProbe = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM customers.Customers'
    $postProbe = Invoke-Customer -Method 'POST' -Path '/customers' -Body '{}'
    $putProbe = Invoke-Customer -Method 'PUT' -Path "/customers/$customerA" -Body '{}'
    $patchProbe = Invoke-Customer -Method 'PATCH' -Path "/customers/$customerA" -Body '{}'
    $deleteProbe = Invoke-Customer -Method 'DELETE' -Path "/customers/$customerA"
    $customer360Probe = Invoke-Customer -Method 'GET' -Path "/customers/$customerA/360"
    Add-Result 'create Customer route rejects missing command metadata before mutation' '400' $postProbe.Status
    Add-Result 'replace Customer method is not mapped' '405' $putProbe.Status
    Add-Result 'update Customer route rejects missing command metadata before mutation' '400' $patchProbe.Status
    Add-Result 'delete Customer method is not mapped' '405' $deleteProbe.Status
    Add-Result 'Customer360 refuses unresolved owner participant identity' '404' $customer360Probe.Status
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
    $customerEndpointSource = Get-Content -Raw `
        -LiteralPath (Join-Path $repositoryRoot 'src/UnicoreCRM.Crm/Customers/Contracts/CustomersEndpoints.cs')
    Add-Result 'Customer production core maps list, detail, and 360 GET operations' '3' `
        ([string]([regex]::Matches($customerEndpointSource, 'endpoints\.MapGet\(').Count))
    Add-Result 'Customer list operationId remains exact' 'True' `
        ($customerEndpointSource -cmatch '\.WithName\("listCustomers"\)').ToString()
    Add-Result 'Customer detail operationId remains exact' 'True' `
        ($customerEndpointSource -cmatch '\.WithName\("getCustomer"\)').ToString()
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

