#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-proof-storage/advanced-'+[guid]::NewGuid().ToString('N'))
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
$emails=@('quote.creator@example.test','quote.approver@example.test','quote.approver2@example.test');$passwords=@{}
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing process-only credential derivation required'
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null;$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$status='FAIL'
$securityKey='unicore-proof-quarantine-'+$run.Substring(0,8)
$securityParseTokens=$null;$securityParseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-delivery.ps1'),[ref]$securityParseTokens,[ref]$securityParseErrors)
$definition=$ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ProvisionSecurityMember'},$false)[0]
. ([ScriptBlock]::Create($definition.Extent.Text.Replace('unicore-quote-approval-isolation',('unicore-b6-isolation-'+$run.Substring(0,8))).Replace("@('contacts','quotes')","@('contacts','quotes','deals')").Replace("@('crm')","@('crm','studio','people')").Replace('B2','B6')))
function LifecyclePolicy{
 $prefix="QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}

function DocumentPolicy{
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Version" 'b6-adjusted-package-a-v1'
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Enabled" 'true'
}
function DraftQuote([string]$Name,[string]$Amount='1000',[string]$Currency='VND'){
 $body=Draft "B6 synthetic $Name $run" $Amount;$body.currency=$Currency;$body.lineItems[0].unitPrice.currency=$Currency
 $body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $r=Api $emails[0] POST '/quotes' $body "b6-create-$Name-$run";Check $r 201;return $r.body.result.quote
}
function Assessed($Quote,[string]$Name){
 $q=RequestApproval $Quote "b6-$Name-$run"
 if($q.approvalStatus -eq 'PENDING'){$r=Api $emails[1] POST "/quotes/$($q.id)/approve" @{} "b6-approve-$Name-$run" $q.resourceVersion;Check $r 200;$q=$r.body.result.quote}
 return $q
}
function Issue($Quote,[string]$Name,[string]$Actor=$emails[0],[string]$Reason=''){
 $body=@{};if($Reason){$body.reason=$Reason}
 return Api $Actor POST "/quotes/$($Quote.id)/documents/issue" $body "b6-issue-$Name-$run" $Quote.resourceVersion
}
function Download([string]$QuoteId,[string]$DocumentId,[string]$Name,[string]$Actor=$emails[0]){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,"http://127.0.0.1:5098/quotes/$QuoteId/documents/$DocumentId/content")
 try{
  foreach($h in @{Authorization="Bearer $($script:tokens[$Actor])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b6-download-$Name-$run";'X-Correlation-Id'="b6-download-$Name-$run"}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{
   $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();$status=[int]$response.StatusCode
   if($status -eq 200){
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes));$path=Join-Path $script:stateDirectory "$Name.pdf";[IO.File]::WriteAllBytes($path,$bytes)
    $value=@{status=$status;sha256=$hash;bytes=$bytes.Length;mediaType=[string]$response.Content.Headers.ContentType;cacheControl=[string]$response.Headers.CacheControl;fileName=[string]$response.Content.Headers.ContentDisposition.FileNameStar;path=$path}
   }else{$value=@{status=$status;body=([Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -AsHashtable)}}
   $cases.Add(@{actor=$Actor;method='GET';path="/quotes/$QuoteId/documents/$DocumentId/content";binary=$status -eq 200;response=$value});return $value
  }finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
}
function Upload([string]$QuoteId,[string]$CaseId,[byte[]]$Bytes,[string]$Key,[long]$Version,[string]$Actor=$emails[0],[string]$Mime='application/pdf',[hashtable]$Extra=@{},[string]$Name='synthetic-proof.pdf'){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$QuoteId/acceptance-cases/$CaseId/proofs")
 try{
  $headers=@{Authorization="Bearer $($script:tokens[$Actor])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'=('proof-'+[guid]::NewGuid().ToString('N'));'X-Correlation-Id'=('proof-'+[guid]::NewGuid().ToString('N'));'Idempotency-Key'=$Key;'If-Match'=('"'+$Version+'"');'X-Proof-File-Name'=[Uri]::EscapeDataString($Name);'X-Proof-Evidence-Type'='SUPPORTING_DOCUMENT'}
  foreach($h in $Extra.GetEnumerator()){$headers[$h.Key]=$h.Value}
  foreach($h in $headers.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $request.Content=[Net.Http.ByteArrayContent]::new($Bytes);$request.Content.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new($Mime)
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{$value=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{scenario='B61_BINARY_UPLOAD';actor=$Actor;quoteId=$QuoteId;caseId=$CaseId;response=$value});return $value}finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
}

$fixture=Get-Content -LiteralPath (Join-Path $SourceDirectory 'result.json') -Raw|ConvertFrom-Json -AsHashtable
$script:workspace=$fixture.workspaceId;$securityKey=$fixture.workspaceKey;$quote=@{id=$fixture.quoteId};$case=@{caseId=$fixture.caseId}
function ParallelUploads([string[]]$Keys,[byte[][]]$Payloads,[long]$Version){
 $client=[Net.Http.HttpClient]::new();$pending=@();$values=@()
 try{
  for($i=0;$i -lt $Keys.Count;$i++){
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$($quote.id)/acceptance-cases/$($case.caseId)/proofs")
   foreach($h in @{Authorization="Bearer $($script:tokens[$emails[0]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'=('proof-'+[guid]::NewGuid().ToString('N'));'X-Correlation-Id'=('proof-'+[guid]::NewGuid().ToString('N'));'Idempotency-Key'=$Keys[$i];'If-Match'=('"'+$Version+'"');'X-Proof-File-Name'='synthetic-race.pdf';'X-Proof-Evidence-Type'='SUPPORTING_DOCUMENT'}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
   $request.Content=[Net.Http.ByteArrayContent]::new($Payloads[$i]);$request.Content.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new('application/pdf');$pending+=@{request=$request;task=$client.SendAsync($request)}
  }
  foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$value=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{scenario='PARALLEL_UPLOAD';response=$value});$values+=,$value}finally{$response.Dispose()}}
  return ,$values
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
function State{
 return SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.AcceptanceProofs WHERE WorkspaceId=@ws) Proofs,(SELECT COUNT(*) FROM quotes.AcceptanceProofContents WHERE WorkspaceId=@ws) Contents,(SELECT COUNT(*) FROM quotes.CommandRecords WHERE WorkspaceId=@ws) Receipts,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws) Events,(SELECT ResourceVersion FROM quotes.AcceptanceCases WHERE WorkspaceId=@ws AND CaseId=@c) CaseVersion,(SELECT ResourceVersion FROM quotes.Quotes WHERE WorkspaceId=@ws AND QuoteId=@q) QuoteVersion' @{'@ws'=$script:workspace;'@c'=$case.caseId;'@q'=$quote.id}
}
try{
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 SetPolicy 'b6-approval-v1';LifecyclePolicy;DocumentPolicy
 foreach($group in 'QuoteVerifiedAcceptancePolicies','QuoteProofStoragePolicies'){EnvValue "$($group)__Workspaces__$($script:workspace)__Enabled" 'true'}
 EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Version" 'b6-approved-documentary-v1'
 EnvValue "QuoteProofStoragePolicies__Workspaces__$($script:workspace)__Version" 'b61-approved-v1'
 StartHost;LoginAll
 $base="/quotes/$($quote.id)/acceptance-cases/$($case.caseId)"
 $originalCase=Api $emails[0] GET $base;Check $originalCase 200;$version=[long]$originalCase.body.resourceVersion
 $bytes=[IO.File]::ReadAllBytes((Join-Path $SourceDirectory 'safe-synthetic-fixture.pdf'))
 $a=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% safe synthetic concurrent A $run"));$b=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% safe synthetic concurrent B $run"))
 $races=ParallelUploads @("proof-race-a-$run","proof-race-b-$run") @($a,$b) $version
 Assert-Demo ((@($races.status|Sort-Object) -join ',') -ceq '201,412') 'PROOF_RACE' 'Different-key concurrent updates must have one winner and one stale version'
 $after=Api $emails[0] GET $base;Check $after 200;$version=[long]$after.body.resourceVersion
 $c=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% safe synthetic replay $run"))
 $replays=ParallelUploads @("proof-race-replay-$run","proof-race-replay-$run") @($c,$c) $version
 Assert-Demo ((@($replays.status|Sort-Object) -join ',') -ceq '201,201' -and @($replays.body.outcome|Sort-Object) -join ',' -ceq 'COMMITTED,REPLAYED') 'PROOF_REPLAY_RACE' 'Concurrent same-key replay must converge on one proof'
 $after=Api $emails[0] GET $base;Check $after 200;$version=[long]$after.body.resourceVersion
 # SQL flush succeeds, controlled Development owner probe aborts before commit.
 $beforeRollback=State
 Stop-DemoHost;EnvValue 'QuoteProofVerification__Enabled' 'true';EnvValue 'QuoteProofVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteProofVerification__FailAfterStorageFlush' 'true';StartHost;LoginAll
 $rollbackBytes=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% safe synthetic rollback $run"))
 Check (Upload $quote.id $case.caseId $rollbackBytes "proof-rollback-$run" $version) 500
 $afterRollback=State
 Assert-Demo (($beforeRollback|ConvertTo-Json -Compress) -ceq ($afterRollback|ConvertTo-Json -Compress)) 'PROOF_ROLLBACK' 'No orphan, receipt, event or version change after transaction rollback'
 @{before=$beforeRollback;after=$afterRollback}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $script:stateDirectory 'rollback.json')
 Stop-DemoHost;EnvValue 'QuoteProofVerification__Enabled' 'false';StartHost;LoginAll
 Check (Upload $quote.id $case.caseId $rollbackBytes "proof-rollback-$run" $version) 201;$version++
 # Quota is default five files per case, no limits are raised.
 $last=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% safe synthetic fifth $run"))
 Check (Upload $quote.id $case.caseId $last "proof-fifth-$run" $version) 201;$version++
 $sixth=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% safe synthetic over quota $run"))
 Check (Upload $quote.id $case.caseId $sixth "proof-sixth-$run" $version) 409 'QUOTE_PROOF_QUOTA_EXCEEDED'
 # Existing authorised replay works even when quota is full.
 $replayedFull=Upload $quote.id $case.caseId $c "proof-race-replay-$run" ($version-3) $emails[0] 'application/pdf' @{} 'synthetic-race.pdf'
 Check $replayedFull 201;Assert-Demo ($replayedFull.body.outcome -ceq 'REPLAYED') 'REPLAY_FULL_QUOTA' 'Authorised replay precedes quota and stale checks'
 $foreign=[string](Invoke-DemoSql 'SELECT TOP 1 WorkspaceId FROM workspace.Workspaces WHERE [Key] LIKE ''unicore-b6-isolation-%'' ORDER BY CreatedAt DESC').Rows[0].WorkspaceId
 $old=$script:workspace;$script:workspace=$foreign
 Check (Api $emails[0] GET $base) 403
 Check (Upload $quote.id $case.caseId $bytes "proof-cross-$run" $version) 403
 $script:workspace=$old
 # Assign a hidden commercial field through the AccessControl owner, then exercise read and replay authorization.
 $role=Api $emails[1] POST '/access/roles' @{name="B61 proof hidden total $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.submitAcceptanceEvidence','quotes.readAcceptanceEvidence','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='grandTotal';access='HIDDEN'})} "proof-fls-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "proof-fls-assign-$run" $memberVersion) 200
 Check (Api $emails[2] GET $base) 403
 Check (Upload $quote.id $case.caseId $bytes "proof-fls-upload-$run" 1 $emails[2]) 403
 $restore=Api $emails[1] POST '/access/roles' @{name="B61 restricted restored $run";capabilities=@('workspace.context.resolve','quotes.read','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@()} "proof-fls-restore-$run";Check $restore 200
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($restore.body.aggregateId);teamIds=@()} "proof-fls-restored-$run" $memberVersion) 200
 $state=State;$state|ConvertTo-Json -Depth 5|Set-Content (Join-Path $script:stateDirectory 'state.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;source=$SourceDirectory;workspaceId=$script:workspace;directory=$script:stateDirectory;requests=$cases.Count}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.1 advanced $script:stateDirectory"
}
