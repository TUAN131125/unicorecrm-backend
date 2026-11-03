#requires -Version 7.0
[CmdletBinding()]
param()
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
$securityParseTokens=$null;$securityParseErrors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-delivery.ps1'),[ref]$securityParseTokens,[ref]$securityParseErrors)
$definition=$ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ProvisionSecurityMember'},$false)[0]
. ([ScriptBlock]::Create($definition.Extent.Text.Replace('unicore-quote-approval-isolation',('unicore-quote-response-isolation-'+$run.Substring(0,8))).Replace('B2','B3')))
$securityKey='unicore-quote-response-security-'+$run.Substring(0,8)
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','contacts.read','contacts.create','access.read','access.configure')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.update')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 SetPolicy 'b3-security-approval-v1';CustomerPolicy;StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="B3 security buyer $run"} "b3-security-buyer-$run";Check $contact 201
 $script:buyer=$contact.body.result.contact.id;if(!$script:buyer){$script:buyer=$contact.body.id}
 $sent=FreshSent 'fls'
 $body=Claim 'fls'
 $first=Api $emails[1] POST "/quotes/$($sent.id)/customer-decisions" $body "b3-fls-first-$run" $sent.resourceVersion;Check $first 200
 function AssignHistoryAccess([string]$Access){
  $role=Api $emails[0] POST '/access/roles' @{name="B3 response $Access $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.update');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='customerDecisionHistory';access=$Access})} "b3-role-$Access-$run";Check $role 200
  $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[1];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
  Assert-Demo (!!$member.membershipId) 'B3_MEMBERSHIP' 'Official owner directory must identify new membership'
  $version=if($null -ne $script:securityMemberVersion){$script:securityMemberVersion}else{$member.version}
  $assignment=Api $emails[0] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b3-assign-$Access-$run" $version;Check $assignment 200
  $script:securityMemberVersion=$assignment.body.version
 }
 AssignHistoryAccess 'READ_ONLY'
 Check (Api $emails[1] POST "/quotes/$($sent.id)/customer-decisions" $body "b3-fls-first-$run" $sent.resourceVersion) 403 'ACCESS_DENIED'
 Check (Api $emails[1] POST "/quotes/$($sent.id)/customer-decisions" (Claim 'fls-new') "b3-fls-new-$run" $first.body.version) 403 'ACCESS_DENIED'
 Check (Api $emails[1] POST "/quotes/$($sent.id)/reject" @{reason='Synthetic refusal'} "b3-fls-reject-$run" $first.body.version) 403 'ACCESS_DENIED'
 $read=Api $emails[1] GET "/quotes/$($sent.id)";Check $read 200
 Assert-Demo ($read.body.customerDecisionHistory.Count -eq 1 -and !$read.body.actions.recordCustomerResponse.allowed -and !$read.body.actions.reject.allowed) 'B3_READONLY_HISTORY' 'Read-only history remains readable with actions blocked'
 AssignHistoryAccess 'HIDDEN'
 $read=Api $emails[1] GET "/quotes/$($sent.id)";Check $read 200
 Assert-Demo (!$read.body.ContainsKey('customerDecisionHistory')) 'B3_HIDDEN_HISTORY' 'Hidden response history must be omitted'
 Check (Api $emails[1] POST "/quotes/$($sent.id)/customer-decisions" $body "b3-fls-first-$run" $sent.resourceVersion) 403 'ACCESS_DENIED'
 $proof=SQLRows "SELECT q.Status,q.ResourceVersion,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords c WHERE c.WorkspaceId=q.WorkspaceId AND c.QuoteId=q.QuoteId) Decisions,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_CUSTOMER_RESPONSE_RECORDED') Events FROM quotes.Quotes q WHERE q.WorkspaceId=@ws AND q.QuoteId=@q" @{'@ws'=$script:workspace;'@q'=$sent.id}
 Assert-Demo ($proof[0].Status -ceq 'SENT' -and $proof[0].Decisions -eq 1 -and $proof[0].Events -eq 1) 'B3_FLS_SQL' 'Denied calls must leave a single UNVERIFIED response and SENT Quote'
 $proof|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'security-sql.json');$status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 $script:httpEvidence|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$sent.id;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B3 FLS/API/SQL: $($cases.Count) requests. $script:stateDirectory"
}

