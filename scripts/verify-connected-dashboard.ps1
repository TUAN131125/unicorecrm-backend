#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('before','after')][string]$Phase='after')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path $repo 'artifacts/dashboard-hydration'
[void](New-Item -ItemType Directory -Path $stateDirectory -Force)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
$script:httpEvidence=[Collections.Generic.List[object]]::new()
$script:token=$null;$script:workspace=$null;$script:hostProcess=$null
$prior=@{}
function Set-TestEnvironment([string]$Name,[string]$Value){$prior[$Name]=[Environment]::GetEnvironmentVariable($Name);[Environment]::SetEnvironmentVariable($Name,$Value)}
try {
    $configuration=Read-DemoConfiguration
    $script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
    $builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
    Assert-Demo ($builder.InitialCatalog -ceq 'UnicoreCRM_Development' -and $builder.DataSource -ceq 'DESKTOP-ICH225R') 'BLOCKED_ENVIRONMENT' 'Only the previously verified existing Development database is admitted'
    $identity=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) MachineName").Rows[0]
    Assert-Demo ($identity.MachineName -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'Expected local SQL Server'
    $before=Get-DemoCounts
    $before|ConvertTo-Json -Depth 6|Set-Content (Join-Path $stateDirectory "$Phase-before-counts.json")
    Set-TestEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-TestEnvironment 'DOTNET_ENVIRONMENT' 'Development'
    Set-TestEnvironment 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3039'
    & dotnet build (Join-Path $hostDirectory 'UnicoreCRM.ApiHost.csproj') --no-restore *> (Join-Path $stateDirectory 'build.log')
    Assert-Demo ($LASTEXITCODE -eq 0) 'BUILD_FAILED' 'Inspect build.log'
    # Host mode disables background delivery; no bootstrap, seed, migration or CRM mutation is invoked.
    Start-DemoHost
    $password=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$password){$password=$configuration['DevelopmentDemoBootstrap:Password']}
    $login=Invoke-DemoApi POST '/auth/sessions' @{email='demo.anhduong@example.com';password=$password} "dashboard-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($login.status -eq 200) 'LOGIN_FAILED' 'Existing demo identity required'
    $script:token=$login.body.accessToken
    $member=(Invoke-DemoApi GET '/auth/session').body.principal.memberId
    $ws='ws_f2332a12d56a421d8a722bd2f65c5efb'
    $script:workspace=$ws
    $forecast=Invoke-DemoApi GET '/deals/forecast-summary'
    $overdue=Invoke-DemoApi GET "/tasks?limit=1&status=OPEN&assigneeId=$member&overdueAt=$([Uri]::EscapeDataString([DateTimeOffset]::UtcNow.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')))"
    $supportCounts=@(foreach($status in @('new','in_progress','waiting_customer','waiting_internal','reopened')){
        $page=Invoke-DemoApi GET "/support/cases?limit=1&status=$status"
        Assert-Demo ($page.status -eq 200) 'SUPPORT_READ_FAILED' $status
        @{status=$status;httpStatus=$page.status;count=[int]$page.body.pageInfo.totalCount}
    })
    # This verifier targets the existing Vietnam demo fixture; no Workspace setting is changed.
    $zone=[TimeZoneInfo]::FindSystemTimeZoneById('Asia/Ho_Chi_Minh')
    $today=[TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow,$zone).Date
    $todayStart=[TimeZoneInfo]::ConvertTimeToUtc($today,$zone)
    $tomorrowStart=[TimeZoneInfo]::ConvertTimeToUtc($today.AddDays(1),$zone)
    $sql=(Invoke-DemoSql @'
SELECT (SELECT COUNT(*) FROM deals.Deals WHERE WorkspaceId=@ws AND StageCategory='Open' AND ArchivedAt IS NULL) OpenDeals,
(SELECT SUM(TRY_CONVERT(decimal(38,6),JSON_VALUE(Profile,'$.amount.amount'))) FROM deals.Deals WHERE WorkspaceId=@ws AND StageCategory='Open' AND ArchivedAt IS NULL) PipelineVnd,
(SELECT COUNT(*) FROM tasks.Tasks WHERE WorkspaceId=@ws AND AssigneeId=@member AND Status=0 AND ArchivedAt IS NULL AND DueAt<SYSUTCDATETIME()) OverdueTasks,
(SELECT COUNT(*) FROM support.SupportCases WHERE WorkspaceId=@ws AND Status NOT IN (4,5,7)) OpenSupport,
(SELECT COUNT(*) FROM support.SupportCases WHERE WorkspaceId=@ws AND Status NOT IN (4,5,7) AND Priority IN (2,3)) UrgentSupport,
(SELECT COUNT(*) FROM tasks.Tasks WHERE WorkspaceId=@ws AND AssigneeId=@member AND Status=0 AND ArchivedAt IS NULL AND DueAt>=@today AND DueAt<@tomorrow) DueToday
'@ @{'@ws'=$ws;'@member'=$member;'@today'=$todayStart;'@tomorrow'=$tomorrowStart}).Rows[0]
    Assert-Demo ($forecast.status -eq 200 -and $forecast.body.buckets[0].openDealCount -eq $sql.OpenDeals -and [decimal]$forecast.body.buckets[0].openAmount.amount -eq $sql.PipelineVnd) 'SQL_API_MISMATCH' 'Forecast summary mismatch'
    Assert-Demo ($overdue.status -eq 200 -and $overdue.body.pageInfo.totalCount -eq $sql.OverdueTasks) 'SQL_API_MISMATCH' 'Overdue Tasks mismatch'
    Assert-Demo (($supportCounts|Measure-Object -Property count -Sum).Sum -eq $sql.OpenSupport) 'SQL_API_MISMATCH' 'Support count mismatch'
    @{workspaceId=$ws;openDeals=[int]$sql.OpenDeals;pipeline=[string]$sql.PipelineVnd;overdueTasks=[int]$sql.OverdueTasks;openSupport=[int]$sql.OpenSupport;dueToday=[int]$sql.DueToday;urgentSupport=[int]$sql.UrgentSupport;verifiedAt=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $stateDirectory 'expected.json')
    @{forecast=@{httpStatus=$forecast.status;buckets=$forecast.body.buckets};overdue=@{httpStatus=$overdue.status;count=$overdue.body.pageInfo.totalCount};support=$supportCounts}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $stateDirectory "$Phase-api-summary.json")
    Set-TestEnvironment 'UNICORECRM_TEST_API_BASE_URL' 'http://127.0.0.1:5098'
    Set-TestEnvironment 'UNICORECRM_TEST_EMAIL' 'demo.anhduong@example.com'
    Set-TestEnvironment 'UNICORECRM_TEST_PASSWORD' $password
    Set-TestEnvironment 'UNICORECRM_DASHBOARD_EVIDENCE' $stateDirectory
    Set-TestEnvironment 'UNICORECRM_DASHBOARD_PHASE' $Phase
    Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
    try{& npx playwright test --config playwright.dashboard-connected.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
    $after=Get-DemoCounts
    Assert-Demo (($before|ConvertTo-Json -Depth 6 -Compress) -ceq ($after|ConvertTo-Json -Depth 6 -Compress)) 'BUSINESS_DATA_CHANGED' 'Existing CRM rows must remain identical'
    @{status='PASS';before=$before;after=$after}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $stateDirectory "$Phase-data-preservation.json")
    Assert-Demo ($browserExit -eq 0) 'UI_FAILED' 'Inspect isolated browser results'
    Write-Host "PASS ${Phase}: API/SQL, browser and unchanged CRM data"
}finally{
    Stop-DemoHost
    foreach($entry in $prior.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
}
