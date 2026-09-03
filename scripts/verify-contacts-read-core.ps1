<#
.SYNOPSIS
    Reproducible Contacts Read Core security verification against an isolated database and real ApiHost.

.DESCRIPTION
    Contacts has no admitted mutation API. This harness therefore seeds owner-local read state with
    controlled SQL after applying the real Contacts migration, and exercises the public list/detail
    routes plus the canonical AccessControl evaluator. It never creates a hidden production write path.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
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
        $reader.Close()
        return $rows
    }
    finally {
        $connection.Close()
    }
}

function Invoke-SqlNonQuery {
    param([string] $Query, [string] $Database = 'master')
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString -Database $Database)
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Close()
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
    return ('req-contacts-read-{0:d6}' -f $script:RequestCounter)
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
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-contacts-read-core-0001')
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
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
    }
    finally {
        $client.Dispose()
        $request.Dispose()
    }
    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        try { $payload = $raw | ConvertFrom-Json } catch { $payload = $null }
    }
    return [pscustomobject]@{ Status = $status; Body = $payload; Raw = $raw }
}

function Invoke-Contact {
    param([string] $Method, [string] $Path, [string] $Body)
    return Invoke-Api -Method $Method -Path $Path -Body $Body -Token $script:Token -WorkspaceId $script:WorkspaceId
}

function Set-ContactScope {
    param([string] $RoleId, [string] $Scope)
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_contacts_read_core';
INSERT INTO access.RoleDataScopes (PolicyId, WorkspaceId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_contacts_read_core', '$($script:WorkspaceId)', '$RoleId', 'contacts', '$Scope', '[]');
"@
}

function Clear-ContactFields {
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_contacts_read_%'"
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
$demoEmail = 'contacts.read.provisioned@example.test'
$demoPassword = 'Contacts-Read-Core!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-contacts-read-$([Guid]::NewGuid().ToString('N')).log")
$contactA = 'contact_read_core_a'
$contactB = 'contact_read_core_b'
$contactC = 'contact_read_core_c'
$contactUnknown = 'contact_read_core_unknown'
$secretA = 'contact-a-private@example.test'
$secretB = 'CONTACT-B-HIDDEN-BUSINESS-VALUE'
$secretC = 'CONTACT-C-FOREIGN-BUSINESS-VALUE'

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
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Contacts Provisioning Fixture'
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

    $anonymousList = Invoke-Api -Method 'GET' -Path '/contacts' -WorkspaceId 'ws_unknown'
    $anonymousDetail = Invoke-Api -Method 'GET' -Path "/contacts/$contactA" -WorkspaceId 'ws_unknown'
    Add-Result 'unauthenticated list rejected' '401' $anonymousList.Status
    Add-Result 'unauthenticated detail rejected' '401' $anonymousDetail.Status

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' `
        -IdempotencyKey 'idem-contacts-read-signin-0001' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    $callerMemberId = $session.Body.principal.memberId
    $provisioning = Invoke-Api -Method 'POST' -Path '/workspaces/initial-provisioning' `
        -Token $script:Token -IdempotencyKey 'idem-contacts-read-provisioning-0001' `
        -Body '{"name":"Contacts Read Workspace"}'
    Add-Result 'initial Workspace provisioning succeeds' '201' $provisioning.Status
    $script:WorkspaceId = $provisioning.Body.workspaceId
    $foreignWorkspaceId = 'ws_contacts_read_foreign'
    $roleId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)' AND Name = 'Workspace Owner'"
    if ([string]::IsNullOrWhiteSpace($script:WorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($foreignWorkspaceId) `
        -or [string]::IsNullOrWhiteSpace($callerMemberId) `
        -or [string]::IsNullOrWhiteSpace($roleId)) {
        throw 'The Development identity/workspace/access fixture was not provisioned.'
    }
    $provisionedContactsRead = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'contacts.read'"
    Add-Result 'initial Workspace provisioning grants contacts.read' '1' ([string]$provisionedContactsRead)
    $provisionedBootstrap = Invoke-Api -Method 'GET' -Path "/workspaces/$($script:WorkspaceId)/bootstrap" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'provisioned Workspace bootstrap succeeds' '200' $provisionedBootstrap.Status
    Add-Result 'initial Workspace provisioning enables exact Contacts module set' `
        'contacts,leads,deals,tasks' `
        ((@($provisionedBootstrap.Body.configuration.enabledModuleKeys)) -join ',')

    $contactsTable = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'contacts' AND TABLE_NAME = 'Contacts'"
    Add-Result 'Contacts migration created owner table' '1' ([string]$contactsTable)
    $readAuditTable = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'contacts' AND TABLE_NAME = 'ReadAuditRecords'"
    Add-Result 'Contacts migration created read-audit table' '1' ([string]$readAuditTable)
    $indexCount = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'contacts' AND t.name = 'Contacts' AND i.name IN
('IX_Contacts_WorkspaceId_CreatedAt_ContactId','IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId')
"@
    Add-Result 'Contacts read-shape indexes applied' '2' ([string]$indexCount)

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Workspaces WHERE WorkspaceId = '$foreignWorkspaceId')
INSERT INTO workspace.Workspaces (WorkspaceId, [Key], Name, LogoText, CreatedAt)
VALUES ('$foreignWorkspaceId', 'contacts-read-foreign', 'Contacts Read Foreign Workspace', 'CF', SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM workspace.Memberships WHERE MemberId = 'mem-contacts-read-other')
INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, Status, CreatedAt)
VALUES ('wsm-contacts-read-other', '$($script:WorkspaceId)', 'acc-contacts-read-other', 'mem-contacts-read-other', 'Active', SYSUTCDATETIME());

