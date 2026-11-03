#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-proof-storage/api-'+[guid]::NewGuid().ToString('N'))
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
function BrowserPhase([string]$Phase){
 foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_RESTRICTED_EMAIL=$emails[2];UNICORECRM_TEST_RESTRICTED_PASSWORD=$passwords[$emails[2]];UNICORECRM_TEST_WORKSPACE_KEY=$securityKey;UNICORECRM_TEST_LOW_QUOTE_ID=$browserQuote.id;UNICORECRM_TEST_DOCUMENT_ID=$browserDocument.documentId;UNICORECRM_TEST_PROOF_FIXTURE=$browserFixture.path;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 [void](New-Item -ItemType Directory -Force (Join-Path $script:stateDirectory 'browser'))
 Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
 try{npx playwright test --config playwright.quote-proof.config.ts --grep "@$Phase";$exit=$LASTEXITCODE}finally{Pop-Location}
 Get-ChildItem -LiteralPath $script:stateDirectory -Recurse -File -Filter 'error-context.md'|ForEach-Object{$text=[IO.File]::ReadAllText($_.FullName);[IO.File]::WriteAllText($_.FullName,[regex]::Replace($text,'B1-[A-Fa-f0-9]{64}!a9','[REDACTED_SYNTHETIC_CREDENTIAL]'))}
 Assert-Demo ($exit -eq 0) 'B61_BROWSER' "Connected browser phase $Phase failed"
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.issueDocument','quotes.submitAcceptanceEvidence','quotes.readAcceptanceEvidence','contacts.read','contacts.create')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.update','quotes.approve','quotes.readAcceptanceEvidence','contacts.read','studio.read','studio.configure','access.read','access.configure')
 ProvisionSecurityMember $emails[2] @('workspace.context.resolve','quotes.read','contacts.read')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044')
 SetPolicy 'b6-approval-v1';LifecyclePolicy;DocumentPolicy
 EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Version" 'b6-approved-documentary-v1'
 $decision="QuoteCustomerDecisionPolicies__Workspaces__$($script:workspace)__";EnvValue ($decision+'Version') 'b6-keep-b3-v1';EnvValue ($decision+'AllowReportedAcceptance') 'true';EnvValue ($decision+'AllowRejection') 'true'
 EnvValue "QuoteProofStoragePolicies__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "QuoteProofStoragePolicies__Workspaces__$($script:workspace)__Version" 'b61-approved-v1'
 StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="KHÁCH HÀNG SYNTHETIC B6 $run";workEmail='b6-buyer@example.test'} "b6-buyer-$run";Check $contact 201
 $script:buyer=$contact.body.result.contact.id
 $cfg=Api $emails[1] GET '/workspace-configuration';Check $cfg 200
 $business=$cfg.body.businessInformation;$business.displayName='SYNTHETIC B6 SELLER';$business.legalName='CÔNG TY SYNTHETIC B6 — CHỈ KIỂM THỬ';$business.email='b6-seller@example.test';$business.taxId='';$business.logoReference=''
 $address=@{id="b6_registered_$run";name='Địa chỉ đăng ký synthetic';purposes=@('REGISTERED');addressLine1='ĐỊA CHỈ SYNTHETIC B6 — KHÔNG DÙNG THỰC';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='';contactPhone='';active=$true}
 Check (Api $emails[1] PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=@($address)} "b6-seller-$run" $cfg.body.revision) 200
 $draft=Assessed (DraftQuote 'acceptance-readiness') 'readiness';$issued=Issue $draft 'readiness';Check $issued 201;$document=$issued.body.result.document
 $sent=Api $emails[0] POST "/quotes/$($draft.id)/send" @{deliveryId="b6_sent_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic channel only';note='B2 manual confirmation, not customer consent'} "b6-send-$run" $issued.body.version;Check $sent 200
 $report=Api $emails[0] POST "/quotes/$($draft.id)/customer-decisions" @{decisionId="b6_report_$run";evidenceType='USER_REPORTED_ACCEPTANCE';sourceChannel='OTHER';sourceDescription='Synthetic employee report remains unverified'} "b6-report-$run" $sent.body.version;Check $report 200;$quote=$report.body.result.quote

 $base="/quotes/$($quote.id)/acceptance-cases"
 $binding=@{documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint}
 $caseResult=Api $emails[0] POST $base $binding "proof-case-$run" $quote.resourceVersion;Check $caseResult 201;$case=$caseResult.body.result.case
 $wrong=$binding.Clone();$wrong.quoteRevision=999;Check (Api $emails[0] POST $base $wrong "proof-wrong-rev-$run" $quote.resourceVersion) 409
 $wrong=$binding.Clone();$wrong.documentId='qdoc_wrong';Check (Api $emails[0] POST $base $wrong "proof-wrong-doc-$run" $quote.resourceVersion) 404
 Check (Api $emails[2] GET $base) 403
 Check (Api $emails[0] GET "$base/$($case.caseId)") 200
 Check (Api $emails[0] GET "$base/$($case.caseId)/proofs?cursor=case") 422
 $download=Download $quote.id $document.documentId 'safe-synthetic-fixture';Check $download 200;$bytes=[IO.File]::ReadAllBytes($download.path)
 $key="proof-upload-$run";$r=Upload $quote.id $case.caseId $bytes $key 1;Check $r 201;$proof=$r.body.result.proof
 Assert-Demo ($proof.storageStatus -ceq 'QUARANTINED' -and $proof.scanStatus -ceq 'SCAN_PENDING' -and !$proof.contentAvailable -and $proof.sha256 -ceq $download.sha256) 'QUARANTINE' 'Uploaded bytes must remain unavailable and pending scan'
 $replay=Upload $quote.id $case.caseId $bytes $key 1;Check $replay 201;Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.result.proof.proofId -ceq $proof.proofId) 'REPLAY' 'One immutable proof per replay'
 $changed=[Text.Encoding]::ASCII.GetBytes("%PDF-1.7`n% SAFE SYNTHETIC DIFFERENT INPUT`n%%EOF")
 Check (Upload $quote.id $case.caseId $changed $key 1) 409 'IDEMPOTENCY_KEY_REUSED'
 Check (Upload $quote.id $case.caseId $changed "proof-stale-$run" 1) 412 'VERSION_CONFLICT'
 Check (Upload $quote.id $case.caseId $bytes "proof-duplicate-$run" 2) 409 'PROOF_ALREADY_EXISTS'
 Check (Upload $quote.id $case.caseId $bytes "proof-missing-perm-$run" 2 $emails[2]) 403
 Check (Upload 'quote_other' $case.caseId $bytes "proof-wrong-quote-$run" 2) 404
 Check (Upload $quote.id $case.caseId $bytes "proof-mime-$run" 2 $emails[0] 'text/plain') 415
 Check (Upload $quote.id $case.caseId ([byte[]]::new(0)) "proof-empty-$run" 2) 422
 Check (Upload $quote.id $case.caseId ([Text.Encoding]::ASCII.GetBytes('ordinary corrupt input')) "proof-bad-$run" 2) 422
 Check (Upload $quote.id $case.caseId ([byte[]]::new(10485761)) "proof-large-$run" 2) 413
 Check (Upload $quote.id $case.caseId $bytes "proof-clean-$run" 2 $emails[0] 'application/pdf' @{'X-Proof-ScanStatus'='CLEAN'}) 422
 Check (Upload $quote.id $case.caseId $bytes "proof-verified-$run" 2 $emails[0] 'application/pdf' @{'X-Proof-VerificationStatus'='VERIFIED'}) 422
 $proofPath="$base/$($case.caseId)/proofs/$($proof.proofId)";Check (Api $emails[0] GET $proofPath) 200
 $security=Api $emails[0] GET ($proofPath+'/security');Check $security 200;Assert-Demo (!$security.body.contentAvailable -and !$security.body.scannerAvailable) 'NO_RELEASE' 'Metadata never releases quarantine bytes'
 # B6.2 adds a protected content route; B6.1 actors receive no new content capability.
 Check (Api $emails[0] GET ($proofPath+'/content')) 403
 Check (Api $emails[2] GET ($proofPath+'/content')) 403
 Check (Api $emails[0] POST "/workflows/quote-acceptance/$($quote.id)/accept-and-close-deal" @{} "proof-accept-guard-$run" $quote.resourceVersion) 409
 $counts=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.AcceptanceCases WHERE WorkspaceId=@ws) Cases,(SELECT COUNT(*) FROM quotes.AcceptanceProofs WHERE WorkspaceId=@ws) Proofs,(SELECT COUNT(*) FROM quotes.AcceptanceProofContents WHERE WorkspaceId=@ws) Contents,(SELECT COUNT(*) FROM quotes.Quotes WHERE WorkspaceId=@ws AND Status=''ACCEPTED'') Accepted,(SELECT COUNT(*) FROM deals.Deals WHERE WorkspaceId=@ws AND WonAt IS NOT NULL) Won' @{'@ws'=$script:workspace}
 $counts|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'sql-counts.json')
 if($Browser){
  $browserDraft=Assessed (DraftQuote 'browser-proof') 'browser-proof';$bi=Issue $browserDraft 'browser-proof';Check $bi 201;$browserDocument=$bi.body.result.document
  $bs=Api $emails[0] POST "/quotes/$($browserDraft.id)/send" @{deliveryId="proof_browser_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic proof test'} "proof-browser-send-$run" $bi.body.version;Check $bs 200;$browserQuote=$bs.body.result.quote
  $browserFixture=Download $browserQuote.id $browserDocument.documentId 'browser-safe-fixture';Check $browserFixture 200
  BrowserPhase 'upload'
 }
 Stop-DemoHost;StartHost;LoginAll
 if($Browser){BrowserPhase 'restart'}
 $persisted=Api $emails[0] GET $proofPath;Check $persisted 200;Assert-Demo ($persisted.body.sha256 -ceq $proof.sha256) 'RESTART' 'Proof metadata persists'
 EnvValue 'UNICORE_PROOF_VERIFY_CONNECTION' $script:connectionString
 $integrity=& (Join-Path $repo 'scripts/QuoteProofVerifier/bin/Debug/net10.0/UnicoreCRM.QuoteProofVerifier.exe') $script:workspace $case.caseId $proof.proofId
 Assert-Demo ($LASTEXITCODE -eq 0) 'PROOF_INTEGRITY' 'Persisted encrypted bytes must decrypt with retained keys and match server SHA'
 $integrity|Set-Content (Join-Path $script:stateDirectory 'integrity-after-restart.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;documentId=$document.documentId;caseId=$case.caseId;proofId=$proof.proofId;sha256=$proof.sha256;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.1 API/SQL $script:stateDirectory"
}
