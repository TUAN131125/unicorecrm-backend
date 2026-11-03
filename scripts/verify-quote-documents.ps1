#requires -Version 7.0
[CmdletBinding()]
param()
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
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.delete','quotes.issueDocument','contacts.read','contacts.create','deals.read','deals.create')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.approve','contacts.read','studio.read','studio.configure','access.read','access.configure')
 ProvisionSecurityMember $emails[2] @('workspace.context.resolve','quotes.read','contacts.read')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 SetPolicy 'b5-approval-v1';LifecyclePolicy;DocumentPolicy;StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="KHÁCH HÀNG SYNTHETIC B5 $($run.Substring(0,8))";workEmail='buyer@example.test'} "b5-buyer-$run";Check $contact 201
 $script:buyer=$contact.body.result.contact.id;if(!$script:buyer){$script:buyer=$contact.body.id}
 $readiness=Api $emails[0] GET '/quotes/document-seller';Check $readiness 200;Assert-Demo (!$readiness.body.ready) 'B5_MISSING_SELLER' 'Unconfigured Workspace must not be ready'
 $draft=DraftQuote 'missing-seller';$q=Assessed $draft 'missing-seller'
 Check (Issue $q 'missing-seller') 409 'QUOTE_DOCUMENT_BLOCKED' 'SELLER_LEGAL_NAME_REQUIRED'
 Check (Api $emails[0] GET '/workspace-configuration') 403 'ACCESS_DENIED'
 $cfg=Api $emails[1] GET '/workspace-configuration';Check $cfg 200
 $business=$cfg.body.businessInformation;$business.displayName="SYNTHETIC SELLER B5 $($run.Substring(0,8))";$business.legalName='CÔNG TY SYNTHETIC B5 — CHỈ KIỂM THỬ';$business.email='seller@example.test';$business.phone='';$business.taxId='';$business.logoReference=''
 $address=@{id="b5_registered_$run";name='Địa chỉ đăng ký synthetic';purposes=@('REGISTERED');addressLine1='ĐỊA CHỈ SYNTHETIC — KHÔNG DÙNG CHỨNG TỪ THỰC';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='';contactPhone='';active=$true}
 $saved=Api $emails[1] PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=@($address)} "b5-seller-save-$run" $cfg.body.revision;Check $saved 200
 $readiness=Api $emails[0] GET '/quotes/document-seller';Check $readiness 200;Assert-Demo ($readiness.body.ready -and $readiness.body.seller.workspaceId -ceq $script:workspace) 'B5_SELLER_READY' 'Official owner configuration must establish readiness'
 $unassessed=DraftQuote 'unassessed';Check (Issue $unassessed 'unassessed') 409 'QUOTE_DOCUMENT_BLOCKED' 'APPROVAL_BINDING_STALE'
 Check (Issue $q 'restricted' $emails[2]) 403 'ACCESS_DENIED'
 Check (Api $emails[0] POST "/quotes/$($q.id)/documents/issue" @{seller=@{legalName='Injected'}} "b5-forged-$run" $q.resourceVersion) 422 'VALIDATION_FAILED'
 Check (Api $emails[0] POST "/quotes/$($q.id)/documents/issue" @{} "b5-stale-$run" ($q.resourceVersion-1)) 412 'VERSION_CONFLICT'
 $issued=Issue $q 'main';Check $issued 201;$doc=$issued.body.result.document;$issuedQuote=$issued.body.result.quote
 Assert-Demo ($issuedQuote.status -ceq $q.status -and !$issuedQuote.deliveryHistory -and !$issuedQuote.customerDecisionHistory -and $issued.body.version -eq $q.resourceVersion+1 -and $doc.sourceResourceVersion -eq $q.resourceVersion -and $doc.resultResourceVersion -eq $issued.body.version) 'B5_ISOLATION' 'Issuance changes resource version only, not lifecycle or delivery/customer evidence'
 $download=Download $q.id $doc.documentId 'issued-main';Check $download 200
 Assert-Demo ($download.sha256 -ceq $doc.pdfSha256 -and $download.bytes -eq $doc.fileSize -and $download.mediaType -ceq 'application/pdf' -and $download.cacheControl -match 'no-store') 'B5_DOWNLOAD' 'Download must match immutable stored bytes/hash/media/cache policy'
 $replay=Issue $q 'main';Check $replay 201;Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.result.document.documentId -ceq $doc.documentId) 'B5_REPLAY' 'Replay must preserve document identity'
 Check (Issue $q 'main' $emails[0] 'Different payload') 409 'IDEMPOTENCY_KEY_REUSED'
 Check (Issue $issuedQuote 'duplicate') 409 'QUOTE_DOCUMENT_BLOCKED' 'DOCUMENT_ALREADY_ISSUED'
 $viewer=Download $q.id $doc.documentId 'read-only-viewer' $emails[2];Check $viewer 200
 $cfg=Api $emails[1] GET '/workspace-configuration';Check $cfg 200;$business=$cfg.body.businessInformation;$business.legalName='CÔNG TY SYNTHETIC B5 ĐÃ THAY ĐỔI';$address.addressLine1='ĐỊA CHỈ SYNTHETIC MỚI — CHỈ KIỂM THỬ'
 Check (Api $emails[1] PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=@($address)} "b5-seller-change-$run" $cfg.body.revision) 200
 $old=Download $q.id $doc.documentId 'after-seller-change';Check $old 200;Assert-Demo ($old.sha256 -ceq $doc.pdfSha256) 'B5_SELLER_IMMUTABLE' 'Later Seller changes must not change issued PDF bytes'
 $newIssue=Issue $issuedQuote 'new-seller';Check $newIssue 201;$second=$newIssue.body.result.document
 Assert-Demo ($second.documentId -cne $doc.documentId -and $second.documentVersion -eq 2 -and $second.pdfSha256 -cne $doc.pdfSha256) 'B5_NEW_BINDING' 'Changed Seller must issue a new identity/version without replacing old document'
 $list=Api $emails[0] GET "/quotes/$($q.id)/documents?limit=1";Check $list 200;Assert-Demo ($list.body.items.Count -eq 1 -and $list.body.pageInfo.hasNextPage) 'B5_PAGING' 'Document history uses bounded real pagination'
 $next=Api $emails[0] GET "/quotes/$($q.id)/documents?limit=1&cursor=$($list.body.pageInfo.nextCursor)";Check $next 200;Assert-Demo ($next.body.items[0].documentId -ceq $doc.documentId) 'B5_PAGING_OLD' 'Old document remains discoverable'
 $archive=Api $emails[0] POST "/quotes/$($q.id)/archive" @{reason='B5 synthetic history retention'} "b5-archive-$run" $newIssue.body.version;Check $archive 200
 $retained=Download $q.id $doc.documentId 'after-archive';Check $retained 200;Assert-Demo ($retained.sha256 -ceq $doc.pdfSha256) 'B5_ARCHIVE_RETAIN' 'Archive must preserve original bytes'
 Check (Issue $archive.body.result.quote 'archived') 409 'QUOTE_DOCUMENT_BLOCKED' 'QUOTE_STATE_INVALID'
 Stop-DemoHost;StartHost;LoginAll
 $restart=Download $q.id $doc.documentId 'after-restart';Check $restart 200;Assert-Demo ($restart.sha256 -ceq $doc.pdfSha256) 'B5_RESTART' 'Restart/login must return exactly original bytes'
 $proof=SQLRows "SELECT d.DocumentId,d.QuoteId,d.DocumentVersion,d.SourceResourceVersion,d.ResultResourceVersion,d.FileSize,d.PageCount,d.PdfSha256,DATALENGTH(c.Bytes) StoredBytes,CONVERT(varchar(64),HASHBYTES('SHA2_256',c.Bytes),2) StoredHash,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=d.WorkspaceId AND o.QuoteId=d.QuoteId AND o.EventType='QUOTE_DOCUMENT_ISSUED') IssueEvents FROM quotes.Documents d JOIN quotes.DocumentContents c ON c.WorkspaceId=d.WorkspaceId AND c.DocumentId=d.DocumentId WHERE d.WorkspaceId=@ws AND d.QuoteId=@q ORDER BY d.DocumentVersion" @{'@ws'=$script:workspace;'@q'=$q.id}
 Assert-Demo ($proof.Count -eq 2 -and @($proof|Where-Object{ $_.FileSize -ne $_.StoredBytes -or $_.PdfSha256 -cne $_.StoredHash -or $_.IssueEvents -ne 2 }).Count -eq 0) 'B5_SQL' 'Two committed documents must each have matching bytes/hash and exactly two issue events'
 $proof|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'documents-sql.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;buyerId=$script:buyer;quoteId=$q.id;documentId=$doc.documentId;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B5 Quote Documents API/SQL/bytes. $script:stateDirectory"
}