INSERT INTO contacts.Contacts
(ContactId, WorkspaceId, OwnerId, FullName, Status, Version, CreatedAt, UpdatedAt, Profile)
VALUES
('$contactA', '$($script:WorkspaceId)', '$callerMemberId', 'Contact Alpha', 'active', 4,
 DATEADD(minute, -30, SYSUTCDATETIME()), DATEADD(minute, -5, SYSUTCDATETIME()),
 N'{"workEmail":"$secretA","mobilePhone":"0900000001","source":"verified-fixture","notes":"READ_ONLY-CONTACT-NOTE","tags":["core","visible"],"displayName":"Contact Alpha"}'),
('$contactB', '$($script:WorkspaceId)', 'mem-contacts-read-other', 'Contact Beta', 'needs_follow_up', 2,
 DATEADD(minute, -20, SYSUTCDATETIME()), DATEADD(minute, -4, SYSUTCDATETIME()),
 N'{"workEmail":"beta@example.test","mobilePhone":"$secretB","source":"verified-fixture","notes":"Other owner note"}'),
('$contactC', '$foreignWorkspaceId', 'mem-contacts-read-other', 'Contact Foreign', 'active', 1,
 DATEADD(minute, -10, SYSUTCDATETIME()), DATEADD(minute, -3, SYSUTCDATETIME()),
 N'{"workEmail":"foreign@example.test","mobilePhone":"$secretC","notes":"Foreign contact note"}');
