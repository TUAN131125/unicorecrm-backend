#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-lifecycle-b4/api-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-approval-synthetic.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Verification helper parse failure'}
$helpers=@('SetPolicy','Draft','RequestApproval','EnvValue','OverrideArray','DisableBootstraps','StartHost','LoginAll','Api','Check','Snapshot','SQLRows')
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($definition.Name -in $helpers){. ([ScriptBlock]::Create($definition.Extent.Text))}}
function OverrideArray([string]$Prefix,[string[]]$Values){
 if(!$Values.Count){throw 'Empty override array is not admitted'}
 $key=$Prefix.Replace('__',':')
 $indices=@($configuration.Keys|Where-Object{$_ -match ('^'+[regex]::Escape($key)+':\d+$')}|ForEach-Object{[int]($_.Split(':')[-1])})
 $indices+=@([Environment]::GetEnvironmentVariables().Keys|Where-Object{$_ -match ('^'+[regex]::Escape($Prefix)+'__\d+$')}|ForEach-Object{[int](($_ -split '__')[-1])})
 $maximum=[Math]::Max($Values.Count-1, $(if($indices.Count){($indices|Measure-Object -Maximum).Maximum}else{0}))
 for($i=0;$i -le $maximum;$i++){EnvValue ($Prefix+'__'+$i) $Values[$i % $Values.Count]}
}
$configuration=Read-DemoConfiguration;$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Verified Development database required'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine").Rows[0].Machine
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL must be local'
$script:workspace='ws_096302f33ec94b3fb6ab9a41422d5531'
$emails=@('quote.creator@example.test','quote.approver@example.test');$passwords=@{}
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing process-only credential derivation required'
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null;$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$status='FAIL'
$fixture=Get-Content (Join-Path (Split-Path $repo) 'task-runtime/quote-lifecycle-b4/api-32c88969d33c4371878a3633c9ff3edd/result.json') -Raw|ConvertFrom-Json -AsHashtable
$script:workspace=$fixture.workspaceId;$script:buyer=$fixture.buyerId
function LifecyclePolicy{
 $prefix="QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
 $prefix="QuoteCustomerDecisionPolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-customer-v1';AllowReportedAcceptance='true';AllowRejection='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}
function SetClock([string]$Utc){
 EnvValue 'QuoteLifecycleVerification__Enabled' 'true';EnvValue 'QuoteLifecycleVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteLifecycleVerification__UtcNow' $Utc
}
function CreateExpiring([string]$Name){
 $body=Draft "B4 expiry $Name $run" '1000';$body.validUntil=$date
 $r=Api $emails[0] POST '/quotes' $body "b4-expiry-create-$Name-$run";Check $r 201
 $q=RequestApproval $r.body.result.quote "b4-expiry-request-$Name-$run"
 $r=Api $emails[0] POST "/quotes/$($q.id)/send" @{deliveryId="b4_expiry_$Name`_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test'} "b4-expiry-send-$Name-$run" $q.resourceVersion;Check $r 200
 $r=Api $emails[0] POST "/quotes/$($q.id)/customer-decisions" @{decisionId="b4_expiry_report_$Name`_$run";evidenceType='USER_REPORTED_ACCEPTANCE';sourceChannel='EMAIL';sourceDescription='Synthetic B4 employee report; unverified'} "b4-expiry-report-$Name-$run" $r.body.version;Check $r 200
 return $r.body.result.quote
}
function NormalSent([string]$Name,[bool]$Shipping=$false){
 $body=Draft "B4 browser $Name $run" '1000'
 if($Shipping){
  $body.adjustments=@(@{id='b4_shipping';label='B4 shipping';type='SHIPPING_FEE';calculation='FIXED_AMOUNT';value='200'})
  $body.paymentAgreement=@{kind='FULL_PAYMENT';currency='VND';lines=@(@{sequence=1;label='Full payment';purpose='FULL';amountRule=@{type='REMAINDER'};dueRule=@{type='EVENT_RELATIVE';event='ORDER_CONFIRMED';offsetDays=0;dayBasis='CALENDAR'};allowedMethodCodes=@('BANK_TRANSFER');fulfillmentGate='NONE'})}
 }
 $r=Api $emails[0] POST '/quotes' $body "b4-ui-create-$Name-$run";Check $r 201
 $q=RequestApproval $r.body.result.quote "b4-ui-request-$Name-$run"
 $r=Api $emails[0] POST "/quotes/$($q.id)/send" @{deliveryId="b4_ui_$Name`_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test'} "b4-ui-send-$Name-$run" $q.resourceVersion;Check $r 200
 $r=Api $emails[0] POST "/quotes/$($q.id)/customer-decisions" @{decisionId="b4_ui_report_$Name`_$run";evidenceType='USER_REPORTED_ACCEPTANCE';sourceChannel='EMAIL';sourceDescription='Synthetic B4 employee-reported response; unverified'} "b4-ui-report-$Name-$run" $r.body.version;Check $r 200
 return $r.body.result.quote
}
function SourceHash([string]$Id){return [string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT q.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$Id}).Rows[0].Hash}
function BrowserPhase([string]$Phase){
 foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_APPROVER_EMAIL=$emails[1];UNICORECRM_TEST_APPROVER_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_WORKSPACE_KEY=$fixture.workspaceKey;UNICORECRM_TEST_LOW_QUOTE_ID=$source.id;UNICORECRM_TEST_ARCHIVE_QUOTE_ID=$rejected.id;UNICORECRM_TEST_RESTRICTED_QUOTE_ID=$restricted.id;UNICORECRM_TEST_EXPIRY_QUOTE_ID=$expiry.id;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory ("browser-"+$Phase))}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
 try{& npx playwright test --config playwright.quote-lifecycle.config.ts --grep "@$Phase";$exit=$LASTEXITCODE}finally{Pop-Location}
 Assert-Demo ($exit -eq 0) 'B4_BROWSER' "Connected browser phase $Phase failed"
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044')
 SetPolicy 'b4-approval-v1';LifecyclePolicy;StartHost;LoginAll
 $source=NormalSent 'revision' $true;$sourceHash=SourceHash $source.id
 $target=NormalSent 'rejected'
 $response=Api $emails[0] POST "/quotes/$($target.id)/reject" @{reason='Synthetic browser rejection source'} "b4-ui-reject-$run" $target.resourceVersion;Check $response 200;$rejected=$response.body.result.quote
 $restricted=NormalSent 'restricted'
 Stop-DemoHost
 $date=[DateTime]::UtcNow.Date.AddDays(2).ToString('yyyy-MM-dd');$beforeMidnight=$date+'T16:59:59.9999999Z';$afterMidnight=$date+'T17:00:00Z'
 SetClock $beforeMidnight;StartHost;LoginAll;$expiry=CreateExpiring 'browser';Stop-DemoHost
 EnvValue 'QuoteLifecycleVerification__Enabled' 'false';StartHost;LoginAll;BrowserPhase 'normal'
 Assert-Demo ((SourceHash $source.id) -ceq $sourceHash) 'B4_BROWSER_IMMUTABLE' 'Revision browser must leave full source SQL row unchanged'
 $chain=SQLRows 'SELECT QuoteId,RootQuoteId,RevisionOfQuoteId,QuoteRevision,ResourceVersion,Status,Title FROM quotes.Quotes WHERE WorkspaceId=@ws AND RootQuoteId=@root ORDER BY QuoteRevision' @{'@ws'=$script:workspace;'@root'=$source.rootQuoteId}
 Assert-Demo ($chain.Count -eq 2 -and $chain[1].QuoteRevision -eq 2 -and $chain[1].Status -ceq 'DRAFT' -and $chain[1].Title -ceq 'B4 browser edited revision') 'B4_BROWSER_REVISION_SQL' 'Browser must create and edit only the new Draft'
 $chain|ConvertTo-Json -Depth 6|Set-Content (Join-Path $script:stateDirectory 'browser-revision-sql.json')
 Stop-DemoHost;SetClock $afterMidnight;StartHost;LoginAll;BrowserPhase 'expiry'
 $proof=SQLRows "SELECT q.QuoteId,q.Status,q.ArchivedAt,q.ResourceVersion,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords d WHERE d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId) Decisions,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_EXPIRED') ExpiryEvents,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_ARCHIVED') ArchiveEvents FROM quotes.Quotes q WHERE q.WorkspaceId=@ws AND q.QuoteId IN (@a,@b)" @{'@ws'=$script:workspace;'@a'=$rejected.id;'@b'=$expiry.id}
 Assert-Demo ($proof.Count -eq 2 -and @($proof|Where-Object ArchiveEvents -ne 1).Count -eq 0 -and ($proof|Where-Object QuoteId -ceq $expiry.id).ExpiryEvents -eq 1) 'B4_BROWSER_RETENTION_SQL' 'Browser must persist exactly one archive per target and one explicit expiration'
 $proof|ConvertTo-Json -Depth 6|Set-Content (Join-Path $script:stateDirectory 'browser-retention-sql.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;sourceQuoteId=$source.id;archivedQuoteId=$rejected.id;expiryQuoteId=$expiry.id;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B4 Connected browser/API/SQL. $script:stateDirectory"
}
