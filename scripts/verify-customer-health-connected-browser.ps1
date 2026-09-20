[CmdletBinding()]
param(
    [string] $DatabaseName = 'UnicoreCRM_CustomerHealth_Browser_Verify',
    [int] $ApiPort = 5360,
    [string] $FrontendPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($FrontendPath)) { $FrontendPath = (Resolve-Path "$PSScriptRoot/../../frontend/unicorecrm-web").Path }
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$apiUrl = "http://127.0.0.1:$ApiPort"
$workspaceKey = 'customer-health-browser'
$ownerEmail = 'customer.health.browser.owner@example.test'
$ownerPassword = 'Customer-Health-Browser!234'
$deniedEmail = 'customer.health.browser.denied@example.test'
$deniedPassword = 'Customer-Health-Denied!234'
$jwt = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$dll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$hostRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-customer-health-browser-' + [Guid]::NewGuid().ToString('N')))

function SqlScalar([string] $query) {
    return (((& sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query") | Where-Object { $_.Trim() }) -join '').Trim()
}

function Api([string] $method, [string] $path, [object] $body, [string] $token, [string] $workspaceId, [string] $idempotencyKey) {
    $headers = @{ 'X-Request-Id' = 'browser-' + [Guid]::NewGuid().ToString('N'); 'X-Correlation-Id' = 'browser-' + [Guid]::NewGuid().ToString('N') }
    if ($token) { $headers.Authorization = "Bearer $token" }
    if ($workspaceId) { $headers['X-Workspace-Id'] = $workspaceId }
    if ($idempotencyKey) { $headers['Idempotency-Key'] = $idempotencyKey }
    $parameters = @{ Uri="$apiUrl$path"; Method=$method; Headers=$headers; UseBasicParsing=$true }
    if ($null -ne $body) { $parameters.ContentType = 'application/json'; $parameters.Body = ($body | ConvertTo-Json -Depth 8 -Compress) }
    return (Invoke-WebRequest @parameters).Content | ConvertFrom-Json
}

function Set-Environment {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = $apiUrl
    $env:Frontend__AllowedOrigins__0 = 'http://127.0.0.1:3000'; $env:ConnectionStrings__UnicoreCRM = $connection
    $env:IdentityAuth__Jwt__SigningKey = $jwt; $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'; $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $ownerEmail; $env:IdentityAuth__DevelopmentBootstrap__Password = $ownerPassword
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Customer Health Browser Owner'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'true'; $env:Workspace__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Workspace__DevelopmentBootstrap__IdentityEmail = $ownerEmail; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Key = $workspaceKey
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Name = 'Customer Health Browser'; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__LogoText = 'CH'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Locale = 'en'; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__TimeZone = 'UTC'
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__BaseCurrency = 'USD'; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__AvailableProductSpaces__0 = 'crm'
    @('contacts','customers') | ForEach-Object -Begin { $i=0 } -Process { Set-Item "env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__$i" $_; $i++ }
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Key = 'customer-health-foreign'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Name = 'Customer Health Foreign'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__LogoText = 'CF'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Locale = 'en'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__TimeZone = 'UTC'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__BaseCurrency = 'USD'
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'true'; $env:AccessControl__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:AccessControl__DevelopmentBootstrap__IdentityEmail = $ownerEmail; $env:AccessControl__DevelopmentBootstrap__WorkspaceKey = $workspaceKey
    $env:AccessControl__DevelopmentBootstrap__RoleName = 'Customer Health Browser Owner'
    @('access.read','workspace.context.resolve','contacts.read','contacts.create','customers.view','customers.onboard_existing') | ForEach-Object -Begin { $i=0 } -Process { Set-Item "env:AccessControl__DevelopmentBootstrap__Capabilities__$i" $_; $i++ }
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'
}

$hostProcess = $null
try {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    Set-Environment
    & dotnet $dll --migrate | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Migration failed.' }
    & dotnet $dll --seed-demo | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Bootstrap failed.' }
    $output = Join-Path $temporaryDirectory 'host.out'; $errorOutput = Join-Path $temporaryDirectory 'host.err'
    $hostProcess = Start-Process dotnet -ArgumentList @($dll) -WorkingDirectory $hostRoot -WindowStyle Hidden -RedirectStandardOutput $output -RedirectStandardError $errorOutput -PassThru
    for ($attempt=0; $attempt -lt 120; $attempt++) { try { if ((Invoke-WebRequest -UseBasicParsing -Uri "$apiUrl/health" -TimeoutSec 2).StatusCode -eq 200) { break } } catch {}; Start-Sleep -Milliseconds 250 }
    if ($hostProcess.HasExited) { throw "ApiHost exited: $(Get-Content -Raw $errorOutput)" }

    $workspaceId = SqlScalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$workspaceKey';"
    $session = Api 'POST' '/auth/sessions' @{ email=$ownerEmail; password=$ownerPassword } '' '' 'browser-owner-signin'
    $token = $session.accessToken
    $customers = @()
    foreach ($fixture in @(
        @{ Name='Health Unknown'; Suffix='unknown' },
        @{ Name='Health Recent'; Suffix='recent' },
        @{ Name='Health Overdue'; Suffix='overdue' }
    )) {
        $contact = Api 'POST' '/contacts' @{ fullName=$fixture.Name; workEmail="$($fixture.Suffix)@health.example.test" } $token $workspaceId "browser-contact-$($fixture.Suffix)"
        $customer = Api 'POST' '/customers' @{ relationshipRef=@{ type='CONTACT'; id=$contact.result.contact.id } } $token $workspaceId "browser-customer-$($fixture.Suffix)"
        $customers += [pscustomobject]@{ Suffix=$fixture.Suffix; ContactId=$contact.result.contact.id; CustomerId=$customer.result.id; CustomerCode=$customer.result.customerCode; Version=$customer.result.version }
    }
    $recent = $customers | Where-Object Suffix -eq 'recent'; $overdue = $customers | Where-Object Suffix -eq 'overdue'; $unknown = $customers | Where-Object Suffix -eq 'unknown'
    & sqlcmd -S $server -d $DatabaseName -b -Q @"
INSERT INTO commercial_evidence.PurchaseEvidence (WorkspaceId,EvidenceId,EvidenceType,BuyerRefType,BuyerRefId,SourceType,SourceSystem,SourceId,OccurredAt,PolicyVersion,CorrelationId) VALUES
('$workspaceId','browser_health_recent_1','HISTORICAL_PURCHASE_IMPORTED','CONTACT','$($recent.ContactId)','HISTORICAL_IMPORT','browser-health','recent-1',DATEADD(day,-62,SYSUTCDATETIME()),'COMMERCIAL_EVIDENCE_ORIGINAL_V1','corr-browser-recent-1'),
('$workspaceId','browser_health_recent_2','EXTERNAL_PURCHASE_CONFIRMED','CONTACT','$($recent.ContactId)','EXTERNAL_PURCHASE','browser-health','recent-2',DATEADD(day,-32,SYSUTCDATETIME()),'COMMERCIAL_EVIDENCE_ORIGINAL_V1','corr-browser-recent-2'),
('$workspaceId','browser_health_recent_3','ORDER_COMPLETED','CONTACT','$($recent.ContactId)','ORDER',NULL,'recent-3',DATEADD(day,-2,SYSUTCDATETIME()),'COMMERCIAL_EVIDENCE_ORIGINAL_V1','corr-browser-recent-3'),
('$workspaceId','browser_health_overdue_1','ORDER_COMPLETED','CONTACT','$($overdue.ContactId)','ORDER',NULL,'overdue-1',DATEADD(day,-200,SYSUTCDATETIME()),'COMMERCIAL_EVIDENCE_ORIGINAL_V1','corr-browser-overdue-1');
"@ | Out-Null

    Api 'POST' '/auth/accounts' @{ email=$deniedEmail; password=$deniedPassword; displayName='Customer Health Denied' } '' '' 'browser-denied-register' | Out-Null
    & sqlcmd -S $server -d $DatabaseName -b -Q @"
UPDATE iam.Accounts SET Status='Active', EmailVerifiedAt=SYSUTCDATETIME() WHERE NormalizedEmail=UPPER('$deniedEmail');
DECLARE @account nvarchar(128)=(SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail=UPPER('$deniedEmail'));
DECLARE @member nvarchar(128)=(SELECT MemberId FROM iam.Accounts WHERE AccountId=@account);
INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES('membership_health_denied','$workspaceId',@account,@member,'Active',SYSUTCDATETIME());
INSERT INTO access.Roles(RoleId,WorkspaceId,Name,NormalizedName,Description,SourceTemplateId,IsActive,Version,CreatedAt,UpdatedAt) VALUES('role_health_denied','$workspaceId','Health Denied','HEALTH DENIED',NULL,NULL,1,0,SYSUTCDATETIME(),SYSUTCDATETIME());
INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('role_health_denied','workspace.context.resolve'),('role_health_denied','access.read'),('role_health_denied','customers.view');
INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES('scope_health_denied','$workspaceId','role_health_denied','customers','Custom','[]');
INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES('assignment_health_denied','$workspaceId','membership_health_denied','role_health_denied',SYSUTCDATETIME());
"@ | Out-Null

    $env:UNICORECRM_TEST_API_BASE_URL=$apiUrl; $env:UNICORECRM_TEST_WORKSPACE_KEY=$workspaceKey; $env:UNICORECRM_TEST_EMAIL=$ownerEmail; $env:UNICORECRM_TEST_PASSWORD=$ownerPassword
    $env:UNICORECRM_TEST_DENIED_EMAIL=$deniedEmail; $env:UNICORECRM_TEST_DENIED_PASSWORD=$deniedPassword
    $env:UNICORECRM_TEST_UNKNOWN_CUSTOMER_ID=$unknown.CustomerId; $env:UNICORECRM_TEST_UNKNOWN_CUSTOMER_CODE=$unknown.CustomerCode; $env:UNICORECRM_TEST_RECENT_CUSTOMER_ID=$recent.CustomerId
    $env:UNICORECRM_TEST_RECENT_CUSTOMER_CODE=$recent.CustomerCode; $env:UNICORECRM_TEST_OVERDUE_CUSTOMER_CODE=$overdue.CustomerCode; $env:PLAYWRIGHT_DISABLE_VIDEO='1'
    Push-Location $FrontendPath
    try { & node node_modules/@playwright/test/cli.js test --config playwright.customer-health-connected.config.ts; if ($LASTEXITCODE -ne 0) { throw 'Connected Customer Health browser E2E failed.' } }
    finally { Pop-Location }

    foreach ($fixture in $customers) {
        $versionAfter = SqlScalar "SELECT Version FROM customers.Customers WHERE CustomerId='$($fixture.CustomerId)';"
        if ([long]$versionAfter -ne [long]$fixture.Version) { throw 'Customer Health browser reads mutated a Customer aggregate version.' }
    }
    if ([int](SqlScalar "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name='customers' AND t.name LIKE '%Health%';") -ne 0) { throw 'Customer Health persistence table was introduced.' }
    Write-Output "CONNECTED CUSTOMER HEALTH BROWSER E2E: PASS (unknown=$($unknown.CustomerId), recent=$($recent.CustomerId), overdue=$($overdue.CustomerId), noMutation=true)"
}
finally {
    if ($hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force; $hostProcess.WaitForExit() }
}
