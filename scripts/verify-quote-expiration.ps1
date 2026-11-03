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
 $date=[DateTime]::UtcNow.Date.AddDays(2).ToString('yyyy-MM-dd');$beforeMidnight=$date+'T16:59:59.9999999Z';$afterMidnight=$date+'T17:00:00Z'
 SetPolicy 'b4-approval-v1';LifecyclePolicy;SetClock $beforeMidnight;StartHost;LoginAll
 $sent=CreateExpiring 'boundary'
 $read=Api $emails[0] GET "/quotes/$($sent.id)";Check $read 200
 Assert-Demo ($read.body.validity.state -ceq 'VALID' -and $read.body.validity.localDate -ceq $date -and $read.body.status -ceq 'SENT') 'B4_LAST_MOMENT' 'End of Workspace day must remain valid and SENT'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/expire" @{} "b4-early-expire-$run" $sent.resourceVersion) 409 'QUOTE_EXPIRATION_BLOCKED' 'QUOTE_NOT_OVERDUE'
 $beforeVersion=$sent.resourceVersion
 Stop-DemoHost;SetClock $afterMidnight;StartHost;LoginAll
 $read=Api $emails[0] GET "/quotes/$($sent.id)";Check $read 200
 Assert-Demo ($read.body.status -ceq 'SENT' -and $read.body.validity.state -ceq 'OVERDUE' -and $read.body.resourceVersion -eq $beforeVersion) 'B4_EFFECTIVE_EXPIRY' 'GET must show OVERDUE separately without persisting EXPIRED'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/send" @{deliveryId="b4_overdue_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test'} "b4-overdue-send-$run" $sent.resourceVersion) 409 'QUOTE_SEND_BLOCKED' 'QUOTE_EXPIRED'
 Check (Api $emails[1] POST "/quotes/$($sent.id)/expire" @{} "b4-expiry-denied-$run" $sent.resourceVersion) 403 'ACCESS_DENIED'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/expire" @{expiredAt=$afterMidnight} "b4-expiry-spoof-$run" $sent.resourceVersion) 422 'VALIDATION_FAILED'
 $expired=Api $emails[0] POST "/quotes/$($sent.id)/expire" @{reason='Synthetic end-of-Workspace-day expiry'} "b4-expire-$run" $sent.resourceVersion;Check $expired 200
 $q=$expired.body.result.quote
 Assert-Demo ($q.status -ceq 'EXPIRED' -and $q.resourceVersion -eq ($beforeVersion+1) -and $q.deliveryHistory.Count -eq 1 -and $q.customerDecisionHistory.Count -eq 1 -and !$q.rejectedAt -and !$q.acceptedAt) 'B4_PERSISTED_EXPIRY' 'Expiration retains delivery and unverified response history without customer rejection/acceptance'
 $replay=Api $emails[0] POST "/quotes/$($sent.id)/expire" @{reason='Synthetic end-of-Workspace-day expiry'} "b4-expire-$run" $sent.resourceVersion;Check $replay 200
 Assert-Demo ($replay.body.outcome -ceq 'REPLAYED') 'B4_EXPIRY_REPLAY' 'Same command replays original expiration receipt'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/expire" @{} "b4-expiry-stale-$run" $sent.resourceVersion) 412 'VERSION_CONFLICT'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/expire" @{} "b4-expiry-terminal-$run" $q.resourceVersion) 409 'QUOTE_EXPIRATION_BLOCKED' 'QUOTE_STATE_INVALID'
 $proof=SQLRows "SELECT q.QuoteId,q.Status,q.ResourceVersion,q.ExpiredAt,(SELECT COUNT(*) FROM OPENJSON(q.DeliveryHistoryJson)) Deliveries,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords d WHERE d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId) Decisions,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_EXPIRED') ExpiryEvents FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q" @{'@ws'=$script:workspace;'@q'=$q.id}
 Assert-Demo ($proof[0].ExpiryEvents -eq 1 -and $proof[0].Deliveries -eq 1 -and $proof[0].Decisions -eq 1) 'B4_EXPIRY_SQL' 'Exactly one expiration event must remain alongside both histories'
 $proof|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'expiration-sql.json')
 $rev=Api $emails[0] POST "/quotes/$($q.id)/revise" @{} "b4-revise-expired-$run" $q.resourceVersion;Check $rev 201
 Assert-Demo ($rev.body.result.quote.status -ceq 'DRAFT' -and !$rev.body.result.quote.customerDecisionHistory -and !$rev.body.result.quote.deliveryHistory) 'B4_EXPIRED_REVISION' 'Expired source can create a clean Draft without inherited evidence'
 Stop-DemoHost;StartHost;LoginAll
 $read=Api $emails[0] GET "/quotes/$($q.id)";Check $read 200
 Assert-Demo ($read.body.status -ceq 'EXPIRED' -and $read.body.customerDecisionHistory.Count -eq 1 -and $read.body.deliveryHistory.Count -eq 1) 'B4_EXPIRY_RESTART' 'Persisted expiry and history survive restart/login'
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 $script:httpEvidence|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
 @{status=$status;workspaceId=$script:workspace;quoteId=$sent.id;revisionId=$rev.body.aggregateId;requests=$cases.Count;testClock=@{scope='Quotes only, exact B4 Workspace, Development';before=$beforeMidnight;after=$afterMidnight};directory=$script:stateDirectory}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B4 Expiration API/SQL: $($cases.Count) requests. $script:stateDirectory"
}
