#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-verified-acceptance-b6/api-'+[guid]::NewGuid().ToString('N'))
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
$securityKey='unicore-quote-verified-acceptance-b6-'+$run.Substring(0,8)
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
function BrowserPhase([string]$Phase){
 foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_RESTRICTED_EMAIL=$emails[2];UNICORECRM_TEST_RESTRICTED_PASSWORD=$passwords[$emails[2]];UNICORECRM_TEST_WORKSPACE_KEY=$securityKey;UNICORECRM_TEST_LOW_QUOTE_ID=$quote.id;UNICORECRM_TEST_DOCUMENT_ID=$document.documentId;UNICORECRM_TEST_DOCUMENT_SHA=$document.pdfSha256;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 [void](New-Item -ItemType Directory -Force (Join-Path $script:stateDirectory 'browser'))
 Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
 try{npx playwright test --config playwright.quote-acceptance.config.ts --grep "@$Phase";$exit=$LASTEXITCODE}finally{Pop-Location}
 Get-ChildItem -LiteralPath $script:stateDirectory -Recurse -File -Filter 'error-context.md'|ForEach-Object{$text=[IO.File]::ReadAllText($_.FullName);[IO.File]::WriteAllText($_.FullName,[regex]::Replace($text,'B1-[A-Fa-f0-9]{64}!a9','[REDACTED_SYNTHETIC_CREDENTIAL]'))}
 Assert-Demo ($exit -eq 0) 'B6_BROWSER' "Connected browser phase $Phase failed"
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.delete','quotes.issueDocument','quotes.submitAcceptanceEvidence','quotes.readAcceptanceEvidence','quotes.verifyAcceptanceEvidence','quotes.acceptVerified','contacts.read','contacts.create','deals.read','deals.create','deals.close')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.update','quotes.approve','quotes.readAcceptanceEvidence','quotes.verifyAcceptanceEvidence','quotes.acceptVerified','contacts.read','studio.read','studio.configure','access.read','access.configure')
 ProvisionSecurityMember $emails[2] @('workspace.context.resolve','quotes.read','contacts.read')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044')
 SetPolicy 'b6-approval-v1';LifecyclePolicy;DocumentPolicy
 EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Version" 'b6-approved-documentary-v1'
 $decision="QuoteCustomerDecisionPolicies__Workspaces__$($script:workspace)__";EnvValue ($decision+'Version') 'b6-keep-b3-v1';EnvValue ($decision+'AllowReportedAcceptance') 'true';EnvValue ($decision+'AllowRejection') 'true'
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
 $baseline=SQLRows 'SELECT ResourceVersion,Status,(SELECT COUNT(*) FROM quotes.Documents d WHERE d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId) Documents,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords e WHERE e.WorkspaceId=q.WorkspaceId AND e.QuoteId=q.QuoteId) Decisions FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$quote.id}
 $path="/quotes/$($quote.id)/customer-acceptance";$bindingPath=$path+'?documentId='+$document.documentId
 $r=Api $emails[1] GET $bindingPath;Check $r 200
 Assert-Demo ($r.body.status -ceq 'BLOCKED_CONTRACT' -and $r.body.verificationStatus -ceq 'NOT_VERIFIED' -and !$r.body.lifecycleCommitted -and $r.body.binding.pdfSha256 -ceq $document.pdfSha256 -and $r.body.resourceVersion -ne $r.body.binding.sourceResourceVersion -and $r.body.authorityFreshnessHours -eq 24) 'B6_BINDING' 'Exact B5 binding and blocked truth must survive unrelated resource-version changes'
 foreach($code in 'BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE','BLOCKED_CONTRACT_TRUSTED_SCANNER','BLOCKED_CONTRACT_ACCEPTANCE_PROOF_STORAGE'){Assert-Demo ($code -in $r.body.blockerCodes) 'B6_DEPENDENCY' 'All missing trusted boundaries must be explicit'}
 $r.body|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'readiness.json')
 $assessment=@{intent='VERIFY';evidenceId='evidence_nonexistent';documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint}
 Check (Api $emails[0] POST ($path+'/evaluate') $assessment "b6-self-$run" $quote.resourceVersion) 403 'ACCESS_DENIED' 'SELF_VERIFICATION_FORBIDDEN'
 foreach($attempt in 1..2){$r=Api $emails[1] POST ($path+'/evaluate') $assessment "b6-repeat-$run" $quote.resourceVersion;Check $r 200;Assert-Demo (!$r.body.lifecycleCommitted -and $r.body.verificationStatus -ceq 'NOT_VERIFIED') 'B6_REPEAT' 'Repeated read assessments never create a verified receipt'}
 Check (Api $emails[1] POST ($path+'/evaluate') $assessment "b6-stale-$run" ($quote.resourceVersion-1)) 412 'VERSION_CONFLICT'
 foreach($field in 'pdfSha256','commercialContentFingerprint','quoteRevision'){$wrong=$assessment.Clone();$wrong[$field]=$(if($field -eq 'quoteRevision'){999}else{'0'*64});Check (Api $emails[1] POST ($path+'/evaluate') $wrong "b6-wrong-$field-$run" $quote.resourceVersion) 409 'QUOTE_ACCEPTANCE_BINDING_MISMATCH'}
 $wrong=$assessment.Clone();$wrong.documentId='qdoc_fabricated';Check (Api $emails[1] POST ($path+'/evaluate') $wrong "b6-fake-document-$run" $quote.resourceVersion) 404 'RESOURCE_NOT_FOUND'
 foreach($field in 'verificationStatus','verifiedBy','signerAuthorityVerified','pdfBytes','scannerClean'){$forged=$assessment.Clone();$forged[$field]='FORGED';Check (Api $emails[1] POST ($path+'/evaluate') $forged "b6-forged-$field-$run" $quote.resourceVersion) 422 'VALIDATION_FAILED'}
 Check (Api $emails[1] POST ($path+'/evaluate') @{} "b6-empty-$run" $quote.resourceVersion) 422 'VALIDATION_FAILED'
 $accept=$assessment.Clone();$accept.intent='ACCEPT';$r=Api $emails[1] POST ($path+'/evaluate') $accept "b6-accept-assessment-$run" $quote.resourceVersion;Check $r 200;Assert-Demo (!$r.body.lifecycleCommitted) 'B6_NO_COMMIT' 'Assessment is not a Direct Sale acceptance command'
 Check (Api $emails[0] POST "/workflows/quote-acceptance/$($quote.id)/accept-and-close-deal" @{} "b6-legacy-guard-$run" $quote.resourceVersion) 409 'QUOTE_ACCEPTANCE_BLOCKED'
 Check (Api $emails[2] GET $bindingPath) 403 'ACCESS_DENIED'
 Check (Api $emails[2] POST ($path+'/evaluate') $assessment "b6-restricted-$run" $quote.resourceVersion) 403 'ACCESS_DENIED'
 # Unsupported binary proof input is refused before storage/scanning; this is not a malware-scanner PASS.
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098$path/evaluate")
 try{
  foreach($h in @{Authorization="Bearer $($script:tokens[$emails[1]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b6-binary-$run";'X-Correlation-Id'="b6-binary-$run";'Idempotency-Key'="b6-binary-$run";'If-Match'=('"'+$quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $request.Content=[Net.Http.ByteArrayContent]::new([Text.Encoding]::UTF8.GetBytes('%PDF-1.7 /OpenAction /JavaScript (hostile test payload)'))
  $request.Content.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new('application/pdf')
  $response=$client.SendAsync($request).GetAwaiter().GetResult();try{$binary=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};Check $binary 422 'VALIDATION_FAILED';$cases.Add(@{scenario='UNSUPPORTED_BINARY_INGRESS_REFUSED_NOT_MALWARE_SCAN';response=$binary})}finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($n in 1..2){$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098$path/evaluate")
   foreach($h in @{Authorization="Bearer $($script:tokens[$emails[1]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b6-parallel-$n-$run";'X-Correlation-Id'="b6-parallel-$n-$run";'Idempotency-Key'="b6-parallel-$run";'If-Match'=('"'+$quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
   $request.Content=[Net.Http.StringContent]::new(($assessment|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json');$pending+=@{request=$request;task=$client.SendAsync($request)}
  }
  foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$parallel=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};Check $parallel 200;Assert-Demo (!$parallel.body.lifecycleCommitted -and $parallel.body.verificationStatus -ceq 'NOT_VERIFIED') 'B6_PARALLEL_READS' 'Concurrent assessments cannot commit a contradictory customer decision';$cases.Add(@{scenario='CONCURRENT_READ_ASSESSMENT_ONLY';response=$parallel})}finally{$response.Dispose()}}
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
 $oldWorkspace=$script:workspace;$oldKey=$securityKey;$securityKey='unicore-b6-foreign-'+$run.Substring(0,8)
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.verifyAcceptanceEvidence','quotes.acceptVerified','quotes.update','contacts.read')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 Check (Api $emails[0] GET $bindingPath) 404 'RESOURCE_NOT_FOUND'
 Check (Api $emails[0] POST ($path+'/evaluate') $assessment "b6-foreign-$run" $quote.resourceVersion) 404 'RESOURCE_NOT_FOUND'
 $script:workspace=$oldWorkspace;$securityKey=$oldKey
 Check (Api 'anonymous@example.test' GET $bindingPath) 401 'AUTHENTICATION_REQUIRED'
 $role=Api $emails[1] POST '/access/roles' @{name="B6 FLS proof restriction $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.verifyAcceptanceEvidence','quotes.acceptVerified','quotes.update','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='grandTotal';access='HIDDEN'})} "b6-fls-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 $version=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 $assigned=Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b6-fls-assign-$run" $version;Check $assigned 200
 Check (Api $emails[2] GET $bindingPath) 403 'ACCESS_DENIED'
 Check (Api $emails[2] POST ($path+'/evaluate') $assessment "b6-repeat-$run" $quote.resourceVersion) 403 'ACCESS_DENIED'
 $restore=Api $emails[1] POST '/access/roles' @{name="B6 readonly restored $run";capabilities=@('workspace.context.resolve','quotes.read','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@()} "b6-role-restore-$run";Check $restore 200
 $version=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($restore.body.aggregateId);teamIds=@()} "b6-fls-restore-$run" $version) 200
 $download=Download $quote.id $document.documentId 'b6-existing-document';Check $download 200;Assert-Demo ($download.sha256 -ceq $document.pdfSha256) 'B6_B5_HASH' 'Assessment never changes issued B5 bytes'
 $expiredBody=Draft "B6 effective expiry $run" '1000';$expiredBody.validUntil=[DateTime]::UtcNow.Date.AddDays(-1).ToString('yyyy-MM-dd')
 $expired=Api $emails[0] POST '/quotes' $expiredBody "b6-expired-create-$run";Check $expired 201
 $r=Api $emails[1] GET "/quotes/$($expired.body.aggregateId)/customer-acceptance";Check $r 200;Assert-Demo ('QUOTE_EXPIRED' -in $r.body.blockerCodes) 'B6_EXPIRED' 'Effective expiry must remain a blocker independently of missing providers'
 $archive=DraftQuote 'archived-assessment';$ar=Api $emails[0] POST "/quotes/$($archive.id)/archive" @{reason='Synthetic B6 archive guard'} "b6-archive-$run" $archive.resourceVersion;Check $ar 200
 $r=Api $emails[1] GET "/quotes/$($archive.id)/customer-acceptance";Check $r 200;Assert-Demo ('QUOTE_STATE_INVALID' -in $r.body.blockerCodes) 'B6_ARCHIVED' 'Archived Quote cannot become eligible'
 $invalid=Assessed (DraftQuote 'invalidated-approval') 'invalidated-approval';$change=Draft 'B6 changed commercial content' '1001';$change.validUntil=$invalid.validUntil
 $updated=Api $emails[0] POST "/quotes/$($invalid.id)/update-draft" $change "b6-invalidate-$run" $invalid.resourceVersion;Check $updated 200
 $r=Api $emails[1] GET "/quotes/$($invalid.id)/customer-acceptance";Check $r 200;Assert-Demo ('APPROVAL_BINDING_STALE' -in $r.body.blockerCodes) 'B6_APPROVAL_STALE' 'Changed commercial content cannot reuse its prior B1 assessment'
 $profile=Api $emails[0] GET '/access/context';Check $profile 200
 $di=@{name="B6 linked synthetic Deal $run";buyerRef=@{type='CONTACT';id=$script:buyer};amount=@{amount='1000';currency='VND'};opportunityScore='70';ownerId=$profile.body.memberId;expectedCloseDate=[DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd');interestedProductIds=@();lineItems=@();contactId=$script:buyer}
 $dr=Api $emails[0] POST '/deals' $di "b6-linked-deal-$run";Check $dr 201;$deal=$dr.body.result.deal
 $draftBody=Draft "B6 linked Quote $run" '1000';$draftBody.Remove('buyerRef');$draftBody.Remove('sourcePath');$draftBody.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $lr=Api $emails[0] POST "/deals/$($deal.id)/quotes" $draftBody "b6-linked-quote-$run" $deal.resourceVersion;Check $lr 201
 $linked=Assessed $lr.body.result.quote 'linked';$li=Issue $linked 'linked';Check $li 201
 $ls=Api $emails[0] POST "/quotes/$($linked.id)/send" @{deliveryId="b6_linked_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic linked channel'} "b6-linked-send-$run" $li.body.version;Check $ls 200
 $dealHash=[string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT d.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM deals.Deals d WHERE WorkspaceId=@ws AND DealId=@d' @{'@ws'=$script:workspace;'@d'=$deal.id}).Rows[0].Hash
 $r=Api $emails[1] GET "/quotes/$($linked.id)/customer-acceptance?documentId=$($li.body.result.document.documentId)";Check $r 200;Assert-Demo ('LINKED_ACCEPTANCE_BLOCKED_GATE_C' -in $r.body.blockerCodes -and $r.body.binding.quoteId -ceq $linked.id) 'B6_LINKED_BINDING' 'Linked document binding must never imply atomic acceptance/Won'
 Check (Api $emails[0] POST "/workflows/quote-acceptance/$($linked.id)/accept-and-close-deal" @{} "b6-linked-accept-$run" $ls.body.version) 409 'QUOTE_ACCEPTANCE_BLOCKED' 'ATOMIC_ACCEPTANCE_CONTRACT_UNAVAILABLE'
 Check (Api $emails[0] POST "/deals/$($deal.id)/mark-won" @{evidence=@{type='QUOTE_ACCEPTED';sourceId=$linked.id;occurredAt=[DateTime]::UtcNow.ToString('O')}} "b6-won-guard-$run" $deal.resourceVersion) 422 'DEAL_WIN_EVIDENCE_INVALID'
 $afterHash=[string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT d.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM deals.Deals d WHERE WorkspaceId=@ws AND DealId=@d' @{'@ws'=$script:workspace;'@d'=$deal.id}).Rows[0].Hash
 Assert-Demo ($dealHash -ceq $afterHash) 'B6_DEAL_UNCHANGED' 'Evidence assessment and denied Won must not change Deal/Pipeline/Stage'
 @{dealId=$deal.id;quoteId=$linked.id;beforeHash=$dealHash;afterHash=$afterHash}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'linked-deal-proof.json')
 if($Browser){BrowserPhase 'review';Stop-DemoHost;StartHost;LoginAll;BrowserPhase 'restart'}
 $afterAssessment=SQLRows 'SELECT ResourceVersion,Status,(SELECT COUNT(*) FROM quotes.Documents d WHERE d.WorkspaceId=q.WorkspaceId AND d.QuoteId=q.QuoteId) Documents,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords e WHERE e.WorkspaceId=q.WorkspaceId AND e.QuoteId=q.QuoteId) Decisions FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@q' @{'@ws'=$script:workspace;'@q'=$quote.id}
 Assert-Demo (($baseline|ConvertTo-Json -Compress) -ceq ($afterAssessment|ConvertTo-Json -Compress)) 'B6_NO_SIDE_EFFECTS' 'Assessments must not mutate Quote/documents/decisions'
 $afterAssessment|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'assessment-sql.json')
 Stop-DemoHost;EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Enabled" 'invalid';StartHost;LoginAll
 $r=Api $emails[1] GET $bindingPath;Check $r 200;Assert-Demo ('VERIFICATION_POLICY_MISSING_OR_INVALID' -in $r.body.blockerCodes) 'B6_BAD_CONFIG' 'Invalid policy must fail closed without breaking reads'
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;positiveVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;documentId=$document.documentId;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6 independent readiness/binding/negative API/SQL; positive BLOCKED_CONTRACT. $script:stateDirectory"
}
