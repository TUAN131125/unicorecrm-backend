#requires -Version 7.0
[CmdletBinding()]
param()
$scannerEvidenceKind='api'
. (Join-Path $PSScriptRoot 'scanner/QuoteIdentityVerificationRuntime.ps1')

try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false';QuoteProofScan__Enabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.issueDocument','quotes.submitAcceptanceEvidence','quotes.readAcceptanceEvidence','quotes.readAcceptanceProofContent','contacts.read','contacts.create','quotes.recordAcceptanceSourceCheck','quotes.assessAcceptanceIdentityAuthority')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.update','quotes.approve','quotes.readAcceptanceEvidence','contacts.read','studio.read','studio.configure','access.read','access.configure','quotes.manageAcceptanceEvidenceSources','quotes.assessAcceptanceIdentityAuthority')
 ProvisionSecurityMember $emails[2] @('workspace.context.resolve','quotes.read','contacts.read','quotes.readAcceptanceEvidence','quotes.readAcceptanceProofContent','quotes.recordAcceptanceSourceCheck','quotes.assessAcceptanceIdentityAuthority','quotes.manageAcceptanceEvidenceSources')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__Version" 'b63-approved-v1'
 StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="KHÁCH HÀNG SYNTHETIC SCANNER $run";workEmail='b62-buyer@example.test'} "b62-buyer-$run";Check $contact 201;$script:buyer=$contact.body.result.contact.id
 $cfg=Api $emails[1] GET '/workspace-configuration';Check $cfg 200
 $business=$cfg.body.businessInformation;$business.displayName='SYNTHETIC SCANNER SELLER';$business.legalName='CÔNG TY SYNTHETIC — CHỈ KIỂM THỬ';$business.email='b62-seller@example.test';$business.taxId='';$business.logoReference=''
 $address=@{id="b62_registered_$run";name='Địa chỉ đăng ký synthetic';purposes=@('REGISTERED');addressLine1='ĐỊA CHỈ SYNTHETIC — KHÔNG DÙNG THỰC';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='';contactPhone='';active=$true}
 Check (Api $emails[1] PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=@($address)} "b62-seller-$run" $cfg.body.revision) 200
 $draft=Assessed (DraftQuote 'scanner') 'scanner';$issued=Issue $draft 'scanner';Check $issued 201;$document=$issued.body.result.document
 $sent=Api $emails[0] POST "/quotes/$($draft.id)/send" @{deliveryId="b62_sent_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic scanner test only'} "b62-send-$run" $issued.body.version;Check $sent 200;$quote=$sent.body.result.quote
 $base="/quotes/$($quote.id)/acceptance-cases"
 $binding=@{documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint}
 $created=Api $emails[0] POST $base $binding "b62-case-$run" $quote.resourceVersion;Check $created 201;$case=$created.body.result.case
 $fixture=Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf';$bytes=[IO.File]::ReadAllBytes($fixture)
 $uploaded=Upload $quote.id $case.caseId $bytes "b63-upload-$run" $case.resourceVersion;Check $uploaded 201;$proof=$uploaded.body.result.proof
 $proofPath="$base/$($case.caseId)/proofs/$($proof.proofId)";$identityPath="$base/$($case.caseId)/identity-authority";$sourcesPath="/quotes/$($quote.id)/acceptance-evidence-sources"
 $initial=Api $emails[0] GET $identityPath;Check $initial 200
 $bindingInput=@{contactId=$script:buyer;identityProofIds=@($proof.proofId);authorityProofIds=@($proof.proofId);reason='SYNTHETIC documentary binding only'}
 Check (Api $emails[0] PUT "$identityPath/binding" $bindingInput "b63-unclean-$run" 0) 409
 Stop-DemoHost;ScanPolicy;StartHost;LoginAll
 $null=WaitScan $proofPath 'CLEAN'
 $saved=Api $emails[0] PUT "$identityPath/binding" $bindingInput "b63-binding-$run" 0;Check $saved 200;$view=$saved.body.result
 Assert-Demo (!$view.assessment.readyForIndependentReview -and $view.binding.trustStatus -ceq 'DOCUMENT_SUBMITTED') 'B63_UNVERIFIED' 'Safe documents are not identity verification'
 $replay=Api $emails[0] PUT "$identityPath/binding" $bindingInput "b63-binding-$run" 0;Check $replay 200
 Assert-Demo ($replay.body.outcome -ceq 'REPLAYED') 'B63_REPLAY' 'No duplicate identity version'
 Check (Api $emails[0] POST "$identityPath/source-checks" @{subjectId=$view.binding.subjectId} "b63-self-$run" 1) 403
 Check (Api $emails[0] POST $sourcesPath @{name='Denied'} "b63-source-deny-$run" 0) 403
 $sourceInput=@{name='SYNTHETIC unapproved source draft';sourceType='INTERNAL_PROCEDURE';issuerReference='SYNTHETIC NOT AN AUTHORITY';verificationMethod='UNADMITTED_DOCUMENTARY';provenanceReference='SYNTHETIC test metadata';requirements='No actual provider or legal source. Must never become approved.'}
 $registered=Api $emails[1] POST $sourcesPath $sourceInput "b63-source-$run" 0;Check $registered 200;$source=$registered.body.result
 Check (Api $emails[1] POST "$sourcesPath/$($source.sourceId)/approve" @{reason='Synthetic self-admission negative test'} "b63-self-admit-$run" 1) 403
 Check (Api $emails[2] POST "$sourcesPath/$($source.sourceId)/approve" @{reason='No actual method admitted'} "b63-no-source-$run" 1) 409
 $bindingInput.expectedSubjectId=$view.binding.subjectId;$bindingInput.sourceId=$source.sourceId;$bindingInput.sourceVersion=$source.resourceVersion;$bindingInput.reason='Attach unapproved draft for negative provenance test'
 $saved=Api $emails[0] PUT "$identityPath/binding" $bindingInput "b63-source-bind-$run" 1;Check $saved 200;$view=$saved.body.result
 $check=Api $emails[2] POST "$identityPath/source-checks" @{subjectId=$view.binding.subjectId} "b63-check-$run" 2;Check $check 200;$view=$check.body.result
 Assert-Demo ($view.lastSourceCheck.outcome -ceq 'SOURCE_UNAVAILABLE' -and !$view.lastSourceCheck.checkedAt -and !$view.assessment.readyForIndependentReview) 'B63_NO_FALSE_SOURCE_CHECK' 'Attempt timestamp is not a successful source check'
 $assessed=Api $emails[1] POST "$identityPath/assessments" @{subjectId=$view.binding.subjectId} "b63-assess-$run" 3;Check $assessed 200;$view=$assessed.body.result
 Check (Api $emails[1] POST "$identityPath/assessments" @{subjectId=$view.binding.subjectId} "b63-stale-$run" 3) 412
 Check (Api $emails[1] POST "$identityPath/assessments" @{subjectId='qsubject_wrong'} "b63-subject-$run" 4) 409
 $forged=$bindingInput.Clone();$forged.verified=$true;Check (Api $emails[0] PUT "$identityPath/binding" $forged "b63-forged-$run" 4) 422
 $revoked=Api $emails[1] POST "$sourcesPath/$($source.sourceId)/revoke" @{reason='Synthetic revocation guard test'} "b63-revoke-$run" 1;Check $revoked 200
 $current=Api $emails[0] GET $identityPath;Check $current 200
 Assert-Demo ($current.body.assessment.reasons -contains 'SOURCE_REVOKED' -and $current.body.assessment.reasons -contains 'SOURCE_VERSION_CHANGED') 'B63_REVOCATION' 'Current facts must invalidate the old draft binding'
 $history=Api $emails[1] GET "$identityPath/history";Check $history 200
 Assert-Demo ($history.body.items.Count -eq 4) 'B63_HISTORY' 'Binding, rebind, unavailable source attempt and assessment append immutable versions'
 Stop-DemoHost;StartHost;LoginAll
 $current=Api $emails[0] GET $identityPath;Check $current 200
 $ready=Api $emails[0] GET "/quotes/$($quote.id)/customer-acceptance?documentId=$($document.documentId)";Check $ready 200
 Assert-Demo ($ready.body.status -ceq 'BLOCKED_CONTRACT' -and $ready.body.identityAuthority.status -ceq 'AUTHORITY_INVALID') 'B63_READINESS' 'Existing readiness incorporates current identity component'
 $counts=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.IdentityAuthorityVersions WHERE WorkspaceId=@ws) IdentityVersions,(SELECT COUNT(*) FROM quotes.AcceptanceEvidenceSourceVersions WHERE WorkspaceId=@ws) SourceVersions,(SELECT COUNT(*) FROM quotes.Quotes WHERE WorkspaceId=@ws AND Status=''ACCEPTED'') Accepted,(SELECT COUNT(*) FROM deals.Deals WHERE WorkspaceId=@ws AND WonAt IS NOT NULL) Won' @{'@ws'=$script:workspace}
 $counts|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'identity-sql-counts.json')
 $status='PARTIAL_FUNCTIONAL_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;documentId=$document.documentId;caseId=$case.caseId;proofId=$proof.proofId;sourceId=$source.sourceId;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3 API/SQL $script:stateDirectory"
}
