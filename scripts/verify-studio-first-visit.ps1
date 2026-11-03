[CmdletBinding()]
param([string]$SqlServer = 'localhost', [int]$Port = 5397)
$ErrorActionPreference = 'Stop'
$runId = [Guid]::NewGuid().ToString('N')
$startedAt = [DateTimeOffset]::UtcNow.ToString('O')
$database = "UnicoreCRM_StudioTest_$runId"
$repo = Split-Path $PSScriptRoot
$evidence = Join-Path $repo "artifacts/studio-first-visit/$runId"
[void](New-Item -ItemType Directory -Path $evidence -Force)
$connection = "Server=$SqlServer;Database=$database;Integrated Security=True;TrustServerCertificate=True"
function Sql([string]$query, [string]$db = $database) {
    $c = [System.Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=$db;Integrated Security=True;TrustServerCertificate=True")
    $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $query; $cmd.CommandTimeout = 120
        $table = [System.Data.DataTable]::new(); $table.Load($cmd.ExecuteReader()); return ,$table
    } finally { $c.Dispose() }
}
$results = [Collections.Generic.List[object]]::new()
$http = [Collections.Generic.List[object]]::new()
$token = $null; $workspace = $null; $counter = 0
function Api([string]$method, [string]$path, $body = $null, [string]$key = '', [Nullable[int]]$version = $null) {
    $script:counter++
    $correlation = "activity-$runId-$script:counter"
    $headers = @{ 'X-Request-Id' = $correlation; 'X-Correlation-Id' = $correlation }
    if ($script:token) { $headers.Authorization = "Bearer $script:token" }
    if ($script:workspace) { $headers['X-Workspace-Id'] = $script:workspace }
    if ($key) { $headers['Idempotency-Key'] = $key }
    if ($null -ne $version) { $headers['If-Match'] = '"' + $version + '"' }
    $args = @{ Method = $method; Uri = "http://127.0.0.1:$Port$path"; Headers = $headers; SkipHttpErrorCheck = $true }
    if ($null -ne $body) { $args.Body = $body | ConvertTo-Json -Depth 12 -Compress; $args.ContentType = 'application/json' }
    $response = Invoke-WebRequest @args
    $value = if ($response.Content) { $response.Content | ConvertFrom-Json } else { $null }
    # Authentication payloads and free-form content never enter evidence.
    $http.Add(@{ method = $method; path = $path; status = [int]$response.StatusCode; correlationId = $correlation;
        code = $value.code; itemCount = @($value.items).Count; pageInfo = $value.pageInfo })
    return @{ Status = [int]$response.StatusCode; Body = $value }
}
function Check([string]$name, $expected, $actual) {
    $pass = "$expected" -ceq "$actual"
    $results.Add(@{ test = $name; expected = $expected; actual = $actual; status = $(if ($pass) {'PASS'} else {'FAIL'}) })
    Write-Host "$(if ($pass) {'PASS'} else {'FAIL'}) $name expected=$expected actual=$actual"
}

