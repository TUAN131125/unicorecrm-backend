[CmdletBinding()]
param(
    [ValidatePattern('^UnicoreCRM_PagingBrowser_[A-Za-z0-9_]+$')]
    [string] $DatabaseName = ('UnicoreCRM_PagingBrowser_' + [Guid]::NewGuid().ToString('N')),
    [int] $ApiPort = 5368,
    [string] $EvidenceDirectory = 'D:/Project_All/UnicoreCRM/review-artifacts/PRODUCTION_HARDENING_20261006'
)
$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
$apiUrl = "http://127.0.0.1:$ApiPort"
$backend = (Resolve-Path "$PSScriptRoot/..").Path
$frontend = (Resolve-Path "$PSScriptRoot/../../frontend/unicorecrm-web").Path
$dll = Join-Path $backend 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll'
$hostRoot = Join-Path $backend 'src/UnicoreCRM.ApiHost'
$email = 'paging.browser.owner@example.test'
$password = 'Paging-Browser!2026'
$hostProcess = $null
$created = $false
$savedEnvironment = @{}
function Set-FixtureEnvironment([string] $Name, [string] $Value) {
    if (-not $savedEnvironment.ContainsKey($Name)) { $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process') }
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}
function Sql([string] $Query, [string] $Database = $DatabaseName) {
    $sqlConnection = [System.Data.SqlClient.SqlConnection]::new("Server=$server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True")
    $sqlConnection.Open()
    try {
        $command = $sqlConnection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 120
        [void]$command.ExecuteNonQuery()
    } finally { $sqlConnection.Dispose() }
}
function Api([string] $Method, [string] $Path, $Body, [string] $Token = '', [string] $Workspace = '') {
    $headers = @{ 'X-Request-Id' = 'paging-browser-' + [Guid]::NewGuid().ToString('N'); 'X-Correlation-Id' = 'paging-browser-real'; 'Idempotency-Key' = 'paging-browser-' + [Guid]::NewGuid().ToString('N') }
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    if ($Workspace) { $headers['X-Workspace-Id'] = $Workspace }
    $parameters = @{ Uri = "$apiUrl$Path"; Method = $Method; Headers = $headers; TimeoutSec = 30 }
    if ($null -ne $Body) { $parameters.ContentType = 'application/json'; $parameters.Body = $Body | ConvertTo-Json -Depth 8 -Compress }
    return Invoke-RestMethod @parameters
}
try {
    [void](New-Item -ItemType Directory -Path $EvidenceDirectory -Force)
    Sql "IF DB_ID('$DatabaseName') IS NOT NULL THROW 50001, 'Fixture database already exists', 1; CREATE DATABASE [$DatabaseName];" 'master'
    $created = $true
    foreach ($entry in @{
        ASPNETCORE_ENVIRONMENT = 'Development'; DOTNET_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = $apiUrl
        ConnectionStrings__UnicoreCRM = $connection; Development__ApplyMigrations = 'true'
        Frontend__AllowedOrigins__0 = 'http://127.0.0.1:3018'
        IdentityAuth__Jwt__SigningKey = ([Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N'))
        IdentityAuth__RefreshTokenPepper = ([Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N'))
        IdentityAuth__DevelopmentBootstrap__Enabled = 'true'; IdentityAuth__DevelopmentBootstrap__Email = $email
        IdentityAuth__DevelopmentBootstrap__Password = $password; IdentityAuth__DevelopmentBootstrap__DisplayName = 'Paging Browser Owner'
        IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
        Workspace__DevelopmentBootstrap__Enabled = 'false'; AccessControl__DevelopmentBootstrap__Enabled = 'false'
        Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'; UNICORE_DEV_SEED_ENABLED = 'false'
        AI__Provider__Kind = 'DevelopmentDeterministic'
        'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command' = 'Information'
    }.GetEnumerator()) { Set-FixtureEnvironment $entry.Key $entry.Value }
    Push-Location $backend
    try {
        & dotnet $dll --migrate *> (Join-Path $EvidenceDirectory 'paging-browser-migrate.log')
        if ($LASTEXITCODE -ne 0) { throw 'Real owner migrations failed.' }
        & dotnet $dll --seed-demo *> (Join-Path $EvidenceDirectory 'paging-browser-seed.log')
        if ($LASTEXITCODE -ne 0) { throw 'Real identity bootstrap failed.' }
    } finally { Pop-Location }
    $hostProcess = Start-Process dotnet -ArgumentList @($dll) -WorkingDirectory $hostRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceDirectory 'paging-browser-host.log') -RedirectStandardError (Join-Path $EvidenceDirectory 'paging-browser-host.err')
    $ready = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($hostProcess.HasExited) { throw 'Real ApiHost exited before readiness.' }
        try { if ((Invoke-WebRequest -Uri "$apiUrl/auth/session" -TimeoutSec 2).StatusCode -eq 200) { $ready = $true; break } }
        catch { if ($_.Exception.Response.StatusCode -eq 401) { $ready = $true; break } }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'Real ApiHost readiness timeout.' }
    $session = Api 'POST' '/auth/sessions' @{ email = $email; password = $password }
    $token = $session.accessToken
    $principal = Api 'GET' '/auth/session' $null $token
    $member = $principal.principal.memberId
    $provisioned = Api 'POST' '/workspaces/initial-provisioning' @{ name = 'Paging Browser Workspace' } $token
    $workspace = $provisioned.workspaceId
    $bootstrap = Api 'GET' "/workspaces/$workspace/bootstrap" $null $token $workspace
    if (-not $token -or -not $member -or -not $workspace -or $bootstrap.configuration.enabledModuleKeys -notcontains 'contacts') { throw 'Real JWT/workspace bootstrap incomplete.' }
    $memberships = Api 'GET' '/workspaces' $null $token
    $membership = @($memberships.items | Where-Object { $_.workspaceId -eq $workspace })
    if ($membership.Count -ne 1 -or $membership[0].status -ne 'active' -or $membership[0].workspaceKey -ne $bootstrap.workspace.workspaceKey) { throw 'Fixture membership/bootstrap route mismatch.' }
    $workspaceKey = $membership[0].workspaceKey
    Sql @"
DECLARE @i int = 1;
WHILE @i <= 130
BEGIN
 INSERT contacts.Contacts (ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile)
 VALUES(CONCAT('contact_browser_',FORMAT(@i,'000')),'$workspace',CASE WHEN @i%2=1 THEN '$member' ELSE 'member_browser_other' END,
 CONCAT('Browser Contact ',FORMAT(@i,'000')),CASE WHEN @i%2=1 THEN 'active' ELSE 'needs_follow_up' END,0,SYSUTCDATETIME(),DATEADD(second,@i,'2026-01-01'),N'{"source":"browser-real"}');
 SET @i+=1;
END;
SET @i=1;
WHILE @i <= 65
BEGIN
 INSERT leads.Leads (LeadId,WorkspaceId,ScopeOwnerId,SearchText,PhoneSearchText,WorkState,Score,Version,CreatedAt,UpdatedAt,Profile)
 VALUES(CONCAT('lead_browser_new_',FORMAT(@i,'000')),'$workspace','$member',CONCAT('BROWSER NEW ',FORMAT(@i,'000')),N'',0,0,0,SYSUTCDATETIME(),DATEADD(second,@i,'2026-01-01'),
 CONCAT(N'{"displayName":"Browser New ',FORMAT(@i,'000'),N'","source":"WEB","ownerId":"$member","interestedProducts":[],"tags":[],"customFields":[]}'));
 INSERT leads.Leads (LeadId,WorkspaceId,ScopeOwnerId,SearchText,PhoneSearchText,WorkState,Score,Version,CreatedAt,UpdatedAt,Profile)
 VALUES(CONCAT('lead_browser_contacting_',FORMAT(@i,'000')),'$workspace','$member',CONCAT('BROWSER CONTACTING ',FORMAT(@i,'000')),N'',1,0,0,SYSUTCDATETIME(),DATEADD(second,@i,'2026-01-01'),
 CONCAT(N'{"displayName":"Browser Contacting ',FORMAT(@i,'000'),N'","source":"WEB","ownerId":"$member","interestedProducts":[],"tags":[],"customFields":[]}'));
 SET @i+=1;
END;
"@
    foreach ($entry in @{
        UNICORECRM_TEST_API_BASE_URL = $apiUrl; UNICORECRM_TEST_WORKSPACE_ID = $workspace
        UNICORECRM_TEST_EMAIL = $email; UNICORECRM_TEST_PASSWORD = $password
        UNICORECRM_TEST_WORKSPACE_KEY = $workspaceKey; UNICORECRM_TEST_MEMBER_ID = $member; UNICORECRM_TEST_EVIDENCE_DIR = $EvidenceDirectory
    }.GetEnumerator()) { Set-FixtureEnvironment $entry.Key $entry.Value }
    Push-Location $frontend
    try {
        & node node_modules/@playwright/test/cli.js test --config playwright.contact-paging-kanban-real.config.ts
        if ($LASTEXITCODE -ne 0) { throw 'Real Contact paging/Lead Kanban browser verification failed.' }
    } finally { Pop-Location }
    Sql "IF EXISTS(SELECT 1 FROM contacts.Contacts WHERE WorkspaceId='$workspace' AND Version<>0) OR EXISTS(SELECT 1 FROM leads.Leads WHERE WorkspaceId='$workspace' AND Version<>0) THROW 50002,'Browser read mutated aggregate versions',1;"
    Write-Output 'REAL CONTACT PAGING + LEAD KANBAN BROWSER: PASS'
} finally {
    if ($hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force; [void]$hostProcess.WaitForExit(10000) }
    if ($created) { Sql "ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName];" 'master' }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
