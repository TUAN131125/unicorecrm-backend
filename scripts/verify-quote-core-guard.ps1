#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) 'task-runtime/quote-core/connected-guard'
[void](New-Item -ItemType Directory -Force -Path $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$script:httpEvidence=[Collections.Generic.List[object]]::new()
$cases=[Collections.Generic.List[object]]::new()
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Only the verified existing local Development database is admitted'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Name").Rows[0].Name
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL Server must be local'
$fixture=Get-Content -Raw (Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline/backend/provisioning-acceptance.json')|ConvertFrom-Json -AsHashtable
$priorAcceptance=Get-Content -Raw (Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline-final-acceptance/api-review.json')|ConvertFrom-Json -AsHashtable
$dealId=$priorAcceptance.dealId
Assert-Demo ($fixture.workspaceId -ceq 'ws_772b487b87e84456b5020631fbaedd27') 'FIXTURE_SCOPE_REQUIRED' 'Existing isolated control workspace required'
$script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
function BusinessSnapshot {
    $values=[Collections.Generic.List[object]]::new()
    foreach($row in (Get-DemoCounts)){$values.Add($row)}
    foreach($entry in @(@{name='Quote';table='quotes.Quotes';id='QuoteId'},@{name='Order';table='orders.Orders';id='OrderId'})){
        foreach($row in (Invoke-DemoSql "SELECT WorkspaceId,COUNT(*) RecordCount FROM $($entry.table) GROUP BY WorkspaceId").Rows){
            $hash=(Invoke-DemoSql "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM $($entry.table) WHERE WorkspaceId=@ws ORDER BY $($entry.id) FOR JSON PATH)),2) RowHash" @{'@ws'=[string]$row.WorkspaceId}).Rows[0].RowHash
            $values.Add(@{module=$entry.name;workspaceId=[string]$row.WorkspaceId;count=[int]$row.RecordCount;hash=[string]$hash})
        }
    }
    return ,$values.ToArray()
}
function MutationSnapshot {
    $row=(Invoke-DemoSql "SELECT (SELECT COUNT(*) FROM deals.AuditRecords WHERE Outcome='COMMITTED') CommittedAudit,(SELECT COUNT(*) FROM deals.IdempotencyRecords) Idempotency,(SELECT COUNT(*) FROM deals.OutboxMessages) Outbox").Rows[0]
    return @{committedAudit=[int]$row.CommittedAudit;idempotency=[int]$row.Idempotency;outbox=[int]$row.Outbox}
}
function Api([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
    $value=Invoke-DemoApi $Method $Path $Body $Key $Version
    if($value.body -is [array] -and $value.body.Count -gt 0 -and $value.body[0] -is [long]){$value.body=[Text.Encoding]::UTF8.GetString([byte[]]$value.body)|ConvertFrom-Json -AsHashtable}
    return $value
}
function Login([string]$Email){
    $script:workspace=$null;$script:token=$null
    $value=Api POST '/auth/sessions' @{email=$Email;password=$password} "quote-guard-login-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($value.status -eq 200) 'LOGIN_FAILED' 'Existing isolated identity required'
    $script:token=$value.body.accessToken
    $script:workspace=$fixture.workspaceId
}
function Probe([string]$Name,$Body,[int]$ExpectedStatus,[string]$ExpectedCode,$Version,[string]$Key=''){
    if(!$Key){$Key="quote-guard-$Name-$([Guid]::NewGuid().ToString('N'))"}
    $response=Api POST "/deals/$dealId/mark-won" $Body $Key $Version
    $cases.Add(@{name=$Name;request=@{method='POST';path="/deals/$dealId/mark-won";workspaceId=$script:workspace;body=$Body;expectedVersion=$Version;idempotencyKey=$Key};response=$response;status=if($response.status -eq $ExpectedStatus -and $response.body.code -ceq $ExpectedCode){'PASS'}else{'FAIL'}})
    Assert-Demo ($response.status -eq $ExpectedStatus -and $response.body.code -ceq $ExpectedCode) 'GUARD_FAILED' "$Name HTTP $($response.status) $($response.body.code)"
}
$before=BusinessSnapshot
$beforeMutation=MutationSnapshot
$before|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'business-before.json')
$password=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$password){$password=$configuration['DevelopmentDemoBootstrap:Password']}
$priorEnvironment=@{}
foreach($name in @('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT')){$priorEnvironment[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,'Development')}
try{
    Start-DemoHost
    Login 'pipeline.provisioning@example.com'
    $deal=Api GET "/deals/$dealId"
    Assert-Demo ($deal.status -eq 200 -and $deal.body.stageCategory -ceq 'OPEN') 'CONTROL_DEAL_REQUIRED' 'Readable existing OPEN Deal required; no fixture will be created'
    $version=$deal.body.resourceVersion
    $intent=@{evidence=@{type='QUOTE_ACCEPTED';sourceId='quote_nonexistent_guard_control';occurredAt='2026-10-24T01:00:00.0000000Z'}}
    $retryKey="quote-guard-retry-$([Guid]::NewGuid().ToString('N'))"
    Probe 'missing-quote' $intent 422 'DEAL_WIN_EVIDENCE_INVALID' $version $retryKey
    Probe 'same-key-retry' $intent 422 'DEAL_WIN_EVIDENCE_INVALID' $version $retryKey
    Probe 'missing-order' @{evidence=@{type='ORDER_CONFIRMED';sourceId='order_nonexistent_guard_control';occurredAt='2026-10-24T01:00:00.0000000Z'}} 422 'DEAL_WIN_EVIDENCE_INVALID' $version
    Probe 'invented-timestamp' @{evidence=@{type='QUOTE_ACCEPTED';sourceId='quote_nonexistent_guard_control';occurredAt='2099-01-01T00:00:00.0000000Z'}} 422 'DEAL_WIN_EVIDENCE_INVALID' $version
    Probe 'version-conflict' $intent 412 'VERSION_CONFLICT' ($version+1)
    Probe 'malformed-evidence' @{evidence=@{type='QUOTE_ACCEPTED';sourceId='quote_nonexistent_guard_control';occurredAt='invalid'}} 422 'DEAL_WIN_EVIDENCE_INVALID' $version
    # Source-state probes use existing owner rows only. Absence is BLOCKED, never a claimed PASS.
    foreach($status in @('DRAFT','SENT','ACCEPTED','EXPIRED')){
        $rows=(Invoke-DemoSql 'SELECT TOP(1) QuoteId,WorkspaceId,SourceDealId,AcceptedAt,ArchivedAt,ValidUntil FROM quotes.Quotes WHERE Status=@status ORDER BY WorkspaceId,QuoteId' @{'@status'=$status}).Rows
        if($rows.Count -eq 0){$cases.Add(@{name="existing-$status-quote";status='BLOCKED';reason='No existing source fixture; no Quote SQL write or acceptance lifecycle is admitted'})}
        else{Probe "existing-$status-quote" @{evidence=@{type='QUOTE_ACCEPTED';sourceId=[string]$rows[0].QuoteId;occurredAt='2026-10-24T01:00:00.0000000Z'}} 422 'DEAL_WIN_EVIDENCE_INVALID' $version}
    }
    $script:workspace='ws_f2332a12d56a421d8a722bd2f65c5efb'
    $foreign=Api POST "/deals/$dealId/mark-won" $intent "quote-guard-foreign-$([Guid]::NewGuid().ToString('N'))" $version
    $cases.Add(@{name='cross-workspace';request=@{workspaceId=$script:workspace;body=$intent};response=$foreign;status=if($foreign.status -in @(403,404)){'PASS'}else{'FAIL'}})
    Assert-Demo ($foreign.status -in 403,404) 'WORKSPACE_LEAK' 'Foreign context must be refused'
    Login 'pipeline.readonly@example.com'
    Probe 'restricted-role' $intent 403 'ACCESS_DENIED' $version
    Login 'pipeline.provisioning@example.com'
    $afterRead=Api GET "/deals/$dealId"
    Assert-Demo ($afterRead.status -eq 200 -and $afterRead.body.stageCategory -ceq 'OPEN' -and $afterRead.body.resourceVersion -eq $version) 'DEAL_CHANGED' 'Rejected evidence must not change Deal'
    $after=BusinessSnapshot
    $afterMutation=MutationSnapshot
    $after|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'business-after.json')
    Assert-Demo (($before|ConvertTo-Json -Depth 8 -Compress) -ceq ($after|ConvertTo-Json -Depth 8 -Compress)) 'BUSINESS_DATA_CHANGED' 'Every existing CRM/Quote/Order workspace count and hash must remain unchanged'
    Assert-Demo (($beforeMutation|ConvertTo-Json -Compress) -ceq ($afterMutation|ConvertTo-Json -Compress)) 'MUTATION_EVIDENCE_CHANGED' 'No committed Deal command audit, idempotency or outbox may be written'
    @{status='PASS';scope='MarkWon fail-closed guard only; Quote Core remains BLOCKED_AUTHORITY';server=$builder.DataSource;database=$builder.InitialCatalog;workspaceId=$fixture.workspaceId;dealId=$dealId;resourceVersion=$version;before=$before;after=$after;beforeMutation=$beforeMutation;afterMutation=$afterMutation;businessDataPreserved=$true;cases=$cases}|ConvertTo-Json -Depth 30|Set-Content (Join-Path $script:stateDirectory 'result.json')
    Write-Host 'PASS: connected MarkWon guard, authorization/version/retry rejection, all business hashes and committed Deal evidence preserved'
}finally{
    $cases|ConvertTo-Json -Depth 30|Set-Content (Join-Path $script:stateDirectory 'cases.json')
    $script:httpEvidence|ConvertTo-Json -Depth 6|Set-Content (Join-Path $script:stateDirectory 'http.json')
    Stop-DemoHost
    foreach($entry in $priorEnvironment.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
}
