#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Fixture)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-document-b5/api-'+[guid]::NewGuid().ToString('N'))
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
$securityKey='unicore-quote-document-b5-'+$run.Substring(0,8)
$securityParseTokens=$null;$securityParseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-delivery.ps1'),[ref]$securityParseTokens,[ref]$securityParseErrors)
$definition=$ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ProvisionSecurityMember'},$false)[0]
. ([ScriptBlock]::Create($definition.Extent.Text.Replace('unicore-quote-approval-isolation',('unicore-b5-isolation-'+$run.Substring(0,8))).Replace("@('contacts','quotes')","@('contacts','quotes','deals')").Replace("@('crm')","@('crm','studio','people')").Replace('B2','B5')))
function LifecyclePolicy{
 $prefix="QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}

function DocumentPolicy{
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Version" 'b5-adjusted-package-a-v1'
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Enabled" 'true'
}
function DraftQuote([string]$Name,[string]$Amount='1000',[string]$Currency='VND'){
 $body=Draft "B5 synthetic $Name $run" $Amount;$body.currency=$Currency;$body.lineItems[0].unitPrice.currency=$Currency
 $body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $r=Api $emails[0] POST '/quotes' $body "b5-create-$Name-$run";Check $r 201;return $r.body.result.quote
}
function Assessed($Quote,[string]$Name){
 $q=RequestApproval $Quote "b5-$Name-$run"
 if($q.approvalStatus -eq 'PENDING'){$r=Api $emails[1] POST "/quotes/$($q.id)/approve" @{} "b5-approve-$Name-$run" $q.resourceVersion;Check $r 200;$q=$r.body.result.quote}
 return $q
}
function Issue($Quote,[string]$Name,[string]$Actor=$emails[0],[string]$Reason=''){
 $body=@{};if($Reason){$body.reason=$Reason}
 return Api $Actor POST "/quotes/$($Quote.id)/documents/issue" $body "b5-issue-$Name-$run" $Quote.resourceVersion
}
function Download([string]$QuoteId,[string]$DocumentId,[string]$Name,[string]$Actor=$emails[0]){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,"http://127.0.0.1:5098/quotes/$QuoteId/documents/$DocumentId/content")
 try{
  foreach($h in @{Authorization="Bearer $($script:tokens[$Actor])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b5-download-$Name-$run";'X-Correlation-Id'="b5-download-$Name-$run"}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
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
$fixtureEvidence=Get-Content -LiteralPath $Fixture -Raw|ConvertFrom-Json -AsHashtable
Assert-Demo ($fixtureEvidence.status -ceq 'PASS' -and $fixtureEvidence.workspaceKey.StartsWith('unicore-quote-document-b5-')) 'B5_FIXTURE' 'A successful isolated B5 fixture is required'
$script:workspace=$fixtureEvidence.workspaceId;$script:buyer=$fixtureEvidence.buyerId;$securityKey=$fixtureEvidence.workspaceKey
$allowedAssignments=[Collections.Generic.HashSet[string]]::new()
function RowHash([string]$Id){return [string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT d.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM deals.Deals d WHERE WorkspaceId=@ws AND DealId=@d' @{'@ws'=$script:workspace;'@d'=$Id}).Rows[0].Hash}
function ParallelRequests($Quote,[bool]$SameKey=$false,[bool]$WithEdit=$false){
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($name in 'one','two'){
   $path="/quotes/$($Quote.id)/documents/issue";$body=@{};$key="b5-race-$name-$run-$($Quote.id)"
   if($SameKey){$key="b5-race-same-$run-$($Quote.id)"}
   if($WithEdit -and $name -eq 'two'){$path="/quotes/$($Quote.id)/update-draft";$body=Draft "B5 race edited $run" '1000';$body.validUntil=$Quote.validUntil}
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098$path")
   foreach($h in @{Authorization="Bearer $($script:tokens[$emails[0]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b5-race-$name-$run";'X-Correlation-Id'="b5-race-$name-$run";'Idempotency-Key'=$key;'If-Match'=('"'+$Quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
   $request.Content=[Net.Http.StringContent]::new(($body|ConvertTo-Json -Depth 20 -Compress),[Text.Encoding]::UTF8,'application/json');$pending+=@{path=$path;request=$request;task=$client.SendAsync($request)}
  }
  return ,@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$r=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{method='POST';path=$entry.path;concurrent=$true;response=$r});$r}finally{$response.Dispose()}})
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
function AssignViewer($Role,[string]$Name){
 $profile=$Role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$Role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 foreach($a in $Role.body.result.assignments|Where-Object membershipId -ceq $member.membershipId){[void]$allowedAssignments.Add([string]$a.assignmentId)}
 $v=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 $r=Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($Role.body.aggregateId);teamIds=@()} "b5-viewer-$Name-$run" $v;Check $r 200
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 SetPolicy 'b5-approval-v1';LifecyclePolicy;DocumentPolicy;StartHost;LoginAll
 $raceQuote=Assessed (DraftQuote 'race-different') 'race-different';$race=ParallelRequests $raceQuote
 Assert-Demo (@($race|Where-Object status -eq 201).Count -eq 1 -and @($race|Where-Object status -eq 412).Count -eq 1) 'B5_RACE' 'Different-key issuance must commit only once under If-Match'
 $sameQuote=Assessed (DraftQuote 'race-same') 'race-same';$same=ParallelRequests $sameQuote $true
 Assert-Demo (@($same|Where-Object status -eq 201).Count -eq 2 -and @($same|Where-Object{$_.body.outcome -eq 'COMMITTED'}).Count -eq 1 -and @($same|Where-Object{$_.body.outcome -eq 'REPLAYED'}).Count -eq 1 -and $same[0].body.result.document.documentId -ceq $same[1].body.result.document.documentId) 'B5_SAME_KEY' 'Concurrent same-key retries return one stable document'
 $editQuote=Assessed (DraftQuote 'race-edit') 'race-edit';$edit=ParallelRequests $editQuote $false $true
 Assert-Demo (@($edit|Where-Object{$_.status -in 200,201}).Count -eq 1 -and @($edit|Where-Object status -eq 412).Count -eq 1) 'B5_EDIT_RACE' 'Edit and issuance must serialize; one stale operation is rejected'
 $cfg=Api $emails[1] GET '/workspace-configuration';Check $cfg 200;$locale=$cfg.body.localeRegion;$locale.currencies.enabledCurrencies=@('VND','USD','EUR','JPY','KWD')
 Check (Api $emails[1] PATCH '/workspace-configuration/locale-region' @{localeRegion=$locale} "b5-currencies-$run" $cfg.body.revision) 200
 $currencyDocuments=@()
 foreach($currency in @('USD','EUR','JPY','KWD','VND')){
  $amount=switch($currency){'USD'{'0.004'}'EUR'{'12.34'}'KWD'{'1.234'}default{'1000'}}
  $body=Draft "B5 currency $currency $run" $amount;$body.currency=$currency;$body.lineItems[0].unitPrice.currency=$currency;$body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
  if($currency -eq 'USD'){
   $body.lineItems[0].quantity='1000';$body.lineItems[0].billingCycleSnapshot='monthly';$body.lineItems[0].descriptionSnapshot='ĐƠN GIÁ CÓ PHẦN THẬP PHÂN NHỎ — GIỮ NGUYÊN GIÁ NGUỒN'
   $body.paymentAgreement=@{kind='FULL_PAYMENT';currency='USD';lines=@(@{sequence=1;label='Điều khoản synthetic đã phê duyệt';purpose='FULL';amountRule=@{type='REMAINDER'};dueRule=@{type='EVENT_RELATIVE';event='ORDER_CONFIRMED';offsetDays=45;dayBasis='CALENDAR'};allowedMethodCodes=@('BANK_TRANSFER');preferredMethodCode='BANK_TRANSFER';channel='BANK';fulfillmentGate='BEFORE_DISPATCH';invoicePolicyCode='SYNTHETIC_TEST_ONLY'})}
  }
  $r=Api $emails[0] POST '/quotes' $body "b5-currency-create-$currency-$run";Check $r 201;$q=Assessed $r.body.result.quote "currency-$currency"
  $r=Issue $q "currency-$currency";Check $r 201;$d=$r.body.result.document;$file=Download $q.id $d.documentId "currency-$currency";Check $file 200
  Assert-Demo ($file.sha256 -ceq $d.pdfSha256) 'B5_CURRENCY_HASH' 'Currency PDF download must match stored bytes'
  $currencyDocuments+=@{currency=$currency;document=$d;quote=$r.body.result.quote;download=$file}
 }
 $currencyDocuments|ConvertTo-Json -Depth 35|Set-Content (Join-Path $script:stateDirectory 'currency-documents.json')
 $high=DraftQuote 'pending' '150000000';$pending=RequestApproval $high 'b5-pending'
 Check (Issue $pending 'pending') 409 'QUOTE_DOCUMENT_BLOCKED' 'INDEPENDENT_APPROVAL_REQUIRED'
 $invalidBody=Draft 'B5 invalidate approval' '150000001';$invalidBody.validUntil=$pending.validUntil
 $changed=Api $emails[0] POST "/quotes/$($pending.id)/update-draft" $invalidBody "b5-invalidate-$run" $pending.resourceVersion;Check $changed 200
 Check (Issue $changed.body.result.quote 'invalidated') 409 'QUOTE_DOCUMENT_BLOCKED' 'APPROVAL_BINDING_STALE'
 $expiredBody=Draft "B5 overdue $run" '1000';$expiredBody.validUntil=[DateTime]::UtcNow.Date.AddDays(-1).ToString('yyyy-MM-dd');$e=Api $emails[0] POST '/quotes' $expiredBody "b5-overdue-create-$run";Check $e 201;$overdue=Assessed $e.body.result.quote 'overdue'
 Check (Issue $overdue 'overdue') 409 'QUOTE_DOCUMENT_BLOCKED' 'QUOTE_EXPIRED'
 $source=Assessed (DraftQuote 'history-source') 'history-source';$i=Issue $source 'history-source';Check $i 201;$oldDocument=$i.body.result.document
 $sent=Api $emails[0] POST "/quotes/$($source.id)/send" @{deliveryId="b5_history_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic test channel';note='Synthetic employee confirmation only; no provider delivery claimed'} "b5-history-send-$run" $i.body.version;Check $sent 200
 $sourceHash=[string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT q.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$source.id}).Rows[0].Hash
 $rev=Api $emails[0] POST "/quotes/$($source.id)/revise" @{} "b5-history-revise-$run" $sent.body.version;Check $rev 201
 $newDocs=Api $emails[0] GET "/quotes/$($rev.body.aggregateId)/documents";Check $newDocs 200;Assert-Demo ($newDocs.body.items.Count -eq 0) 'B5_REVISION_DOCS' 'A new revision never inherits document identity'
 $old=Download $source.id $oldDocument.documentId 'after-revision';Check $old 200;Assert-Demo ($old.sha256 -ceq $oldDocument.pdfSha256) 'B5_REVISION_BYTES' 'Old revision document remains byte-identical'
 $sourceHashAfter=[string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT q.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$source.id}).Rows[0].Hash
 Assert-Demo ($sourceHashAfter -ceq $sourceHash) 'B5_REVISION_SOURCE' 'Revision/download never modifies the sent source'
 Check (Api $emails[0] GET "/quotes/$($source.id)/documents/qdoc_fabricated") 404 'RESOURCE_NOT_FOUND'
 Check (Api $emails[0] POST "/quotes/$($source.id)/documents/issue" @{storageReference='../../private';pdfSha256='fake';documentId='fake'} "b5-storage-inject-$run" $sent.body.version) 422 'VALIDATION_FAILED'
 $oldWorkspace=$script:workspace;$oldKey=$securityKey;$securityKey='unicore-quote-doc-b5-foreign-'+$run.Substring(0,8)
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.issueDocument','contacts.read')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 Check (Api $emails[0] POST "/quotes/$($source.id)/documents/issue" @{} "b5-foreign-$run" $sent.body.version) 404 'RESOURCE_NOT_FOUND'
 Check (Api $emails[0] GET "/quotes/$($source.id)/documents/$($oldDocument.documentId)") 404 'RESOURCE_NOT_FOUND'
 Check (Download $source.id $oldDocument.documentId 'foreign-denied') 404 'RESOURCE_NOT_FOUND'
 $script:workspace=$oldWorkspace;$securityKey=$oldKey
 $role=Api $emails[1] POST '/access/roles' @{name="B5 hidden document amount $run";capabilities=@('workspace.context.resolve','quotes.read','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='grandTotal';access='HIDDEN'})} "b5-fls-role-$run";Check $role 200
 AssignViewer $role 'hidden'
 Check (Download $source.id $oldDocument.documentId 'fls-denied' $emails[2]) 403 'ACCESS_DENIED'
 Check (Api $emails[2] GET "/quotes/$($source.id)/documents/$($oldDocument.documentId)") 403 'ACCESS_DENIED'
 $restore=Api $emails[1] POST '/access/roles' @{name="B5 restored readonly viewer $run";capabilities=@('workspace.context.resolve','quotes.read','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@()} "b5-viewer-restore-$run";Check $restore 200;AssignViewer $restore 'restored'
 $storageQuote=Assessed (DraftQuote 'storage-fault') 'storage-fault'
 Stop-DemoHost;EnvValue 'QuoteDocumentVerification__Enabled' 'true';EnvValue 'QuoteDocumentVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteDocumentVerification__FailAfterStorageFlush' 'true';StartHost;LoginAll
 Check (Issue $storageQuote 'storage-retry') 500 'INTERNAL_ERROR'
 $failure=SQLRows "SELECT q.ResourceVersion,(SELECT COUNT(*) FROM quotes.Documents d WHERE d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId) Documents,(SELECT COUNT(*) FROM quotes.CommandRecords c WHERE c.WorkspaceId=q.WorkspaceId AND c.QuoteId=q.QuoteId AND c.Operation='issueQuoteDocumentCommand') Receipts,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_DOCUMENT_ISSUED') Events FROM quotes.Quotes q WHERE q.WorkspaceId=@ws AND q.QuoteId=@q" @{'@ws'=$script:workspace;'@q'=$storageQuote.id}
 Assert-Demo ($failure[0].ResourceVersion -eq $storageQuote.resourceVersion -and $failure[0].Documents -eq 0 -and $failure[0].Receipts -eq 0 -and $failure[0].Events -eq 0) 'B5_ROLLBACK' 'Failure after SQL flush must rollback bytes/metadata/version/receipt/outbox'
 $failure|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'storage-failure-sql.json')
 Stop-DemoHost;EnvValue 'QuoteDocumentVerification__FailAfterStorageFlush' 'false';StartHost;LoginAll
 $recovered=Issue $storageQuote 'storage-retry';Check $recovered 201
 $retry=Download $storageQuote.id $recovered.body.result.document.documentId 'storage-recovered';Check $retry 200;Assert-Demo ($retry.sha256 -ceq $recovered.body.result.document.pdfSha256) 'B5_STORAGE_RECOVERY' 'Retry after rollback must commit one complete downloadable document'
 $largeBody=Draft "B5 renderer limit $run" '1000';$largeBody.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd');$largeBody.lineItems=@(1..180|ForEach-Object{@{productNameSnapshot="Dòng synthetic $_";descriptionSnapshot=('Mô tả dài để kiểm tra giới hạn tài nguyên và không cắt mất nội dung. '*15);quantity='1';unitPrice=@{amount='1000';currency='VND'};discountRate='0';taxMode='NONE'}})
 $large=Api $emails[0] POST '/quotes' $largeBody "b5-large-create-$run";Check $large 201;$largeQuote=Assessed $large.body.result.quote 'large'
 $limit=Issue $largeQuote 'large';Check $limit 409 'QUOTE_DOCUMENT_BLOCKED';Assert-Demo ($limit.body.detail -like 'RENDER_*') 'B5_RENDER_LIMIT' 'Oversized multi-page rendering must fail explicitly'
 $largeRows=SQLRows 'SELECT ResourceVersion,(SELECT COUNT(*) FROM quotes.Documents d WHERE d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId) Documents FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$largeQuote.id}
 Assert-Demo ($largeRows[0].ResourceVersion -eq $largeQuote.resourceVersion -and $largeRows[0].Documents -eq 0) 'B5_RENDER_NO_COMMIT' 'Render limit must not produce a partial document or version change'
 $largeRows|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'render-limit-sql.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}})
 $unexpected=@($changes|Where-Object{if($_.table -eq 'access.MembershipRoleAssignments'){$pk=$_.key|ConvertFrom-Json -AsHashtable;!$allowedAssignments.Contains([string]$pk.AssignmentId)}elseif($_.table -in @('workspace.StudioConfigurations','workspace.BootstrapProjections')){$pk=$_.key|ConvertFrom-Json -AsHashtable;$pk.WorkspaceId -cne $fixtureEvidence.workspaceId}else{$true}})
 @{unchanged=$unexpected.Count -eq 0;intentionalSyntheticRoleAndLocaleChanges=$changes.Count-$unexpected.Count;changes=$unexpected}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($unexpected.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$fixtureEvidence.workspaceId;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B5 advanced concurrency/FLS/currency/failure/API/SQL. $script:stateDirectory"
}
