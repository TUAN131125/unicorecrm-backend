<#
.SYNOPSIS
    Reproducible Organizations Read Core security verification against an isolated database and real ApiHost.

.DESCRIPTION
    Organizations has no admitted mutation API. This harness therefore seeds owner-local read state with
    controlled SQL after applying the real Organizations migration, and exercises the public list/detail
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
    return ('req-organizations-read-{0:d6}' -f $script:RequestCounter)
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
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-organizations-read-core-0001')
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

function Invoke-Organization {
    param([string] $Method, [string] $Path, [string] $Body)
    return Invoke-Api -Method $Method -Path $Path -Body $Body -Token $script:Token -WorkspaceId $script:WorkspaceId
}

function Set-OrganizationScope {
    param([string] $RoleId, [string] $Scope)
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_organizations_read_core';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_organizations_read_core', '$RoleId', 'organizations', '$Scope', '[]');
"@
}

function Clear-OrganizationFields {
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_organizations_read_%'"
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
$demoEmail = 'organizations.read.provisioned@example.test'
$demoPassword = 'Organizations-Read-Core!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-organizations-read-$([Guid]::NewGuid().ToString('N')).log")
$organizationA = 'organization_read_core_a'
$organizationB = 'organization_read_core_b'
$organizationC = 'organization_read_core_c'
$organizationUnknown = 'organization_read_core_unknown'
$secretA = 'organization-a-private@example.test'
$secretB = 'ORGANIZATION-B-HIDDEN-BUSINESS-VALUE'
$secretC = 'ORGANIZATION-C-FOREIGN-BUSINESS-VALUE'

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
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Organizations Provisioning Fixture'
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

    $anonymousList = Invoke-Api -Method 'GET' -Path '/organizations' -WorkspaceId 'ws_unknown'
    $anonymousDetail = Invoke-Api -Method 'GET' -Path "/organizations/$organizationA" -WorkspaceId 'ws_unknown'
    Add-Result 'unauthenticated list rejected' '401' $anonymousList.Status
    Add-Result 'unauthenticated detail rejected' '401' $anonymousDetail.Status

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' `
        -IdempotencyKey 'idem-organizations-read-signin-0001' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    $callerMemberId = $session.Body.principal.memberId
    $provisioning = Invoke-Api -Method 'POST' -Path '/workspaces/initial-provisioning' `
        -Token $script:Token -IdempotencyKey 'idem-organizations-read-provisioning-0001' `
        -Body '{"name":"Organizations Read Workspace"}'
    Add-Result 'initial Workspace provisioning succeeds' '201' $provisioning.Status
    $script:WorkspaceId = $provisioning.Body.workspaceId
    $foreignWorkspaceId = 'ws_organizations_read_foreign'
    $roleId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)' AND Name = 'Workspace Owner'"
    if ([string]::IsNullOrWhiteSpace($script:WorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($foreignWorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($callerMemberId) `
        -or [string]::IsNullOrWhiteSpace($roleId)) {
        throw 'The Development identity/workspace/access fixture was not provisioned.'
    }
    $provisionedOrganizationsRead = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'organizations.read'"
    Add-Result 'initial Workspace provisioning does not invent organizations.read default authority' '0' ([string]$provisionedOrganizationsRead)
    $provisionedBootstrap = Invoke-Api -Method 'GET' -Path "/workspaces/$($script:WorkspaceId)/bootstrap" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'provisioned Workspace bootstrap succeeds' '200' $provisionedBootstrap.Status
    Add-Result 'initial Workspace provisioning preserves the exact existing module defaults' `
        'contacts,leads,deals,tasks' `
        ((@($provisionedBootstrap.Body.configuration.enabledModuleKeys)) -join ',')

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'organizations.read')"
    Add-Result 'controlled fixture grants one canonical organizations.read' '1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'organizations.read'"))

    $organizationsTable = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'organizations' AND TABLE_NAME = 'Organizations'"
    Add-Result 'Organizations migration created owner table' '1' ([string]$organizationsTable)
    $readAuditTable = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'organizations' AND TABLE_NAME = 'ReadAuditRecords'"
    Add-Result 'Organizations migration created read-audit table' '1' ([string]$readAuditTable)
    $indexCount = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'organizations' AND t.name = 'Organizations'
  AND i.name = 'IX_Organizations_WorkspaceId_CreatedAt_OrganizationId'
"@
    Add-Result 'Organizations Workspace list index applied' '1' ([string]$indexCount)

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Workspaces WHERE WorkspaceId = '$foreignWorkspaceId')
INSERT INTO workspace.Workspaces (WorkspaceId, [Key], Name, LogoText, CreatedAt)
VALUES ('$foreignWorkspaceId', 'organizations-read-foreign', 'Organizations Read Foreign Workspace', 'CF', SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM workspace.Memberships WHERE MemberId = 'mem-organizations-read-other')
INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, Status, CreatedAt)
VALUES ('wsm-organizations-read-other', '$($script:WorkspaceId)', 'acc-organizations-read-other', 'mem-organizations-read-other', 'Active', SYSUTCDATETIME());

INSERT INTO organizations.Organizations
(OrganizationId, WorkspaceId, DisplayName, Status, Version, CreatedAt, UpdatedAt, Profile)
VALUES
('$organizationA', '$($script:WorkspaceId)', 'Organization Alpha', 'active', 4,
 DATEADD(minute, -30, SYSUTCDATETIME()), DATEADD(minute, -5, SYSUTCDATETIME()),
 N'{"legalName":"Organization Alpha Legal","email":"$secretA","phone":"0900000001","source":"verified-fixture","notes":"READ_ONLY-ORGANIZATION-NOTE","ownerId":"$callerMemberId","primaryContactId":"contact_scalar_a","contactRefs":["contact_scalar_a"],"employeeCount":25,"annualRevenue":1234.56}'),
('$organizationB', '$($script:WorkspaceId)', 'Organization Beta', 'prospect', 2,
 DATEADD(minute, -20, SYSUTCDATETIME()), DATEADD(minute, -4, SYSUTCDATETIME()),
 N'{"email":"beta@example.test","phone":"$secretB","source":"verified-fixture","notes":"Other organization note","ownerId":"mem-organizations-read-other"}'),
('$organizationC', '$foreignWorkspaceId', 'Organization Foreign', 'active', 1,
 DATEADD(minute, -10, SYSUTCDATETIME()), DATEADD(minute, -3, SYSUTCDATETIME()),
 N'{"email":"foreign@example.test","phone":"$secretC","notes":"Foreign organization note","ownerId":"mem-organizations-read-other"}');
"@

    Set-OrganizationScope -RoleId $roleId -Scope 'Workspace'

    $provisionedList = Invoke-Organization -Method 'GET' -Path '/organizations'
    Add-Result 'controlled organizations.read permits the first Organizations list' '200' $provisionedList.Status
    Add-Result 'first Organizations list contains only trusted Workspace rows' '2' ([string]$provisionedList.Body.Count)

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'organizations.read'"
    Add-Result 'no Organizations read capability denies list' '403' (Invoke-Organization -Method 'GET' -Path '/organizations').Status
    Add-Result 'no Organizations read capability denies detail' '403' (Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA").Status
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'organizations.read')"
    Add-Result 'negative capability test restores one canonical organizations.read' '1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'organizations.read'"))

    $workspaceList = Invoke-Organization -Method 'GET' -Path '/organizations'
    Add-Result 'WORKSPACE list succeeds' '200' $workspaceList.Status
    Add-Result 'list is the admitted plain array representation' '2' ([string]$workspaceList.Body.Count)
    Add-Result 'list includes trusted Organization A' 'True' ($workspaceList.Body.id -contains $organizationA).ToString()
    Add-Result 'list includes trusted Organization B' 'True' ($workspaceList.Body.id -contains $organizationB).ToString()
    Add-Result 'foreign Workspace Organization absent from list' 'False' ($workspaceList.Body.id -contains $organizationC).ToString()
    Add-Result 'foreign business value absent from list bytes' 'True' ($workspaceList.Raw -notmatch [regex]::Escape($secretC)).ToString()
    Add-Result 'unadmitted page metadata absent' 'True' ($workspaceList.Raw -notmatch 'pageInfo|totalCount|nextCursor').ToString()

    $organizationDetail = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA"
    Add-Result 'own Workspace detail succeeds' '200' $organizationDetail.Status
    Add-Result 'detail identity is Organization-owned ID' $organizationA $organizationDetail.Body.id
    Add-Result 'detail carries trusted Workspace' $script:WorkspaceId $organizationDetail.Body.workspaceId
    Add-Result 'detail required displayName present' 'Organization Alpha' $organizationDetail.Body.displayName
    Add-Result 'detail required version present' '4' ([string]$organizationDetail.Body.version)

    $foreignDetail = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationC"
    $unknownDetail = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationUnknown"
    Add-Result 'foreign detail is not found' '404' $foreignDetail.Status
    Add-Result 'unknown detail is not found' '404' $unknownDetail.Status
    Add-Result 'foreign and unknown problem behavior match' 'True' (Same-Problem $foreignDetail $unknownDetail).ToString()
    Add-Result 'foreign detail leaks no business value' 'True' `
        (($foreignDetail.Raw -notmatch [regex]::Escape($secretC)) -and ($foreignDetail.Raw -notmatch 'Organization Foreign')).ToString()

    Set-OrganizationScope -RoleId $roleId -Scope 'Own'
    $ownDetail = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA"
    $hiddenDetail = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationB"
    $ownUnknown = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationUnknown"
    Add-Result 'unresolved OWN fails closed even when ownerId resembles caller' '404' $ownDetail.Status
    Add-Result 'unresolved OWN hides same-Workspace Organization' '404' $hiddenDetail.Status
    Add-Result 'scope-hidden and unknown problem behavior match' 'True' (Same-Problem $hiddenDetail $ownUnknown).ToString()
    Add-Result 'scope-hidden response leaks no business value' 'True' `
        (($hiddenDetail.Raw -notmatch [regex]::Escape($secretB)) -and ($hiddenDetail.Raw -notmatch 'Organization Beta')).ToString()
    $ownList = Invoke-Organization -Method 'GET' -Path '/organizations'
    Add-Result 'unresolved OWN list fails closed before materialization' '0' ([string]$ownList.Body.Count)

    foreach ($unsupported in @('Team', 'Custom')) {
        Set-OrganizationScope -RoleId $roleId -Scope $unsupported
        Add-Result ("{0} detail fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA").Status
        Add-Result ("{0} list fails closed" -f $unsupported.ToUpperInvariant()) '0' `
            ([string](Invoke-Organization -Method 'GET' -Path '/organizations').Body.Count)
    }

    Set-OrganizationScope -RoleId $roleId -Scope 'Workspace'
    Clear-OrganizationFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_organizations_read_email', '$roleId', 'organizations', 'email', 'Hidden'),
('field_organizations_read_phone', '$roleId', 'organizations', 'phone', 'Masked'),
('field_organizations_read_notes', '$roleId', 'organizations', 'notes', 'ReadOnly'),
('field_organizations_read_source', '$roleId', 'organizations', 'source', 'ReadWrite'),
('field_organizations_read_unknown', '$roleId', 'organizations', 'ghostField', 'ReadWrite');
"@
    $fieldDetail = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA"
    Add-Result 'optional HIDDEN field omitted' 'True' ($fieldDetail.Raw -notmatch '"email"').ToString()
    Add-Result 'hidden business value absent from raw bytes' 'True' ($fieldDetail.Raw -notmatch [regex]::Escape($secretA)).ToString()
    Add-Result 'MASKED value withheld safely' 'True' ($fieldDetail.Raw -notmatch '"phone"|0900000001').ToString()
    Add-Result 'READ_ONLY field remains readable' 'True' ($fieldDetail.Raw -match 'READ_ONLY-ORGANIZATION-NOTE').ToString()
    Add-Result 'READ_WRITE field remains readable' 'True' ($fieldDetail.Raw -match 'verified-fixture').ToString()

    $unknownField = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'organizations'; recordId = $organizationA; requestedFields = @('ghostField') } | ConvertTo-Json -Compress)
    Add-Result 'unknown field evaluation succeeds safely' '200' $unknownField.Status
    Add-Result 'unknown field cannot widen read access' 'HIDDEN' $unknownField.Body.fieldAccess.ghostField
    Add-Result 'unknown field has no projected value' 'True' ($fieldDetail.Raw -notmatch 'ghostField').ToString()

    $spoofOwner = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'organizations'; recordId = $organizationB; ownerId = $callerMemberId } | ConvertTo-Json -Compress)
    $spoofWorkspace = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'organizations'; recordId = $organizationA; workspaceId = $foreignWorkspaceId } | ConvertTo-Json -Compress)
    $spoofTeam = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'organizations'; recordId = $organizationA; teamId = 'team_spoof' } | ConvertTo-Json -Compress)
    Add-Result 'caller-supplied owner fact rejected' '422' $spoofOwner.Status
    Add-Result 'caller-supplied Workspace fact rejected' '422' $spoofWorkspace.Status
    Add-Result 'caller-supplied team fact rejected' '422' $spoofTeam.Status

    Clear-OrganizationFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_organizations_read_required', '$roleId', 'organizations', 'displayName', 'Hidden');