"@

    Set-ContactScope -RoleId $roleId -Scope 'Workspace'

    $provisionedList = Invoke-Contact -Method 'GET' -Path '/contacts'
    Add-Result 'provisioned contacts.read permits the first Contacts list' '200' $provisionedList.Status
    Add-Result 'first Contacts success does not depend on a manual capability grant' '2' ([string]$provisionedList.Body.Count)

    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "DELETE FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'contacts.read'"
    Add-Result 'no Contacts read capability denies list' '403' (Invoke-Contact -Method 'GET' -Path '/contacts').Status
    Add-Result 'no Contacts read capability denies detail' '403' (Invoke-Contact -Method 'GET' -Path "/contacts/$contactA").Status
    Invoke-SqlNonQuery -Database $DatabaseName `
        -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'contacts.read')"
    Add-Result 'negative capability test restores one canonical contacts.read' '1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId = '$roleId' AND Capability = 'contacts.read'"))

    $workspaceList = Invoke-Contact -Method 'GET' -Path '/contacts'
    Add-Result 'WORKSPACE list succeeds' '200' $workspaceList.Status
    Add-Result 'list is the admitted plain array representation' '2' ([string]$workspaceList.Body.Count)
    Add-Result 'list includes trusted Contact A' 'True' ($workspaceList.Body.id -contains $contactA).ToString()
    Add-Result 'list includes trusted Contact B' 'True' ($workspaceList.Body.id -contains $contactB).ToString()
    Add-Result 'foreign Workspace Contact absent from list' 'False' ($workspaceList.Body.id -contains $contactC).ToString()
    Add-Result 'foreign business value absent from list bytes' 'True' ($workspaceList.Raw -notmatch [regex]::Escape($secretC)).ToString()
    Add-Result 'unadmitted page metadata absent' 'True' ($workspaceList.Raw -notmatch 'pageInfo|totalCount|nextCursor').ToString()

    $contactDetail = Invoke-Contact -Method 'GET' -Path "/contacts/$contactA"
    Add-Result 'own Workspace detail succeeds' '200' $contactDetail.Status
    Add-Result 'detail identity is Contact-owned ID' $contactA $contactDetail.Body.id
    Add-Result 'detail carries trusted Workspace' $script:WorkspaceId $contactDetail.Body.workspaceId
    Add-Result 'detail required fullName present' 'Contact Alpha' $contactDetail.Body.fullName
    Add-Result 'detail required version present' '4' ([string]$contactDetail.Body.version)

    $foreignDetail = Invoke-Contact -Method 'GET' -Path "/contacts/$contactC"
    $unknownDetail = Invoke-Contact -Method 'GET' -Path "/contacts/$contactUnknown"
    Add-Result 'foreign detail is not found' '404' $foreignDetail.Status
    Add-Result 'unknown detail is not found' '404' $unknownDetail.Status
    Add-Result 'foreign and unknown problem behavior match' 'True' (Same-Problem $foreignDetail $unknownDetail).ToString()
    Add-Result 'foreign detail leaks no business value' 'True' `
        (($foreignDetail.Raw -notmatch [regex]::Escape($secretC)) -and ($foreignDetail.Raw -notmatch 'Contact Foreign')).ToString()

    Set-ContactScope -RoleId $roleId -Scope 'Own'
    $ownDetail = Invoke-Contact -Method 'GET' -Path "/contacts/$contactA"
    $hiddenDetail = Invoke-Contact -Method 'GET' -Path "/contacts/$contactB"
    $ownUnknown = Invoke-Contact -Method 'GET' -Path "/contacts/$contactUnknown"
    Add-Result 'OWN detail permits caller-owned Contact' '200' $ownDetail.Status
    Add-Result 'OWN detail hides other-owner Contact' '404' $hiddenDetail.Status
    Add-Result 'scope-hidden and unknown problem behavior match' 'True' (Same-Problem $hiddenDetail $ownUnknown).ToString()
    Add-Result 'scope-hidden response leaks no business value' 'True' `
        (($hiddenDetail.Raw -notmatch [regex]::Escape($secretB)) -and ($hiddenDetail.Raw -notmatch 'Contact Beta')).ToString()
    $ownList = Invoke-Contact -Method 'GET' -Path '/contacts'
    Add-Result 'OWN list returns only caller-owned Contact' '1' ([string]$ownList.Body.Count)
    Add-Result 'OWN list excludes hidden Contact before materialization' $contactA $ownList.Body[0].id

    foreach ($unsupported in @('Team', 'Custom')) {
        Set-ContactScope -RoleId $roleId -Scope $unsupported
        Add-Result ("{0} detail fails closed" -f $unsupported.ToUpperInvariant()) '404' `
            (Invoke-Contact -Method 'GET' -Path "/contacts/$contactA").Status
        Add-Result ("{0} list fails closed" -f $unsupported.ToUpperInvariant()) '0' `
            ([string](Invoke-Contact -Method 'GET' -Path '/contacts').Body.Count)
    }

    Set-ContactScope -RoleId $roleId -Scope 'Workspace'
    Clear-ContactFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access) VALUES
('field_contacts_read_email', '$($script:WorkspaceId)', '$roleId', 'contacts', 'workEmail', 'Hidden'),
('field_contacts_read_phone', '$($script:WorkspaceId)', '$roleId', 'CONTACTS', 'mobilePhone', 'Masked'),
('field_contacts_read_notes', '$($script:WorkspaceId)', '$roleId', 'contacts', 'notes', 'ReadOnly'),
('field_contacts_read_source', '$($script:WorkspaceId)', '$roleId', 'contacts', 'source', 'ReadWrite'),
('field_contacts_read_unknown', '$($script:WorkspaceId)', '$roleId', 'contacts', 'ghostField', 'ReadWrite');
"@
    $fieldDetail = Invoke-Contact -Method 'GET' -Path "/contacts/$contactA"
    Add-Result 'optional HIDDEN field omitted' 'True' ($fieldDetail.Raw -notmatch '"workEmail"').ToString()
    Add-Result 'hidden business value absent from raw bytes' 'True' ($fieldDetail.Raw -notmatch [regex]::Escape($secretA)).ToString()
    Add-Result 'MASKED value withheld safely' 'True' ($fieldDetail.Raw -notmatch '"mobilePhone"|0900000001').ToString()
    Add-Result 'READ_ONLY field remains readable' 'True' ($fieldDetail.Raw -match 'READ_ONLY-CONTACT-NOTE').ToString()
    Add-Result 'READ_WRITE field remains readable' 'True' ($fieldDetail.Raw -match 'verified-fixture').ToString()

    $unknownField = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'contacts'; recordId = $contactA; requestedFields = @('ghostField') } | ConvertTo-Json -Compress)
    Add-Result 'unknown field evaluation succeeds safely' '200' $unknownField.Status
    Add-Result 'unknown field cannot widen read access' 'HIDDEN' $unknownField.Body.fieldAccess.ghostField
    Add-Result 'unknown field has no projected value' 'True' ($fieldDetail.Raw -notmatch 'ghostField').ToString()

    $spoofOwner = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'contacts'; recordId = $contactB; ownerId = $callerMemberId } | ConvertTo-Json -Compress)
    $spoofWorkspace = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'contacts'; recordId = $contactA; workspaceId = $foreignWorkspaceId } | ConvertTo-Json -Compress)
    $spoofTeam = Invoke-Api -Method 'POST' -Path '/access/records/evaluate' `
        -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -Body (@{ resourceKey = 'contacts'; recordId = $contactA; teamId = 'team_spoof' } | ConvertTo-Json -Compress)
    Add-Result 'caller-supplied owner fact rejected' '422' $spoofOwner.Status
    Add-Result 'caller-supplied Workspace fact rejected' '422' $spoofWorkspace.Status
    Add-Result 'caller-supplied team fact rejected' '422' $spoofTeam.Status

    Clear-ContactFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleFieldSecurity (PolicyId, WorkspaceId, RoleId, ResourceKey, FieldKey, Access)