function Start-StudioHost {
    $hostExe = Join-Path $repo 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.exe'
    Start-Process -FilePath $hostExe -WorkingDirectory (Join-Path $repo 'src/UnicoreCRM.ApiHost') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'host.log') -RedirectStandardError (Join-Path $evidence 'host-error.log')
}
function Wait-StudioHost {
    for ($i=0; $i -lt 60; $i++) { try { $null=Api GET '/auth/session'; return } catch { Start-Sleep -Milliseconds 500 } }
    throw 'Isolated ApiHost readiness timeout'
}
$process = $null
$oldEnvironment = @{}
try {
    # Always allocate a fresh name; never drop or reuse an existing database.
    $null = Sql "IF DB_ID('$database') IS NOT NULL THROW 50000,'Test database already exists',1; CREATE DATABASE [$database];" 'master'
    $settings = @{
        ASPNETCORE_ENVIRONMENT='Development'; ASPNETCORE_URLS="http://127.0.0.1:$Port";
        ConnectionStrings__UnicoreCRM=$connection;
        DevelopmentDemoBootstrap__Email="activity-$runId@example.test";
        DevelopmentDemoBootstrap__Password="Test!$([Guid]::NewGuid().ToString('N'))aA9";
        IdentityAuth__EmailVerification__Sender__Kind='DevelopmentLog'
        AI__DataProtection__KeyRingPath=(Join-Path $evidence 'keys')
        Frontend__AllowedOrigins__0='http://127.0.0.1:3039'
    }
    foreach ($setting in $settings.GetEnumerator()) {
        $oldEnvironment[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key)
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
    $project = Join-Path $repo 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
    & dotnet run --no-launch-profile --project $project -- --migrate *> (Join-Path $evidence 'migration.log')
    if ($LASTEXITCODE) { throw 'Migration failed; inspect local migration.log' }
    & dotnet run --no-build --no-launch-profile --project $project -- --seed-demo *> (Join-Path $evidence 'bootstrap.log')
    if ($LASTEXITCODE) { throw 'Bootstrap failed; inspect local bootstrap.log' }
    $process = Start-StudioHost
    $ready = $false
    for ($i=0; $i -lt 90; $i++) {
        try { $null = Api GET '/auth/session'; $ready=$true; break } catch { Start-Sleep -Seconds 1 }
        if ($process.HasExited) { throw 'ApiHost exited' }
    }
    if (!$ready) { throw 'ApiHost readiness timeout' }
    $login = Api POST '/auth/sessions' @{email=$settings.DevelopmentDemoBootstrap__Email;password=$settings.DevelopmentDemoBootstrap__Password} "login-$runId"
    if ($login.Status -ne 200) { throw "Login failed: $($login.Status)" }
    $script:token = $login.Body.accessToken
    $session = Api GET '/auth/session'
    $member = $session.Body.principal.memberId
    $script:workspace = (Sql "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo'").Rows[0].WorkspaceId
    $foreign = (Sql "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='unicore-demo-isolated'").Rows[0].WorkspaceId
    $role = (Sql "SELECT TOP 1 RoleId FROM access.Roles WHERE WorkspaceId='$workspace'").Rows[0].RoleId


    # Authentication fixture only; Workspace/Studio records must be created by the real provisioning endpoint.
    $adminAccount=$session.Body.principal.accountId
    foreach($suffix in @('first','second')) {
        $newEmail="studio-$suffix-$runId@example.test"
        $null=Sql @"
SELECT * INTO #studio_account FROM iam.Accounts WHERE AccountId='$adminAccount';
UPDATE #studio_account SET AccountId='acc_studio_$suffix',MemberId='mem_studio_$suffix',Email='$newEmail',NormalizedEmail=UPPER('$newEmail'),DisplayName='Studio isolated $suffix';
INSERT INTO iam.Accounts SELECT * FROM #studio_account;
INSERT INTO iam.Credentials (AccountId,PasswordHash,UpdatedAt) SELECT 'acc_studio_$suffix',PasswordHash,SYSUTCDATETIME() FROM iam.Credentials WHERE AccountId='$adminAccount';
DROP TABLE #studio_account;
"@
        $script:workspace=$null
        $auth=Api POST '/auth/sessions' @{email=$newEmail;password=$settings.DevelopmentDemoBootstrap__Password} "studio-login-$suffix-$runId"
        Check "test $suffix identity authenticates" 200 $auth.Status
        $script:token=$auth.Body.accessToken
        $created=Api POST '/workspaces/initial-provisioning' @{} "studio-provision-$suffix-$runId"
        Check "real $suffix default Workspace provisioning" 201 $created.Status
        if($suffix -eq 'first'){$firstWorkspace=$created.Body.workspaceId;$firstKey=$created.Body.workspace.workspaceKey;$firstToken=$script:token;$firstEmail=$newEmail}
        else{$foreign=$created.Body.workspaceId;$secondKey=$created.Body.workspace.workspaceKey}
    }
    $script:workspace=$firstWorkspace;$script:token=$firstToken
    $session=Api GET '/auth/session';$member=$session.Body.principal.memberId
    $role=(Sql "SELECT TOP 1 RoleId FROM access.Roles WHERE WorkspaceId='$workspace'").Rows[0].RoleId
    $initial=Sql "SELECT b.WorkspaceId,b.BaseCurrency,JSON_VALUE(c.LocaleRegionJson,'$.currencies.baseCurrency') AS StudioCurrency FROM workspace.BootstrapProjections b JOIN workspace.StudioConfigurations c ON c.WorkspaceId=b.WorkspaceId"
    @($initial.Rows | ForEach-Object {@{workspaceId=$_.WorkspaceId;bootstrapCurrency=$_.BaseCurrency;studioCurrency=$_.StudioCurrency}}) | ConvertTo-Json | Set-Content (Join-Path $evidence 'initial-sql-currency.json')
    Check 'new SQL Workspace bootstrap base currency' 'USD' ($initial.Rows | Where-Object WorkspaceId -eq $workspace).BaseCurrency
    Check 'Studio API base currency matches bootstrap API' (Api GET "/workspaces/$workspace/bootstrap").Body.configuration.baseCurrency (Api GET '/workspace-configuration').Body.localeRegion.currencies.baseCurrency
    # Isolated fixture only: same authenticated member can visit a second workspace with read-only Studio permission.
    $account=$session.Body.principal.accountId
    $null=Sql @"
INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('wsm_studio_second','$foreign','$account','$member','Active',SYSUTCDATETIME());
INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,IsActive,Version,CreatedAt,UpdatedAt) VALUES ('role_studio_second','$foreign','Studio second','STUDIO SECOND',1,0,SYSUTCDATETIME(),SYSUTCDATETIME());
INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES ('assignment_studio_second','$foreign','wsm_studio_second','role_studio_second',SYSUTCDATETIME());
INSERT INTO access.RoleCapabilities (RoleId,Capability) SELECT 'role_studio_second',Capability FROM access.RoleCapabilities WHERE RoleId='$role' AND Capability<>'studio.configure';
UPDATE workspace.BootstrapProjections SET AvailableProductSpacesJson='["crm","studio","people"]' WHERE WorkspaceId='$foreign';
"@

    function ProductDraft([string]$currency,[string]$sku) { return @{sku=$sku;name="Studio audit $currency";type='physical_product';status='ACTIVE';category='Audit';unit='item';unitPrice=@{amount='10';currency=$currency};taxRate='0';taxMode='none';billingCycle='one_time';isSubscription=$false;isRenewable=$false;tags=@()} }
    $usdProduct=Api POST '/products' (ProductDraft 'USD' "studio-usd-$runId") "product-usd-$runId"
    Check 'Product accepts initial effective USD base currency' 201 $usdProduct.Status
    $frontend=Join-Path (Split-Path $repo) 'frontend/unicorecrm-web'
    $browserSettings=@{UNICORECRM_TEST_API_BASE_URL="http://127.0.0.1:$Port";UNICORECRM_TEST_EMAIL=$firstEmail;UNICORECRM_TEST_PASSWORD=$settings.DevelopmentDemoBootstrap__Password;UNICORECRM_TEST_WORKSPACE_ID=$workspace;UNICORECRM_TEST_WORKSPACE_KEY=$firstKey;UNICORECRM_TEST_SECOND_KEY=$secondKey;UNICORECRM_TEST_CONTROL_DIR=$evidence}
    foreach($setting in $browserSettings.GetEnumerator()) { $oldEnvironment[$setting.Key]=[Environment]::GetEnvironmentVariable($setting.Key);[Environment]::SetEnvironmentVariable($setting.Key,$setting.Value) }
    $browserJob=Start-Process pwsh -WorkingDirectory $frontend -ArgumentList @('-NoProfile','-Command','npx playwright test --config playwright.studio-connected.config.ts') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'browser.log') -RedirectStandardError (Join-Path $evidence 'browser-error.log')
    $deadline=[DateTimeOffset]::UtcNow.AddMinutes(5)
    while(!$browserJob.HasExited) {
        if([DateTimeOffset]::UtcNow -gt $deadline){throw 'Browser acceptance exceeded five minutes'}
        foreach($action in @('restart','read-only','configure','stop')) {
            if((Test-Path (Join-Path $evidence "$action.request")) -and !(Test-Path (Join-Path $evidence "$action.done"))) {
                switch($action) {
                    'restart' { Stop-Process -Id $process.Id; $process=Start-StudioHost; Wait-StudioHost }
                    'read-only' { Check 'second Workspace unvisited before read-only UI' 'NOT_STARTED' (Sql "SELECT Status FROM workspace.StudioQuickSetups WHERE WorkspaceId='$foreign'").Rows[0].Status }
                    'configure' { Check 'read-only visit did not open SQL Quick Setup' 'NOT_STARTED' (Sql "SELECT Status FROM workspace.StudioQuickSetups WHERE WorkspaceId='$foreign'").Rows[0].Status; $null=Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('role_studio_second','studio.configure')" }
                    'stop' { Stop-Process -Id $process.Id }
                }
                Set-Content (Join-Path $evidence "$action.done") 'done'
            }
        }
        Start-Sleep -Milliseconds 200
        $browserJob.Refresh()
    }
    $browserEvidence=Join-Path $evidence 'browser'
    [void](New-Item -ItemType Directory -Path $browserEvidence -Force)
    Get-ChildItem (Join-Path $frontend 'test-results/studio-connected') -Recurse -File | Where-Object Extension -In @('.json','.png') | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $browserEvidence $_.Name)}
    Check 'real Studio UI acceptance' 0 $browserJob.ExitCode
    $rows=Sql "SELECT q.WorkspaceId,q.Status,q.CompletedStepIdsJson,q.SkippedStepIdsJson,q.AutoOpenDismissedAt,q.LastOpenedAt,b.BaseCurrency,JSON_VALUE(c.LocaleRegionJson,'$.currencies.baseCurrency') AS StudioCurrency FROM workspace.StudioQuickSetups q JOIN workspace.BootstrapProjections b ON b.WorkspaceId=q.WorkspaceId JOIN workspace.StudioConfigurations c ON c.WorkspaceId=q.WorkspaceId"
    @($rows.Rows | ForEach-Object {@{workspaceId=$_.WorkspaceId;status=$_.Status;completed=$_.CompletedStepIdsJson;skipped=$_.SkippedStepIdsJson;dismissed="$($_.AutoOpenDismissedAt)";opened="$($_.LastOpenedAt)";bootstrapCurrency=$_.BaseCurrency;studioCurrency=$_.StudioCurrency}}) | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'final-sql-state.json')
    if(!$process -or $process.HasExited){$process=Start-StudioHost;Wait-StudioHost}
    Check 'persisted Studio/Bootstrap currency agrees after restart' (Api GET "/workspaces/$workspace/bootstrap").Body.configuration.baseCurrency (Api GET '/workspace-configuration').Body.localeRegion.currencies.baseCurrency
    if($browserJob.ExitCode -eq 0) {
        Check 'currency update persisted in SQL bootstrap' 'EUR' ($rows.Rows | Where-Object WorkspaceId -eq $workspace).BaseCurrency
        Check 'currency update persisted in SQL Studio' 'EUR' ($rows.Rows | Where-Object WorkspaceId -eq $workspace).StudioCurrency
        Check 'three skipped steps SQL completed' 'COMPLETED' ($rows.Rows | Where-Object WorkspaceId -eq $workspace).Status
        Check 'other Workspace did not inherit base currency' 'USD' ($rows.Rows | Where-Object WorkspaceId -eq $foreign).BaseCurrency

        $reject=Api POST '/products' (ProductDraft 'USD' "studio-old-$runId") "product-reject-$runId"
        Check 'Product rejects previous USD default after EUR Studio update' 422 $reject.Status
        Check 'Product creates with authoritative new EUR currency' 201 (Api POST '/products' (ProductDraft 'EUR' "studio-eur-$runId") "product-vnd-$runId").Status
        $products=Sql "SELECT NormalizedSku,JSON_VALUE(Profile,'$.unitPrice.amount') AS Amount,JSON_VALUE(Profile,'$.unitPrice.currency') AS Currency FROM products.Products WHERE WorkspaceId='$workspace'"
        @($products.Rows | ForEach-Object {@{sku=$_.NormalizedSku;amount=$_.Amount;currency=$_.Currency}}) | ConvertTo-Json | Set-Content (Join-Path $evidence 'product-currency-sql.json')
        Check 'existing Product USD amount remains unchanged' '10' ($products.Rows | Where-Object NormalizedSku -eq "STUDIO-USD-$($runId.ToUpperInvariant())").Amount
        Check 'existing Product retains transaction USD currency' 'USD' ($products.Rows | Where-Object NormalizedSku -eq "STUDIO-USD-$($runId.ToUpperInvariant())").Currency
        # Exercise the existing completion endpoint independently from the all-skip UI path.
        $script:workspace=$foreign
        foreach($step in @('business-profile','locale-currency','workspace-blueprint')) {
            $q=(Api GET '/studio/quick-setup').Body
            Check "complete $step HTTP" 200 (Api POST "/studio/quick-setup/steps/$step/complete" @{} "complete-$step" $q.revision).Status
        }
        Check 'all completed steps API persisted' 'COMPLETED' (Api GET '/studio/quick-setup').Body.status
        $script:workspace=$workspace
    }
} finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id }
    if ($browserJob -and !$browserJob.HasExited) { Stop-Process -Id $browserJob.Id }
    foreach ($setting in $oldEnvironment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value) }
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'results.json')
    $http | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'http.json')
    @{runId=$runId;startedAtUtc=$startedAt;finishedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');backendCommit=(& git -C $repo rev-parse HEAD);frontendCommit=(& git -C (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web') rev-parse HEAD);database=$database;sqlServer=$SqlServer} | ConvertTo-Json | Set-Content (Join-Path $evidence 'run.json')
    Write-Host "Evidence: $evidence; isolated database retained: $database"
}
if (@($results | Where-Object status -eq FAIL).Count) { exit 1 }
