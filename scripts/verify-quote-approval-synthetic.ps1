#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser,[switch]$BrowserOnly,[switch]$ChangesOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$syntheticRoot=Join-Path (Split-Path $repo) 'task-runtime/quote-lifecycle/b1-synthetic'
$script:stateDirectory=if($ChangesOnly){Join-Path $syntheticRoot 'changes'}else{$syntheticRoot}
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
$previousResultPath=Join-Path $syntheticRoot 'result.json'
$previousResult=if(Test-Path $previousResultPath){Get-Content -LiteralPath $previousResultPath -Raw|ConvertFrom-Json -AsHashtable}else{$null}
if(($BrowserOnly -or $ChangesOnly) -and !$previousResult){throw 'Targeted verification requires an existing synthetic result with fixture identities'}
if($ChangesOnly -and (Test-Path (Join-Path $script:stateDirectory 'result.json'))){
    $archive=Join-Path $script:stateDirectory ('history/'+[guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Force $archive)
    foreach($name in 'result.json','preservation.json','entry-row-hashes.json','sql-evidence.json','http-evidence.json','cases.json','credential-scan.json','host.log','host-error.log'){
        $source=Join-Path $script:stateDirectory $name
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $archive}
    }
}
if($previousResult -and !$ChangesOnly){
    $archive=Join-Path $script:stateDirectory ('history/'+[guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Force $archive)
    foreach($name in 'result.json','preservation.json','entry-row-hashes.json','sql-evidence.json','http-evidence.json','browser'){
        $source=Join-Path $script:stateDirectory $name
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $archive -Recurse}
    }
}
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Exact verified Development database required'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine").Rows[0].Machine
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL must be local'
$workspaceKey='unicore-quote-approval-test'
$isolationKey='unicore-quote-approval-isolation'
$emails=@('quote.creator@example.test','quote.approver@example.test','quote.approver2@example.test')
$creatorCaps=@('quotes.read','quotes.create','quotes.update','contacts.create','contacts.read','workspace.context.resolve')
$approverCaps=@('quotes.read','quotes.approve','workspace.context.resolve')
$secret=$configuration['IdentityAuth:RefreshTokenPepper']
if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing secret configuration required for process-only derived credentials'
$passwords=@{}
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null;$script:workspace=$null
$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$bootstrapIndex=0;$status='FAIL';$apiStatus='NOT_COMPLETED';$before=$null
$browserMutableQuoteIds=if($ChangesOnly){@($previousResult.uiPendingQuoteId)}elseif($BrowserOnly){@($previousResult.uiLowQuoteId,$previousResult.uiHighQuoteId,$previousResult.uiPendingQuoteId)}else{@()}
function EnvValue([string]$Name,[string]$Value){if(!$original.ContainsKey($Name)){$original[$Name]=[Environment]::GetEnvironmentVariable($Name)};[Environment]::SetEnvironmentVariable($Name,$Value)}
function OverrideArray([string]$Prefix,[string[]]$Values){
    $key=$Prefix.Replace('__',':');$indices=@($configuration.Keys|Where-Object{$_ -match ('^'+[regex]::Escape($key)+':\d+$')}|ForEach-Object{[int]($_.Split(':')[-1])})
    $maximum=[Math]::Max($Values.Count-1, $(if($indices.Count){($indices|Measure-Object -Maximum).Maximum}else{0}))
    for($i=0;$i -le $maximum;$i++){EnvValue ($Prefix+'__'+$i) $Values[$i % $Values.Count]}
}
function DisableBootstraps{
    foreach($key in $configuration.Keys|Where-Object{$_ -like '*DevelopmentBootstrap:Enabled'}){EnvValue ($key.Replace(':','__')) 'false'}
    foreach($owner in 'IdentityAuth','Workspace','AccessControl','Integrations'){EnvValue ($owner+'__DevelopmentBootstrap__Enabled') 'false'}
}
function Bootstrap([string]$Email,[string[]]$Capabilities,[string]$Role,[bool]$IdentityAndWorkspace=$true){
    Assert-Demo (!(Get-NetTCPConnection -State Listen -LocalPort 5098 -ErrorAction SilentlyContinue)) 'BLOCKED_ENVIRONMENT' 'Host must stop before explicit owner bootstrap'
    DisableBootstraps
    EnvValue 'AccessControl__DevelopmentBootstrap__Enabled' 'true';EnvValue 'AccessControl__DevelopmentBootstrap__IdentityEmail' $Email
    EnvValue 'AccessControl__DevelopmentBootstrap__WorkspaceKey' $workspaceKey;EnvValue 'AccessControl__DevelopmentBootstrap__RoleName' $Role
    OverrideArray 'AccessControl__DevelopmentBootstrap__Capabilities' $Capabilities
    if($IdentityAndWorkspace){
        EnvValue 'IdentityAuth__DevelopmentBootstrap__Enabled' 'true';EnvValue 'IdentityAuth__DevelopmentBootstrap__Email' $Email
        EnvValue 'IdentityAuth__DevelopmentBootstrap__Password' $passwords[$Email];EnvValue 'IdentityAuth__DevelopmentBootstrap__DisplayName' ($Email.Split('@')[0])
        EnvValue 'Workspace__DevelopmentBootstrap__Enabled' 'true';EnvValue 'Workspace__DevelopmentBootstrap__IdentityEmail' $Email
        EnvValue 'Workspace__DevelopmentBootstrap__EnsureStudioConfiguration' 'true'
        foreach($target in @(@{Section='MemberWorkspace';Key=$workspaceKey;Name='Quote Approval Synthetic Test'},@{Section='NonMemberWorkspace';Key=$isolationKey;Name='Quote Approval Isolation Companion'})){
            $prefix='Workspace__DevelopmentBootstrap__'+$target.Section+'__'
            foreach($entry in @{Key=$target.Key;Name=$target.Name;LogoText='B1';Locale='vi';TimeZone='Asia/Ho_Chi_Minh';BaseCurrency='VND'}.GetEnumerator()){EnvValue ($prefix+$entry.Key) $entry.Value}
            OverrideArray ($prefix+'Capabilities') $creatorCaps
            OverrideArray ($prefix+'EnabledModuleKeys') @('contacts','quotes')
            OverrideArray ($prefix+'AvailableProductSpaces') @('crm')
        }
    }
    $script:bootstrapIndex++
    $process=Start-Process -FilePath (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -ArgumentList '--seed-demo' -WorkingDirectory $script:hostDirectory -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $script:stateDirectory "bootstrap-$script:bootstrapIndex.log") -RedirectStandardError (Join-Path $script:stateDirectory "bootstrap-$script:bootstrapIndex-error.log")
    Assert-Demo ($process.ExitCode -eq 0) 'BOOTSTRAP_FAILED' 'Official owner bootstrap failed; inspect local bootstrap diagnostics without copying credentials'
    DisableBootstraps
}
function SetPolicy([string]$Version){
    $prefix="QuoteApprovalPolicies__Workspaces__$($script:workspace)__"
    foreach($entry in @{Version=$Version;LineDiscountPercent='10';TotalDiscountPercent='10';PostpaidDays='30';AdvancedPaymentRequiresApproval='true';AlwaysRequireApproval='false';GrandTotalLimits__VND='100000000'}.GetEnumerator()){EnvValue ($prefix+$entry.Key) $entry.Value}
}
function StartHost{
    DisableBootstraps
    Assert-Demo (!(Get-NetTCPConnection -State Listen -LocalPort 5098 -ErrorAction SilentlyContinue)) 'BLOCKED_ENVIRONMENT' 'Port 5098 must be free'
    $script:hostProcess=Start-Process -FilePath (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -WorkingDirectory $script:hostDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:stateDirectory 'host.log') -RedirectStandardError (Join-Path $script:stateDirectory 'host-error.log')
    for($i=0;$i -lt 90;$i++){
        Assert-Demo (!$script:hostProcess.HasExited) 'HOST_FAILED' 'Normal ApiHost exited'
        try{$null=Invoke-DemoApi GET '/auth/session';return}catch{Start-Sleep -Milliseconds 500}
    }
    throw 'Normal ApiHost readiness timeout'
}
function LoginAll{
    foreach($email in $emails){
        $script:token=$null
        $response=Invoke-DemoApi POST '/auth/sessions' @{email=$email;password=$passwords[$email]} "b1-login-$run-$([guid]::NewGuid().ToString('N'))"
        Assert-Demo ($response.status -eq 200) 'LOGIN_FAILED' "Synthetic identity could not authenticate (HTTP $($response.status))"
        $script:tokens[$email]=$response.body.accessToken
    }
    $script:token=$script:tokens[$emails[0]]
}
function Api([string]$Email,[string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
    $script:token=$script:tokens[$Email]
    $response=Invoke-DemoApi $Method $Path $Body $Key $Version
    if($response.body -is [array]){$response.body=[Text.Encoding]::UTF8.GetString([byte[]]$response.body)|ConvertFrom-Json -AsHashtable}
    $cases.Add(@{actor=$Email;method=$Method;path=$Path;body=$Body;key=$Key;version=$Version;response=$response})
    return $response
}
function Check($Response,[int]$Status,[string]$Code='',[string]$Detail=''){
    Assert-Demo ($Response.status -eq $Status -and (!$Code -or $Response.body.code -ceq $Code) -and (!$Detail -or $Response.body.detail -ceq $Detail)) 'B1_API_FAILED' "Expected $Status/$Code/$Detail; actual $($Response.status)/$($Response.body.code)/$($Response.body.detail)"
}
function Draft([string]$Title,[string]$Amount){return @{buyerRef=@{type='CONTACT';id=$script:buyer};sourcePath='DIRECT_SALE';title=$Title;currency='VND';lineItems=@(@{productNameSnapshot='B1 synthetic manual service';quantity='1';unitPrice=@{amount=$Amount;currency='VND'};discountRate='0';taxMode='NONE'})}}
function CreateQuote([string]$Name,[string]$Amount){$response=Api $emails[0] POST '/quotes' (Draft "B1 synthetic $Name $run" $Amount) "b1-create-$Name-$run";Check $response 201;return $response.body.result.quote}
function RequestApproval($Quote,[string]$Name){$response=Api $emails[0] POST "/quotes/$($Quote.id)/request-approval" @{} "b1-request-$Name-$run" $Quote.resourceVersion;Check $response 200;return $response.body.result.quote}
function ParallelApprove($Quote){
    $client=[Net.Http.HttpClient]::new();$pending=@()
    try{
        foreach($email in $emails[1..2]){
            $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$($Quote.id)/approve")
            foreach($header in @{Authorization="Bearer $($script:tokens[$email])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b1-race-$email-$run";'X-Correlation-Id'="b1-race-$email-$run";'Idempotency-Key'="b1-race-$email-$run";'If-Match'=('"'+$Quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($header.Key,$header.Value)}
            $request.Content=[Net.Http.StringContent]::new('{}',[Text.Encoding]::UTF8,'application/json')
            $pending+=@{request=$request;actor=$email;task=$client.SendAsync($request)}
        }
        $results=@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$result=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{actor=$entry.actor;method='POST';path="/quotes/$($Quote.id)/approve";concurrent=$true;version=$Quote.resourceVersion;response=$result});$result}finally{$response.Dispose()}})
        return ,$results
    }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
