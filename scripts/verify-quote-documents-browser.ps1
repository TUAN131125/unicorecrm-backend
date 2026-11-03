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
function BrowserPhase([string]$Phase){
 foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_APPROVER_EMAIL=$emails[1];UNICORECRM_TEST_APPROVER_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_RESTRICTED_EMAIL=$emails[2];UNICORECRM_TEST_RESTRICTED_PASSWORD=$passwords[$emails[2]];UNICORECRM_TEST_WORKSPACE_KEY=$securityKey;UNICORECRM_TEST_LOW_QUOTE_ID=$quote.id;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 [void](New-Item -ItemType Directory -Force (Join-Path $script:stateDirectory 'browser'))
 Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
 try{npx playwright test --config playwright.quote-documents.config.ts --grep "@$Phase";$exit=$LASTEXITCODE}finally{Pop-Location}
 # Playwright error-context serializes password inputs even when screenshots mask them.
 Get-ChildItem -LiteralPath $script:stateDirectory -Recurse -File -Filter 'error-context.md'|ForEach-Object{
  $text=[IO.File]::ReadAllText($_.FullName);$clean=[regex]::Replace($text,'B1-[A-Fa-f0-9]{64}!a9','[REDACTED_SYNTHETIC_CREDENTIAL]');[IO.File]::WriteAllText($_.FullName,$clean)
 }
 Assert-Demo ($exit -eq 0) 'B5_BROWSER' "Connected browser phase $Phase failed"
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.delete','quotes.issueDocument','contacts.read','contacts.create')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.approve','contacts.read','studio.read','studio.configure')
 ProvisionSecurityMember $emails[2] @('workspace.context.resolve','quotes.read','contacts.read')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044')
 SetPolicy 'b5-approval-v1';LifecyclePolicy;DocumentPolicy;StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="KHÁCH HÀNG SYNTHETIC B5 BROWSER";workEmail='browser-buyer@example.test'} "b5-ui-buyer-$run";Check $contact 201
 $script:buyer=$contact.body.result.contact.id;if(!$script:buyer){$script:buyer=$contact.body.id}
 $quote=Assessed (DraftQuote 'browser') 'browser'
 BrowserPhase 'issue'
 $proof=Get-Content (Join-Path $script:stateDirectory 'browser/issued-proof.json') -Raw|ConvertFrom-Json -AsHashtable
 $doc=$proof.document
 $sql=SQLRows "SELECT q.QuoteId,q.Status,q.ResourceVersion,(SELECT COUNT(*) FROM OPENJSON(q.DeliveryHistoryJson)) Deliveries,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords x WHERE x.WorkspaceId=q.WorkspaceId AND x.QuoteId=q.QuoteId) Decisions,d.DocumentId,d.PdfSha256,d.FileSize,CONVERT(varchar(64),HASHBYTES('SHA2_256',c.Bytes),2) StoredHash FROM quotes.Quotes q JOIN quotes.Documents d ON d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId JOIN quotes.DocumentContents c ON c.WorkspaceId=d.WorkspaceId AND c.DocumentId=d.DocumentId WHERE q.WorkspaceId=@ws AND q.QuoteId=@q" @{'@ws'=$script:workspace;'@q'=$quote.id}
 Assert-Demo ($sql.Count -eq 1 -and $sql[0].Status -ceq 'DRAFT' -and $sql[0].Deliveries -eq 0 -and $sql[0].Decisions -eq 0 -and $sql[0].StoredHash -ceq $proof.first.sha256) 'B5_BROWSER_SQL' 'Studio/browser issuance must commit bytes/hash with no lifecycle/delivery/customer side effect'
 $sql|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'browser-sql.json')
 Stop-DemoHost;StartHost;LoginAll;BrowserPhase 'restart'
 $after=Download $quote.id $doc.documentId 'after-browser-restart';Check $after 200;Assert-Demo ($after.sha256 -ceq $doc.pdfSha256) 'B5_BROWSER_RESTART_SQL' 'Browser and direct protected download must agree after restart'
 $fresh=Api $emails[0] GET "/quotes/$($quote.id)";Check $fresh 200
 $sent=Api $emails[0] POST "/quotes/$($quote.id)/send" @{deliveryId="b5_browser_history_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic browser history test';note='Explicit B2 synthetic user confirmation; no provider delivery'} "b5-browser-send-$run" $fresh.body.resourceVersion;Check $sent 200
 $sourceHash=[string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT q.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$quote.id}).Rows[0].Hash
 BrowserPhase 'history'
 $sourceHashAfter=[string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT q.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$quote.id}).Rows[0].Hash
 Assert-Demo ($sourceHash -ceq $sourceHashAfter) 'B5_BROWSER_HISTORY' 'Browser revision and download must preserve the original sent Quote row'
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;buyerId=$script:buyer;quoteId=$quote.id;documentId=$doc.documentId;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B5 Connected Studio/PDF/browser/SQL. $script:stateDirectory"
}