VALUES ('field_contacts_read_required', '$($script:WorkspaceId)', '$roleId', 'contacts', 'fullName', 'Hidden');
"@
    $requiredRestricted = Invoke-Contact -Method 'GET' -Path "/contacts/$contactA"
    Add-Result 'required-field restriction fails operation closed' '403' $requiredRestricted.Status
    Add-Result 'required restricted value absent' 'True' ($requiredRestricted.Raw -notmatch 'Contact Alpha').ToString()
    Clear-ContactFields

    $wrongWorkspace = Invoke-Api -Method 'GET' -Path "/contacts/$contactA" `
        -Token $script:Token -WorkspaceId $foreignWorkspaceId
    Add-Result 'wrong Workspace header cannot become authority' '403' $wrongWorkspace.Status

    $recordDecisionsBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions"
    $authorizationsBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.AuthorizationDecisions"
    $listReadAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM contacts.ReadAuditRecords WHERE Operation = 'listContacts'"
    [void](Invoke-Contact -Method 'GET' -Path '/contacts')
    $recordDecisionsAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions"
    $authorizationsAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM access.AuthorizationDecisions"
    $listReadAuditAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM contacts.ReadAuditRecords WHERE Operation = 'listContacts'"
    Add-Result 'list performs no per-row record evaluations' '0' `
        ([string]([int]$recordDecisionsAfter - [int]$recordDecisionsBefore))
    Add-Result 'list performs exactly one resource authorization' '1' `
        ([string]([int]$authorizationsAfter - [int]$authorizationsBefore))
    Add-Result 'successful list writes one Contacts read audit' '1' `
        ([string]([int]$listReadAuditAfter - [int]$listReadAuditBefore))

    $detailReadAuditBefore = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM contacts.ReadAuditRecords WHERE Operation = 'getContact' AND ContactId = '$contactA'"
    [void](Invoke-Contact -Method 'GET' -Path "/contacts/$contactA")
    $detailReadAuditAfter = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM contacts.ReadAuditRecords WHERE Operation = 'getContact' AND ContactId = '$contactA'"
    Add-Result 'successful detail writes one Contacts read audit' '1' `
        ([string]([int]$detailReadAuditAfter - [int]$detailReadAuditBefore))
    $completeReadAudit = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM contacts.ReadAuditRecords
WHERE Operation = 'getContact' AND ContactId = '$contactA'
  AND WorkspaceId = '$($script:WorkspaceId)' AND ActorId = '$callerMemberId'
  AND ContactVersion = 4 AND RequestId <> '' AND CorrelationId <> ''
"@
    Add-Result 'Contacts read audit carries trusted actor and request evidence' 'True' `
        ([int]$completeReadAudit -gt 0).ToString()

    $foreignAuditRows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM access.RecordAccessDecisions
