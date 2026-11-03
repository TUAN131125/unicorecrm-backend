#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Baseline','Migrate','Api','Acceptance','Provisioning','Permissions','SyntheticPermissions','Browser','Legacy','Metrics')][string]$Mode='Api')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline/backend'
[void](New-Item -ItemType Directory -Force -Path $stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$script:httpEvidence=[Collections.Generic.List[object]]::new()
function Invoke-PipelineApi([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null) {
    $value=Invoke-DemoApi $Method $Path $Body $Key $Version
    if($value.body -is [array] -and $value.body.Count -gt 0 -and $value.body[0] -is [long]){
        $value.body=[Text.Encoding]::UTF8.GetString([byte[]]$value.body)|ConvertFrom-Json -AsHashtable
        $script:httpEvidence[$script:httpEvidence.Count-1].code=$value.body.code
    }
    return $value
}
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Only the verified existing Development database is admitted'
$server=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Name").Rows[0].Name
Assert-Demo ($server -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL Server must be local'
$baselinePath=Join-Path $stateDirectory 'sql-before.json'
if($Mode -eq 'Baseline'){
    Assert-Demo (!(Test-Path -LiteralPath $baselinePath)) 'BASELINE_ALREADY_EXISTS' 'An existing preservation baseline must never be overwritten'
    $columns=@((Invoke-DemoSql "SELECT QUOTENAME(name) Name FROM sys.columns WHERE object_id=OBJECT_ID('deals.Deals') AND name<>'PipelineId' ORDER BY column_id").Rows|ForEach-Object {[string]$_.Name})
    Assert-Demo ($columns.Count -gt 0) 'BASELINE_SCHEMA_MISSING' 'The existing Deals schema is required'
    $projection=$columns -join ','
    $deals=@(foreach($row in (Invoke-DemoSql 'SELECT WorkspaceId,COUNT(*) Count FROM deals.Deals GROUP BY WorkspaceId').Rows){
        $hash=(Invoke-DemoSql "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $projection FROM deals.Deals WHERE WorkspaceId=@ws ORDER BY DealId FOR JSON PATH)),2) LegacyHash" @{'@ws'=[string]$row.WorkspaceId}).Rows[0].LegacyHash
        @{workspaceId=[string]$row.WorkspaceId;count=[int]$row.Count;legacyHash=[string]$hash}
    })
    @{server=$builder.DataSource;database=$builder.InitialCatalog;columns=$columns;deals=$deals;capturedAt=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $baselinePath
    Write-Host 'PASS: read-only original Deal preservation baseline captured; no database writes'
    return
}
Assert-Demo (Test-Path -LiteralPath $baselinePath) 'BASELINE_REQUIRED' 'Capture the original Deal columns and hashes before migration'
$baseline=Get-Content -Raw -LiteralPath $baselinePath|ConvertFrom-Json -AsHashtable
$projection=$baseline.columns -join ','
function Verify-LegacyDeals {
    foreach($expected in $baseline.deals){
        $actual=(Invoke-DemoSql "SELECT COUNT(*) Count,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $projection FROM deals.Deals d WHERE d.WorkspaceId=@ws ORDER BY DealId FOR JSON PATH)),2) LegacyHash FROM deals.Deals WHERE WorkspaceId=@ws" @{'@ws'=$expected.workspaceId}).Rows[0]
        Assert-Demo ($actual.Count -eq $expected.count -and $actual.LegacyHash -ceq $expected.legacyHash) 'LEGACY_DEALS_CHANGED' $expected.workspaceId
    }
    $orphans=(Invoke-DemoSql 'SELECT COUNT(*) Count FROM deals.Deals d LEFT JOIN deals.PipelineStages s ON s.WorkspaceId=d.WorkspaceId AND s.PipelineId=d.PipelineId AND s.Code=d.StageCode WHERE s.StageId IS NULL OR s.Category<>d.StageCategory').Rows[0].Count
    Assert-Demo ($orphans -eq 0) 'ORPHAN_REFERENCE' 'Every Deal must reference its own workspace pipeline/stage'
    @{legacyDeals='PASS';orphans=[int]$orphans;baseline=$baseline.deals}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $stateDirectory 'migration-preservation.json')
}
$prior=@{}
function Set-PipelineEnvironment([string]$Name,[string]$Value){$prior[$Name]=[Environment]::GetEnvironmentVariable($Name);[Environment]::SetEnvironmentVariable($Name,$Value)}
try {
    Set-PipelineEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-PipelineEnvironment 'DOTNET_ENVIRONMENT' 'Development'
    if($Mode -eq 'Migrate'){
        Set-PipelineEnvironment 'ConnectionStrings__UnicoreCRM' $script:connectionString
        Push-Location $repo
        try {
            & dotnet ef database update 20261023150422_AuthoritativeDealPipelines --project src/UnicoreCRM.Crm --startup-project src/UnicoreCRM.Crm --context DealsDbContext --no-build *> (Join-Path $stateDirectory 'migration.log')
            Assert-Demo ($LASTEXITCODE -eq 0) 'MIGRATION_FAILED' 'Inspect migration.log'
        }finally{Pop-Location}
        Verify-LegacyDeals
        Write-Host 'PASS: migration, original Deal hashes and relationships'
        return
    }
    $password=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$password){$password=$configuration['DevelopmentDemoBootstrap:Password']}
    if($Mode -eq 'Provisioning'){
        . (Join-Path $PSScriptRoot 'demo/DealPipelineProvisioning.ps1')
        Initialize-PipelineTestIdentity $password
    }
    if($Mode -eq 'SyntheticPermissions'){
        . (Join-Path $PSScriptRoot 'demo/DealPipelinePermissions.ps1')
        Initialize-PipelineReadOnlyIdentity $password
    }
    if($Mode -eq 'Browser'){Set-PipelineEnvironment 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3042'}
    Start-DemoHost
    if($Mode -in @('Permissions','SyntheticPermissions')){
        . (Join-Path $PSScriptRoot 'demo/DealPipelinePermissions.ps1')
        Test-DealPipelinePermissions;return
    }
    if($Mode -eq 'Provisioning'){Test-NewWorkspacePipeline $password;return}
    $login=Invoke-PipelineApi POST '/auth/sessions' @{email='demo.anhduong@example.com';password=$password} "pipeline-login-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($login.status -eq 200) 'LOGIN_FAILED' 'Existing synthetic demo owner required'
    $script:token=$login.body.accessToken
    if($Mode -eq 'Metrics'){
        . (Join-Path $PSScriptRoot 'demo/DealPipelineMetrics.ps1')
        Test-DealPipelineMetrics;return
    }
    if($Mode -eq 'Legacy'){
        . (Join-Path $PSScriptRoot 'demo/DealPipelineLegacyReplay.ps1')
        Test-DealPipelineLegacyReplay;return
    }
    if($Mode -eq 'Browser'){
        $acceptance=Get-Content -Raw (Join-Path $stateDirectory 'api-acceptance.json')|ConvertFrom-Json -AsHashtable
        Set-PipelineEnvironment 'UNICORECRM_TEST_API_BASE_URL' 'http://127.0.0.1:5098'
        Set-PipelineEnvironment 'UNICORECRM_TEST_EMAIL' 'demo.anhduong@example.com'
        Set-PipelineEnvironment 'UNICORECRM_TEST_PASSWORD' $password
        Set-PipelineEnvironment 'UNICORECRM_TEST_BUYER_ID' $acceptance.contactId
        Set-PipelineEnvironment 'UNICORECRM_TEST_BUYER_SEARCH' 'Nguyễn An Thử Nghiệm'
        Set-PipelineEnvironment 'UNICORECRM_TEST_EVIDENCE_DIR' (Join-Path $stateDirectory 'browser')
        Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
        try{& npx playwright test --config playwright.deal-pipeline.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
        Verify-LegacyDeals
        Assert-Demo ($browserExit -eq 0) 'BROWSER_FAILED' 'Inspect isolated pipeline browser evidence'
        Write-Host 'PASS: connected Studio → Kanban → Deal Detail, persistent backend configuration, original Deals unchanged'
        return
    }
    $script:workspace='ws_811689cfc4b446eab4f29dc109e2abf2'
    $list=Invoke-PipelineApi GET '/crm-configuration/pipelines'
    Assert-Demo ($list.status -eq 200 -and $list.body.items.Count -ge 1) 'REGISTRY_READ_FAILED' 'Control workspace default required'
    $default=$list.body.items|Where-Object isDefault
    $stageInputs=@($default.stages|ForEach-Object {@{id=$_.id;code=$_.code;labelVi=$_.labelVi;labelEn=$_.labelEn;order=$_.order;category=$_.category;defaultProbability=$_.defaultProbability;color=$_.color;isActive=$_.isActive}})
    $request=@{nameVi='Quy trình tư vấn kiểm thử';nameEn='Pipeline acceptance advisory';description='Synthetic authoritative pipeline acceptance';isActive=$true;isDefault=$false;stages=$stageInputs+@(@{id='acceptance_advisory';code='ADVISORY';labelVi='Tư vấn chuyên sâu';labelEn='Detailed advisory';order=35;category='OPEN';defaultProbability=45;color='teal';isActive=$true})}
    $intentPath=Join-Path $stateDirectory 'initial-create-intent.json'
    if(Test-Path $intentPath){$request=Get-Content -Raw $intentPath|ConvertFrom-Json -AsHashtable}
    elseif(Test-Path (Join-Path $stateDirectory 'checkpoint-a-api.json')){
        $original=(Get-Content -Raw (Join-Path $stateDirectory 'checkpoint-a-api.json')|ConvertFrom-Json -AsHashtable).pipeline
        $request=@{nameVi=$original.nameVi;nameEn=$original.nameEn;description=$original.description;isActive=$original.isActive;isDefault=$original.isDefault;stages=@($original.stages|ForEach-Object {@{id=$_.id;code=$_.code;labelVi=$_.labelVi;labelEn=$_.labelEn;order=$_.order;category=$_.category;defaultProbability=$_.defaultProbability;color=$_.color;isActive=$_.isActive}})}
    }
    $request.stages=@($request.stages|Sort-Object @{Expression={if($_.code -eq 'ADVISORY'){1}else{0}}},order)
    $request|ConvertTo-Json -Depth 16|Set-Content $intentPath
    $created=Invoke-PipelineApi POST '/crm-configuration/pipelines' $request 'pipeline-acceptance-create-v1'
    Assert-Demo ($created.status -eq 201) 'REGISTRY_WRITE_FAILED' "HTTP $($created.status) $($created.body.code)"
    $pipeline=$created.body.pipeline
    $replay=Invoke-PipelineApi POST '/crm-configuration/pipelines' $request 'pipeline-acceptance-create-v1'
    Assert-Demo ($replay.body.outcome -eq 'REPLAYED' -and $replay.body.pipeline.id -ceq $pipeline.id) 'REPLAY_FAILED' 'Create must replay'
    $read=Invoke-PipelineApi GET "/crm-configuration/pipelines/$($pipeline.id)"
    Assert-Demo ($read.status -eq 200 -and @($read.body.stages|Where-Object code -eq ADVISORY).Count -eq 1) 'READBACK_FAILED' 'Custom stage must persist'
    $sql=(Invoke-DemoSql 'SELECT COUNT(*) Count FROM deals.PipelineStages WHERE WorkspaceId=@ws AND PipelineId=@id AND Code=''ADVISORY''' @{'@ws'=$script:workspace;'@id'=$pipeline.id}).Rows[0].Count
    Assert-Demo ($sql -eq 1) 'SQL_READBACK_FAILED' 'Exactly one authoritative custom stage expected'
    $script:workspace='ws_f2332a12d56a421d8a722bd2f65c5efb'
    $foreign=Invoke-PipelineApi GET "/crm-configuration/pipelines/$($pipeline.id)"
    Assert-Demo ($foreign.status -eq 404) 'ISOLATION_FAILED' 'Foreign pipeline must be withheld'
    Verify-LegacyDeals
    @{status='PASS';pipeline=$pipeline;http=$script:httpEvidence;sqlStageCount=$sql}|ConvertTo-Json -Depth 14|Set-Content (Join-Path $stateDirectory 'checkpoint-a-api.json')
    Write-Host 'PASS: authoritative create/read, SQL, idempotency, workspace isolation, legacy preservation'
    if($Mode -eq 'Acceptance'){
        . (Join-Path $PSScriptRoot 'demo/DealPipelineAcceptance.ps1')
        Test-DealPipelineAcceptance $pipeline $request
    }
}finally{
    $script:httpEvidence|ConvertTo-Json -Depth 6|Set-Content (Join-Path $script:stateDirectory 'last-http.json')
    Stop-DemoHost
    foreach($entry in $prior.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
}
