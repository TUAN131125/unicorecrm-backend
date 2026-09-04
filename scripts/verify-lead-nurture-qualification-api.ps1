<#
.SYNOPSIS
    Public-API verification of POST /workflows/lead-qualification/{leadId}/nurture.

.DESCRIPTION
    Drives the admitted NURTURE qualification operation over HTTP against a real ApiHost and an
    isolated database. Leads have no admitted VERIFYING seeding path other than the real create and
    advance operations, so the fixtures are built through the public Leads API; Contact fixtures are
    seeded with controlled SQL because Contacts still has no admitted mutation API.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5347,

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
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                # An expression column such as COUNT(*) has no name, and Windows PowerShell refuses
                # to build a PSCustomObject from a hashtable carrying an empty key.
                $columnName = $reader.GetName($i)
                if ([string]::IsNullOrWhiteSpace($columnName)) { $columnName = "Column$i" }
                $row[$columnName] = if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) }
            }
            [void]$rows.Add([pscustomobject]$row)
        }
        $reader.Close()
        return $rows
    }
    finally { $connection.Dispose() }
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
    finally { $connection.Dispose() }
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
    return ('req-nurture-api-{0:d6}' -f $script:RequestCounter)
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
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-nurture-api-000001')
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

function Invoke-Nurture {
    param(
        [string] $LeadId,
        [string] $Body,
        [string] $IdempotencyKey,
        [long] $ExpectedVersion = -1,
        [string] $WorkspaceId
    )
    if ([string]::IsNullOrWhiteSpace($WorkspaceId)) { $WorkspaceId = $script:WorkspaceId }
    if ($ExpectedVersion -lt 0) {
        # A Lead created and advanced through the public API is not at version 0. The caller states a
        # version only when the test is specifically about a stale one.
        $current = Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$LeadId'"
        $ExpectedVersion = if ($null -eq $current) { 0 } else { [long]$current }
    }
    return Invoke-Api -Method 'POST' -Path "/workflows/lead-qualification/$LeadId/nurture" `
        -Token $script:Token -WorkspaceId $WorkspaceId `
        -IdempotencyKey $IdempotencyKey -IfMatch ('"{0}"' -f $ExpectedVersion) -Body $Body
}

# The declared field pointers on a problem response, as a stable sorted list. Reading them through
# one helper keeps every assertion safe when fieldErrors is absent entirely, which is itself a
# meaningful outcome for the non-disclosing refusals.
function Get-FieldErrorKeys {
    param($Response)
    $errors = $Response.Body.fieldErrors
    if ($null -eq $errors) { return '' }
    return (($errors.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object -Culture 'en-US') -join ',')
}

function New-NurtureBody {
    param(
        [string] $Mode = 'NEW',
        [string] $SelectedId,
        [string] $DisplayName = 'Nurture Person',
        [string] $Email,
        [string] $Phone = '0900000001',
        [string] $Title = 'Manager',
        [string] $Reason = 'Revisit after budget cycle',
        [string] $OwnerId
    )
    $contact = @{ displayName = $DisplayName }
    if ($Email) { $contact.email = $Email }
    if ($Phone) { $contact.phone = $Phone }
    if ($Title) { $contact.title = $Title }
    $relationship = @{ kind = 'CONTACT'; mode = $Mode; contact = $contact }
    if ($SelectedId) { $relationship.selectedId = $SelectedId }
    $body = @{
        relationship = $relationship
        revisitAt    = '2026-10-01T09:00:00.0000000Z'
        reason       = $Reason
    }
    if ($OwnerId) { $body.ownerId = $OwnerId }
    return ($body | ConvertTo-Json -Depth 6 -Compress)
}

