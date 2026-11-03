#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-customer-decision/guards-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-approval-synthetic.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Verification helper parse failure'}
$helpers=@('SetPolicy','Draft','RequestApproval','EnvValue','OverrideArray','DisableBootstraps','StartHost','LoginAll','Api','Check','Snapshot','SQLRows')
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($definition.Name -in $helpers){. ([ScriptBlock]::Create($definition.Extent.Text))}}
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
$script:buyer=(Invoke-DemoSql 'SELECT TOP(1) ContactId FROM contacts.Contacts WHERE WorkspaceId=@ws ORDER BY ContactId' @{'@ws'=$script:workspace}).Rows[0].ContactId
function CustomerPolicy([string]$Version='b3-user-approved-v1',[string]$Acceptance='true',[string]$Rejection='true'){
 $prefix="QuoteCustomerDecisionPolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version=$Version;AllowReportedAcceptance=$Acceptance;AllowRejection=$Rejection}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}
function FreshSent([string]$Name){
 $response=Api $emails[0] POST '/quotes' (Draft "B3 $Name $run" '1000') "b3-create-$Name-$run";Check $response 201
 $quote=RequestApproval $response.body.result.quote "b3-request-$Name-$run"
 $response=Api $emails[0] POST "/quotes/$($quote.id)/send" @{deliveryId="b3_delivery_$Name`_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test';note='Synthetic employee confirmation; no email sent'} "b3-send-$Name-$run" $quote.resourceVersion
 Check $response 200;return $response.body.result.quote
}
function Claim([string]$Name){return @{decisionId="b3_decision_$Name`_$run";evidenceType='USER_REPORTED_ACCEPTANCE';sourceChannel='EMAIL';sourceDescription='Synthetic employee report of customer agreement; UNVERIFIED'}}
function Record($Quote,$Body,[string]$Key){return Api $emails[0] POST "/quotes/$($Quote.id)/customer-decisions" $Body $Key $Quote.resourceVersion}
function ParallelDecision($Quote){
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($name in 'one','two'){
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$($Quote.id)/customer-decisions")
   foreach($header in @{Authorization="Bearer $($script:tokens[$emails[0]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b3-race-$name-$run";'X-Correlation-Id'="b3-race-$name-$run";'Idempotency-Key'="b3-race-$name-$run";'If-Match'=('"'+$Quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($header.Key,$header.Value)}
   $request.Content=[Net.Http.StringContent]::new(((Claim "race-$name")|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json');$pending+=@{request=$request;task=$client.SendAsync($request)}
  }
  return ,@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$result=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{method='POST';path="/quotes/$($Quote.id)/customer-decisions";concurrent=$true;response=$result});$result}finally{$response.Dispose()}})
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 SetPolicy 'b1-synthetic-approved-v2';CustomerPolicy;StartHost;LoginAll
 $sent=FreshSent 'main';$body=Claim 'main'
 $first=Record $sent $body "b3-report-$run";Check $first 200
 Assert-Demo ($first.body.result.quote.status -ceq 'SENT' -and $first.body.result.quote.customerDecisionHistory.Count -eq 1 -and $first.body.result.quote.customerDecisionHistory[0].verificationStatus -ceq 'UNVERIFIED') 'B3_REPORT' 'Employee report must remain UNVERIFIED/SENT'
 Check (Record $sent $body "b3-report-$run") 200
 Check (Record $sent $body "b3-alias-$run") 200
 $changed=$body.Clone();$changed.sourceDescription='Different report'
 Check (Record $sent $changed "b3-report-$run") 409 'IDEMPOTENCY_KEY_REUSED'
 Check (Record $first.body.result.quote $changed "b3-reuse-id-$run") 409 'QUOTE_CUSTOMER_DECISION_ID_REUSED'
 Check (Record $sent (Claim 'stale') "b3-stale-$run") 412 'VERSION_CONFLICT'
 foreach($field in 'verified','verificationStatus','quoteRevision','commercialContentFingerprint','buyerRef','workspaceId','actorId'){
  $fake=(Claim "spoof-$field");$fake[$field]='client-spoof';Check (Record $first.body.result.quote $fake "b3-spoof-$field-$run") 422 'VALIDATION_FAILED'
 }
 $fake=Claim 'provider';$fake.evidenceType='VERIFIED_ACCEPTANCE';Check (Record $first.body.result.quote $fake "b3-fake-provider-$run") 422 'VALIDATION_FAILED'
 $fake=Claim 'future';$fake.reportedCustomerDecisionAt=[DateTime]::UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");Check (Record $first.body.result.quote $fake "b3-future-$run") 422 'VALIDATION_FAILED'
 Check (Api $emails[1] POST "/quotes/$($sent.id)/customer-decisions" (Claim 'denied') "b3-denied-$run" $first.body.version) 403 'ACCESS_DENIED'
 $ownWorkspace=$script:workspace;$script:workspace='ws_11f7ec273a2849a7b8d7e1aef5e53ed4'
 Check (Record $first.body.result.quote (Claim 'foreign') "b3-foreign-$run") 404 'RESOURCE_NOT_FOUND';$script:workspace=$ownWorkspace
 Check (Api $emails[0] POST "/quotes/$($sent.id)/reject" @{reason=' '} "b3-empty-reason-$run" $first.body.version) 422 'VALIDATION_FAILED'
 $rejected=Api $emails[0] POST "/quotes/$($sent.id)/reject" @{reason='Synthetic employee reports customer declined price';note='Not independently verified'} "b3-reject-$run" $first.body.version;Check $rejected 200
 Assert-Demo ($rejected.body.result.quote.status -ceq 'REJECTED' -and $rejected.body.result.quote.customerDecisionHistory.Count -eq 2 -and $rejected.body.result.quote.deliveryHistory.Count -eq 1) 'B3_REJECTION' 'Reject must preserve response and delivery histories'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/reject" @{reason='Synthetic employee reports customer declined price';note='Not independently verified'} "b3-reject-$run" $first.body.version) 200
 Check (Record $rejected.body.result.quote (Claim 'terminal') "b3-terminal-$run") 409 'QUOTE_CUSTOMER_RESPONSE_BLOCKED'
 Check (Record $sent $body "b3-report-$run") 200
 $ledger=SQLRows 'SELECT DecisionType,VerificationStatus,ContentResourceVersion,ResultResourceVersion,ContentFingerprint,PolicyVersion,ActorId FROM quotes.CustomerDecisionRecords WHERE WorkspaceId=@ws AND QuoteId=@q ORDER BY ResultResourceVersion' @{'@ws'=$script:workspace;'@q'=$sent.id}
 Assert-Demo ($ledger.Count -eq 2 -and @($ledger|Where-Object VerificationStatus -cne 'UNVERIFIED').Count -eq 0) 'B3_SQL' 'Ledger must contain exactly the employee report and rejection'
 $ledger|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'ledger.json')
 $raceQuote=FreshSent 'race';$race=ParallelDecision $raceQuote
 Assert-Demo (@($race|Where-Object status -eq 200).Count -eq 1 -and @($race|Where-Object status -eq 412).Count -eq 1) 'B3_CONCURRENCY' 'Exactly one decision must commit; stale contender must fail'
 $raceSql=SQLRows "SELECT (SELECT COUNT(*) FROM quotes.CustomerDecisionRecords WHERE WorkspaceId=@ws AND QuoteId=@q) Decisions,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws AND QuoteId=@q AND EventType='QUOTE_CUSTOMER_RESPONSE_RECORDED') Events" @{'@ws'=$script:workspace;'@q'=$raceQuote.id}
 Assert-Demo ($raceSql[0].Decisions -eq 1 -and $raceSql[0].Events -eq 1) 'B3_RACE_SQL' 'Concurrent decisions must leave one ledger row and event'
 $raceSql|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'concurrency.json')
 $restricted=FreshSent 'restricted'
 $ui=FreshSent 'browser' 
 Stop-DemoHost;CustomerPolicy 'b3-user-approved-v2';StartHost;LoginAll
 $read=Api $emails[0] GET "/quotes/$($sent.id)";Check $read 200
 Assert-Demo ($read.body.status -ceq 'REJECTED' -and $read.body.customerDecisionHistory.Count -eq 2) 'B3_PERSISTENCE' 'Restart/login must retain historical decisions'
 Check (Api $emails[0] POST "/workflows/quote-acceptance/$($ui.id)/accept-and-close-deal" @{} "b3-accept-guard-$run" $ui.resourceVersion) 409 'QUOTE_ACCEPTANCE_BLOCKED'
 Stop-DemoHost;CustomerPolicy '';StartHost;LoginAll
 Check (Record $sent $body "b3-report-$run") 503 'INTEGRATION_UNAVAILABLE' 'CUSTOMER_DECISION_POLICY_MISSING_OR_INVALID'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/reject" @{reason='Synthetic employee reports customer declined price';note='Not independently verified'} "b3-reject-$run" $first.body.version) 503 'INTEGRATION_UNAVAILABLE' 'CUSTOMER_DECISION_POLICY_MISSING_OR_INVALID'
 Stop-DemoHost;CustomerPolicy 'b3-disabled'  'false' 'false';StartHost;LoginAll
 Check (Record $sent $body "b3-report-$run") 409 'QUOTE_CUSTOMER_RESPONSE_BLOCKED'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/reject" @{reason='Synthetic employee reports customer declined price';note='Not independently verified'} "b3-reject-$run" $first.body.version) 409 'QUOTE_REJECTION_BLOCKED'
 if($Browser){
  Stop-DemoHost;CustomerPolicy;OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');StartHost;LoginAll
  foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_APPROVER_EMAIL=$emails[1];UNICORECRM_TEST_APPROVER_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_WORKSPACE_KEY='unicore-quote-approval-test';UNICORECRM_TEST_LOW_QUOTE_ID=$ui.id;UNICORECRM_TEST_RESTRICTED_QUOTE_ID=$restricted.id;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
  Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
  try{& npx playwright test --config playwright.quote-customer-response.config.ts;$exit=$LASTEXITCODE}finally{Pop-Location}
  Assert-Demo ($exit -eq 0) 'B3_BROWSER' 'Connected browser failed'
 }
 $status='PASS' 
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 $script:httpEvidence|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
 @{status=$status;requests=$cases.Count;quoteId=$sent.id;browserQuoteId=$ui.id;workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B3 API/SQL: $($cases.Count) requests. $script:stateDirectory"
}
