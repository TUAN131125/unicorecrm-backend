#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Before','After')][string]$Mode='After')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$evidence=Join-Path (Split-Path $repo) 'task-runtime/quote-list-pipeline'
$script:stateDirectory=Join-Path $evidence $Mode.ToLowerInvariant()
[void](New-Item -ItemType Directory -Force -Path $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$script:httpEvidence=[Collections.Generic.List[object]]::new()
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Verified existing SQL only'
$script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
function SqlRows([string]$Query){
    $table=Invoke-DemoSql $Query
    return ,@(foreach($row in $table.Rows){$v=@{};foreach($col in $row.Table.Columns){$v[$col.ColumnName]=if($row[$col.ColumnName] -is [DBNull]){$null}else{$row[$col.ColumnName]}};$v})
}
function PipelineHashes {
    return SqlRows "SELECT 'Pipelines' TableName,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM deals.Pipelines ORDER BY WorkspaceId,PipelineId FOR JSON PATH)),2) RowHash UNION ALL SELECT 'PipelineStages',CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM deals.PipelineStages ORDER BY WorkspaceId,PipelineId,StageId FOR JSON PATH)),2)"
}
$pipelineHashes=PipelineHashes
$pipelineHashes|ConvertTo-Json -Depth 5|Set-Content (Join-Path $script:stateDirectory 'pipeline-hashes-before.json')
SqlRows 'SELECT WorkspaceId,PipelineId,NameVi,NameEn,IsDefault,IsActive,Version FROM deals.Pipelines ORDER BY WorkspaceId,PipelineId'|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'pipelines-sql.json')
SqlRows 'SELECT WorkspaceId,PipelineId,COUNT(*) Total,SUM(CASE WHEN ArchivedAt IS NOT NULL THEN 1 ELSE 0 END) Archived FROM deals.Deals GROUP BY WorkspaceId,PipelineId'|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'deal-assignments-sql.json')
SqlRows "SELECT WorkspaceId,DealId,JSON_VALUE(Profile,'$.name') Name,PipelineId,StageCode,Version FROM deals.Deals ORDER BY WorkspaceId,DealId"|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'deals-sql.json')
$baseline=Get-DemoCounts
$quoteHash=(Invoke-DemoSql "SELECT COUNT(*) Count,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM quotes.Quotes ORDER BY WorkspaceId,QuoteId FOR JSON PATH)),2) RowHash FROM quotes.Quotes").Rows[0]
$baseline|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'crm-before.json')
$password=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$password){$password=$configuration['DevelopmentDemoBootstrap:Password']}
$fixture=Get-Content -Raw (Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline/backend/provisioning-acceptance.json')|ConvertFrom-Json -AsHashtable
$core=Get-Content -Raw (Join-Path (Split-Path $repo) 'task-runtime/quote-core/acceptance/api-result.json')|ConvertFrom-Json -AsHashtable
$prior=@{}
function EnvValue([string]$Name,[string]$Value){$prior[$Name]=[Environment]::GetEnvironmentVariable($Name);[Environment]::SetEnvironmentVariable($Name,$Value)}
try{
    EnvValue 'ASPNETCORE_ENVIRONMENT' 'Development';EnvValue 'DOTNET_ENVIRONMENT' 'Development';EnvValue 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3044'
    Start-DemoHost
    $login=Invoke-DemoApi POST '/auth/sessions' @{email='pipeline.provisioning@example.com';password=$password} "quote-list-login-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($login.status -eq 200) 'LOGIN_FAILED' 'Existing control identity required'
    $script:token=$login.body.accessToken;$script:workspace=$fixture.workspaceId
    $coreQuote=(Invoke-DemoSql 'SELECT QuoteNumber,Title FROM quotes.Quotes WHERE WorkspaceId=@ws AND QuoteId=@id' @{'@ws'=$fixture.workspaceId;'@id'=$core.directQuoteId}).Rows[0]
    $paths=@('/workspaces','/crm-configuration/pipelines','/quotes?limit=25&sortBy=updatedAt&sortDirection=desc',"/quotes?search=$($coreQuote.QuoteNumber)",'/quotes?status=DRAFT&limit=1','/quotes?search=no-match-quote-list-acceptance','/quotes?limit=251','/quotes?cursor=invalid')
    $api=@(foreach($path in $paths){$r=Invoke-DemoApi GET $path;@{path=$path;response=$r;correlationId=$script:httpEvidence[-1].correlationId}})
    $list=($api|Where-Object path -eq '/quotes?limit=25&sortBy=updatedAt&sortDirection=desc').response
    Assert-Demo ($list.status -eq 200 -and $list.body.items.Count -eq 25 -and $list.body.pageInfo.hasNextPage) 'LIST_PAGE' 'Existing Quotes require actual first cursor page'
    $next=Invoke-DemoApi GET ("/quotes?limit=25&sortBy=updatedAt&sortDirection=desc&cursor="+[Uri]::EscapeDataString($list.body.pageInfo.nextCursor))
    Assert-Demo ($next.status -eq 200 -and $next.body.items.Count -gt 0 -and $next.body.items[0].id -cne $list.body.items[0].id) 'LIST_NEXT' 'Second cursor page must differ'
    $api+=@{path='quotes-next-page';response=$next;correlationId=$script:httpEvidence[-1].correlationId}
    $found=($api|Where-Object path -eq "/quotes?search=$($coreQuote.QuoteNumber)").response
    Assert-Demo ($found.status -eq 200 -and $found.body.items.Count -eq 1 -and $found.body.items[0].id -ceq $core.directQuoteId) 'CORE_QUOTE_VISIBLE' 'Checkpoint A Quote must be searchable'
    Assert-Demo (($api|Where-Object path -eq '/quotes?search=no-match-quote-list-acceptance').response.body.items.Count -eq 0) 'VALID_EMPTY' 'Successful no-match query is empty'
    foreach($invalid in '/quotes?limit=251','/quotes?cursor=invalid'){Assert-Demo (($api|Where-Object path -eq $invalid).response.status -eq 422) 'LIST_VALIDATION' $invalid}
    $source=Invoke-DemoApi GET "/quotes?sourceDealId=$($core.dealId)&status=DRAFT&limit=250"
    Assert-Demo ($source.status -eq 200 -and @($source.body.items|Where-Object {$_.sourceDealId -cne $core.dealId -or $_.status -cne 'DRAFT'}).Count -eq 0) 'LIST_FILTERS' 'Server filters cannot widen results'
    $api+=@{path='quotes-source-status-filter';response=$source;correlationId=$script:httpEvidence[-1].correlationId}
    $script:workspace='ws_nonexistent_quote_list_control'
    $denied=Invoke-DemoApi GET '/quotes'
    Assert-Demo ($denied.status -eq 403) 'WORKSPACE_SCOPE' 'Non-member scope is denied'
    $api+=@{path='quotes-foreign-workspace';response=$denied;correlationId=$script:httpEvidence[-1].correlationId}
    $script:workspace=$fixture.workspaceId
    $api|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'api-list.json')
        $memberships=($api|Where-Object path -eq '/workspaces').response.body
    $memberships|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'memberships.json')
    $records=(Invoke-DemoSql 'SELECT WorkspaceId,QuoteId,QuoteNumber,Title,Status,SourceDealId,OwnerId,ResourceVersion FROM quotes.Quotes ORDER BY WorkspaceId,QuoteId').Rows
    $rows=@(foreach($row in $records){$v=@{};foreach($col in $row.Table.Columns){$v[$col.ColumnName]=if($row[$col.ColumnName] -is [DBNull]){$null}else{$row[$col.ColumnName]}};$v})
    $rows|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'quotes-sql.json')
    $demoLogin=Invoke-DemoApi POST '/auth/sessions' @{email='demo.anhduong@example.com';password=$password} "quote-list-demo-login-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($demoLogin.status -eq 200) 'SWITCH_IDENTITY' 'Existing multi-Workspace identity required'
    $script:token=$demoLogin.body.accessToken
    $switchWorkspaces=Get-Content -Raw (Join-Path $evidence 'switch-workspaces.json')|ConvertFrom-Json -AsHashtable
    $switchReads=@(foreach($ws in $switchWorkspaces){
        $script:workspace=$ws.id
        $quotes=Invoke-DemoApi GET '/quotes?limit=25';Assert-Demo ($quotes.status -eq 200) 'SWITCH_QUOTES' 'Authorized Workspace Quote list required'
        $pipelines=Invoke-DemoApi GET '/crm-configuration/pipelines';Assert-Demo ($pipelines.status -eq 200) 'SWITCH_PIPELINES' 'Workspace pipeline registry required'
        @{workspace=$ws;quotes=$quotes;pipelines=$pipelines}
    })
    $switchReads|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'switch-api.json')
    foreach($entry in @{
        UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL='pipeline.provisioning@example.com';UNICORECRM_TEST_PASSWORD=$password
        UNICORECRM_TEST_WORKSPACE_ID=$fixture.workspaceId;UNICORECRM_TEST_WORKSPACE_KEY=$fixture.workspaceKey
        UNICORECRM_TEST_QUOTE_NUMBER=[string]$coreQuote.QuoteNumber;UNICORECRM_TEST_QUOTE_TITLE=[string]$coreQuote.Title;UNICORECRM_TEST_DEAL_ID=$core.dealId;UNICORECRM_TEST_SWITCH_EMAIL='demo.anhduong@example.com';UNICORECRM_TEST_SWITCH_WORKSPACES=($switchWorkspaces|ConvertTo-Json -Depth 5 -Compress);UNICORECRM_TEST_QUOTE_ID=$core.directQuoteId;UNICORECRM_TEST_EVIDENCE_DIR=$script:stateDirectory;UNICORECRM_TEST_LIST_MODE=$Mode
    }.GetEnumerator()){EnvValue $entry.Key $entry.Value}
    Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
    try{& npx playwright test --config playwright.quote-list-pipeline.config.ts;$exit=$LASTEXITCODE}finally{Pop-Location}
    Assert-Demo ($exit -eq 0) 'BROWSER_FAILED' 'Inspect browser evidence'
    $browserRequests=Get-Content -Raw (Join-Path $script:stateDirectory 'browser-http.json')|ConvertFrom-Json -AsHashtable
    $readAudits=@(foreach($request in $browserRequests|Where-Object {$_.correlationId -and $_.status -eq 200 -and $_.url -like '*:5098/quotes*'}) {
        $audit=(Invoke-DemoSql 'SELECT WorkspaceId,Operation,Outcome,CorrelationId,ResourceVersion FROM quotes.ReadAuditRecords WHERE CorrelationId=@id' @{'@id'=$request.correlationId}).Rows
        foreach($row in $audit){@{workspaceId=[string]$row.WorkspaceId;operation=[string]$row.Operation;outcome=[string]$row.Outcome;correlationId=[string]$row.CorrelationId}}
    })
    $readAudits|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'browser-read-audits.json')
    Assert-Demo ($readAudits.Count -gt 0) 'READ_AUDIT_REQUIRED' 'Browser HTTP correlations must join backend SQL read audit'
    $after=Get-DemoCounts
    foreach($old in $baseline){$new=$after|Where-Object {$_.module -ceq $old.module -and $_.workspaceId -ceq $old.workspaceId};Assert-Demo ($new.count -eq $old.count -and $new.hash -ceq $old.hash) 'CRM_CHANGED' "$($old.module) $($old.workspaceId)"}
    $after|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'crm-after.json')
    $afterQuoteHash=(Invoke-DemoSql "SELECT COUNT(*) Count,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM quotes.Quotes ORDER BY WorkspaceId,QuoteId FOR JSON PATH)),2) RowHash FROM quotes.Quotes").Rows[0]
    Assert-Demo ($quoteHash.Count -eq $afterQuoteHash.Count -and $quoteHash.RowHash -ceq $afterQuoteHash.RowHash) 'QUOTES_CHANGED' 'Read acceptance must preserve all Quote rows'
    $afterPipelines=PipelineHashes
    Assert-Demo (($pipelineHashes|ConvertTo-Json -Compress) -ceq ($afterPipelines|ConvertTo-Json -Compress)) 'PIPELINES_CHANGED' 'Dynamic Pipeline registry must remain byte-identical'
    $afterPipelines|ConvertTo-Json -Depth 5|Set-Content (Join-Path $script:stateDirectory 'pipeline-hashes-after.json')
    @{status='PASS';mode=$Mode;pipelinesPreserved=$true;crmPreserved=$true;quotesPreserved=$true;quoteCount=[int]$quoteHash.Count}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
}finally{
    $script:httpEvidence|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'http-correlations.json')
    Stop-DemoHost
    foreach($entry in $prior.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
}
