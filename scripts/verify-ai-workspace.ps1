[CmdletBinding()]
param(
    [string] $DatabaseName = ('UnicoreCRM_AI_' + [Guid]::NewGuid().ToString('N')),
    [string] $SqlServer = 'localhost',
    [int] $ApiPort = 5350
)
$ErrorActionPreference = 'Stop'
if ($DatabaseName -notmatch '^UnicoreCRM_AI_[A-Za-z0-9_]+$') { throw 'Use an isolated UnicoreCRM_AI_ database.' }
$backend = (Resolve-Path "$PSScriptRoot/..").Path
$frontend = (Resolve-Path "$backend/../frontend/unicorecrm-web").Path
$root = (Resolve-Path "$backend/src/UnicoreCRM.ApiHost").Path
$dll = Join-Path $root 'bin/Debug/net10.0/UnicoreCRM.ApiHost.dll'
$runtime = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('unicore-ai-' + [Guid]::NewGuid().ToString('N')))
$evidence = New-Item -ItemType Directory -Force -Path (Join-Path $backend "docs/verification/evidence/ai-connected-20261022/$DatabaseName")
$api = "http://127.0.0.1:$ApiPort"
$email = 'ai.workspace@example.test'
$password = 'AI-Workspace-' + [Guid]::NewGuid().ToString('N') + '!2'
$env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = $api
$env:ConnectionStrings__UnicoreCRM = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
$env:IdentityAuth__Jwt__SigningKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:IdentityAuth__RefreshTokenPepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:Frontend__AllowedOrigins__0 = 'http://127.0.0.1:3015'
$env:AI__Provider__Kind = 'WorkspaceProduction'; $env:AI__ProviderTesting__UseDeterministicTransport = 'true'
$env:AI__DataProtection__KeyRingPath = Join-Path $runtime 'keys'
$env:AI__Providers__GEMINI__ApiKey = 'deterministic-deployment-key'
$env:AI__Providers__OPENAI__ApiKey = 'deterministic-deployment-key'
$env:AI__Provider__TimeoutSeconds = '10'
$env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'; $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
$env:IdentityAuth__DevelopmentBootstrap__Email = $email; $env:IdentityAuth__DevelopmentBootstrap__Password = $password; $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'AI Workspace Verifier'
$env:Workspace__DevelopmentBootstrap__Enabled = 'true'; $env:Workspace__DevelopmentBootstrap__ApplyMigrations = 'false'; $env:Workspace__DevelopmentBootstrap__IdentityEmail = $email
$env:AccessControl__DevelopmentBootstrap__Enabled = 'true'; $env:AccessControl__DevelopmentBootstrap__ApplyMigrations = 'false'; $env:AccessControl__DevelopmentBootstrap__IdentityEmail = $email; $env:AccessControl__DevelopmentBootstrap__RoleName = 'AI Workspace Verifier'
$capabilities = @('access.read','workspace.context.resolve','leads.read','leads.create','leads.update','contacts.read','contacts.create','customers.view','customers.onboard_existing','customers.edit','organizations.read','deals.read','tasks.read','tasks.create','tasks.update','tasks.assign','tasks.complete','activities.read','ai.configuration.read','ai.configuration.manage')
Get-ChildItem Env: | Where-Object { $_.Name.StartsWith('AccessControl__DevelopmentBootstrap__Capabilities__') } | Remove-Item
for ($i=0; $i -lt $capabilities.Count; $i++) { Set-Item "env:AccessControl__DevelopmentBootstrap__Capabilities__$i" $capabilities[$i] }
foreach ($kind in @('MemberWorkspace','NonMemberWorkspace')) {
    Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__Key" 'ai-workspace-foreign'
    Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__Name" 'AI Workspace Foreign'
    Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__LogoText" 'AI'
    Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__Locale" 'vi'
    Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__TimeZone" 'Asia/Saigon'
    Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__BaseCurrency" 'USD'
    Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__AvailableProductSpaces__0" 'crm'
    $modules = @('leads','contacts','customers','organizations','deals','tasks')
    for ($i=0; $i -lt $modules.Count; $i++) { Set-Item "env:Workspace__DevelopmentBootstrap__${kind}__EnabledModuleKeys__$i" $modules[$i] }
}
function Sql([string] $query) {
    $result = & sqlcmd -S $SqlServer -d $DatabaseName -b -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw 'SQL verification failed.' }; return ($result | Where-Object { $_.Trim() }) -join ''
}
function Stop-HostProcess {
    if ($script:hostProcess -and -not $script:hostProcess.HasExited) { Stop-Process -Id $script:hostProcess.Id; $script:hostProcess.WaitForExit() }
}
function Start-HostProcess {
    $id = [Guid]::NewGuid().ToString('N')
    $script:hostProcess = Start-Process dotnet -ArgumentList @($dll) -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runtime "$id.out") -RedirectStandardError (Join-Path $runtime "$id.err") -PassThru
    for ($i=0; $i -lt 120; $i++) {
        if ($script:hostProcess.HasExited) { throw "ApiHost exited; inspect private runtime $runtime" }
        try { if ((Invoke-WebRequest "$api/auth/session" -TimeoutSec 2 -SkipHttpErrorCheck).StatusCode -eq 401) { return } } catch {}
        Start-Sleep -Milliseconds 250
    }; throw 'ApiHost startup timed out.'
}
function Sign-In {
    $id = [Guid]::NewGuid().ToString('N')
    (Invoke-RestMethod "$api/auth/sessions" -Method Post -ContentType 'application/json' -Headers @{ 'Idempotency-Key'=$id; 'X-Request-Id'=$id; 'X-Correlation-Id'=$id } -Body (@{email=$email;password=$password;deviceLabel='AI restart verifier'} | ConvertTo-Json)).accessToken
}
$script:hostProcess = $null
try {
    & sqlcmd -S $SqlServer -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL THROW 51000, 'Test database already exists; refusing reuse', 1; CREATE DATABASE [$DatabaseName];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Fresh test database creation failed.' }
    Push-Location $root
    try {
        & dotnet $dll --migrate *> (Join-Path $runtime 'migrate.log'); if ($LASTEXITCODE -ne 0) { throw 'Migration failed.' }
        foreach ($key in @('ai-workspace-a','ai-workspace-b')) {
            $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Key = $key; $env:Workspace__DevelopmentBootstrap__MemberWorkspace__Name = $key; $env:AccessControl__DevelopmentBootstrap__WorkspaceKey = $key
            & dotnet $dll --seed-demo *> (Join-Path $runtime "$key.log"); if ($LASTEXITCODE -ne 0) { throw 'Seed failed.' }
        }
    } finally { Pop-Location }
    $workspace = (Sql "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='ai-workspace-a';").Trim()
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'false'; $env:Workspace__DevelopmentBootstrap__Enabled = 'false'; $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    Start-HostProcess
    $env:UNICORECRM_TEST_API_BASE_URL=$api; $env:UNICORECRM_TEST_EMAIL=$email; $env:UNICORECRM_TEST_PASSWORD=$password; $env:UNICORECRM_TEST_WORKSPACE_ID=$workspace; $env:UNICORECRM_TEST_WORKSPACE_KEY='ai-workspace-a'; $env:UNICORECRM_TEST_WORKSPACE_B_KEY='ai-workspace-b'; $env:UNICORECRM_TEST_DATABASE=$DatabaseName; $env:UNICORECRM_TEST_SQL_SERVER=$SqlServer; $env:UNICORECRM_AI_EVIDENCE=$evidence.FullName
    Push-Location $frontend
    try { & node node_modules/@playwright/test/cli.js test --config playwright.ai-workspace.config.ts 2>&1 | Tee-Object -FilePath (Join-Path $evidence 'browser.log'); if ($LASTEXITCODE -ne 0) { throw 'AI Workspace browser verification failed.' } } finally { Pop-Location }
    $records = Get-Content (Join-Path $evidence 'journey.json') -Raw | ConvertFrom-Json
    Stop-HostProcess; Start-HostProcess
    $headers = @{Authorization="Bearer $(Sign-In)"; 'X-Workspace-Id'=$workspace; 'X-Request-Id'=[Guid]::NewGuid().ToString('N'); 'X-Correlation-Id'=[Guid]::NewGuid().ToString('N')}
    $body = @{question='Summarize this Customer using available evidence.';locale='en';contextReferences=@(@{type='customer';id=$records.customerId})} | ConvertTo-Json -Depth 5
    $restored = Invoke-RestMethod "$api/ai/advisories" -Method Post -ContentType 'application/json' -Headers $headers -Body $body
    if ($restored.provider.name -ne 'OPENAI') { throw 'Active Workspace provider did not survive restart.' }
    Stop-HostProcess
    $goodRing = $env:AI__DataProtection__KeyRingPath; $env:AI__DataProtection__KeyRingPath = Join-Path $runtime 'wrong-keys'
    Start-HostProcess
    $failed = Invoke-WebRequest "$api/ai/advisories" -Method Post -ContentType 'application/json' -Headers $headers -Body $body -SkipHttpErrorCheck
    $failureText = if ($failed.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($failed.Content) } else { [string]$failed.Content }
    $failureBody = $failureText | ConvertFrom-Json
    $failureReason = @($failureBody.fieldErrors.provider)[0]
    if ($failed.StatusCode -ne 503 -or $failureReason -ne 'CREDENTIAL_UNREADABLE') { throw "Wrong key ring expected 503/CREDENTIAL_UNREADABLE; got $($failed.StatusCode)/$($failureBody.code)/$failureReason" }
    Stop-HostProcess; $env:AI__DataProtection__KeyRingPath=$goodRing; Start-HostProcess
    $restoredAgain = Invoke-RestMethod "$api/ai/advisories" -Method Post -ContentType 'application/json' -Headers $headers -Body $body
    if ($restoredAgain.provider.name -ne 'OPENAI') { throw 'Restoring key ring did not recover credential.' }
    $safeCounts = Sql "SELECT CONCAT('executions=',COUNT(*)) FROM platform_ai.AiExecutions WHERE WorkspaceId='$workspace';"
    $keyXml = Get-ChildItem -LiteralPath $goodRing -Filter 'key-*.xml' | Select-Object -First 1
    if ([OperatingSystem]::IsWindows() -and (Get-Content $keyXml.FullName -Raw) -notmatch 'encryptedSecret') { throw 'Configured key ring is not protected with DPAPI.' }
    @{status='PASS'; providerTransport='DETERMINISTIC'; realProvider='BLOCKED_NO_CREDENTIAL'; database=$DatabaseName; restart='PASS'; wrongKeyRing='PASS'; restoredKeyRing='PASS'; keyRingAtRest='DPAPI_CURRENT_WINDOWS_USER'; counts=$safeCounts; records=$records} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'connected-results.json')
    Write-Output "AI WORKSPACE CONNECTED PASS; deterministic transport; real-provider BLOCKED. Evidence: $evidence"
} finally { Stop-HostProcess }