# Creates a Lead through the public API and advances it to VERIFYING, optionally with restrictions.
function New-VerifyingLead {
    param(
        [string] $DisplayName,
        [string] $Email,
        [switch] $DoNotCall,
        [switch] $DoNotEmail,
        [switch] $StayNew
    )
    $profile = @{
        displayName    = $DisplayName
        phone          = '0911000111'
        email          = $Email
        source         = 'verifier'
        ownerId        = $script:MemberId
        estimatedValue = @{ amount = '0'; currency = 'VND' }
    }
    if ($DoNotCall) { $profile.doNotCall = $true }
    if ($DoNotEmail) { $profile.doNotEmail = $true }

    $created = Invoke-Api -Method 'POST' -Path '/leads' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey ('idem-lead-' + [Guid]::NewGuid().ToString('N').Substring(0, 16)) `
        -Body ($profile | ConvertTo-Json -Depth 6 -Compress)
    if ($created.Status -ne 201) { throw "Lead creation failed with $($created.Status): $($created.Raw)" }
    $leadId = $created.Body.result.id

    if ($StayNew) { return $leadId }

    foreach ($target in @('CONTACTING', 'VERIFYING')) {
        $advanced = Invoke-Api -Method 'POST' -Path "/leads/$leadId/advance-work-state" `
            -Token $script:Token -WorkspaceId $script:WorkspaceId `
            -IdempotencyKey ('idem-adv-' + [Guid]::NewGuid().ToString('N').Substring(0, 16)) `
            -IfMatch ('"{0}"' -f (Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadId'")) `
            -Body (@{ targetWorkState = $target } | ConvertTo-Json -Compress)
        if ($advanced.Status -ne 200) { throw "Lead advance to $target failed with $($advanced.Status): $($advanced.Raw)" }
    }
    return $leadId
}

function New-SeededContact {
    param([string] $FullName, [string] $Email, [string] $OwnerId, [string] $WorkspaceId)
    if ([string]::IsNullOrWhiteSpace($WorkspaceId)) { $WorkspaceId = $script:WorkspaceId }
    $contactId = 'contact_seed_' + [Guid]::NewGuid().ToString('N')
    $profile = '{"workEmail":"' + $Email + '","displayName":"' + $FullName + '"}'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO contacts.Contacts
    (ContactId, WorkspaceId, OwnerId, FullName, [Status], [Version], CreatedAt, UpdatedAt, Profile,
     NormalizedWorkEmail, NormalizedPersonalEmail)
VALUES
    (N'$contactId', N'$WorkspaceId', N'$OwnerId', N'$FullName', N'active', 0,
     SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'$profile', N'$($Email.ToUpperInvariant())', NULL);
"@
    return $contactId
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostProject = Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
$demoEmail = 'nurture.qualification@example.test'
$demoPassword = 'Nurture-Qualification!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-nurture-api-$([Guid]::NewGuid().ToString('N')).log")

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
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Nurture Qualification Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'

    Push-Location $repositoryRoot
    try { & dotnet build $hostProject -v q --nologo | Out-Null } finally { Pop-Location }

    $hostProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--no-build', '--no-launch-profile', '--project', $hostProject) `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

    $ready = $false
    for ($attempt = 0; $attempt -lt $ReadyTimeoutSeconds; $attempt++) {
        Start-Sleep -Seconds 1
        if ($hostProcess.HasExited) { throw "ApiHost exited with code $($hostProcess.ExitCode). See $logPath" }
        try {
            $probe = Invoke-Api -Method 'GET' -Path '/auth/session'
            if ($probe.Status -gt 0) { $ready = $true; break }
        }
        catch { }
    }
    if (-not $ready) { throw "ApiHost did not become ready within $ReadyTimeoutSeconds seconds. See $logPath" }

    # ---------------------------------------------------------------- session and workspace

    $unauthenticated = Invoke-Nurture -LeadId 'lead_x' -Body (New-NurtureBody -Email 'x@example.test') -IdempotencyKey 'idem-nurture-unauth-01' -ExpectedVersion 0 -WorkspaceId 'ws_unknown'
    Add-Result 'unauthenticated qualification rejected' '401' $unauthenticated.Status

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' -IdempotencyKey 'idem-nurture-signin-0001' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken

    $session = Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token
    $script:MemberId = $session.Body.principal.memberId
    $provisioning = Invoke-Api -Method 'POST' -Path '/workspaces/initial-provisioning' `
        -Token $script:Token -IdempotencyKey 'idem-nurture-provisioning-0001' `
        -Body '{"name":"Nurture Qualification Workspace"}'
    if ($provisioning.Status -ne 201) { throw "Provisioning failed with $($provisioning.Status): $($provisioning.Raw)" }
    $script:WorkspaceId = $provisioning.Body.workspaceId
    $roleId = Get-Scalar -Database $DatabaseName `
        -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)' AND Name = 'Workspace Owner'"
    if ([string]::IsNullOrWhiteSpace($script:WorkspaceId) -or [string]::IsNullOrWhiteSpace($roleId)) {
        throw 'The Development identity/workspace/access fixture was not provisioned.'
    }

    $anchorTable = Get-Scalar -Database $DatabaseName `
        -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='workflow' AND TABLE_NAME='LeadQualificationAnchors'"
    Add-Result 'workflow anchor table applied' '1' ([string]$anchorTable)

    # ---------------------------------------------------------------- NEW contact success

    $leadNew = New-VerifyingLead -DisplayName 'API Lead One' -Email 'api.lead.one@example.test'
    # Captured so the replay below re-sends the original If-Match verbatim, as a real client would.
    $leadNewVersion = [long](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadNew'")
    $newResult = Invoke-Nurture -LeadId $leadNew -IdempotencyKey 'idem-nurture-new-000001' -ExpectedVersion $leadNewVersion `
        -Body (New-NurtureBody -DisplayName 'API Person One' -Email 'api.person.one@example.test')
    Add-Result 'NEW qualification succeeds' '200' $newResult.Status
    Add-Result 'NEW outcome is COMMITTED' 'COMMITTED' $newResult.Body.outcome
    Add-Result 'NEW aggregate is the Lead' $leadNew $newResult.Body.aggregateId
    Add-Result 'NEW aggregateType is Lead' 'Lead' $newResult.Body.aggregateType
    Add-Result 'NEW result outcome is NURTURE' 'NURTURE' $newResult.Body.result.qualificationOutcome
    Add-Result 'NEW relationshipRef type is CONTACT' 'CONTACT' $newResult.Body.result.relationship.relationshipRef.type
    Add-Result 'NEW relationship displayName present' 'API Person One' $newResult.Body.result.relationship.displayName
    $newContactId = $newResult.Body.result.contactId
    $newTaskId = $newResult.Body.result.taskId
    Add-Result 'NEW returns a contactId' 'True' ([string](-not [string]::IsNullOrWhiteSpace($newContactId)))
    Add-Result 'NEW returns a taskId' 'True' ([string](-not [string]::IsNullOrWhiteSpace($newTaskId)))
    Add-Result 'NEW reports both created resources' 'CONTACT,TASK' `
        ((@($newResult.Body.result.createdResources | ForEach-Object { $_.resourceType })) -join ',')
    Add-Result 'NEW commandId present' 'True' ([string](-not [string]::IsNullOrWhiteSpace($newResult.Body.commandId)))
    Add-Result 'NEW emitted a Lead event' 'True' ([string](@($newResult.Body.emittedEventIds).Count -ge 1))
    Add-Result 'exactly one Contact created' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE ContactId='$newContactId'"))
    Add-Result 'exactly one NURTURE Task created' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadNew'"))
    Add-Result 'Lead terminal state is CLOSED' '3' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT WorkState FROM leads.Leads WHERE LeadId='$leadNew'"))
    Add-Result 'Lead advanced exactly once by qualification' '3' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadNew'"))
    Add-Result 'Lead relationship is the Contact' $newContactId ([string](Get-Scalar -Database $DatabaseName -Query "SELECT RelationshipId FROM leads.Leads WHERE LeadId='$leadNew'"))

    $detail = Invoke-Api -Method 'GET' -Path "/leads/$leadNew" -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'qualified Lead reads back CLOSED' 'CLOSED' $detail.Body.leadWorkState
    Add-Result 'qualified Lead reads back NURTURE' 'NURTURE' $detail.Body.qualificationOutcome
    Add-Result 'qualified Lead exposes relationshipRef' $newContactId $detail.Body.relationshipRef.id
    Add-Result 'qualified Lead exposes no contactId field' 'True' ([string]($detail.Raw -notmatch '"contactId"'))
    Add-Result 'qualified Lead exposes no qualifiedAt field' 'True' ([string]($detail.Raw -notmatch '"qualifiedAt"'))
    Add-Result 'qualified Lead exposes no qualifiedBy field' 'True' ([string]($detail.Raw -notmatch '"qualifiedBy"'))

    # ---------------------------------------------------------------- consent transfer

    $leadRestricted = New-VerifyingLead -DisplayName 'API Restricted Lead' -Email 'api.restricted@example.test' -DoNotCall -DoNotEmail
    $restricted = Invoke-Nurture -LeadId $leadRestricted -IdempotencyKey 'idem-nurture-consent-0001' `
        -Body (New-NurtureBody -DisplayName 'API Restricted Person' -Email 'api.restricted.person@example.test')
    Add-Result 'restricted Lead qualifies' '200' $restricted.Status
    $restrictedContact = $restricted.Body.result.contactId
    Add-Result 'doNotCall restriction transfers' 'true' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT JSON_VALUE(Profile,'`$.doNotCall') FROM contacts.Contacts WHERE ContactId='$restrictedContact'"))
    Add-Result 'doNotEmail restriction transfers' 'true' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT JSON_VALUE(Profile,'`$.doNotEmail') FROM contacts.Contacts WHERE ContactId='$restrictedContact'"))
    Add-Result 'no consent ledger is fabricated' '' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT ISNULL(JSON_VALUE(Profile,'`$.consent'),'') FROM contacts.Contacts WHERE ContactId='$restrictedContact'"))
    Add-Result 'no doNotSms is invented' '' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT ISNULL(JSON_VALUE(Profile,'`$.doNotSms'),'') FROM contacts.Contacts WHERE ContactId='$restrictedContact'"))
    Add-Result 'no doNotZalo is invented' '' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT ISNULL(JSON_VALUE(Profile,'`$.doNotZalo'),'') FROM contacts.Contacts WHERE ContactId='$restrictedContact'"))
    Add-Result 'no preferredContactChannel is invented' '' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT ISNULL(JSON_VALUE(Profile,'`$.preferredContactChannel'),'') FROM contacts.Contacts WHERE ContactId='$restrictedContact'"))

    # ---------------------------------------------------------------- reason preservation

    # The adopted contract admits a reason of up to 1000 characters while createTask.title stops at
    # 300, so the title can only ever be a bounded derived summary. The complete reason is an
    # admitted caller fact and must survive on the already-admitted sourceRef.evidence, whose bound
    # is exactly the reason's own. The persisted Task row is read; a 200 alone proves nothing.
    $reasonText = -join (0..999 | ForEach-Object { [char]([int][char]'a' + ($_ % 26)) })
    $leadReason = New-VerifyingLead -DisplayName 'API Reason Lead' -Email 'api.reason.lead@example.test'
    $reasonResult = Invoke-Nurture -LeadId $leadReason -IdempotencyKey 'idem-nurture-reason-0001' `
        -Body (New-NurtureBody -DisplayName 'API Reason Person' -Email 'api.reason.person@example.test' -Reason $reasonText)
    Add-Result 'a 1000-character reason qualifies' '200' $reasonResult.Status
    $reasonTaskId = $reasonResult.Body.result.taskId
    $storedReason = [string](Get-Scalar -Database $DatabaseName -Query "SELECT SourceEvidence FROM tasks.Tasks WHERE TaskId='$reasonTaskId'")
    Add-Result 'the accepted reason is persisted in full' $reasonText $storedReason
    Add-Result 'no accepted reason content is discarded' '1000' ([string]$storedReason.Length)
    Add-Result 'the Task title is the bounded derived summary' ($reasonText.Substring(0, 300)) `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT Title FROM tasks.Tasks WHERE TaskId='$reasonTaskId'"))
    Add-Result 'the reason does not displace the source Lead reference' $leadReason `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT SourceId FROM tasks.Tasks WHERE TaskId='$reasonTaskId'"))
    $reasonReplay = Invoke-Nurture -LeadId $leadReason -IdempotencyKey 'idem-nurture-reason-0001' `
        -ExpectedVersion ([long](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadReason'")) `
        -Body (New-NurtureBody -DisplayName 'API Reason Person' -Email 'api.reason.person@example.test' -Reason $reasonText)
    Add-Result 'the reason replay returns the original Task' $reasonTaskId $reasonReplay.Body.result.taskId
    Add-Result 'the reason replay creates no second Task' '1' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadReason'"))
    Add-Result 'the reason replay leaves the stored reason unchanged' $reasonText `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT SourceEvidence FROM tasks.Tasks WHERE TaskId='$reasonTaskId'"))

    # An unrestricted Lead must not produce an affirmative "false" permission on the Contact.
    Add-Result 'absent restriction stays unset, not false' '' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT ISNULL(JSON_VALUE(Profile,'`$.doNotEmail'),'') FROM contacts.Contacts WHERE ContactId='$newContactId'"))
    Add-Result 'absent call restriction stays unset, not false' '' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT ISNULL(JSON_VALUE(Profile,'`$.doNotCall'),'') FROM contacts.Contacts WHERE ContactId='$newContactId'"))

    # ---------------------------------------------------------------- EXISTING contact

    $existingContact = New-SeededContact -FullName 'API Existing Person' -Email 'api.existing@example.test' -OwnerId $script:MemberId
    $beforeExisting = Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(FullName,'|',[Version],'|',ISNULL(Profile,'')) FROM contacts.Contacts WHERE ContactId='$existingContact'"
    $leadExisting = New-VerifyingLead -DisplayName 'API Lead Two' -Email 'api.lead.two@example.test' -DoNotCall
    $existingResult = Invoke-Nurture -LeadId $leadExisting -IdempotencyKey 'idem-nurture-existing-001' `
        -Body (New-NurtureBody -Mode 'EXISTING' -SelectedId $existingContact -DisplayName 'Ignored Name' -Email 'ignored@example.test')
    Add-Result 'EXISTING qualification succeeds' '200' $existingResult.Status
    Add-Result 'EXISTING links the selected Contact' $existingContact $existingResult.Body.result.contactId
    Add-Result 'EXISTING reports only the Task as created' 'TASK' `
        ((@($existingResult.Body.result.createdResources | ForEach-Object { $_.resourceType })) -join ',')
    Add-Result 'EXISTING Contact byte-identical afterwards' $beforeExisting `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT CONCAT(FullName,'|',[Version],'|',ISNULL(Profile,'')) FROM contacts.Contacts WHERE ContactId='$existingContact'"))
    Add-Result 'EXISTING still creates one Task' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadExisting'"))
    Add-Result 'EXISTING closes the Lead' '3' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT WorkState FROM leads.Leads WHERE LeadId='$leadExisting'"))

    # ---------------------------------------------------------------- replay and idempotency

    $replay = Invoke-Nurture -LeadId $leadNew -IdempotencyKey 'idem-nurture-new-000001' -ExpectedVersion $leadNewVersion `
        -Body (New-NurtureBody -DisplayName 'API Person One' -Email 'api.person.one@example.test')
    Add-Result 'replay returns 200' '200' $replay.Status
    Add-Result 'replay outcome is REPLAYED' 'REPLAYED' $replay.Body.outcome
    Add-Result 'replay returns the same Contact' $newContactId $replay.Body.result.contactId
    Add-Result 'replay returns the same Task' $newTaskId $replay.Body.result.taskId
    Add-Result 'replay creates no second Task' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadNew'"))
    Add-Result 'replay does not advance the Lead' '3' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadNew'"))

    $changedIntent = Invoke-Nurture -LeadId $leadNew -IdempotencyKey 'idem-nurture-new-000001' -ExpectedVersion $leadNewVersion `
        -Body (New-NurtureBody -DisplayName 'API Person One' -Email 'api.person.one@example.test' -Reason 'A materially different reason')
    Add-Result 'changed intent conflicts' '409' $changedIntent.Status
    Add-Result 'changed intent reports idempotency reuse' 'IDEMPOTENCY_KEY_REUSED' $changedIntent.Body.code

    # Task 8B: the optional Task owner is immutable caller intent; If-Match is not.
    $ownerIntent = (New-NurtureBody -DisplayName 'API Person One' -Email 'api.person.one@example.test') | ConvertFrom-Json
    $ownerIntent | Add-Member -NotePropertyName ownerId -NotePropertyValue $script:MemberId
    $ownerConflict = Invoke-Nurture -LeadId $leadNew -IdempotencyKey 'idem-nurture-new-000001' -ExpectedVersion $leadNewVersion `
        -Body ($ownerIntent | ConvertTo-Json -Depth 8 -Compress)
    Add-Result '8B: changing only Task owner intent conflicts' '409|IDEMPOTENCY_KEY_REUSED' "$($ownerConflict.Status)|$($ownerConflict.Body.code)"
    $currentVersionReplay = Invoke-Nurture -LeadId $leadNew -IdempotencyKey 'idem-nurture-new-000001' -ExpectedVersion ($leadNewVersion + 1) `
        -Body (New-NurtureBody -DisplayName 'API Person One' -Email 'api.person.one@example.test')
    Add-Result '8B: completed replay accepts refreshed concurrency token' '200|REPLAYED' "$($currentVersionReplay.Status)|$($currentVersionReplay.Body.outcome)"

    # Task 8A: current authorization must precede all anchor disclosure. Use the original
    # If-Match throughout: a completed replay is authorized without revalidating CLOSED state.
    $originalReplayBody = New-NurtureBody -DisplayName 'API Person One' -Email 'api.person.one@example.test'
    $conflictingReplayBody = New-NurtureBody -DisplayName 'API Person One' -Email 'api.person.one@example.test' -Reason 'A materially different reason'
    $replayCases = @(
        @{ Name = 'absent anchor'; Key = 'idem-nurture-8a-absent'; Body = $originalReplayBody },
        @{ Name = 'completed anchor'; Key = 'idem-nurture-new-000001'; Body = $originalReplayBody },
        @{ Name = 'conflicting anchor'; Key = 'idem-nurture-new-000001'; Body = $conflictingReplayBody }
    )
    $effectsQuery = @"
SELECT (SELECT * FROM leads.Leads WHERE LeadId='$leadNew' FOR JSON PATH) AS LeadState,
       (SELECT * FROM workflow.LeadQualificationAnchors WHERE LeadId='$leadNew' FOR JSON PATH) AS Anchors,
       (SELECT COUNT(*) FROM contacts.Contacts) AS Contacts,
       (SELECT COUNT(*) FROM contacts.AuditRecords) AS ContactAudits,
       (SELECT COUNT(*) FROM contacts.OutboxMessages) AS ContactOutbox,
       (SELECT COUNT(*) FROM tasks.Tasks) AS Tasks,
       (SELECT COUNT(*) FROM tasks.AuditRecords) AS TaskAudits,
       (SELECT COUNT(*) FROM tasks.OutboxMessages) AS TaskOutbox,
       (SELECT COUNT(*) FROM leads.AuditRecords) AS LeadAudits,
       (SELECT COUNT(*) FROM leads.OutboxMessages) AS LeadOutbox
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
"@
    $effectsBefore = Get-Scalar -Database $DatabaseName -Query $effectsQuery
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.qualify'"
    try {
        $deniedWithoutCapability = Invoke-Nurture -LeadId 'lead_8a_unknown' -IdempotencyKey 'idem-nurture-8a-unknown' -ExpectedVersion 0 -Body $originalReplayBody
        Add-Result '8A: revoked qualify on unknown Lead is denied' '403' $deniedWithoutCapability.Status
        Add-Result '8A: revoked qualify uses canonical denial' 'ACCESS_DENIED' $deniedWithoutCapability.Body.code
        foreach ($case in $replayCases) {
            $denied = Invoke-Nurture -LeadId $leadNew -IdempotencyKey $case.Key -ExpectedVersion $leadNewVersion -Body $case.Body
            Add-Result "8A: revoked qualify denies $($case.Name)" '403' $denied.Status
            Add-Result "8A: revoked qualify hides $($case.Name) byte-for-byte" 'True' ([string]::Equals($deniedWithoutCapability.Raw, $denied.Raw, [StringComparison]::Ordinal).ToString())
            Add-Result "8A: revoked qualify discloses no workflow result for $($case.Name)" 'True' ([string]($denied.Raw -notmatch 'contactId|taskId|REPLAYED|IDEMPOTENCY_KEY_REUSED|createdResources|currentVersion|expectedVersion'))
        }
    }
    finally {
        Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId','leads.qualify')"
    }
    Add-Result '8A: capability denials leave all owner effects unchanged' $effectsBefore (Get-Scalar -Database $DatabaseName -Query $effectsQuery)

    # OWN follows the current authoritative owner; TEAM/CUSTOM retain canonical fail-closed behavior.
    foreach ($scope in @('Own', 'Team', 'Custom')) {
        Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO access.RoleDataScopes (PolicyId, WorkspaceId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_nurture_8a', '$($script:WorkspaceId)', '$roleId', 'leads', '$scope', '[]');
"@
        try {
            if ($scope -eq 'Own') {
                $ownReplay = Invoke-Nurture -LeadId $leadNew -IdempotencyKey 'idem-nurture-new-000001' -ExpectedVersion $leadNewVersion -Body $originalReplayBody
                Add-Result '8A: current OWN access can replay CLOSED Lead' '200|REPLAYED' "$($ownReplay.Status)|$($ownReplay.Body.outcome)"
                Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE leads.Leads SET Profile=JSON_MODIFY(Profile, '$.ownerId', 'member_8a_other'), ScopeOwnerId='member_8a_other' WHERE LeadId='$leadNew'"
            }
            $scopedEffectsBefore = Get-Scalar -Database $DatabaseName -Query $effectsQuery
            $scopeUnknown = Invoke-Nurture -LeadId 'lead_8a_unknown' -IdempotencyKey 'idem-nurture-8a-unknown' -ExpectedVersion 0 -Body $originalReplayBody
            Add-Result "8A: $scope unknown Lead uses canonical not-found" '404|RESOURCE_NOT_FOUND' "$($scopeUnknown.Status)|$($scopeUnknown.Body.code)"
            foreach ($case in $replayCases) {
                $denied = Invoke-Nurture -LeadId $leadNew -IdempotencyKey $case.Key -ExpectedVersion $leadNewVersion -Body $case.Body
                Add-Result "8A: $scope denies $($case.Name)" '404' $denied.Status
                Add-Result "8A: $scope hides $($case.Name) byte-for-byte" 'True' ([string]::Equals($scopeUnknown.Raw, $denied.Raw, [StringComparison]::Ordinal).ToString())
            }
            Add-Result "8A: $scope denials leave all owner effects unchanged" $scopedEffectsBefore (Get-Scalar -Database $DatabaseName -Query $effectsQuery)
        }
        finally {
            Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_nurture_8a';
UPDATE leads.Leads SET Profile=JSON_MODIFY(Profile, '$.ownerId', '$($script:MemberId)'), ScopeOwnerId='$($script:MemberId)' WHERE LeadId='$leadNew';
"@
        }
    }
    $restoredReplay = Invoke-Nurture -LeadId $leadNew -IdempotencyKey 'idem-nurture-new-000001' -ExpectedVersion $leadNewVersion -Body $originalReplayBody
    Add-Result '8A: restored authorization replays stored CLOSED result' '200|REPLAYED' "$($restoredReplay.Status)|$($restoredReplay.Body.outcome)"
    Add-Result '8A: restored authorization returns the original Contact and Task' "$newContactId|$newTaskId" "$($restoredReplay.Body.result.contactId)|$($restoredReplay.Body.result.taskId)"

    $missingKey = Invoke-Api -Method 'POST' -Path "/workflows/lead-qualification/$leadNew/nurture" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId -IfMatch '"0"' -Body (New-NurtureBody -Email 'x@example.test')
    Add-Result 'missing Idempotency-Key is refused' '422' $missingKey.Status
    Add-Result 'missing Idempotency-Key names the header' 'True' ([string]($missingKey.Raw -match 'Idempotency-Key'))

    # ---------------------------------------------------------------- lifecycle and concurrency

    $leadStale = New-VerifyingLead -DisplayName 'API Stale Lead' -Email 'api.stale@example.test'
    $stale = Invoke-Nurture -LeadId $leadStale -IdempotencyKey 'idem-nurture-stale-00001' -ExpectedVersion 9 `
        -Body (New-NurtureBody -DisplayName 'API Stale Person' -Email 'api.stale.person@example.test')
    Add-Result 'stale If-Match is refused' '412' $stale.Status
    Add-Result 'stale If-Match reports version conflict' 'VERSION_CONFLICT' $stale.Body.code
    Add-Result 'stale If-Match creates no Contact' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='API.STALE.PERSON@EXAMPLE.TEST'"))
    Add-Result 'stale If-Match creates no Task' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadStale'"))

    $leadNewState = New-VerifyingLead -DisplayName 'API New State Lead' -Email 'api.newstate@example.test' -StayNew
    $wrongState = Invoke-Nurture -LeadId $leadNewState -IdempotencyKey 'idem-nurture-state-00001' `
        -Body (New-NurtureBody -DisplayName 'API New State Person' -Email 'api.newstate.person@example.test')
    Add-Result 'a NEW-state Lead cannot qualify' '409' $wrongState.Status
    Add-Result 'wrong lifecycle reports invalid transition' 'LEAD_INVALID_TRANSITION' $wrongState.Body.code
    Add-Result 'wrong lifecycle creates no Contact' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='API.NEWSTATE.PERSON@EXAMPLE.TEST'"))

    # A VERIFYING Lead edited into an incomplete profile: replaceLeadProfile does not re-check it.
    $leadIncomplete = New-VerifyingLead -DisplayName 'API Incomplete Lead' -Email 'api.incomplete@example.test'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
UPDATE leads.Leads
SET Profile = JSON_MODIFY(JSON_MODIFY(Profile, '$.phone', NULL), '$.email', NULL)
WHERE LeadId = '$leadIncomplete';
"@
    $incomplete = Invoke-Nurture -LeadId $leadIncomplete -IdempotencyKey 'idem-nurture-incompl-001' `
        -Body (New-NurtureBody -DisplayName 'API Incomplete Person' -Email 'api.incomplete.person@example.test')
    Add-Result 'an incomplete progressive profile is refused' '409' $incomplete.Status
    Add-Result 'incomplete profile reports invalid transition' 'LEAD_INVALID_TRANSITION' $incomplete.Body.code
    Add-Result 'incomplete profile creates no Contact' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='API.INCOMPLETE.PERSON@EXAMPLE.TEST'"))

    # ---------------------------------------------------------------- adopted request contract
    #
    # The whole QualifyLeadNurtureRequest, refused over the real route before the first owner
    # mutation. Rejection alone is not the claim: the coordinator commits Contact, then Task, then
    # the Lead close in three separate owner-local transactions and never compensates, so each case
    # also proves that the global owner-effect snapshot is byte-identical afterwards.

    $contractEffectsQuery = @"
SELECT (SELECT COUNT(*) FROM contacts.Contacts) AS Contacts,
       (SELECT COUNT(*) FROM contacts.AuditRecords) AS ContactAudits,
       (SELECT COUNT(*) FROM contacts.OutboxMessages) AS ContactOutbox,
       (SELECT COUNT(*) FROM contacts.ConversionRecords) AS ContactConversions,
       (SELECT COUNT(*) FROM tasks.Tasks) AS Tasks,
       (SELECT COUNT(*) FROM tasks.AuditRecords) AS TaskAudits,
       (SELECT COUNT(*) FROM tasks.OutboxMessages) AS TaskOutbox,
       (SELECT COUNT(*) FROM leads.AuditRecords) AS LeadAudits,
       (SELECT COUNT(*) FROM leads.OutboxMessages) AS LeadOutbox,
       (SELECT COUNT(*) FROM workflow.LeadQualificationAnchors) AS Anchors,
       (SELECT SUM(CAST([Version] AS BIGINT)) FROM leads.Leads) AS LeadVersions,
       (SELECT SUM(CAST(WorkState AS BIGINT)) FROM leads.Leads) AS LeadWorkStates
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
"@

    $overLimitReason = 'r' * 1001
    $maxName = 'n' * 200
    $overLimitName = 'n' * 201
    $validRevisitAt = '2026-10-01T09:00:00.0000000Z'
    $contactBody = @{ displayName = 'Contract Person'; email = 'api.contract@example.test'; phone = '0900000001'; title = 'Manager' }

    function New-ContractBody {
        param([hashtable] $Relationship, [string] $RevisitAt = '2026-10-01T09:00:00.0000000Z', [string] $Reason = 'Revisit after budget cycle', [hashtable] $Extra)
        $body = @{ relationship = $Relationship; revisitAt = $RevisitAt; reason = $Reason }
        if ($Extra) { foreach ($key in $Extra.Keys) { $body[$key] = $Extra[$key] } }
        return ($body | ConvertTo-Json -Depth 8 -Compress)
    }

    $contractCases = @(
        @{ Name = 'invalid email'; Field = 'relationship.contact.email'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = @{ displayName = 'Contract Person'; email = 'not-an-address' } } }
        @{ Name = 'over-limit reason'; Field = 'reason'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody } -Reason $overLimitReason }
        @{ Name = 'empty reason'; Field = 'reason'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody } -Reason '   ' }
        @{ Name = 'invalid revisitAt'; Field = 'revisitAt'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody } -RevisitAt '2026-13-45T99:00:00Z' }
        @{ Name = 'non-UTC revisitAt'; Field = 'revisitAt'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody } -RevisitAt '2026-10-01T09:00:00+07:00' }
        @{ Name = 'over-limit note'; Field = 'note'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody } -Extra @{ note = ('x' * 4001) } }
        @{ Name = 'over-limit displayName'; Field = 'relationship.contact.displayName'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = @{ displayName = $overLimitName; email = 'api.contract.name@example.test' } } }
        @{ Name = 'over-limit title'; Field = 'relationship.contact.title'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = @{ displayName = 'Contract Person'; title = ('t' * 161) } } }
        @{ Name = 'over-limit phone'; Field = 'relationship.contact.phone'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = @{ displayName = 'Contract Person'; phone = ('9' * 65) } } }
        @{ Name = 'malformed NEW without a contact object'; Field = 'relationship.contact'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW' } }
        @{ Name = 'malformed NEW without a displayName'; Field = 'relationship.contact.displayName'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = @{ email = 'api.contract.noname@example.test' } } }
        @{ Name = 'malformed EXISTING without a selectedId'; Field = 'relationship.selectedId'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'EXISTING'; contact = $contactBody } }
        @{ Name = 'malformed EXISTING without a contact object'; Field = 'relationship.contact'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'EXISTING'; selectedId = 'contact_something' } }
        @{ Name = 'malformed EXISTING with a non-identifier selectedId'; Field = 'relationship.selectedId'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'EXISTING'; selectedId = 'not a valid id'; contact = $contactBody } }
        @{ Name = 'inconsistent NEW carrying a selectedId'; Field = 'relationship.selectedId'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; selectedId = 'contact_something'; contact = $contactBody } }
        @{ Name = 'unadmitted organization on a CONTACT relationship'; Field = 'relationship.organization'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody; organization = @{ displayName = 'Some Org' } } }
        @{ Name = 'invalid ownerId'; Field = 'ownerId'
           Body = New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody } -Extra @{ ownerId = 'not a member id' } }
    )

    $contractCounter = 0
    foreach ($case in $contractCases) {
        $contractCounter++
        $contractLead = New-VerifyingLead -DisplayName "API Contract $contractCounter" -Email "api.contract.$contractCounter@example.test"
        $effectsBefore = Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery
        $rejected = Invoke-Nurture -LeadId $contractLead -IdempotencyKey ('idem-contract-{0:d6}' -f $contractCounter) -Body $case.Body
        Add-Result "contract: $($case.Name) is refused" '422|VALIDATION_FAILED' "$($rejected.Status)|$($rejected.Body.code)"
        Add-Result "contract: $($case.Name) names $($case.Field)" $case.Field (Get-FieldErrorKeys -Response $rejected)
        Add-Result "contract: $($case.Name) performs zero owner effects" $effectsBefore `
            (Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery)
    }

    # A syntactically valid but nonexistent Task owner is deterministic Tasks-owned validation.
    # It must run before the coordinator starts an anchor or calls Contacts. Reusing the same key
    # immediately with the real member proves the refusal poisoned no owner state or workflow state.
    $invalidOwnerLead = New-VerifyingLead -DisplayName 'API Invalid Task Owner Lead' -Email 'api.invalid.task.owner.lead@example.test'
    $invalidOwnerKey = 'idem-nurture-invalid-owner'
    $invalidOwnerEmail = 'api.invalid.task.owner.person@example.test'
    $invalidOwnerBefore = Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery
    $invalidOwnerLeadCount = [long](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM leads.Leads')
    $invalidOwnerLeadVersion = [long](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$invalidOwnerLead'")
    $invalidOwnerLeadState = [long](Get-Scalar -Database $DatabaseName -Query "SELECT WorkState FROM leads.Leads WHERE LeadId='$invalidOwnerLead'")
    $invalidOwnerContactCount = [long](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.Contacts')
    $invalidOwnerTaskCount = [long](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM tasks.Tasks')
    $invalidOwnerReceiptCount = [long](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.ConversionRecords')
    $invalidOwnerAnchorCount = [long](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM workflow.LeadQualificationAnchors')
    $invalidOwner = Invoke-Nurture -LeadId $invalidOwnerLead -IdempotencyKey $invalidOwnerKey -ExpectedVersion $invalidOwnerLeadVersion `
        -Body (New-NurtureBody -DisplayName 'API Invalid Task Owner Person' -Email $invalidOwnerEmail -OwnerId 'member_nurture_missing')
    Add-Result 'invalid Task owner is refused before mutation' '422|VALIDATION_FAILED' "$($invalidOwner.Status)|$($invalidOwner.Body.code)"
    Add-Result 'invalid Task owner names assigneeId' 'assigneeId' (Get-FieldErrorKeys -Response $invalidOwner)
    Add-Result 'invalid Task owner leaves all owner effects unchanged' $invalidOwnerBefore `
        (Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery)
    Add-Result 'invalid Task owner leaves Lead count unchanged' ([string]$invalidOwnerLeadCount) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM leads.Leads'))
    Add-Result 'invalid Task owner leaves Lead version unchanged' ([string]$invalidOwnerLeadVersion) `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$invalidOwnerLead'"))
    Add-Result 'invalid Task owner leaves Lead state unchanged' ([string]$invalidOwnerLeadState) `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT WorkState FROM leads.Leads WHERE LeadId='$invalidOwnerLead'"))
    Add-Result 'invalid Task owner leaves Contact count unchanged' ([string]$invalidOwnerContactCount) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.Contacts'))
    Add-Result 'invalid Task owner leaves Task count unchanged' ([string]$invalidOwnerTaskCount) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM tasks.Tasks'))
    Add-Result 'invalid Task owner leaves Contact receipt count unchanged' ([string]$invalidOwnerReceiptCount) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.ConversionRecords'))
    Add-Result 'invalid Task owner leaves workflow anchor count unchanged' ([string]$invalidOwnerAnchorCount) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM workflow.LeadQualificationAnchors'))
    Add-Result 'invalid Task owner creates no workflow anchor' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM workflow.LeadQualificationAnchors WHERE LeadId='$invalidOwnerLead'"))
    Add-Result 'invalid Task owner creates no Contact' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='$($invalidOwnerEmail.ToUpperInvariant())'"))

    $validAfterInvalid = Invoke-Nurture -LeadId $invalidOwnerLead -IdempotencyKey $invalidOwnerKey -ExpectedVersion $invalidOwnerLeadVersion `
        -Body (New-NurtureBody -DisplayName 'API Invalid Task Owner Person' -Email $invalidOwnerEmail -OwnerId $script:MemberId)
    Add-Result 'valid NURTURE immediately after invalid owner commits' '200|COMMITTED' "$($validAfterInvalid.Status)|$($validAfterInvalid.Body.outcome)"
    Add-Result 'valid NURTURE creates exactly one intended Contact' ([string]($invalidOwnerContactCount + 1)) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.Contacts'))
    Add-Result 'valid NURTURE creates exactly one intended Task' ([string]($invalidOwnerTaskCount + 1)) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM tasks.Tasks'))
    Add-Result 'valid NURTURE creates exactly one Contact receipt' ([string]($invalidOwnerReceiptCount + 1)) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM contacts.ConversionRecords'))
    Add-Result 'valid NURTURE creates exactly one workflow anchor' ([string]($invalidOwnerAnchorCount + 1)) `
        ([string](Get-Scalar -Database $DatabaseName -Query 'SELECT COUNT(*) FROM workflow.LeadQualificationAnchors'))
    Add-Result 'valid NURTURE closes Lead once after invalid owner' ([string]($invalidOwnerLeadVersion + 1)) `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$invalidOwnerLead'"))
    Add-Result 'valid NURTURE closes Lead after invalid owner' '3' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT WorkState FROM leads.Leads WHERE LeadId='$invalidOwnerLead'"))
    Add-Result 'valid NURTURE completes anchor after invalid owner' 'Completed' `
        (Get-Scalar -Database $DatabaseName -Query "SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId='$invalidOwnerLead'")

    $validAfterInvalidEffects = Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery
    $validAfterInvalidReplay = Invoke-Nurture -LeadId $invalidOwnerLead -IdempotencyKey $invalidOwnerKey -ExpectedVersion $invalidOwnerLeadVersion `
        -Body (New-NurtureBody -DisplayName 'API Invalid Task Owner Person' -Email $invalidOwnerEmail -OwnerId $script:MemberId)
    Add-Result 'valid NURTURE after invalid owner replays' '200|REPLAYED' "$($validAfterInvalidReplay.Status)|$($validAfterInvalidReplay.Body.outcome)"
    Add-Result 'replay after invalid owner increases no counts' $validAfterInvalidEffects `
        (Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery)

    # The wire schema is additionalProperties:false, so an undeclared member is refused as a body
    # that does not match the contract - never accepted and silently dropped.
    $closedLead = New-VerifyingLead -DisplayName 'API Contract Closed' -Email 'api.contract.closed@example.test'
    $closedEffectsBefore = Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery
    $closedSchema = Invoke-Nurture -LeadId $closedLead -IdempotencyKey 'idem-contract-closed01' `
        -Body (New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = $contactBody } -Extra @{ notADeclaredField = 'x' })
    Add-Result 'contract: an undeclared request member is refused' '422|VALIDATION_FAILED' "$($closedSchema.Status)|$($closedSchema.Body.code)"
    Add-Result 'contract: the closed-schema refusal performs zero owner effects' $closedEffectsBefore `
        (Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery)

    # G1 boundary. Both halves are required: a bound proven only by its rejection could have been
    # implemented as a silent truncation, which the frozen verbatim transfer forbids.
    $boundaryLead = New-VerifyingLead -DisplayName 'API Contract Boundary' -Email 'api.contract.boundary@example.test'
    $boundary = Invoke-Nurture -LeadId $boundaryLead -IdempotencyKey 'idem-contract-bound001' `
        -Body (New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = @{ displayName = $maxName; email = 'api.contract.boundary.person@example.test' } })
    Add-Result 'G1: a 200-character displayName qualifies' '200|COMMITTED' "$($boundary.Status)|$($boundary.Body.outcome)"
    Add-Result 'G1: the 200-character name is returned whole' $maxName $boundary.Body.result.relationship.displayName
    Add-Result 'G1: the 200-character name is stored whole, not truncated' $maxName `
        (Get-Scalar -Database $DatabaseName -Query "SELECT FullName FROM contacts.Contacts WHERE ContactId='$($boundary.Body.result.contactId)'")

    $overLead = New-VerifyingLead -DisplayName 'API Contract Over' -Email 'api.contract.over@example.test'
    $overEffectsBefore = Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery
    $over = Invoke-Nurture -LeadId $overLead -IdempotencyKey 'idem-contract-over0001' `
        -Body (New-ContractBody -Relationship @{ kind = 'CONTACT'; mode = 'NEW'; contact = @{ displayName = $overLimitName; email = 'api.contract.over.person@example.test' } })
    Add-Result 'G1: a 201-character displayName is refused' '422|VALIDATION_FAILED' "$($over.Status)|$($over.Body.code)"
    Add-Result 'G1: the 201-character refusal names the display name' 'relationship.contact.displayName' (Get-FieldErrorKeys -Response $over)
    Add-Result 'G1: the 201-character refusal performs zero owner effects' $overEffectsBefore `
        (Get-Scalar -Database $DatabaseName -Query $contractEffectsQuery)

    # ---------------------------------------------------------------- duplicate and non-disclosure

    [void](New-SeededContact -FullName 'API Duplicate Blocker' -Email 'api.blocked@example.test' -OwnerId $script:MemberId)
    $leadDuplicate = New-VerifyingLead -DisplayName 'API Duplicate Lead' -Email 'api.duplicate@example.test'
    $duplicate = Invoke-Nurture -LeadId $leadDuplicate -IdempotencyKey 'idem-nurture-dup-000001' `
        -Body (New-NurtureBody -DisplayName 'API Duplicate Person' -Email 'API.Blocked@Example.test')
    Add-Result 'a duplicate address is refused' '422' $duplicate.Status
    Add-Result 'duplicate reports relationship invalid' 'LEAD_QUALIFICATION_RELATIONSHIP_INVALID' $duplicate.Body.code
    Add-Result 'duplicate discloses no Contact identity' 'True' ([string]($duplicate.Raw -notmatch 'contact_'))
    Add-Result 'duplicate creates no second Contact' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='API.BLOCKED@EXAMPLE.TEST'"))
    # DEC-LEAD-CONTACT-DUPLICATE-POLICY 9.4 freezes this exact pointer, and only this pointer: the
    # refusal names the caller's own input field and still asserts nothing about the matched record.
    Add-Result 'duplicate retains the frozen relationship.contact.email pointer, and only it' `
        'relationship.contact.email' (Get-FieldErrorKeys -Response $duplicate)

    $leadUnresolvable = New-VerifyingLead -DisplayName 'API Unresolvable Lead' -Email 'api.unresolvable@example.test'
    $unresolvable = Invoke-Nurture -LeadId $leadUnresolvable -IdempotencyKey 'idem-nurture-unres-0001' `
        -Body (New-NurtureBody -Mode 'EXISTING' -SelectedId 'contact_not_present' -DisplayName 'Ignored')
    Add-Result 'an unresolvable EXISTING Contact is refused' '422' $unresolvable.Status
    Add-Result 'duplicate and unresolvable are indistinguishable' `
        ('{0}|{1}' -f $duplicate.Status, $duplicate.Body.code) `
        ('{0}|{1}' -f $unresolvable.Status, $unresolvable.Body.code)
    # The unresolvable identifier stays pointer-less. That refusal is the one that would otherwise
    # become an existence oracle for records outside the caller's record scope.
    Add-Result 'an unresolvable EXISTING Contact carries no field pointer' '' (Get-FieldErrorKeys -Response $unresolvable)

    $unknownLead = Invoke-Nurture -LeadId 'lead_does_not_exist' -IdempotencyKey 'idem-nurture-unknown-01' `
        -Body (New-NurtureBody -Email 'api.unknown@example.test')
    Add-Result 'an unknown Lead is not found' '404' $unknownLead.Status
    $foreignWorkspaceId = 'ws_nurture_foreign'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Workspaces WHERE WorkspaceId = '$foreignWorkspaceId')
INSERT INTO workspace.Workspaces (WorkspaceId, [Key], [Name], LogoText, CreatedAt)
VALUES (N'$foreignWorkspaceId', N'$foreignWorkspaceId', N'Foreign', N'FW', SYSDATETIMEOFFSET());
"@
    $foreignLeadId = 'lead_foreign_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO leads.Leads (LeadId, WorkspaceId, Profile, ScopeOwnerId, WorkState, Score, CreatedAt, UpdatedAt, [Version])
VALUES (N'$foreignLeadId', N'$foreignWorkspaceId',
 N'{"displayName":"Foreign","phone":"0900000009","source":"verifier","ownerId":"member_foreign","interestedProducts":[],"estimatedValue":{"amount":"0","currency":"VND"},"tags":[],"customFields":[]}',
 N'member_foreign', 2, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0);
"@
    $foreignLead = Invoke-Nurture -LeadId $foreignLeadId -IdempotencyKey 'idem-nurture-foreign-01' `
        -Body (New-NurtureBody -Email 'api.foreign@example.test')
    Add-Result 'a foreign-Workspace Lead is refused' '404' $foreignLead.Status
    Add-Result 'foreign and unknown Leads are indistinguishable' `
        ('{0}|{1}' -f $unknownLead.Status, $unknownLead.Body.code) `
        ('{0}|{1}' -f $foreignLead.Status, $foreignLead.Body.code)
    Add-Result '8A: foreign and unknown Lead denial bodies are byte-equivalent' 'True' ([string]::Equals($unknownLead.Raw, $foreignLead.Raw, [StringComparison]::Ordinal).ToString())
    Add-Result 'no Contact was created in the foreign Workspace' '0' `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId='$foreignWorkspaceId'"))

    # ---------------------------------------------------------------- crash recovery over HTTP

    $leadRecovery = New-VerifyingLead -DisplayName 'API Recovery Lead' -Email 'api.recovery@example.test'
    $recoveryBody = New-NurtureBody -DisplayName 'API Recovery Person' -Email 'api.recovery.person@example.test'
    $leadRecoveryVersion = [long](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadRecovery'")
    $firstRecovery = Invoke-Nurture -LeadId $leadRecovery -IdempotencyKey 'idem-nurture-recovery-01' -ExpectedVersion $leadRecoveryVersion -Body $recoveryBody
    Add-Result 'recovery baseline commits' 'COMMITTED' $firstRecovery.Body.outcome
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
UPDATE workflow.LeadQualificationAnchors
SET Stage = N'TaskCreated', LeadVersion = NULL, ResponseJson = NULL
WHERE LeadId = '$leadRecovery';
"@
    $resumed = Invoke-Nurture -LeadId $leadRecovery -IdempotencyKey 'idem-nurture-recovery-01' -ExpectedVersion $leadRecoveryVersion -Body $recoveryBody
    Add-Result 'a lost completion resumes over the public route' '200' $resumed.Status
    Add-Result 'resume converges on the same Contact' $firstRecovery.Body.result.contactId $resumed.Body.result.contactId
    Add-Result 'resume converges on the same Task' $firstRecovery.Body.result.taskId $resumed.Body.result.taskId
    Add-Result 'resume creates no second Task' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadRecovery'"))
    Add-Result 'resume advances the Lead only once' '3' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadRecovery'"))
    Add-Result '8B: NEW completion recovery preserves full semantic response' 'True' `
        ([string]::Equals(($firstRecovery.Raw -replace '"outcome":"COMMITTED"','"outcome":"REPLAYED"'), $resumed.Raw, [StringComparison]::Ordinal).ToString())

    # EXISTING resolution is owner-returned data, never the supplied Contact display name.
    $linkRecoveryLead = New-VerifyingLead -DisplayName '8B Existing Recovery' -Email '8b.link.lead@example.test'
    $linkRecoveryBody = New-NurtureBody -Mode 'EXISTING' -SelectedId $existingContact -DisplayName 'Ignored request name'
    $linkRecoveryVersion = [long](Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM leads.Leads WHERE LeadId='$linkRecoveryLead'")
    $linkFirst = Invoke-Nurture -LeadId $linkRecoveryLead -IdempotencyKey 'idem-8b-link-recovery' -ExpectedVersion $linkRecoveryVersion -Body $linkRecoveryBody
    Add-Result '8B: EXISTING recovery baseline commits' '200|COMMITTED' "$($linkFirst.Status)|$($linkFirst.Body.outcome)"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE workflow.LeadQualificationAnchors SET Stage='TaskCreated', LeadVersion=NULL, ResponseJson=NULL WHERE LeadId='$linkRecoveryLead'"
    $linkResume = Invoke-Nurture -LeadId $linkRecoveryLead -IdempotencyKey 'idem-8b-link-recovery' -ExpectedVersion $linkRecoveryVersion -Body $linkRecoveryBody
    Add-Result '8B: EXISTING recovery preserves full semantic response' 'True' `
        ([string]::Equals(($linkFirst.Raw -replace '"outcome":"COMMITTED"','"outcome":"REPLAYED"'), $linkResume.Raw, [StringComparison]::Ordinal).ToString())
    $linkReplay = Invoke-Nurture -LeadId $linkRecoveryLead -IdempotencyKey 'idem-8b-link-recovery' -ExpectedVersion $linkRecoveryVersion -Body $linkRecoveryBody
    Add-Result '8B: EXISTING completed replay remains stable' 'True' ([string]::Equals($linkResume.Raw,$linkReplay.Raw,[StringComparison]::Ordinal).ToString())

    # ---------------------------------------------------------------- capability denial

    $leadNoQualify = New-VerifyingLead -DisplayName 'API No Qualify Lead' -Email 'api.noqualify@example.test'
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.qualify'"
    $noQualify = Invoke-Nurture -LeadId $leadNoQualify -IdempotencyKey 'idem-nurture-noqual-0001' `
        -Body (New-NurtureBody -DisplayName 'API No Qualify Person' -Email 'api.noqualify.person@example.test')
    Add-Result 'missing leads.qualify is denied' '403' $noQualify.Status
    Add-Result 'missing leads.qualify creates no Contact' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='API.NOQUALIFY.PERSON@EXAMPLE.TEST'"))
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId','leads.qualify')"

    $leadNoTask = New-VerifyingLead -DisplayName 'API No Task Lead' -Email 'api.notask@example.test'
    $leadNoTaskVersion = [long](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$leadNoTask'")
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='tasks.create'"
    $noTask = Invoke-Nurture -LeadId $leadNoTask -IdempotencyKey 'idem-nurture-notask-0001' -ExpectedVersion $leadNoTaskVersion `
        -Body (New-NurtureBody -DisplayName 'API No Task Person' -Email 'api.notask.person@example.test')
    Add-Result 'missing tasks.create is denied' '403' $noTask.Status
    Add-Result 'missing tasks.create reports downstream capability' 'LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED' $noTask.Body.code
    Add-Result 'the committed Contact is not deleted' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='API.NOTASK.PERSON@EXAMPLE.TEST'"))
    Add-Result 'no Task was created' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadNoTask'"))
    Add-Result 'the Lead stays open' '2' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT WorkState FROM leads.Leads WHERE LeadId='$leadNoTask'"))
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId','tasks.create')"
    $recoveredTask = Invoke-Nurture -LeadId $leadNoTask -IdempotencyKey 'idem-nurture-notask-0001' -ExpectedVersion $leadNoTaskVersion `
        -Body (New-NurtureBody -DisplayName 'API No Task Person' -Email 'api.notask.person@example.test')
    Add-Result 'restoring tasks.create converges' '200' $recoveredTask.Status
    Add-Result 'convergence reuses the same Contact' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='API.NOTASK.PERSON@EXAMPLE.TEST'"))
    Add-Result 'convergence creates exactly one Task' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$leadNoTask'"))

    # Task 8B: real public same-key recovery after a legitimate intervening Lead edit.
    foreach ($makeIneligible in @($false, $true)) {
        $suffix = if ($makeIneligible) { 'ineligible' } else { 'refresh' }
        $versionLead = New-VerifyingLead -DisplayName "8B $suffix" -Email "8b.$suffix.lead@example.test"
        $versionKey = "idem-8b-$suffix"
        $versionBody = New-NurtureBody -DisplayName "8B $suffix person" -Email "8b.$suffix.person@example.test"
        $v1 = [long](Get-Scalar -Database $DatabaseName -Query "SELECT Version FROM leads.Leads WHERE LeadId='$versionLead'")
        Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='tasks.create'"
        try {
            $partial = Invoke-Nurture -LeadId $versionLead -IdempotencyKey $versionKey -ExpectedVersion $v1 -Body $versionBody
            Add-Result "8B ${suffix}: Task capability blocks after Contact commit" '403|LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED' "$($partial.Status)|$($partial.Body.code)"
            $partialContact = Get-Scalar -Database $DatabaseName -Query "SELECT ContactId FROM workflow.LeadQualificationAnchors WHERE LeadId='$versionLead'"
            Add-Result "8B ${suffix}: Contact durably retained" '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE ContactId='$partialContact'"))
            Add-Result "8B ${suffix}: no Task yet" '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$versionLead'"))
            if ($makeIneligible) {
                $edit = Invoke-Api -Method 'POST' -Path "/leads/$versionLead/disqualify" -Token $script:Token -WorkspaceId $script:WorkspaceId `
                    -IdempotencyKey "$versionKey-edit" -IfMatch ('"{0}"' -f $v1) -Body '{"reason":"No fit","evidence":"Confirmed"}'
            }
            else {
                $replacement = @{ displayName = '8B refreshed profile'; phone = '0911000111'; email = "8b.$suffix.lead@example.test"; source = 'verifier'; ownerId = $script:MemberId; estimatedValue = @{amount='0';currency='VND'} }
                $edit = Invoke-Api -Method 'PUT' -Path "/leads/$versionLead" -Token $script:Token -WorkspaceId $script:WorkspaceId `
                    -IdempotencyKey "$versionKey-edit" -IfMatch ('"{0}"' -f $v1) -Body ($replacement | ConvertTo-Json -Depth 6 -Compress)
            }
            Add-Result "8B ${suffix}: legitimate public Lead edit succeeds" '200' $edit.Status
        }
        finally {
            Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId','tasks.create')"
        }
        $v2 = [long]$edit.Body.version
        Add-Result "8B ${suffix}: edit advances version" ([string]($v1 + 1)) ([string]$v2)
        $oldVersion = Invoke-Nurture -LeadId $versionLead -IdempotencyKey $versionKey -ExpectedVersion $v1 -Body $versionBody
        Add-Result "8B ${suffix}: old If-Match is stale" '412|VERSION_CONFLICT' "$($oldVersion.Status)|$($oldVersion.Body.code)"
        Add-Result "8B ${suffix}: stale failure carries current version" ([string]$v2) ([string]$oldVersion.Body.currentVersion)
        $changedVersionBody = New-NurtureBody -DisplayName "8B $suffix person" -Email "8b.$suffix.person@example.test" -Reason 'Different immutable intent'
        $changedVersion = Invoke-Nurture -LeadId $versionLead -IdempotencyKey $versionKey -ExpectedVersion $v2 -Body $changedVersionBody
        Add-Result "8B ${suffix}: refreshed token cannot change intent" '409|IDEMPOTENCY_KEY_REUSED' "$($changedVersion.Status)|$($changedVersion.Body.code)"
        $resumedVersion = Invoke-Nurture -LeadId $versionLead -IdempotencyKey $versionKey -ExpectedVersion $v2 -Body $versionBody
        Add-Result "8B ${suffix}: committed Contact is never compensated" '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE ContactId='$partialContact'"))
        if ($makeIneligible) {
            Add-Result '8B: ineligible Lead is not forced to qualify' '409|LEAD_INVALID_TRANSITION' "$($resumedVersion.Status)|$($resumedVersion.Body.code)"
            Add-Result '8B: ineligible recovery creates no Task' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$versionLead'"))
            Add-Result '8B: ineligible recovery performs no positive close' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$versionLead' AND Operation='qualifyLeadForNurture'"))
        }
        else {
            Add-Result '8B: refreshed If-Match resumes same key' '200|COMMITTED' "$($resumedVersion.Status)|$($resumedVersion.Body.outcome)"
            Add-Result '8B: refreshed recovery reuses original Contact' $partialContact $resumedVersion.Body.result.contactId
            Add-Result '8B: refreshed recovery reports both created resources' 'CONTACT,TASK' (($resumedVersion.Body.result.createdResources.resourceType) -join ',')
            Add-Result '8B: refreshed recovery creates exactly one Contact' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='8B.REFRESH.PERSON@EXAMPLE.TEST'"))
            Add-Result '8B: refreshed recovery creates exactly one Task' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId='$versionLead'"))
            Add-Result '8B: refreshed recovery performs exactly one positive close' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$versionLead' AND Operation='qualifyLeadForNurture'"))
            Add-Result '8B: refreshed recovery anchor completes' 'Completed' (Get-Scalar -Database $DatabaseName -Query "SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId='$versionLead'")
            $versionReplay = Invoke-Nurture -LeadId $versionLead -IdempotencyKey $versionKey -ExpectedVersion $v2 -Body $versionBody
            Add-Result '8B: refreshed recovery completed response remains stable' 'True' `
                ([string]::Equals(($resumedVersion.Raw -replace '"outcome":"COMMITTED"','"outcome":"REPLAYED"'),$versionReplay.Raw,[StringComparison]::Ordinal).ToString())
        }
    }

    # ---------------------------------------------------------------- no foreign-owner writes

    foreach ($pair in @(@('deals','Deals'), @('customers','Customers'), @('quotes','Quotes'), @('orders','Orders'), @('products','Products'))) {
        $exists = Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='$($pair[0])' AND TABLE_NAME='$($pair[1])'"
        $rows = if ([int]$exists -eq 0) { 0 } else { Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM [$($pair[0])].[$($pair[1])]" }
        Add-Result "no $($pair[0]).$($pair[1]) row was written" '0' ([string]$rows)
    }

    # ---------------------------------------------------------------- unexposed siblings

    $opportunity = Invoke-Api -Method 'POST' -Path "/workflows/lead-qualification/$leadNew/opportunity" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId -IdempotencyKey 'idem-nurture-oppty-0001' -IfMatch '"0"' -Body '{}'
    Add-Result 'qualifyLeadForOpportunity stays unexposed' '404' $opportunity.Status
    $directSale = Invoke-Api -Method 'POST' -Path "/workflows/lead-qualification/$leadNew/direct-sale" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId -IdempotencyKey 'idem-nurture-direct-0001' -IfMatch '"0"' -Body '{}'
    Add-Result 'qualifyLeadForDirectSale stays unexposed' '404' $directSale.Status
    $genericQualify = Invoke-Api -Method 'POST' -Path "/leads/$leadNew/qualify" `
        -Token $script:Token -WorkspaceId $script:WorkspaceId -IdempotencyKey 'idem-nurture-generic-001' -Body '{}'
    Add-Result 'the retired generic qualify stays unexposed' '404' $genericQualify.Status
    $createContact = Invoke-Api -Method 'POST' -Path '/contacts' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-nurture-createcontact' -Body '{"fullName":"Should Not Exist"}'
    # The path exists for GET only, so the host answers 405. Either way there is no POST /contacts.
    Add-Result 'createContact stays blocked' '405' $createContact.Status
}
finally {
    if ($hostProcess -and -not $hostProcess.HasExited) {
        try { $hostProcess.Kill($true) } catch { }
        try { $hostProcess.WaitForExit(30000) | Out-Null } catch { }
    }
    foreach ($name in @(
        'ConnectionStrings__UnicoreCRM','ASPNETCORE_URLS','ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT',
        'Development__ApplyMigrations','IdentityAuth__EmailVerification__Sender__Kind','UNICORE_DEV_SEED_ENABLED',
        'IdentityAuth__DevelopmentBootstrap__Enabled','IdentityAuth__DevelopmentBootstrap__Email',
        'IdentityAuth__DevelopmentBootstrap__Password','IdentityAuth__DevelopmentBootstrap__DisplayName',
        'Workspace__DevelopmentBootstrap__Enabled','AccessControl__DevelopmentBootstrap__Enabled',
        'Workflows__InitialWorkspaceProvisioning__ResumeEnabled','AI__Provider__Kind')) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
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
Write-Host ("NURTURE qualification API verification: passed={0} failed={1}" -f $script:Passed, $script:Failed)
if ($script:Failed -ne 0) { throw 'NURTURE qualification API verification failed.' }
Write-Host 'NURTURE QUALIFICATION PUBLIC API: PASS'
