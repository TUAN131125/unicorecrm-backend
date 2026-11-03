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
function Archive($Quote,[string]$Name){return Api $emails[0] POST "/quotes/$($Quote.id)/archive" @{reason='Synthetic B4 retention test'} "b4-archive-$Name-$run" $Quote.resourceVersion}
function CreateDraft([string]$Name,[string]$Amount='1000'){
 $r=Api $emails[0] POST '/quotes' (Draft "B4 archive $Name $run" $Amount) "b4-archive-create-$Name-$run";Check $r 201;return $r.body.result.quote
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 $date=[DateTime]::UtcNow.Date.AddDays(2).ToString('yyyy-MM-dd');$beforeMidnight=$date+'T16:59:59.9999999Z';$afterMidnight=$date+'T17:00:00Z'
 SetPolicy 'b4-approval-v1';LifecyclePolicy;SetClock $beforeMidnight;StartHost;LoginAll
 $draft=CreateDraft 'draft'
 Check (Api $emails[0] POST "/quotes/$($draft.id)/archive" @{} "b4-archive-no-reason-$run" $draft.resourceVersion) 422 'VALIDATION_FAILED'
 Check (Api $emails[1] POST "/quotes/$($draft.id)/archive" @{reason='Synthetic denial probe'} "b4-archive-permission-$run" $draft.resourceVersion) 403 'ACCESS_DENIED'
 $draftArchived=Archive $draft 'draft';Check $draftArchived 200
 Assert-Demo ($draftArchived.body.result.quote.status -ceq 'DRAFT' -and !!$draftArchived.body.result.quote.archivedAt -and $draftArchived.body.version -eq 2) 'B4_ARCHIVE_DRAFT' 'Archive is durable metadata, not status replacement or delete'
 $replay=Archive $draft 'draft';Check $replay 200;Assert-Demo ($replay.body.outcome -ceq 'REPLAYED') 'B4_ARCHIVE_REPLAY' 'Retry must not duplicate archive'
 Check (Api $emails[0] POST "/quotes/$($draft.id)/archive" @{reason='Different reason'} "b4-archive-draft-$run" $draft.resourceVersion) 409 'IDEMPOTENCY_KEY_REUSED'
 Check (Archive $draft 'stale') 412 'VERSION_CONFLICT'
 $review=RequestApproval (CreateDraft 'review' '150000000') 'b4-archive-review'
 Assert-Demo ($review.status -ceq 'REVIEW') 'B4_REVIEW_FIXTURE' 'B1 policy must create real REVIEW'
 $reviewArchived=Archive $review 'review';Check $reviewArchived 200
 Assert-Demo ($reviewArchived.body.result.quote.status -ceq 'REVIEW' -and $reviewArchived.body.result.quote.approvalStatus -ceq $review.approvalStatus) 'B4_ARCHIVE_REVIEW' 'Archive preserves approval evidence/state'
 Check (Api $emails[1] POST "/quotes/$($review.id)/approve" @{} "b4-archived-approve-$run" $reviewArchived.body.version) 409 'QUOTE_APPROVAL_STALE'
 $sent=CreateExpiring 'rejected'
 Check (Archive $sent 'active-sent') 409 'QUOTE_ARCHIVE_BLOCKED' 'QUOTE_STATE_INVALID'
 $rejected=Api $emails[0] POST "/quotes/$($sent.id)/reject" @{reason='Synthetic employee reports customer refusal'} "b4-archive-reject-$run" $sent.resourceVersion;Check $rejected 200
 $old=$rejected.body.result.quote
 $rejectedRevision=Api $emails[0] POST "/quotes/$($old.id)/revise" @{} "b4-revise-rejected-$run" $old.resourceVersion;Check $rejectedRevision 201
 $revision=$rejectedRevision.body.result.quote
 Assert-Demo ($revision.status -ceq 'DRAFT' -and $revision.quoteRevision -eq 2 -and $revision.revisionOfQuoteId -ceq $old.id -and !$revision.deliveryHistory -and !$revision.customerDecisionHistory -and !$revision.approvalContentFingerprint) 'B4_REVISE_REJECTED' 'Rejected source creates a fresh Draft without inheriting customer/delivery/approval evidence'
 $sourceRead=Api $emails[0] GET "/quotes/$($old.id)";Check $sourceRead 200
 Assert-Demo ($sourceRead.body.resourceVersion -eq $old.resourceVersion -and $sourceRead.body.customerDecisionHistory.Count -eq 2 -and $sourceRead.body.deliveryHistory.Count -eq 1) 'B4_REJECTED_IMMUTABLE' 'Revision must not mutate rejected source or its histories'
 $archived=Archive $old 'rejected';Check $archived 200
 Assert-Demo ($archived.body.result.quote.status -ceq 'REJECTED' -and $archived.body.result.quote.customerDecisionHistory.Count -eq 2 -and $archived.body.result.quote.deliveryHistory.Count -eq 1) 'B4_ARCHIVE_REJECTED' 'Archive must retain both human responses and delivery history'
 Check (Api $emails[0] POST "/quotes/$($old.id)/revise" @{} "b4-revise-archived-$run" $archived.body.version) 409 'QUOTE_REVISION_BLOCKED' 'QUOTE_STATE_INVALID'
 $expirySource=CreateExpiring 'expired'
 Stop-DemoHost;SetClock $afterMidnight;StartHost;LoginAll
 $expired=Api $emails[0] POST "/quotes/$($expirySource.id)/expire" @{} "b4-archive-expire-$run" $expirySource.resourceVersion;Check $expired 200
 $expiredArchived=Archive $expired.body.result.quote 'expired';Check $expiredArchived 200
 Assert-Demo ($expiredArchived.body.result.quote.status -ceq 'EXPIRED' -and $expiredArchived.body.result.quote.customerDecisionHistory.Count -eq 1 -and $expiredArchived.body.result.quote.deliveryHistory.Count -eq 1) 'B4_ARCHIVE_EXPIRED' 'Archive retains EXPIRED and its histories'
 $active=Api $emails[0] GET '/quotes?limit=250';Check $active 200
 Assert-Demo (!($active.body.items|Where-Object id -ceq $old.id)) 'B4_ACTIVE_LIST' 'Archived record is absent from default active view'
 $archivedList=Api $emails[0] GET '/quotes?archived=ARCHIVED&limit=250&search=B4';Check $archivedList 200
 Assert-Demo (!!($archivedList.body.items|Where-Object id -ceq $old.id)) 'B4_ARCHIVED_LIST' 'Archived filtered search returns retained quote'
 $lineage=Api $emails[0] GET "/quotes?archived=ALL&rootQuoteId=$($old.rootQuoteId)&limit=250";Check $lineage 200
 Assert-Demo ($lineage.body.items.Count -eq 2 -and !!($lineage.body.items|Where-Object id -ceq $old.id)) 'B4_ARCHIVED_LINEAGE' 'Root-filtered lineage remains readable'
 $proof=SQLRows "SELECT q.QuoteId,q.Status,q.ArchivedAt,q.ArchiveReason,q.ResourceVersion,(SELECT COUNT(*) FROM OPENJSON(q.DeliveryHistoryJson)) Deliveries,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords d WHERE d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId) Decisions,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_ARCHIVED') ArchiveEvents FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId IN (@a,@b,@c,@d) ORDER BY QuoteId" @{'@ws'=$script:workspace;'@a'=$draft.id;'@b'=$review.id;'@c'=$old.id;'@d'=$expirySource.id}
 Assert-Demo ($proof.Count -eq 4 -and @($proof|Where-Object ArchiveEvents -ne 1).Count -eq 0) 'B4_ARCHIVE_SQL' 'Four eligible states must each persist exactly one archive event'
 $proof|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'archive-sql.json')
 Stop-DemoHost;StartHost;LoginAll
 $read=Api $emails[0] GET "/quotes/$($old.id)";Check $read 200
 Assert-Demo (!!$read.body.archivedAt -and $read.body.customerDecisionHistory.Count -eq 2 -and $read.body.deliveryHistory.Count -eq 1) 'B4_ARCHIVE_RESTART' 'Archived detail remains readable after restart/login'
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 $script:httpEvidence|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
 @{status=$status;workspaceId=$script:workspace;archivedRejectedId=$old.id;archivedExpiredId=$expirySource.id;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B4 Archive API/SQL: $($cases.Count) requests. $script:stateDirectory"
}

