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
$securityKey='unicore-quote-lifecycle-b4-'+$run.Substring(0,8)
$securityParseTokens=$null;$securityParseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-delivery.ps1'),[ref]$securityParseTokens,[ref]$securityParseErrors)
$definition=$ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ProvisionSecurityMember'},$false)[0]
. ([ScriptBlock]::Create($definition.Extent.Text.Replace('unicore-quote-approval-isolation',('unicore-b4-isolation-'+$run.Substring(0,8))).Replace("@('contacts','quotes')","@('contacts','quotes','products')").Replace('B2','B4')))
function LifecyclePolicy{
 $prefix="QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}
function FreshSent([string]$Name,$Line=$null){
 $body=Draft "B4 $Name $run" '1000';if($Line){$body.lineItems=@($Line)}
 $response=Api $emails[0] POST '/quotes' $body "b4-create-$Name-$run";Check $response 201
 $quote=RequestApproval $response.body.result.quote "b4-request-$Name-$run"
 $response=Api $emails[0] POST "/quotes/$($quote.id)/send" @{deliveryId="b4_delivery_$Name`_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test';note='Synthetic employee confirmation; no email sent'} "b4-send-$Name-$run" $quote.resourceVersion
 Check $response 200;return $response.body.result.quote
}
function RowHash([string]$Id){return [string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT q.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$Id}).Rows[0].Hash}
function ParallelRevision($Quote){
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($name in 'one','two'){
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$($Quote.id)/revise")
   foreach($header in @{Authorization="Bearer $($script:tokens[$emails[0]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b4-race-$name-$run";'X-Correlation-Id'="b4-race-$name-$run";'Idempotency-Key'="b4-race-$name-$run";'If-Match'=('"'+$Quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($header.Key,$header.Value)}
   $request.Content=[Net.Http.StringContent]::new('{}',[Text.Encoding]::UTF8,'application/json');$pending+=@{request=$request;task=$client.SendAsync($request)}
  }
  return ,@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$result=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{method='POST';path="/quotes/$($Quote.id)/revise";concurrent=$true;response=$result});$result}finally{$response.Dispose()}})
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.delete','contacts.read','contacts.create','products.read','products.create','products.edit','access.read','access.configure')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.approve')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 SetPolicy 'b4-approval-v1';LifecyclePolicy;StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="B4 buyer $run"} "b4-buyer-$run";Check $contact 201
 $script:buyer=$contact.body.result.contact.id;if(!$script:buyer){$script:buyer=$contact.body.id}
 $productInput=@{sku="B4-$($run.Substring(0,12))";name='B4 historical product';type='service';status='ACTIVE';category='B4';unit='service';unitPrice=@{amount='250000';currency='VND'};taxRate='0';taxMode='none';billingCycle='one_time';isSubscription=$false;isRenewable=$false;tags=@();description='Synthetic B4 historical-price fixture'}
 $productResponse=Api $emails[0] POST '/products' $productInput "b4-product-$run";Check $productResponse 201
 $product=$productResponse.body.result.product
 $line=@{productId=$product.id;productNameSnapshot=$product.name;quantity='1';unitPrice=@{amount='250000';currency='VND'};discountRate='0';taxMode='NONE';taxRate='0'}
 $sent=FreshSent 'historical' $line;$sourceHash=RowHash $sent.id
 $productInput.unitPrice.amount='500000';$productInput.name='B4 changed catalog'
 Check (Api $emails[0] PUT "/products/$($product.id)" $productInput "b4-product-change-$run" $product.version) 200
 $revised=Api $emails[0] POST "/quotes/$($sent.id)/revise" @{} "b4-revise-$run" $sent.resourceVersion;Check $revised 201
 $new=$revised.body.result.quote
 Assert-Demo ($new.status -ceq 'DRAFT' -and $new.quoteRevision -eq 2 -and $new.rootQuoteId -ceq $sent.rootQuoteId -and $new.revisionOfQuoteId -ceq $sent.id -and $new.id -cne $sent.id -and $new.quoteNumber -cne $sent.quoteNumber -and $new.resourceVersion -eq 1) 'B4_REVISION' 'New identity, root, business revision and resource version must be authoritative'
 Assert-Demo ($new.lineItems[0].unitPrice.amount -ceq '250000' -and !$new.deliveryHistory -and !$new.customerDecisionHistory -and !$new.approvalContentFingerprint -and !$new.sentAt) 'B4_RESET' 'Revision must preserve historical price without inherited lifecycle/evidence'
 Assert-Demo ((RowHash $sent.id) -ceq $sourceHash) 'B4_IMMUTABLE' 'Source full SQL row must be unchanged'
 $snapshots=(Invoke-DemoSql 'SELECT CASE WHEN a.ProductSnapshotsJson=b.ProductSnapshotsJson THEN 1 ELSE 0 END EqualSnapshots FROM quotes.Quotes a JOIN quotes.Quotes b ON a.WorkspaceId=b.WorkspaceId WHERE a.WorkspaceId=@ws AND a.QuoteId=@source AND b.QuoteId=@new' @{'@ws'=$script:workspace;'@source'=$sent.id;'@new'=$new.id}).Rows[0].EqualSnapshots
 Assert-Demo ($snapshots -eq 1) 'B4_PRODUCT_PROVENANCE' 'Original Product captures must be copied exactly'
 $replay=Api $emails[0] POST "/quotes/$($sent.id)/revise" @{} "b4-revise-$run" $sent.resourceVersion;Check $replay 201
 Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.aggregateId -ceq $new.id) 'B4_REPLAY' 'Retry must return same revision'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/revise" @{reason='Changed request'} "b4-revise-$run" $sent.resourceVersion) 409 'IDEMPOTENCY_KEY_REUSED'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/revise" @{} "b4-working-$run" $sent.resourceVersion) 409 'QUOTE_REVISION_BLOCKED' 'ROOT_HAS_WORKING_REVISION'
 Check (Api $emails[0] POST "/quotes/$($sent.id)/revise" @{} "b4-stale-$run" ([long]$sent.resourceVersion-1)) 412 'VERSION_CONFLICT'
 Check (Api $emails[1] POST "/quotes/$($sent.id)/revise" @{} "b4-denied-$run" $sent.resourceVersion) 403 'ACCESS_DENIED'
 Check (Api $emails[0] POST "/quotes/$($new.id)/revise" @{} "b4-invalid-state-$run" $new.resourceVersion) 409 'QUOTE_REVISION_BLOCKED' 'QUOTE_STATE_INVALID'
 $update=Draft 'B4 edited revision' '250000';$update.lineItems=@($line)
 $saved=Api $emails[0] POST "/quotes/$($new.id)/update-draft" $update "b4-update-$run" $new.resourceVersion;Check $saved 200
 Assert-Demo ($saved.body.result.quote.quoteRevision -eq 2 -and $saved.body.version -eq 2 -and $saved.body.result.quote.lineItems[0].unitPrice.amount -ceq '250000') 'B4_EDIT' 'Draft edit preserves business revision and captured Product price'
 $assessment=RequestApproval $saved.body.result.quote 'b4-new-assessment'
 Assert-Demo ($assessment.approvalStatus -ceq 'NOT_REQUIRED') 'B4_CREATOR' 'B1 must recognize revision creator and reassess without inherited approval'
 $raceSource=FreshSent 'race';$raceHash=RowHash $raceSource.id;$race=ParallelRevision $raceSource
 Assert-Demo (@($race|Where-Object status -eq 201).Count -eq 1 -and @($race|Where-Object status -eq 409).Count -eq 1) 'B4_RACE' 'One revision must commit and the other must see the existing working revision'
 $proof=SQLRows 'SELECT QuoteId,QuoteNumber,RootQuoteId,RevisionOfQuoteId,QuoteRevision,ResourceVersion,Status FROM quotes.Quotes WHERE WorkspaceId=@ws AND RootQuoteId=@root ORDER BY QuoteRevision' @{'@ws'=$script:workspace;'@root'=$raceSource.rootQuoteId}
 Assert-Demo ($proof.Count -eq 2 -and (RowHash $raceSource.id) -ceq $raceHash) 'B4_RACE_SQL' 'Race must leave source plus one immutable-lineage Draft'
 $proof|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'revision-race-sql.json')
 Stop-DemoHost;StartHost;LoginAll
 $read=Api $emails[0] GET "/quotes/$($new.id)";Check $read 200
 Assert-Demo ($read.body.quoteRevision -eq 2 -and $read.body.revisionOfQuoteId -ceq $sent.id) 'B4_RESTART' 'Revision lineage must persist across restart/login'
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 $script:httpEvidence|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;buyerId=$script:buyer;sourceQuoteId=$sent.id;revisionQuoteId=$new.id;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B4 Revision API/SQL: $($cases.Count) requests. $script:stateDirectory"
}