"@
    $requiredRestricted = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA"
    Add-Result 'required-field restriction fails operation closed' '403' $requiredRestricted.Status
    Add-Result 'required restricted value absent' 'True' ($requiredRestricted.Raw -notmatch 'Organization Alpha').ToString()
    Clear-OrganizationFields

    $wrongWorkspace = Invoke-Api -Method 'GET' -Path "/organizations/$organizationA" `
        -Token $script:Token -WorkspaceId $foreignWorkspaceId
    Add-Result 'wrong Workspace header cannot become authority' '403' $wrongWorkspace.Status

    $recordDecisionsBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions"
    $authorizationsBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.AuthorizationDecisions"
    $listReadAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM organizations.ReadAuditRecords WHERE Operation = 'listOrganizations'"
    [void](Invoke-Organization -Method 'GET' -Path '/organizations')
    $recordDecisionsAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions"
    $authorizationsAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.AuthorizationDecisions"
    $listReadAuditAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM organizations.ReadAuditRecords WHERE Operation = 'listOrganizations'"
    Add-Result 'list performs no per-row record evaluations' '0' `
        ([string]([int]$recordDecisionsAfter - [int]$recordDecisionsBefore))
    Add-Result 'list performs exactly one resource authorization' '1' `
        ([string]([int]$authorizationsAfter - [int]$authorizationsBefore))
    Add-Result 'successful list writes one Organizations read audit' '1' `
        ([string]([int]$listReadAuditAfter - [int]$listReadAuditBefore))

    $detailReadAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM organizations.ReadAuditRecords WHERE Operation = 'getOrganization' AND WorkspaceId = '$($script:WorkspaceId)' AND OrganizationId = '$organizationA'"
    [void](Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA")
    $detailReadAuditAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM organizations.ReadAuditRecords WHERE Operation = 'getOrganization' AND WorkspaceId = '$($script:WorkspaceId)' AND OrganizationId = '$organizationA'"
    Add-Result 'successful detail writes one Organizations read audit' '1' `
        ([string]([int]$detailReadAuditAfter - [int]$detailReadAuditBefore))
    $completeReadAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM organizations.ReadAuditRecords
WHERE Operation = 'getOrganization' AND WorkspaceId = '$($script:WorkspaceId)'
  AND OrganizationId = '$organizationA' AND ActorId = '$callerMemberId'
  AND OrganizationVersion = 4 AND RequestId <> '' AND CorrelationId <> ''
"@
    Add-Result 'Organizations read audit carries trusted actor and request evidence' 'True' `
        ([int]$completeReadAudit -gt 0).ToString()

    $foreignAuditRows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM access.RecordAccessDecisions
WHERE ResourceKey = 'organizations' AND RecordId = '$organizationC'
"@
    Add-Result 'foreign Organization never enters record audit' '0' ([string]$foreignAuditRows)
    $foreignOwnerAuditRows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM organizations.ReadAuditRecords
WHERE WorkspaceId = '$($script:WorkspaceId)' AND OrganizationId IN ('$organizationB', '$organizationC')
"@
    Add-Result 'denied and foreign Organizations never enter owner read audit' '0' ([string]$foreignOwnerAuditRows)

    $countBeforeMutationProbe = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM organizations.Organizations'
    $postProbe = Invoke-Organization -Method 'POST' -Path '/organizations' -Body '{}'
    $putProbe = Invoke-Organization -Method 'PUT' -Path "/organizations/$organizationA" -Body '{}'
    $patchProbe = Invoke-Organization -Method 'PATCH' -Path "/organizations/$organizationA" -Body '{}'
    $deleteProbe = Invoke-Organization -Method 'DELETE' -Path "/organizations/$organizationA"
    $linkProbe = Invoke-Organization -Method 'PUT' -Path "/organizations/$organizationA/contacts/contact_scalar_a" -Body '{}'
    $overviewProbe = Invoke-Organization -Method 'GET' -Path "/organizations/$organizationA/overview"
    Add-Result 'create Organization method is not mapped' '405' $postProbe.Status
    Add-Result 'replace Organization method is not mapped' '405' $putProbe.Status
    Add-Result 'update Organization method is not mapped' '405' $patchProbe.Status
    Add-Result 'delete Organization method is not mapped' '405' $deleteProbe.Status
    Add-Result 'link Contact to Organization route is absent' '404' $linkProbe.Status
    Add-Result 'composed Organization overview route is absent from read core' '404' $overviewProbe.Status
    Add-Result 'mutation probes changed no Organization state' ([string]$countBeforeMutationProbe) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM organizations.Organizations'))

    $healthy = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    Add-Result 'ApiHost healthy after denied requests' '200' $healthy.Status

    $logText = ''
    if (Test-Path -LiteralPath $logPath) { $logText += Get-Content -Raw -LiteralPath $logPath }
    if (Test-Path -LiteralPath "$logPath.err") { $logText += Get-Content -Raw -LiteralPath "$logPath.err" }
    Add-Result 'foreign Organization value absent from host logs' 'True' ($logText -notmatch [regex]::Escape($secretC)).ToString()
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit(10000) | Out-Null
    }

    Push-Location $repositoryRoot
    try {
        & dotnet ef migrations has-pending-model-changes --project $crmProject --context OrganizationsDbContext --no-build
        Add-Result 'no pending Organizations EF model changes' '0' ([string]$LASTEXITCODE)
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
Write-Host ("Organizations Read Core verification: passed={0} failed={1}" -f $script:Passed, $script:Failed)
if ($script:Failed -ne 0) { throw 'Organizations Read Core verification failed.' }
Write-Host 'ORGANIZATIONS READ CORE: PASS'