WHERE ResourceKey = 'contacts' AND RecordId = '$contactC'
"@
    Add-Result 'foreign Contact never enters record audit' '0' ([string]$foreignAuditRows)
    $foreignOwnerAuditRows = Get-Scalar -Database $DatabaseName -Query @"
SELECT COUNT(*) FROM contacts.ReadAuditRecords WHERE ContactId IN ('$contactB', '$contactC')
"@
    Add-Result 'denied and foreign Contacts never enter owner read audit' '0' ([string]$foreignOwnerAuditRows)

    $countBeforeMutationProbe = Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.Contacts'
    $postProbe = Invoke-Contact -Method 'POST' -Path '/contacts' -Body '{}'
    $putProbe = Invoke-Contact -Method 'PUT' -Path "/contacts/$contactA" -Body '{}'
    Add-Result 'no create Contact success path' 'False' ($postProbe.Status -ge 200 -and $postProbe.Status -lt 300).ToString()
    Add-Result 'no update Contact success path' 'False' ($putProbe.Status -ge 200 -and $putProbe.Status -lt 300).ToString()
    Add-Result 'mutation probes changed no Contact state' ([string]$countBeforeMutationProbe) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.Contacts'))

    $healthy = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    Add-Result 'ApiHost healthy after denied requests' '200' $healthy.Status

    $logText = ''
    if (Test-Path -LiteralPath $logPath) { $logText += Get-Content -Raw -LiteralPath $logPath }
    if (Test-Path -LiteralPath "$logPath.err") { $logText += Get-Content -Raw -LiteralPath "$logPath.err" }
    Add-Result 'foreign Contact value absent from host logs' 'True' ($logText -notmatch [regex]::Escape($secretC)).ToString()
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit(10000) | Out-Null
    }

    Push-Location $repositoryRoot
    try {
        & dotnet ef migrations has-pending-model-changes --project $crmProject --context ContactsDbContext --no-build
        Add-Result 'no pending Contacts EF model changes' '0' ([string]$LASTEXITCODE)
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
Write-Host ("Contacts Read Core verification: passed={0} failed={1}" -f $script:Passed, $script:Failed)
if ($script:Failed -ne 0) { throw 'Contacts Read Core verification failed.' }
Write-Host 'CONTACTS READ CORE: PASS'
