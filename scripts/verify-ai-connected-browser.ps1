[CmdletBinding()]
param(
    [string] $DatabaseName = 'UnicoreCRM_AI_Browser_Verify',
    [int] $ApiPort = 5350,
    [string] $FrontendPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($FrontendPath)) { $FrontendPath = (Resolve-Path "$PSScriptRoot/../../frontend/unicorecrm-web").Path }
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$apiUrl = "http://127.0.0.1:$ApiPort"
$email = 'ai.browser.owner@example.test'
$password = 'AI-Browser-E2E!234'
$jwt = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$dll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$root = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-ai-browser-' + [Guid]::NewGuid().ToString('N')))

function Set-Environment([string] $workspaceKey) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = $apiUrl
    $env:Frontend__AllowedOrigins__0 = 'http://127.0.0.1:3000'
    $env:ConnectionStrings__UnicoreCRM = $connection; $env:IdentityAuth__Jwt__SigningKey = $jwt; $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'; $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'; $env:IdentityAuth__DevelopmentBootstrap__Email = $email; $env:IdentityAuth__DevelopmentBootstrap__Password = $password; $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'AI Browser Owner'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'true'; $env:Workspace__DevelopmentBootstrap__ApplyMigrations = 'false'; $env:Workspace__DevelopmentBootstrap__IdentityEmail = $email
    $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Key = $workspaceKey; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Name = "AI Browser $workspaceKey"; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__LogoText = 'AI'; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Locale = 'en'; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__TimeZone = 'UTC'; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__BaseCurrency = 'USD'; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__AvailableProductSpaces__0 = 'crm'
    @('leads','deals','tasks','contacts','organizations','customers') | ForEach-Object -Begin { $i=0 } -Process { Set-Item "env:Workspace__DevelopmentBootstrap__MemberWorkspace__EnabledModuleKeys__$i" $_; $i++ }
    $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Key = 'ai-browser-foreign'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Name = 'AI Browser Foreign'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__LogoText = 'AF'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__Locale = 'en'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__TimeZone = 'UTC'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__BaseCurrency = 'USD'; $env:Workspace__DevelopmentBootstrap__NonMemberWorkspace__AvailableProductSpaces__0 = 'crm'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'true'; $env:AccessControl__DevelopmentBootstrap__ApplyMigrations = 'false'; $env:AccessControl__DevelopmentBootstrap__IdentityEmail = $email; $env:AccessControl__DevelopmentBootstrap__WorkspaceKey = $workspaceKey; $env:AccessControl__DevelopmentBootstrap__RoleName = 'AI Browser Owner'
    @('access.read','workspace.context.resolve','contacts.read','contacts.create','leads.read','deals.read','tasks.read','organizations.read','customers.view','ai.configuration.read','ai.configuration.manage') | ForEach-Object -Begin { $i=0 } -Process { Set-Item "env:AccessControl__DevelopmentBootstrap__Capabilities__$i" $_; $i++ }
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'; $env:AI__Provider__DevelopmentMode = 'Normal'; $env:AI__Provider__TimeoutSeconds = '10'; $env:AI__ProviderTesting__UseDeterministicTransport = 'true'
}

$hostProcess = $null
try {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    Set-Environment 'ai-browser-a'; & dotnet $dll --migrate | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Migration failed.' }; & dotnet $dll --seed-demo | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Workspace A seed failed.' }
    Set-Environment 'ai-browser-b'; & dotnet $dll --seed-demo | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Workspace B seed failed.' }
    Set-Environment 'ai-browser-a'
    $output = Join-Path $temporaryDirectory 'host.out'; $errorOutput = Join-Path $temporaryDirectory 'host.err'
    $hostProcess = Start-Process dotnet -ArgumentList @($dll) -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput $output -RedirectStandardError $errorOutput -PassThru
    for ($attempt=0; $attempt -lt 120; $attempt++) { try { if ((Invoke-WebRequest -UseBasicParsing -Uri "$apiUrl/health" -TimeoutSec 2).StatusCode -eq 200) { break } } catch {}; Start-Sleep -Milliseconds 250 }
    if ($hostProcess.HasExited) { throw "ApiHost exited: $(Get-Content -Raw $errorOutput)" }
    $workspaceA = ((& sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='ai-browser-a';") | Where-Object { $_.Trim() }) -join ''
    $env:UNICORECRM_TEST_API_BASE_URL = $apiUrl; $env:UNICORECRM_TEST_EMAIL = $email; $env:UNICORECRM_TEST_PASSWORD = $password; $env:UNICORECRM_TEST_WORKSPACE_ID = $workspaceA.Trim(); $env:UNICORECRM_TEST_WORKSPACE_KEY = 'ai-browser-a'; $env:UNICORECRM_TEST_WORKSPACE_B_KEY = 'ai-browser-b'; $env:UNICORECRM_TEST_DATABASE = $DatabaseName; $env:UNICORECRM_TEST_SQL_SERVER = $server; $env:PLAYWRIGHT_DISABLE_VIDEO = '1'
    Push-Location $FrontendPath
    try { & node node_modules/@playwright/test/cli.js test --config playwright.ai-connected.config.ts; if ($LASTEXITCODE -ne 0) { throw 'Connected AI browser E2E failed.' } }
    finally { Pop-Location }
    $ledger = ((& sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM platform_ai.AiExecutions WHERE WorkspaceId='$($workspaceA.Trim())' AND Status='SUCCEEDED' AND ContextTypesJson LIKE '%contact.summary.read%';") | Where-Object { $_.Trim() }) -join ''
    if ([int]$ledger.Trim() -lt 1) { throw 'Successful Contact AI execution evidence was not persisted.' }
    $activeAi = ((& sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM platform_ai.WorkspaceAiConfigurations WHERE WorkspaceId='$($workspaceA.Trim())' AND Status='ACTIVE' AND ActivePolicyJson LIKE '%GEMINI%' AND ActivePrimaryProtectedCredential IS NOT NULL;") | Where-Object { $_.Trim() }) -join ''
    if ([int]$activeAi.Trim() -ne 1) { throw 'Connected AI Settings did not persist an active protected Workspace provider configuration.' }
    Write-Output "CONNECTED AI BROWSER E2E: PASS (ledger=$($ledger.Trim()), activeAi=$($activeAi.Trim()))"
}
finally {
    if ($hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force; $hostProcess.WaitForExit() }
}
