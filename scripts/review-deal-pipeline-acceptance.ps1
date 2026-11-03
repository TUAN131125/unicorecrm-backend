#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Api','Browser','Inspect')][string]$Mode='Api')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline-final-acceptance'
[void](New-Item -ItemType Directory -Force -Path $script:stateDirectory)
$originalEvidence=Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline/backend'
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$script:httpEvidence=[Collections.Generic.List[object]]::new()
function Api([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
    $value=Invoke-DemoApi $Method $Path $Body $Key $Version
    if($value.body -is [array] -and $value.body.Count -gt 0 -and $value.body[0] -is [long]){
        $value.body=[Text.Encoding]::UTF8.GetString([byte[]]$value.body)|ConvertFrom-Json -AsHashtable
        $script:httpEvidence[$script:httpEvidence.Count-1].code=$value.body.code
    }
    return $value
}
function Required([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
    $value=Api $Method $Path $Body $Key $Version
    Assert-Demo ($value.status -in 200,201) 'REVIEW_API_FAILED' "$Method $Path HTTP $($value.status) $($value.body.code)"
    return $value.body
}
function Login([string]$Email){
    $script:workspace=$null;$script:token=$null
    $value=Required POST '/auth/sessions' @{email=$Email;password=$password} "pipeline-final-login-$([Guid]::NewGuid().ToString('N'))"
    $script:token=$value.accessToken
    return (Required GET '/auth/session').principal.memberId
}
function SaveBody($Pipeline){
    return @{nameVi=$Pipeline.nameVi;nameEn=$Pipeline.nameEn;description=$Pipeline.description;isActive=$Pipeline.isActive;isDefault=$Pipeline.isDefault;stages=@($Pipeline.stages|ForEach-Object {@{id=$_.id;code=$_.code;labelVi=$_.labelVi;labelEn=$_.labelEn;order=$_.order;category=$_.category;defaultProbability=$_.defaultProbability;color=$_.color;isActive=$_.isActive}})}
}
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Only the verified existing local Development database is admitted'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Name").Rows[0].Name
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL Server must be local'
$fixture=Get-Content -Raw (Join-Path $originalEvidence 'provisioning-acceptance.json')|ConvertFrom-Json -AsHashtable
Assert-Demo ($fixture.workspaceId -ceq 'ws_772b487b87e84456b5020631fbaedd27') 'FIXTURE_SCOPE_REQUIRED' 'Use only the independently provisioned task workspace'
$script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
$before=Get-DemoCounts
if($Mode -eq 'Inspect'){
    $commercial=(Invoke-DemoSql @'
SELECT
 (SELECT COUNT(*) FROM quotes.Quotes) Quotes,
 (SELECT COUNT(*) FROM orders.Orders) Orders,
 (SELECT COUNT(*) FROM deals.Deals WHERE StageCategory='Won') WonDeals,
 (SELECT COUNT(*) FROM quotes.Quotes q JOIN deals.Deals d ON d.WorkspaceId=q.WorkspaceId AND d.DealId=q.SourceDealId WHERE q.Status='ACCEPTED' AND q.AcceptedAt IS NOT NULL AND q.ArchivedAt IS NULL) LinkedAcceptedQuotes,
 (SELECT COUNT(*) FROM orders.Orders o JOIN deals.Deals d ON d.WorkspaceId=o.WorkspaceId AND d.DealId=o.SourceDealId WHERE o.State='CONFIRMED' AND o.ConfirmedAt IS NOT NULL AND o.ArchivedAt IS NULL) LinkedConfirmedOrders,
 (SELECT COUNT(*) FROM deals.Deals d JOIN deals.PipelineStages s ON s.WorkspaceId=d.WorkspaceId AND s.PipelineId=d.PipelineId AND s.Code=d.StageCode WHERE s.IsActive=0) CurrentInactiveDeals
'@).Rows[0]
    $entry=Get-Content -Raw (Join-Path $originalEvidence 'before-hashes.json')|ConvertFrom-Json -AsHashtable
    $source=@(foreach($path in @('src/UnicoreCRM.Crm/Deals/Application/MarkDealWon/Handler.cs','src/UnicoreCRM.Crm/Deals/Application/MarkDealWon/MarkDealWonValidation.cs','src/UnicoreCRM.Sales/Quotes/Contracts/QuotesEndpoints.cs','src/UnicoreCRM.Sales/Orders/Contracts/OrdersEndpoints.cs')){
        $hash=(Get-FileHash -LiteralPath (Join-Path $repo $path) -Algorithm SHA256).Hash
        @{path=$path;hash=$hash;unchangedSincePipelineEntry=($hash -ceq ($entry|Where-Object path -ceq $path).hash)}
    })
    $commercialEvidence=@{};foreach($column in $commercial.Table.Columns){$commercialEvidence[$column.ColumnName]=[int]$commercial[$column.ColumnName]}
    @{server=$builder.DataSource;database=$builder.InitialCatalog;commercial=$commercialEvidence;ownerSource=$source;counts=$before;sqlWrites=0;loadTest='NOT_RUN'}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'source-sql-review.json')
    Write-Host 'PASS: read-only SQL commercial/inactive-state inventory and task-entry source hashes captured'
    return
}
$password=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$password){$password=$configuration['DevelopmentDemoBootstrap:Password']}
$prior=@{}
function Set-ReviewEnvironment([string]$Name,[string]$Value){$prior[$Name]=[Environment]::GetEnvironmentVariable($Name);[Environment]::SetEnvironmentVariable($Name,$Value)}
try{
    Set-ReviewEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development';Set-ReviewEnvironment 'DOTNET_ENVIRONMENT' 'Development'
    if($Mode -eq 'Browser'){Set-ReviewEnvironment 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3042'}
    Start-DemoHost
    $evidencePath=Join-Path $script:stateDirectory 'api-review.json'
    if($Mode -eq 'Browser'){
        $evidence=Get-Content -Raw $evidencePath|ConvertFrom-Json -AsHashtable
        foreach($entry in @{
            UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL='pipeline.readonly@example.com';UNICORECRM_TEST_PASSWORD=$password
            UNICORECRM_TEST_BUYER_ID=$evidence.contactId;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')
            UNICORECRM_TEST_REVIEW_WORKSPACE_KEY=$fixture.workspaceKey;UNICORECRM_TEST_REVIEW_WORKSPACE_ID=$fixture.workspaceId
            UNICORECRM_TEST_REVIEW_DEAL_ID=$evidence.dealId;UNICORECRM_TEST_REVIEW_PIPELINE_ID=$evidence.pipelineId
        }.GetEnumerator()){Set-ReviewEnvironment $entry.Key $entry.Value}
        Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
        try{& npx playwright test --config playwright.deal-pipeline-review.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
        Assert-Demo ($browserExit -eq 0) 'BROWSER_FAILED' 'Inspect restricted-role browser evidence'
        return
    }
    $member=Login 'pipeline.provisioning@example.com'
    $script:workspace=$fixture.workspaceId
    $membership=Required GET '/workspaces'
    Assert-Demo (@($membership.items|Where-Object workspaceId -ceq $fixture.workspaceId).Count -eq 1) 'FIXTURE_MEMBERSHIP_REQUIRED' 'Existing fixture owner must have active membership'
    $stages=@(
        @{id='review_intake';code='REVIEW_INTAKE';labelVi='Tiếp nhận kiểm thử';labelEn='Review intake';order=10;category='OPEN';defaultProbability=20;color='blue';isActive=$true},
        @{id='review_followup';code='REVIEW_FOLLOWUP';labelVi='Theo dõi kiểm thử';labelEn='Review follow-up';order=20;category='OPEN';defaultProbability=40;color='teal';isActive=$true},
        @{id='review_won';code='WON';labelVi='Thắng';labelEn='Won';order=30;category='WON';defaultProbability=100;color='green';isActive=$true},
        @{id='review_lost';code='LOST';labelVi='Thua';labelEn='Lost';order=40;category='LOST';defaultProbability=0;color='red';isActive=$true}
    )
    $pipelineIntent=@{nameVi='Kiểm thử nghiệm thu lịch sử pipeline';nameEn='Pipeline historical acceptance review';description='Isolated synthetic acceptance fixture';isActive=$true;isDefault=$false;stages=$stages}
    $pipeline=(Required POST '/crm-configuration/pipelines' $pipelineIntent 'pipeline-final-history-config-v1').pipeline
    $contact=(Required POST '/contacts' @{fullName='Trần Minh Kiểm Thử';ownerId=$member;workEmail='pipeline.final.review@example.com';notes='Isolated synthetic acceptance fixture'} 'pipeline-final-history-contact-v1').result.contact
    $dealIntent=@{name='Cơ hội kiểm thử lịch sử và quyền chỉ xem';buyerRef=@{type='CONTACT';id=$contact.id};pipelineId=$pipeline.id;stageCode='REVIEW_INTAKE';amount=@{amount='1500000';currency='VND'};opportunityScore='20';ownerId=$member;expectedCloseDate='2027-06-01';interestedProductIds=@();lineItems=@();notes='Isolated synthetic pipeline final acceptance fixture'}
    $created=Required POST '/deals' $dealIntent 'pipeline-final-history-deal-v1'
    $dealId=$created.result.deal.id
    $moved=Required POST "/deals/$dealId/change-stage" @{stageCode='REVIEW_FOLLOWUP'} 'pipeline-final-history-move-v1' $created.result.deal.resourceVersion
    $retire=SaveBody $pipeline
    ($retire.stages|Where-Object code -eq REVIEW_INTAKE).isActive=$false
    $retired=Required PATCH "/crm-configuration/pipelines/$($pipeline.id)" $retire 'pipeline-final-history-retire-v1' $pipeline.resourceVersion
    $current=Required GET "/deals/$dealId"
    $config=Required GET "/crm-configuration/pipelines/$($pipeline.id)"
    $historical=Required POST '/deals' $dealIntent 'pipeline-final-history-deal-v1'
    Assert-Demo ($current.stageCode -ceq 'REVIEW_FOLLOWUP' -and $historical.outcome -ceq 'REPLAYED' -and $historical.result.deal.stageCode -ceq 'REVIEW_INTAKE' -and !($config.stages|Where-Object code -eq REVIEW_INTAKE).isActive) 'HISTORICAL_REPLAY_FAILED' 'Historical snapshots may refer to retired stages; current Deal must remain active'
    $disableCurrent=SaveBody $config
    ($disableCurrent.stages|Where-Object code -eq REVIEW_FOLLOWUP).isActive=$false
    # Keep another OPEN stage active so the usage guard, not the last-OPEN guard, is exercised.
    ($disableCurrent.stages|Where-Object code -eq REVIEW_INTAKE).isActive=$true
    $blocked=Api PATCH "/crm-configuration/pipelines/$($pipeline.id)" $disableCurrent 'pipeline-final-used-stage-forbidden-v1' $config.resourceVersion
    Assert-Demo ($blocked.status -eq 409 -and $blocked.body.code -ceq 'PIPELINE_STAGE_IN_USE') 'IN_USE_GUARD_FAILED' 'Referenced current stage cannot be retired'
    $inactive=Api POST "/deals/$dealId/change-stage" @{stageCode='REVIEW_INTAKE'} 'pipeline-final-inactive-stage-forbidden-v1' $current.resourceVersion
    Assert-Demo ($inactive.status -eq 422 -and $inactive.body.code -ceq 'DEAL_STAGE_INACTIVE') 'INACTIVE_TARGET_GUARD_FAILED' 'Current Deal cannot move into an inactive stage'
    $inactiveCreate=Api POST '/deals' $dealIntent 'pipeline-final-inactive-create-forbidden-v1'
    Assert-Demo ($inactiveCreate.status -eq 422 -and $inactiveCreate.body.code -ceq 'DEAL_STAGE_INACTIVE') 'INACTIVE_CREATE_GUARD_FAILED' 'A fresh Deal cannot start in an inactive stage'
    $sql=(Invoke-DemoSql 'SELECT d.StageCode,d.Version,s.IsActive FROM deals.Deals d JOIN deals.PipelineStages s ON s.WorkspaceId=d.WorkspaceId AND s.PipelineId=d.PipelineId AND s.Code=d.StageCode WHERE d.WorkspaceId=@ws AND d.DealId=@id' @{'@ws'=$fixture.workspaceId;'@id'=$dealId}).Rows[0]
    $inactiveCurrent=(Invoke-DemoSql 'SELECT COUNT(*) Count FROM deals.Deals d JOIN deals.PipelineStages s ON s.WorkspaceId=d.WorkspaceId AND s.PipelineId=d.PipelineId AND s.Code=d.StageCode WHERE s.IsActive=0').Rows[0].Count
    Assert-Demo ($sql.StageCode -ceq 'REVIEW_FOLLOWUP' -and $sql.IsActive -and $inactiveCurrent -eq 0) 'CURRENT_STAGE_INVARIANT_FAILED' 'SQL current rows cannot be mislabeled as historical replay snapshots'
    $samples=[Collections.Generic.List[object]]::new()
    for($index=0;$index -lt 3;$index++){
        $logPath=Join-Path $script:stateDirectory 'host.log';$start=(Get-Content -Raw $logPath).Length
        $timer=[Diagnostics.Stopwatch]::StartNew();$null=Required GET '/crm-configuration/pipelines';$timer.Stop();Start-Sleep -Milliseconds 100
        $delta=(Get-Content -Raw $logPath).Substring($start)
        $selects=[regex]::Matches($delta,'FROM \[deals\]\.\[Pipelines\]').Count
        Assert-Demo ($selects -eq 1) 'REGISTRY_N_PLUS_ONE' 'One eager registry SELECT per read'
        $samples.Add(@{registrySelects=$selects;milliseconds=[math]::Round($timer.Elapsed.TotalMilliseconds,2)})
    }
    $null=Login 'pipeline.readonly@example.com';$script:workspace=$fixture.workspaceId
    $context=Required GET '/access/context'
    Assert-Demo ($context.capabilities -contains 'deals.read' -and $context.capabilities -notcontains 'studio.read' -and $context.capabilities -notcontains 'studio.configure' -and $context.capabilities -notcontains 'deals.update') 'RESTRICTED_ROLE_REQUIRED' 'Use the existing genuinely restricted role; never change permissions'
    $roleRead=Required GET "/deals/$dealId"
    $roleRegistry=Required GET '/crm-configuration/pipelines'
    $denials=@(
        @{name='configuration-create';value=(Api POST '/crm-configuration/pipelines' $pipelineIntent 'pipeline-final-readonly-config-create-v1')},
        @{name='configuration-update';value=(Api PATCH "/crm-configuration/pipelines/$($pipeline.id)" (SaveBody $config) 'pipeline-final-readonly-config-update-v1' $config.resourceVersion)},
        @{name='deal-create';value=(Api POST '/deals' $dealIntent 'pipeline-final-readonly-deal-create-v1')},
        @{name='deal-update-stage';value=(Api POST "/deals/$dealId/change-stage" @{stageCode='REVIEW_INTAKE'} 'pipeline-final-readonly-stage-v1' $current.resourceVersion)},
        @{name='deal-close-won';value=(Api POST "/deals/$dealId/mark-won" @{} 'pipeline-final-readonly-won-v1' $current.resourceVersion)}
    )
    foreach($entry in $denials){Assert-Demo ($entry.value.status -eq 403) 'RESTRICTED_WRITE_FAILED' "$($entry.name) must be refused before business mutation"}
    $script:workspace='ws_f2332a12d56a421d8a722bd2f65c5efb'
    $foreignRegistry=Api GET '/crm-configuration/pipelines';$foreignDeal=Api GET "/deals/$dealId"
    Assert-Demo ($foreignRegistry.status -in 403,404 -and $foreignDeal.status -in 403,404) 'RESTRICTED_TENANT_LEAK' 'Nonmember workspace header must never reveal registry or Deal'
    $after=Get-DemoCounts
    foreach($old in $before|Where-Object workspaceId -ne $fixture.workspaceId){
        $new=$after|Where-Object {$_.module -ceq $old.module -and $_.workspaceId -ceq $old.workspaceId}
        Assert-Demo ($new.count -eq $old.count -and $new.hash -ceq $old.hash) 'ORIGINAL_CRM_CHANGED' "$($old.module) $($old.workspaceId)"
    }
    @{status='PASS';workspaceId=$fixture.workspaceId;workspaceKey=$fixture.workspaceKey;pipelineId=$pipeline.id;dealId=$dealId;contactId=$contact.id;currentStage=$current.stageCode;historicalReplayStage=$historical.result.deal.stageCode;historicalReplayOutcome=$historical.outcome;currentSqlVersion=[long]$sql.Version;inactiveCurrentDeals=[int]$inactiveCurrent;usedStageDenial=$blocked.body.code;inactiveStageDenial=$inactive.body.code;inactiveCreateDenial=$inactiveCreate.body.code;restrictedReadDeal=$roleRead.id;restrictedReadPipelines=$roleRegistry.items.Count;restrictedCapabilities=$context.capabilities;denials=@($denials|ForEach-Object {@{operation=$_.name;status=$_.value.status;code=$_.value.body.code}});foreignRegistry=$foreignRegistry.status;foreignDeal=$foreignDeal.status;querySamples=$samples;originalCrmPreserved=$true;loadTest='NOT_RUN';http=$script:httpEvidence}|ConvertTo-Json -Depth 16|Set-Content $evidencePath
    Write-Host 'PASS: isolated historical replay, active current-row invariant, restricted reads/writes, tenant denial and query count; original CRM preserved'
}finally{
    $script:httpEvidence|ConvertTo-Json -Depth 6|Set-Content (Join-Path $script:stateDirectory 'last-http.json')
    Stop-DemoHost
    foreach($entry in $prior.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
}
