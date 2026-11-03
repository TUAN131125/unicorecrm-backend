#requires -Version 7.0
[CmdletBinding()]
param()
$scannerEvidenceKind='api'
. (Join-Path $PSScriptRoot 'scanner/QuoteIndependentReviewVerificationRuntime.ps1')
$method='ORGANIZATION_ISSUED_AUTHORITY_WITH_INDEPENDENT_SOURCE_CHECK'
try{
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 $baseCaps=@('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.readAcceptanceProofContent','quotes.manageAcceptanceEvidenceSources','quotes.assessAcceptanceIdentityAuthority','contacts.read','organizations.read')
 ProvisionSecurityMember $emails[0] ($baseCaps+@('quotes.create','quotes.update','quotes.issueDocument','quotes.submitAcceptanceEvidence','contacts.create','contacts.update','organizations.create'))
 ProvisionSecurityMember $emails[1] ($baseCaps+@('quotes.update','quotes.approve','quotes.verifyAcceptanceEvidence','quotes.recordAcceptanceSourceCheck','studio.read','studio.configure','access.read','access.configure'))
 ProvisionSecurityMember $emails[2] ($baseCaps+@('quotes.recordAcceptanceSourceCheck'))
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b63b-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-adjusted-v1'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1';QuoteIdentityPolicies='b63b-manual-simulation-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ManualDevelopmentSimulation" 'true'
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__IndependentReviewSimulation" 'true';EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ReviewPolicyVersion" 'b64-review-v1'
 StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="SYNTHETIC Manual Source Representative $run";workEmail='synthetic-representative@example.test'} "b63b-contact-$run";Check $contact 201;$contactId=$contact.body.result.contact.id
 $org=Api $emails[0] POST '/organizations' @{displayName="SYNTHETIC Issuer Organization $run";status='active'} "b63b-org-$run";Check $org 200;$orgId=$org.body.result.id
 $relation=Api $emails[0] POST "/contacts/$contactId/organization-relationships" @{organizationId=$orgId;role='buyer';isPrimaryAffiliation=$true} "b63b-affiliation-$run" 0;Check $relation 200;$relationshipId=$relation.body.result.organizationRelationship.relationshipId
 $cfg=Api $emails[1] GET '/workspace-configuration';Check $cfg 200
 $business=$cfg.body.businessInformation;$business.displayName='SYNTHETIC MANUAL SOURCE SELLER';$business.legalName='SYNTHETIC DEVELOPMENT COMPANY ONLY';$business.email='synthetic-seller@example.test';$business.taxId='';$business.logoReference=''
 $address=@{id="synthetic_registered_$run";name='Synthetic registered address';purposes=@('REGISTERED');addressLine1='SYNTHETIC DEVELOPMENT ADDRESS';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='';contactPhone='';active=$true}
 Check (Api $emails[1] PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=@($address)} "b63b-seller-$run" $cfg.body.revision) 200
 $body=Draft 'SYNTHETIC manual source workflow' '1000';$body.buyerRef=@{type='ORGANIZATION_ACCOUNT';id=$orgId};$body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $created=Api $emails[0] POST '/quotes' $body "b63b-quote-$run";Check $created 201;$quote=Assessed $created.body.result.quote 'manual-source'
 $issued=Issue $quote 'manual-source';Check $issued 201;$document=$issued.body.result.document
 $sent=Api $emails[0] POST "/quotes/$($quote.id)/send" @{deliveryId="b63b_sent_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='SYNTHETIC workflow only'} "b63b-send-$run" $issued.body.version;Check $sent 200;$quote=$sent.body.result.quote
 $base="/quotes/$($quote.id)/acceptance-cases";$newCase=Api $emails[0] POST $base @{documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint} "b63b-case-$run" $quote.resourceVersion;Check $newCase 201;$case=$newCase.body.result.case
 $safe=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf'))
 $proofs=@{};foreach($kind in @('issuer','channel','confirmation')){
  $bytes=[byte[]]($safe+[Text.Encoding]::ASCII.GetBytes("`n% SYNTHETIC $kind $run`n"));$u=Upload $quote.id $case.caseId $bytes "b63b-$kind-$run" $case.resourceVersion -Name "synthetic-$kind.pdf";Check $u 201;$case=$u.body.result.case;$proofs[$kind]=$u.body.result.proof
  $null=WaitScan "$base/$($case.caseId)/proofs/$($proofs[$kind].proofId)" 'CLEAN' 180
 }
 $path="$base/$($case.caseId)/identity-authority";$sources="/quotes/$($quote.id)/acceptance-evidence-sources"
 $claim=@{effectiveFrom=[DateTime]::UtcNow.AddDays(-1).ToString('yyyy-MM-ddTHH:mm:ssZ');effectiveUntil=[DateTime]::UtcNow.AddDays(10).ToString('yyyy-MM-ddTHH:mm:ssZ');currency='VND';amountLimit='100000000';scopeDescription='SYNTHETIC authority to review this exact issued quote'}
 $binding=@{contactId=$contactId;relationshipId=$relationshipId;identityProofIds=@($proofs.issuer.proofId);authorityProofIds=@($proofs.issuer.proofId);authorityClaim=$claim;reason='SYNTHETIC initial binding'}
 $r=Api $emails[0] PUT "$path/binding" $binding "b63b-binding-$run" 0;Check $r 200;$view=$r.body.result;$binding.expectedSubjectId=$view.binding.subjectId
 $sourceInput=@{name="SYNTHETIC organization-issued source $run";sourceType='ORGANIZATION_ISSUED';issuerReference=$orgId;verificationMethod=$method;provenanceReference='SYNTHETIC independently established directory record';requirements='Development simulation only; no real issuer verified';reason='SYNTHETIC source preparation';evidence=@{caseId=$case.caseId;issuerOrganizationId=$orgId;issuerProofIds=@($proofs.issuer.proofId);independentChannelProofIds=@($proofs.channel.proofId);channelOriginKind='PUBLISHED_ORGANIZATION_DIRECTORY';channelOriginDescription='SYNTHETIC independent organization directory evidence, not the authority document itself';independentChannel='SYNTHETIC established directory channel';effectiveFrom=$claim.effectiveFrom;effectiveUntil=$claim.effectiveUntil}}
 $r=Api $emails[0] POST $sources $sourceInput "b63b-source-$run" 0;Check $r 200;$source=$r.body.result
 $binding.sourceId=$source.sourceId;$binding.sourceVersion=1;$binding.reason='Bind unapproved synthetic source for denial test'
 $r=Api $emails[0] PUT "$path/binding" $binding "b63b-draft-bind-$run" 1;Check $r 200;$view=$r.body.result
 $manual=@{reportedOutcome='CONFIRMED';observedAt=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');confirmationProofIds=@($proofs.confirmation.proofId);independentChannelUsed=$sourceInput.evidence.independentChannel;confirmationSummary='SYNTHETIC independent issuer confirmation of representative and exact quoted scope; not real customer verification';confirmedScope=$claim}
 $checkBody=@{subjectId=$view.binding.subjectId;manualCheck=$manual}
 Check (Api $emails[2] POST "$path/source-checks" $checkBody "b63b-draft-check-$run" $view.resourceVersion) 409
 Check (Api $emails[0] POST "$sources/$($source.sourceId)/approve" @{reason='Synthetic self-approval must be denied'} "b63b-self-approve-$run" 1) 403
 $approved=Api $emails[1] POST "$sources/$($source.sourceId)/approve" @{reason='SYNTHETIC independent admission of issuer, provenance, channel and scope'} "b63b-approve-$run" 1;Check $approved 200;$source=$approved.body.result
 Assert-Demo ($source.status -ceq 'APPROVED' -and $source.assuranceLevel -ceq 'DEVELOPMENT_SIMULATION') 'B63B_SOURCE_SIMULATION' 'Approval cannot claim operational verification'
 $binding.sourceVersion=$source.resourceVersion;$binding.reason='Explicitly bind approved simulation source version'
 $r=Api $emails[0] PUT "$path/binding" $binding "b63b-approved-bind-$run" $view.resourceVersion;Check $r 200;$view=$r.body.result
 Check (Api $emails[1] POST "$path/source-checks" $checkBody "b63b-approver-check-$run" $view.resourceVersion) 403
 $missing=$manual.Clone();$missing.confirmationProofIds=@();Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$view.binding.subjectId;manualCheck=$missing} "b63b-missing-confirm-$run" $view.resourceVersion) 409
 $stale=$manual.Clone();$stale.observedAt=[DateTime]::UtcNow.AddHours(-25).ToString('yyyy-MM-ddTHH:mm:ssZ');Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$view.binding.subjectId;manualCheck=$stale} "b63b-stale-$run" $view.resourceVersion) 409
 $checked=Api $emails[2] POST "$path/source-checks" $checkBody "b63b-check-$run" $view.resourceVersion;Check $checked 200;$view=$checked.body.result
 Assert-Demo ($view.lastSourceCheck.outcome -ceq 'SOURCE_CHECKED_MANUAL' -and $view.assessment.readyForIndependentReview -and $view.assessment.assuranceLevel -ceq 'DEVELOPMENT_SIMULATION' -and !$view.assessment.actualSourceVerification) 'B63B_SIMULATION_READY' 'Only complete simulation evidence may become workflow-ready'
 $replay=Api $emails[2] POST "$path/source-checks" $checkBody "b63b-check-$run" ($view.resourceVersion-1);Check $replay 200;Assert-Demo ($replay.body.outcome -ceq 'REPLAYED') 'B63B_REPLAY' 'No duplicate check event'
 Check (Api $emails[2] POST "$path/source-checks" $checkBody "b63b-reuse-confirm-$run" $view.resourceVersion) 409
 Stop-DemoHost;StartHost;LoginAll
 $r=Api $emails[2] GET $path;Check $r 200;Assert-Demo ($r.body.assessment.readyForIndependentReview) 'B63B_RESTART' 'Current simulation readiness survives restart'
 $rootReady=Api $emails[2] GET "/quotes/$($quote.id)/customer-acceptance?documentId=$($document.documentId)";Check $rootReady 200
 Assert-Demo ($rootReady.body.verificationStatus -ceq 'NOT_VERIFIED' -and !$rootReady.body.lifecycleCommitted -and $rootReady.body.status -ceq 'BLOCKED_CONTRACT') 'B63B_NO_ACCEPTANCE' 'Simulation readiness cannot activate acceptance'
 $sql=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.AcceptanceEvidenceSourceVersions WHERE WorkspaceId=@ws AND Status=''APPROVED'') ApprovedSimulationVersions,(SELECT COUNT(*) FROM quotes.Quotes WHERE WorkspaceId=@ws AND Status=''ACCEPTED'') Accepted,(SELECT COUNT(*) FROM deals.Deals WHERE WorkspaceId=@ws AND WonAt IS NOT NULL) Won,(SELECT COUNT(*) FROM orders.Orders WHERE WorkspaceId=@ws) Orders' @{'@ws'=$script:workspace}
 $sql|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'workflow-sql.json')
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 70|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;documentId=$document.documentId;caseId=$case.caseId;sourceId=$source.sourceId;proofs=$proofs;directory=$script:stateDirectory}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3B manual source workflow $script:stateDirectory"
}
