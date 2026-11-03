#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$VerifyOnly,
    [switch]$UiAcceptance,
    [string]$FrontendDirectory=(Join-Path (Split-Path $PSScriptRoot) '../frontend/unicorecrm-web'),
    [switch]$ConfirmedSyntheticDevelopmentDatabase,
    [string]$ExpectedServer,
    [string]$ExpectedDatabase,
    [int]$RandomSeed=20261022,
    [DateTimeOffset]$AsOf=[DateTimeOffset]::UtcNow,
    [string]$StateDirectory,
    [ValidateRange(0,2000)][int]$StopAfterCommit=0
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
if(!$StateDirectory){$StateDirectory=Join-Path $repo 'artifacts/realistic-demo/realistic-v1'}
$script:stateDirectory=[IO.Path]::GetFullPath($StateDirectory)
$script:manifestPath=Join-Path $stateDirectory 'manifest.json'
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
. (Join-Path $PSScriptRoot 'demo/RealisticDataset.ps1')
. (Join-Path $PSScriptRoot 'demo/VerifyDemo.ps1')
$script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
$script:httpEvidence=[Collections.Generic.List[object]]::new()
$script:token=$null;$script:workspace=$null;$script:hostProcess=$null;$script:committedThisRun=0
$originalEnvironment=@{};$lock=$null
$startedAt=[DateTimeOffset]::UtcNow
try {
    Assert-Demo (!$VerifyOnly -or $Apply) 'INVALID_OPTIONS' 'VerifyOnly requires explicit Apply and the same safety checks'
    Assert-Demo ($env:ASPNETCORE_ENVIRONMENT -in $null,'','Development') 'BLOCKED_ENVIRONMENT' 'Ambient ASPNETCORE_ENVIRONMENT is not Development'
    Assert-Demo ($env:DOTNET_ENVIRONMENT -in $null,'','Development') 'BLOCKED_ENVIRONMENT' 'Ambient DOTNET_ENVIRONMENT is not Development'
    if(!$Apply) {
        $data=New-RealisticDataset $RandomSeed $AsOf
        @{mode='DRY_RUN';dataset='realistic-v1';type='SYNTHETIC_DEMO';randomSeed=$RandomSeed;asOf=$AsOf.ToString('O');
          target=@{Lead=150;Contact=100;Organization=35;Customer=70;Deal=180;Task=160;Activity=300;Product=30;Support=25};
          stages=@{DISCOVERY=50;QUALIFIED=35;SOLUTION=30;PROPOSAL=25;NEGOTIATION=25;LOST=15;WON=0};
          blocked=@('Quote/Order/Won write evidence','Deal Activity recordRef','Historical audit timestamp','Task IN_PROGRESS');
          sample=@{organization=$data.organizations[0];contact=$data.contacts[0];lead=$data.leads[0];product=$data.products[0]}}|ConvertTo-Json -Depth 10
        return
    }
    Assert-Demo $ConfirmedSyntheticDevelopmentDatabase.IsPresent 'BLOCKED_ENVIRONMENT' 'Explicit confirmation that this database contains no real customer data is required'
    Assert-Demo ($ExpectedServer -and $ExpectedDatabase -match 'Development$') 'BLOCKED_ENVIRONMENT' 'Exact expected local server and Development database are required'
    $configuration=Read-DemoConfiguration
    $script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
    $builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
    Assert-Demo ($builder.DataSource -ceq $ExpectedServer -and $builder.InitialCatalog -ceq $ExpectedDatabase) 'BLOCKED_ENVIRONMENT' 'Configured database differs from the explicit expected identity'
    $identity=(Invoke-DemoSql "SELECT DB_NAME() DbName,CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) MachineName").Rows[0]
    Assert-Demo ($identity.MachineName -ieq $env:COMPUTERNAME -and $identity.DbName -ceq $ExpectedDatabase) 'BLOCKED_ENVIRONMENT' 'SQL must be on this machine and use the expected Development catalog'
    [void](New-Item -ItemType Directory -Path $stateDirectory -Force)
    $lock=[IO.File]::Open((Join-Path $stateDirectory 'seed.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    if(Test-Path -LiteralPath $manifestPath) {
        $script:manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json -AsHashtable
        Assert-Demo ($manifest.dataset -eq 'realistic-v1' -and $manifest.server -ceq $ExpectedServer -and $manifest.database -ceq $ExpectedDatabase -and $manifest.randomSeed -eq $RandomSeed) 'MANIFEST_MISMATCH' 'Manifest identity/seed must match this run'
        if($PSBoundParameters.ContainsKey('AsOf')){Assert-Demo ($AsOf -eq [DateTimeOffset]::Parse($manifest.asOf)) 'MANIFEST_MISMATCH' 'AsOf is pinned by the first run'}
        $AsOf=[DateTimeOffset]::Parse($manifest.asOf)
    } else {
        Assert-Demo (!$VerifyOnly) 'MANIFEST_REQUIRED' 'VerifyOnly cannot create a manifest'
        $existing=Invoke-DemoSql "SELECT [Key] FROM workspace.Workspaces WHERE [Key] IN ('unicore-demo-anh-duong','unicore-demo-anh-duong-control')"
        Assert-Demo ($existing.Rows.Count -eq 0) 'WORKSPACE_COLLISION' 'Existing workspace keys cannot be adopted without original provenance manifest'
        $script:manifest=[ordered]@{dataset='realistic-v1';type='SYNTHETIC_DEMO';server=$ExpectedServer;database=$ExpectedDatabase;randomSeed=$RandomSeed;asOf=$AsOf.ToString('O');
          startedAt=$startedAt.ToString('O');workspaceKey='unicore-demo-anh-duong';controlKey='unicore-demo-anh-duong-control';
          email='demo.anhduong@example.com';workspaceId=$null;controlId=$null;commands=[ordered]@{};before=(Get-DemoCounts);runs=@()}
        Save-DemoManifest
    }
    foreach($target in @(@{key=$manifest.workspaceKey;id=$manifest.workspaceId},@{key=$manifest.controlKey;id=$manifest.controlId})) {
        if($target.id) {
            $pinned=Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$target.key}
            Assert-Demo ($pinned.Rows.Count -eq 1 -and $pinned.Rows[0].WorkspaceId -ceq $target.id) 'WORKSPACE_IDENTITY_CHANGED' 'Pinned workspace key/ID must match before bootstrap or Studio initialization'
        }
    }
    $script:password=$configuration['UNICORE_DEV_SEED_PASSWORD']
    if(!$password){$script:password=$configuration['DevelopmentDemoBootstrap:Password']}
    Assert-Demo ($password -and $password.Length -ge 8) 'BLOCKED_ENVIRONMENT' 'Configure a Development bootstrap password outside source; no credential is generated or saved by this script'
    function Set-DemoEnvironment([string]$Name,[string]$Value) {
        if(!$originalEnvironment.ContainsKey($Name)){$originalEnvironment[$Name]=[Environment]::GetEnvironmentVariable($Name)}
        [Environment]::SetEnvironmentVariable($Name,$Value)
    }
    Set-DemoEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-DemoEnvironment 'DOTNET_ENVIRONMENT' 'Development'
    Set-DemoEnvironment 'IdentityAuth__EmailVerification__Sender__Kind' 'DevelopmentLog'
    Set-DemoEnvironment 'UNICORE_DEV_SEED_ENABLED' 'true'
    Set-DemoEnvironment 'UNICORE_DEV_SEED_EMAIL' $manifest.email
    Set-DemoEnvironment 'UNICORE_DEV_SEED_PASSWORD' $password
    Set-DemoEnvironment 'DevelopmentDemoBootstrap__Email' $manifest.email
    Set-DemoEnvironment 'DevelopmentDemoBootstrap__DisplayName' 'Nguyễn Minh An — Ánh Dương Demo'
    Set-DemoEnvironment 'DevelopmentDemoBootstrap__RoleName' 'Synthetic Demo Operator'
    # Refuse conflicting direct owner bootstrap settings instead of writing to another tenant.
    foreach($key in $configuration.Keys | Where-Object {$_ -match '^(IdentityAuth|Workspace|AccessControl):DevelopmentBootstrap:'}) {
        Assert-Demo (!$configuration[$key]) 'BLOCKED_ENVIRONMENT' "Remove conflicting direct owner bootstrap configuration: $key"
    }
    Set-DemoEnvironment 'Workspace__DevelopmentBootstrap__EnsureStudioConfiguration' 'true'
    $capabilities=@((Get-Content (Join-Path $hostDirectory 'appsettings.Development.json') -Raw|ConvertFrom-Json).DevelopmentDemoBootstrap.Capabilities)+@('customers.onboard_existing','customers.edit')
    for($i=0;$i -lt $capabilities.Count;$i++){Set-DemoEnvironment "DevelopmentDemoBootstrap__Capabilities__$i" $capabilities[$i]}
    & dotnet build (Join-Path $hostDirectory 'UnicoreCRM.ApiHost.csproj') --no-restore *> (Join-Path $stateDirectory 'build.log')
    Assert-Demo ($LASTEXITCODE -eq 0) 'BUILD_FAILED' 'Inspect local build.log'
    if(!$VerifyOnly) {
        # Both calls use the existing canonical bootstrap. Neither writes an existing workspace.
        foreach($key in @($manifest.workspaceKey,$manifest.controlKey)) {
            $other=if($key -eq $manifest.workspaceKey){$manifest.controlKey}else{$manifest.workspaceKey}
            foreach($prefix in @('Workspace','IsolationWorkspace')) {
                $target=if($prefix -eq 'Workspace'){$key}else{$other}
                $name=if($target -eq $manifest.workspaceKey){'UniCoreCRM Demo - Công ty Ánh Dương'}else{'UniCoreCRM Demo - Kiểm tra cô lập'}
                foreach($entry in @{Key=$target;Name=$name;LogoText='AD';Locale='vi';TimeZone='Asia/Ho_Chi_Minh';BaseCurrency='VND'}.GetEnumerator()) {
                    Set-DemoEnvironment "DevelopmentDemoBootstrap__${prefix}__$($entry.Key)" $entry.Value
                }
            }
            & (Join-Path $hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') --seed-demo --contentRoot $hostDirectory *> (Join-Path $stateDirectory "bootstrap-$key.log")
            Assert-Demo ($LASTEXITCODE -eq 0) 'BOOTSTRAP_FAILED' "Inspect local bootstrap-$key.log"
        }
    }
    Start-DemoHost
    Connect-DemoIdentity
    $data=New-RealisticDataset $RandomSeed $AsOf
    if(!$VerifyOnly){
        $script:workspace=$manifest.controlId
        $controlConfiguration=Invoke-DemoApi GET '/workspace-configuration'
        Assert-Demo ($controlConfiguration.status -eq 200) 'WORKSPACE_CONFIGURATION_MISSING' 'Canonical bootstrap must provision Studio configuration'
        $features=@{};foreach($module in @('leads','customers','contacts','deals','quotes','orders','support','organizations','tasks','payments','invoices','shipping','returns')){$features[$module]=$true}
        $null=Invoke-DemoCommand 'control-features' '/workspace-configuration/features' @{features=$features} $controlConfiguration.body.revision 'PATCH'
        $script:workspace=$manifest.workspaceId
        $products=@(for($i=0;$i -lt 30;$i++){(Invoke-DemoCommand "product-$i" '/products' $data.products[$i]).aggregateId})
        $organizations=@(for($i=0;$i -lt 35;$i++){(Invoke-DemoCommand "organization-$i" '/organizations' $data.organizations[$i]).aggregateId})
        $contacts=@(for($i=0;$i -lt 100;$i++){
            $body=$data.contacts[$i];$body.ownerId=$member
            $created=Invoke-DemoCommand "contact-$i" '/contacts' $body
            $null=Invoke-DemoCommand "affiliation-$i" "/contacts/$($created.aggregateId)/organization-relationships" @{organizationId=$organizations[$i % 35];role='employee';isPrimaryAffiliation=$true;effectiveFrom=$AsOf.AddDays(-90).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')} $created.version
            $created.aggregateId
        })
        $leads=@(for($i=0;$i -lt 150;$i++){
            $body=$data.leads[$i];$body.ownerId=$member
            $created=Invoke-DemoCommand "lead-$i" '/leads' $body
            if($i -lt 100){
                $advanced=Invoke-DemoCommand "contacting-$i" "/leads/$($created.aggregateId)/advance-work-state" @{targetWorkState='CONTACTING'} $created.version
                if($i -lt 50){$null=Invoke-DemoCommand "verifying-$i" "/leads/$($created.aggregateId)/advance-work-state" @{targetWorkState='VERIFYING'} $advanced.version}
            }
            $created.aggregateId
        })
        # Two independent real workflows: one opportunity qualification and one customer conversion.
        $qualified=Invoke-DemoCommand 'qualification-0' "/workflows/lead-qualification/$($leads[0])/opportunity" @{
            relationship=@{kind='CONTACT';mode='EXISTING';selectedId=$contacts[0];contact=@{displayName=$data.contacts[0].fullName}};
            deal=@{name="$($data.services[0]) cho $($data.organizations[0].displayName)";ownerId=$member;interestedProductIds=@($products[0]);
              expectedCloseDate=$AsOf.AddDays(14).ToString('yyyy-MM-dd');estimatedValue=@{amount='55000000';currency='VND'};needSummary=$data.contacts[0].needSummary}
        } $manifest.commands['verifying-0'].response.version
        $converted=Invoke-DemoCommand 'conversion-1' "/workflows/lead-customer-conversion/$($leads[1])" @{
            accountSubject=@{type='ORGANIZATION_ACCOUNT';mode='EXISTING';id=$organizations[1]}
        } $manifest.commands['verifying-1'].response.version
        # 35 corporate + 35 individual subjects; one corporate Customer comes from the workflow.
        for($i=0;$i -lt 70;$i++) {
            if($i -eq 1){continue}
            $ref=if($i -lt 35){@{type='ORGANIZATION_ACCOUNT';id=$organizations[$i]}}else{@{type='CONTACT';id=$contacts[$i-35]}}
            $null=Invoke-DemoCommand "customer-$i" '/customers' @{relationshipRef=$ref;segment=$data.sectors[$i % 5];tier=@('STANDARD','SILVER','GOLD')[$i % 3];serviceLevel=@('STANDARD','PRIORITY')[$i % 2];tags=@('SYNTHETIC_DEMO')}
        }
        for($i=40;$i -lt 70;$i++) {
            $createdCustomer=$manifest.commands["customer-$i"].response
            $active=Invoke-DemoCommand "customer-active-$i" "/customers/$($createdCustomer.aggregateId)" @{status='ACTIVE'} $createdCustomer.version 'PATCH'
            if($i -ge 60){$null=Invoke-DemoCommand "customer-inactive-$i" "/customers/$($createdCustomer.aggregateId)" @{status='INACTIVE'} $active.version 'PATCH'}
        }
        $deals=@($qualified.result.dealId)
        for($i=1;$i -lt 180;$i++) {
            $stage=if($i -lt 50){'DISCOVERY'}elseif($i -lt 85){'QUALIFIED'}elseif($i -lt 115){'SOLUTION'}elseif($i -lt 140){'PROPOSAL'}else{'NEGOTIATION'}
            $buyer=if($i % 3 -eq 0){@{type='CONTACT';id=$contacts[$i % 100]}}else{@{type='ORGANIZATION_ACCOUNT';id=$organizations[$i % 35]}}
            $organizationIndex=if($i % 3 -eq 0){($i % 100) % 35}else{$i % 35}
            $body=@{name="$($data.services[[int][Math]::Floor($i / 35)]) cho $($data.organizations[$organizationIndex].displayName)";
              buyerRef=$buyer;stageCode=$stage;amount=@{amount=([string](5000000 + (($i * 7919) % 1500) * 1000000));currency='VND'};
              opportunityScore=([string](10 + ($i * 7) % 86));ownerId=$member;expectedCloseDate=$AsOf.AddDays(($i % 75)-20).ToString('yyyy-MM-dd');
              interestedProductIds=@($products[$i % 30]);lineItems=@();forecastCategory=@('PIPELINE','BEST_CASE','COMMIT')[$i % 3];
              notes="Cần $($data.needs[$organizationIndex % 5]). Dự kiến triển khai cho $($data.organizations[$organizationIndex].employeeCount) nhân sự; đánh giá phương án theo ngân sách."}
            if($i % 3 -ne 0){$body.nextActionAt=$AsOf.AddDays(($i % 14)-4).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ');$body.nextActionSummary=$data.actions[$i % 6]}
            $created=Invoke-DemoCommand "deal-$i" '/deals' $body
            $deals+=,$created.aggregateId
            if($i -ge 165){$null=Invoke-DemoCommand "lost-$i" "/deals/$($created.aggregateId)/mark-lost" @{reason='Khách hàng tạm hoãn ngân sách triển khai';recycleDecision='DO_NOT_RECYCLE';note='Giữ lại nhu cầu để đánh giá trong kỳ kế hoạch tiếp theo.'} $created.version}
        }
        for($i=0;$i -lt 160;$i++) {
            $organizationIndex=if($i % 3 -eq 0){($i % 100) % 35}else{$i % 35}
            $dealId=$deals[$i]
            $created=Invoke-DemoCommand "task-$i" '/tasks' @{title="$($data.actions[$i % 6]) — $($data.organizations[$organizationIndex].displayName)";
              assigneeId=$member;dueAt=$AsOf.AddDays(($i % 28)-10).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ');priority=@('LOW','NORMAL','HIGH','URGENT')[$i % 4];
              description="Chuẩn bị phương án $($data.needs[$i % 5]); ghi nhận yêu cầu trước buổi trao đổi. Nội dung mô phỏng.";
              recordRef=@{moduleKey='deals';recordId=$dealId};dedupeKey="realistic-v1-task-$i"}
            if($i % 4 -eq 0){$null=Invoke-DemoCommand "complete-$i" "/tasks/$($created.aggregateId)/complete" @{outcome='Đã xác nhận yêu cầu và cập nhật kế hoạch tư vấn.'} $created.version}
        }
        for($i=0;$i -lt 300;$i++) {
            $contactIndex=$i % 100
            $null=Invoke-DemoCommand "activity-$i" '/activities' @{type=@('NOTE','CALL','MEETING')[$i % 3];
              subject="$($data.actions[$i % 6]) tại $($data.organizations[$contactIndex % 35].displayName)";
              body="Ghi nhận thủ công: $($data.contacts[$contactIndex].fullName) đề nghị $($data.needs[$i % 5]). Phương án trao đổi số $([int][Math]::Floor($i / 100)+1): thống nhất phạm vi, lịch đào tạo và tiêu chí nghiệm thu. Nội dung tổng hợp.";
              relationshipRef=@{type='CONTACT';id=$contacts[$contactIndex]};recordRef=@{moduleKey='contacts';recordId=$contacts[$contactIndex]}}
        }
        for($i=0;$i -lt 25;$i++) {
            $support=Invoke-DemoCommand "support-$i" '/support/cases' @{title="Hỗ trợ $($data.needs[$i % 5]) — $($data.organizations[$i].displayName)";
              description="Đề nghị hướng dẫn cấu hình và đào tạo bổ sung cho $($data.organizations[$i].employeeCount) nhân sự. Dữ liệu mô phỏng, chưa có đơn hàng hoặc thanh toán.";
              priority=@('low','medium','high')[$i % 3];category=@('request','consultation','onboarding','usage_issue','follow_up')[$i % 5];source='manual';channel='internal';
              relationshipRef=@{type='ORGANIZATION_ACCOUNT';id=$organizations[$i]};ownerId=$member;tags=@('SYNTHETIC_DEMO');nextFollowUpAt=$AsOf.AddDays(($i % 7)+1).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')}
            if($i % 5 -eq 2){$null=Invoke-DemoCommand "support-waiting-$i" "/support/cases/$($support.aggregateId)/transition" @{nextStatus='waiting_customer'} $support.version}
            elseif($i % 5 -gt 0){
                $progress=Invoke-DemoCommand "support-progress-$i" "/support/cases/$($support.aggregateId)/transition" @{nextStatus='in_progress'} $support.version
                if($i % 5 -ge 3){
                    $resolved=Invoke-DemoCommand "support-resolved-$i" "/support/cases/$($support.aggregateId)/transition" @{nextStatus='resolved';resolutionSummary='Đã hướng dẫn cấu hình và xác nhận phương án đào tạo nội bộ.'} $progress.version
                    if($i % 5 -eq 4){$null=Invoke-DemoCommand "support-closed-$i" "/support/cases/$($support.aggregateId)/transition" @{nextStatus='closed'} $resolved.version}
                }
            }
        }
    }
    Invoke-DemoVerification
    if($UiAcceptance) {
        Set-DemoEnvironment 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3039'
        Stop-DemoHost;Start-DemoHost
        Set-DemoEnvironment 'UNICORECRM_TEST_API_BASE_URL' 'http://127.0.0.1:5098'
        Set-DemoEnvironment 'UNICORECRM_TEST_EMAIL' $manifest.email
        Set-DemoEnvironment 'UNICORECRM_TEST_PASSWORD' $password
        Set-DemoEnvironment 'UNICORECRM_DEMO_EVIDENCE' $stateDirectory
        Push-Location -LiteralPath $FrontendDirectory
        try { & npx playwright test --config playwright.realistic-demo.config.ts; Assert-Demo ($LASTEXITCODE -eq 0) 'UI_ACCEPTANCE_FAILED' 'Inspect frontend test-results/realistic-demo and local ui-verification.json' }
        finally { Pop-Location }
        $verificationPath=Join-Path $stateDirectory 'verification.json'
        $verified=Get-Content -LiteralPath $verificationPath -Raw|ConvertFrom-Json -AsHashtable
        $verified.ui='PASS';$verified.uiVerifiedAt=[DateTimeOffset]::UtcNow.ToString('O')
        $verified|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $verificationPath
    }
    $manifest.runs+=,@{startedAt=$startedAt.ToString('O');finishedAt=[DateTimeOffset]::UtcNow.ToString('O');newCommands=$committedThisRun;status='PASS'}
    Save-DemoManifest
    $failureLocation=Join-Path $stateDirectory 'failure-location.json'
    if(Test-Path -LiteralPath $failureLocation){Remove-Item -LiteralPath $failureLocation}
    Write-Host "Verified workspace $workspace ($($manifest.workspaceKey)); new commands=$committedThisRun"
} catch {
    # Never serialize connection strings, credentials, token-bearing responses or exception internals.
    if(Test-Path -LiteralPath $stateDirectory){@{errorType=$_.Exception.GetType().Name;errorId=$_.FullyQualifiedErrorId;scriptStack=$_.ScriptStackTrace}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $stateDirectory 'failure-location.json')}
    $message=if($_.Exception.Message -match '^[A-Z_]+:'){ $_.Exception.Message }else{'EXECUTION_FAILED: inspect failure-location.json and protected local logs; resume the same manifest'}
    $code=($message -split ':',2)[0]
    Write-Error (@{status=$(if($code -eq 'BLOCKED_ENVIRONMENT'){'BLOCKED_ENVIRONMENT'}else{'FAIL'});code=$code;message=$message;manifest=$manifestPath}|ConvertTo-Json -Compress) -ErrorAction Continue
    exit 1
} finally {
    Stop-DemoHost
    if($manifest -and $httpEvidence.Count){$httpEvidence|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $stateDirectory 'http-evidence.json')}
    foreach($entry in $originalEnvironment.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
    if($lock){$lock.Dispose()}
}