function InvokeBrowser($Low,$High,$Pending){
    foreach($entry in @{UNICORECRM_TEST_SYNTHETIC='1';UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_APPROVER_EMAIL=$emails[1];UNICORECRM_TEST_APPROVER_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_APPROVER2_EMAIL=$emails[2];UNICORECRM_TEST_APPROVER2_PASSWORD=$passwords[$emails[2]];UNICORECRM_TEST_WORKSPACE_KEY=$workspaceKey;UNICORECRM_TEST_LOW_QUOTE_ID=$Low.id;UNICORECRM_TEST_HIGH_QUOTE_ID=$High.id;UNICORECRM_TEST_PENDING_QUOTE_ID=$Pending.id;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
    Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
    try{& npx playwright test --config playwright.quote-approval.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
    Assert-Demo ($browserExit -eq 0) 'B1_BROWSER_FAILED' 'Synthetic Connected browser acceptance failed'
}
function Snapshot{
    # Per-row hashes preserve every row present on entry. New synthetic rows are permitted;
    # no raw identity credentials or authorization tokens enter this evidence.
    $tables=@('leads.Leads','contacts.Contacts','organizations.Organizations','customers.Customers','deals.Deals','tasks.Tasks','tasks.Activities','products.Products','support.SupportCases','quotes.Quotes','deals.Pipelines','deals.PipelineStages','workspace.Workspaces','workspace.Memberships','workspace.BootstrapProjections','workspace.StudioConfigurations','workspace.StudioQuickSetups','access.Roles','access.RoleCapabilities','access.MembershipRoleAssignments','access.RoleDataScopes','access.RoleFieldSecurity','iam.Accounts','iam.Credentials')
    $result=@{}
    foreach($table in $tables){
        $columns=Invoke-DemoSql 'SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(@table) AND i.is_primary_key=1 ORDER BY ic.key_ordinal' @{'@table'=$table}
        if(!$columns.Rows.Count){continue}
        $keys=($columns.Rows|ForEach-Object{'t.['+$_.name+']'}) -join ','
        $rows=Invoke-DemoSql "SELECT (SELECT $keys FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) RowKey, CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) RowHash FROM $table t"
        $result[$table]=@{};foreach($row in $rows.Rows){
            if(($BrowserOnly -or $ChangesOnly) -and $table -ceq 'quotes.Quotes' -and $browserMutableQuoteIds -contains (($row.RowKey|ConvertFrom-Json).QuoteId)){continue}
            $result[$table][[string]$row.RowKey]=[string]$row.RowHash
        }
    }
    return $result
}
function SQLRows([string]$Query,[hashtable]$Parameters=@{}){
    $table=Invoke-DemoSql $Query $Parameters
    return ,@(foreach($row in $table.Rows){$item=@{};foreach($column in $table.Columns){$item[$column.ColumnName]=if($row[$column.ColumnName] -is [DBNull]){$null}else{$row[$column.ColumnName]}};$item})
}
try{
    Assert-Demo (!(Get-NetTCPConnection -State Listen -LocalPort 5098 -ErrorAction SilentlyContinue)) 'BLOCKED_ENVIRONMENT' 'Existing host is never stopped or reused'
    $before=Snapshot;$before|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'entry-row-hashes.json')
    EnvValue 'ASPNETCORE_ENVIRONMENT' 'Development';EnvValue 'DOTNET_ENVIRONMENT' 'Development';EnvValue 'ASPNETCORE_URLS' 'http://127.0.0.1:5098'
    EnvValue 'UNICORE_DEV_SEED_ENABLED' 'false';EnvValue 'Development__ApplyMigrations' 'false'
    EnvValue 'Workflows__InitialWorkspaceProvisioning__ResumeEnabled' 'false'
    OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044')
    if($ChangesOnly){
        $script:workspace=[string]$previousResult.workspaceId
        $uiPending=@{id=$previousResult.uiPendingQuoteId}
        Assert-Demo ($previousResult.workspaceKey -ceq $workspaceKey -and !!$uiPending.id) 'BLOCKED_ENVIRONMENT' 'Exact prior synthetic pending fixture required'
        SetPolicy 'b1-synthetic-approved-v2';StartHost;LoginAll
        $pendingRead=Api $emails[0] GET "/quotes/$($uiPending.id)";Check $pendingRead 200
        $pending=$pendingRead.body
        if($pending.status -ceq 'DRAFT' -and $pending.approvalStatus -ceq 'CHANGES_REQUESTED'){
            $pending=RequestApproval $pending 'targeted-changes-rerun'
        }
        Assert-Demo ($pending.status -ceq 'REVIEW' -and $pending.approvalStatus -ceq 'PENDING') 'B1_CHANGES_PRECONDITION' 'Retained pending synthetic Quote must be unmodified'
        $changesBefore=(Invoke-DemoSql "SELECT COUNT(*) Total FROM quotes.CommandRecords WHERE WorkspaceId=@ws AND QuoteId=@quote AND Operation='requestQuoteApprovalChangesCommand'" @{'@ws'=$script:workspace;'@quote'=$uiPending.id}).Rows[0].Total
        $eventsBefore=(Invoke-DemoSql "SELECT COUNT(*) Total FROM quotes.OutboxMessages WHERE WorkspaceId=@ws AND QuoteId=@quote AND EventType='QUOTE_APPROVAL_CHANGES_REQUESTED'" @{'@ws'=$script:workspace;'@quote'=$uiPending.id}).Rows[0].Total
        $path="/quotes/$($uiPending.id)/request-approval-changes"
        Check (Api $emails[1] POST $path @{} "b1-changes-missing-$run" $pending.resourceVersion) 422 'VALIDATION_FAILED'
        $note="B1 synthetic requested changes $run"
        $change=Api $emails[1] POST $path @{note=$note} "b1-changes-$run" $pending.resourceVersion;Check $change 200
        Assert-Demo ($change.body.result.quote.approvalStatus -ceq 'CHANGES_REQUESTED' -and $change.body.result.quote.status -ceq 'DRAFT' -and $change.body.result.quote.approvalDecisionNote -ceq $note) 'B1_CHANGES_STATE' 'Changes request must persist its note and restore DRAFT'
        $replay=Api $emails[1] POST $path @{note=$note} "b1-changes-$run" $pending.resourceVersion;Check $replay 200
        Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.version -eq $change.body.version) 'B1_CHANGES_REPLAY' 'Replay must retain original version without a second mutation'
        Check (Api $emails[2] POST $path @{note=$note} "b1-changes-stale-$run" $pending.resourceVersion) 412 'VERSION_CONFLICT'
        $proof=(Invoke-DemoSql "SELECT q.Status,q.ApprovalStatus,q.ApprovalDecisionNote,(SELECT COUNT(*) FROM quotes.CommandRecords c WHERE c.WorkspaceId=q.WorkspaceId AND c.QuoteId=q.QuoteId AND c.Operation='requestQuoteApprovalChangesCommand') Audits,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_APPROVAL_CHANGES_REQUESTED') Events FROM quotes.Quotes q WHERE q.WorkspaceId=@ws AND q.QuoteId=@quote" @{'@ws'=$script:workspace;'@quote'=$uiPending.id}).Rows[0]
        Assert-Demo ($proof.Status -ceq 'DRAFT' -and $proof.ApprovalStatus -ceq 'CHANGES_REQUESTED' -and $proof.ApprovalDecisionNote -ceq $note -and $proof.Audits -eq ($changesBefore+1) -and $proof.Events -eq ($eventsBefore+1)) 'B1_CHANGES_SQL' 'SQL must append exactly one decision audit/outbox and the authoritative note per run'
        $apiStatus='PASS';$status='PASS'
        Write-Host 'PASS targeted real API request-changes: required note, independent decision, replay, stale actor version and SQL exactly-once.'
        return
    }
    if($BrowserOnly){
        $script:workspace=[string]$previousResult.workspaceId
        Assert-Demo ($previousResult.workspaceKey -ceq $workspaceKey -and $browserMutableQuoteIds.Count -eq 3 -and @($browserMutableQuoteIds|Where-Object{!$_}).Count -eq 0) 'BLOCKED_ENVIRONMENT' 'Exact prior synthetic fixtures required'
        $uiLow=@{id=$previousResult.uiLowQuoteId};$uiHigh=@{id=$previousResult.uiHighQuoteId};$uiPending=@{id=$previousResult.uiPendingQuoteId}
        SetPolicy 'b1-synthetic-approved-v2';StartHost;LoginAll
        $apiStatus='NOT_EXECUTED_BROWSER_ONLY'
        InvokeBrowser $uiLow $uiHigh $uiPending
        $status='PASS'
        return
    }
    Bootstrap $emails[0] $creatorCaps 'B1 synthetic creator baseline'
    Bootstrap $emails[1] $approverCaps 'B1 synthetic independent approver'
    Bootstrap $emails[2] $approverCaps 'B1 synthetic independent approver'
    $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$workspaceKey}).Rows[0].WorkspaceId
    Assert-Demo (!!$script:workspace) 'BOOTSTRAP_FAILED' 'Synthetic Workspace must exist'
    SetPolicy 'b1-synthetic-approved-v1';StartHost;LoginAll
    $capabilities=(Api $emails[0] GET '/access/context').body.capabilities
    $creatorInitiallyApprove=$capabilities -contains 'quotes.approve'
    $buyer=Api $emails[0] POST '/contacts' @{fullName='B1 synthetic buyer'} "b1-buyer-$run";Check $buyer 201
    $script:buyer=$buyer.body.result.contact.id
    if(!$script:buyer){$script:buyer=$buyer.body.id}
    Assert-Demo (!!$script:buyer) 'B1_BUYER' 'Contact owner must return a buyer identity'
    $low=CreateQuote 'equality' '100000000';$low=RequestApproval $low 'equality'
    Assert-Demo ($low.approvalStatus -ceq 'NOT_REQUIRED' -and $low.approvalRequired -eq $false) 'B1_EQUALITY' 'Equality is not greater than the policy limit'
    $high=CreateQuote 'approval' '100000001';$pending=RequestApproval $high 'approval'
    Assert-Demo ($pending.status -ceq 'REVIEW' -and $pending.approvalStatus -ceq 'PENDING') 'B1_PENDING' 'High Quote must become pending REVIEW'
    if(!$creatorInitiallyApprove){Check (Api $emails[0] POST "/quotes/$($high.id)/approve" @{} "b1-no-cap-$run" $pending.resourceVersion) 403 'ACCESS_DENIED'}
    Stop-DemoHost;Bootstrap $emails[0] @('quotes.approve') 'B1 synthetic creator approval capability' $false
    StartHost;LoginAll
    Check (Api $emails[0] POST "/quotes/$($high.id)/approve" @{} "b1-self-$run" $pending.resourceVersion) 403 'ACCESS_DENIED' 'SELF_APPROVAL_FORBIDDEN'
    $approved=Api $emails[1] POST "/quotes/$($high.id)/approve" @{} "b1-approve-$run" $pending.resourceVersion;Check $approved 200
    Assert-Demo ($approved.body.result.quote.approvalStatus -ceq 'APPROVED') 'B1_APPROVED' 'Independent actor approval must persist'
    $replay=Api $emails[1] POST "/quotes/$($high.id)/approve" @{} "b1-approve-$run" $pending.resourceVersion;Check $replay 200
    Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.version -eq $approved.body.version) 'B1_REPLAY' 'Approval replay must not create another mutation'
    Check (Api $emails[2] POST "/quotes/$($high.id)/approve" @{} "b1-stale-$run" $pending.resourceVersion) 412 'VERSION_CONFLICT'
    $race=CreateQuote 'race' '100000001';$race=RequestApproval $race 'race';$raceResults=ParallelApprove $race
    Assert-Demo (($raceResults.status|Sort-Object) -join ',' -ceq '200,412') 'B1_CONCURRENCY' 'Two independent actors at one version must yield one commit and one version conflict'
    $raceAudit=(Invoke-DemoSql "SELECT (SELECT COUNT(*) FROM quotes.CommandRecords WHERE WorkspaceId=@ws AND QuoteId=@quote AND Operation='approveQuoteCommand') Audits,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws AND QuoteId=@quote AND EventType='QUOTE_APPROVED') Events" @{'@ws'=$script:workspace;'@quote'=$race.id}).Rows[0]
    Assert-Demo ($raceAudit.Audits -eq 1 -and $raceAudit.Events -eq 1) 'B1_EXACTLY_ONCE' 'SQL must contain exactly one approval audit and outbox event'
    $edit=Api $emails[0] POST "/quotes/$($high.id)/update-draft" (Draft "B1 edited approved content $run" '100000002') "b1-edit-$run" $approved.body.version;Check $edit 200
    Assert-Demo ($edit.body.result.quote.status -ceq 'DRAFT' -and !$edit.body.result.quote.ContainsKey('approvalContentFingerprint') -and !$edit.body.result.quote.ContainsKey('approvalStatus')) 'B1_INVALIDATE' 'Content replacement must clear approval binding and restore DRAFT'
    $fresh=RequestApproval $edit.body.result.quote 'reapproval'
    $stale=CreateQuote 'policy-stale' '100000001';$stale=RequestApproval $stale 'policy-stale'
    Stop-DemoHost;SetPolicy 'b1-synthetic-approved-v2';StartHost;LoginAll
    Check (Api $emails[1] POST "/quotes/$($stale.id)/approve" @{} "b1-policy-stale-$run" $stale.resourceVersion) 409 'QUOTE_APPROVAL_STALE'
    $read=Api $emails[0] GET "/quotes/$($high.id)";Check $read 200
    Assert-Demo ($read.body.approvalStatus -ceq 'PENDING') 'B1_RESTART' 'Pending approval must survive normal Backend restart/login'
    $uiLow=CreateQuote 'ui-low' '100000000';$uiHigh=CreateQuote 'ui-high' '100000001';$uiPending=CreateQuote 'ui-pending' '100000001';$uiPending=RequestApproval $uiPending 'ui-pending'
    $apiStatus='PASS'
    if($Browser){
        InvokeBrowser $uiLow $uiHigh $uiPending
    }
    $status='PASS'
    Write-Host 'PASS B1 synthetic API: independent approval, capability+self denial, replay, stale version, concurrent actors, SQL exactly-once, content invalidation, policy restart.'
}finally{
    Stop-DemoHost
    if($script:workspace){
        $sql=SQLRows 'SELECT q.QuoteId,q.Status,q.ResourceVersion,q.ApprovalStatus,q.ApprovalRequired,q.ApprovalRequestedBy,q.ApprovedBy,q.ApprovalDecisionNote,q.ApprovalContentFingerprint,q.ApprovalPolicyVersion FROM quotes.Quotes q WHERE q.WorkspaceId=@ws ORDER BY q.QuoteId' @{'@ws'=$script:workspace}
        $audit=SQLRows 'SELECT QuoteId,Operation,ActorId,PriorVersion,ResourceVersion,AuditId,OccurredAt FROM quotes.CommandRecords WHERE WorkspaceId=@ws ORDER BY OccurredAt,ScopeKey' @{'@ws'=$script:workspace}
        $outbox=SQLRows 'SELECT QuoteId,EventType,EventId,PayloadJson FROM quotes.OutboxMessages WHERE WorkspaceId=@ws ORDER BY OccurredAt,EventId' @{'@ws'=$script:workspace}
        @{quotes=$sql;audits=$audit;outbox=$outbox}|ConvertTo-Json -Depth 20|Set-Content (Join-Path $script:stateDirectory 'sql-evidence.json')
        if($ChangesOnly){@{quotes=$sql;audits=$audit;outbox=$outbox}|ConvertTo-Json -Depth 20|Set-Content (Join-Path $script:stateDirectory 'sql.json')}
    }
    $preserved=$true;$differences=[Collections.Generic.List[object]]::new()
    if($before){$after=Snapshot;foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if(!$after[$table].ContainsKey($key) -or $after[$table][$key] -cne $before[$table][$key]){$preserved=$false;$differences.Add(@{table=$table;key=$key})}}};@{allPreexistingRowsUnchanged=$preserved;excludedBrowserFixtureQuoteIds=$browserMutableQuoteIds;differences=$differences}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'preservation.json')}
    @{status=$status;apiStatus=$apiStatus;preserved=$preserved;workspaceKey=$workspaceKey;workspaceId=$script:workspace;requiredNonMemberCompanion=$isolationKey;syntheticEmails=$emails;baselineCapabilityNegative=if($BrowserOnly){'NOT_EXECUTED_BROWSER_ONLY'}elseif($creatorInitiallyApprove){'NOT_EXECUTED_ALREADY_GRANTED_ON_PRIOR_RUN'}else{'EXECUTED'};browserRequested=([bool]$Browser -or [bool]$BrowserOnly);browserOnly=[bool]$BrowserOnly;uiLowQuoteId=$uiLow.id;uiHighQuoteId=$uiHigh.id;uiPendingQuoteId=$uiPending.id;cases=$cases}|ConvertTo-Json -Depth 40|Set-Content (Join-Path $script:stateDirectory 'result.json')
    $script:httpEvidence|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
    $cases|ConvertTo-Json -Depth 40|Set-Content (Join-Path $script:stateDirectory 'cases.json')
    $passwordMatches=0;$tokenMatches=0;$jwtMatches=0
    $scanFiles=@(Get-ChildItem -LiteralPath $script:stateDirectory -File -Recurse|Where-Object Extension -in '.json','.log','.md','.txt')
    foreach($file in $scanFiles){$text=[IO.File]::ReadAllText($file.FullName);foreach($password in $passwords.Values){if($password -and $text.Contains($password)){$passwordMatches++}};foreach($token in $script:tokens.Values){if($token -and $text.Contains($token)){$tokenMatches++}};$jwtMatches+=[regex]::Matches($text,'(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}').Count}
    @{textFiles=$scanFiles.Count;passwordMatches=$passwordMatches;exactTokenMatches=$tokenMatches;jwtMatches=$jwtMatches}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'credential-scan.json')
    foreach($entry in $original.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
    if(!$preserved){throw 'PRESERVATION_FAILED: preexisting business rows changed; inspect hashed evidence'}
    if($passwordMatches -or $tokenMatches -or $jwtMatches){throw 'CREDENTIAL_EVIDENCE_FAILED: inspect evidence securely; no credential values emitted'}
}
