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
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 LifecyclePolicy;StartHost;LoginAll
 $r=Api $emails[0] POST '/quotes' (Draft "B4 case-variant archive race $run" '1000') "b4-case-create-$run";Check $r 201;$quote=$r.body.result.quote
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($suffix in 'lower','upper'){
   $target=if($suffix -ceq 'upper'){$quote.id.ToUpperInvariant()}else{$quote.id}
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$target/archive")
   foreach($header in @{Authorization="Bearer $($script:tokens[$emails[0]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b4-case-$suffix-$run";'X-Correlation-Id'="b4-case-$suffix-$run";'Idempotency-Key'="b4-case-$suffix-$run";'If-Match'=('"'+$quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($header.Key,$header.Value)}
   $request.Content=[Net.Http.StringContent]::new('{"reason":"Synthetic case-variant concurrency"}',[Text.Encoding]::UTF8,'application/json');$pending+=@{request=$request;task=$client.SendAsync($request);target=$target}
  }
  $results=@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$result=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{method='POST';path="/quotes/$($entry.target)/archive";concurrent=$true;response=$result});$result}finally{$response.Dispose()}})
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
 Assert-Demo (@($results|Where-Object status -eq 200).Count -eq 1 -and @($results|Where-Object status -eq 412).Count -eq 1) 'B4_CASE_RACE' 'SQL-equivalent Quote IDs must share aggregate serialization'
 $success=($results|Where-Object status -eq 200).body
 Assert-Demo ($success.aggregateId -ceq $quote.id -and $success.result.quote.id -ceq $quote.id) 'B4_CANONICAL_RECEIPT' 'Receipt must use persisted canonical identity'
 $sql=SQLRows "SELECT ResourceVersion,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND EventType='QUOTE_ARCHIVED') Events FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q" @{'@ws'=$script:workspace;'@q'=$quote.id}
 Assert-Demo ($sql[0].ResourceVersion -eq 2 -and $sql[0].Events -eq 1) 'B4_CASE_RACE_SQL' 'Race must leave one mutation and event'
 $sql|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'case-race-sql.json');$status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;quoteId=$quote.id;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B4 case-variant concurrency API/SQL. $script:stateDirectory"
}
