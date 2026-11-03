#requires -Version 7.0
param([Parameter(Mandatory)][string]$FixtureResult,[switch]$SkipBrowserFixture)
$scannerEvidenceKind='security'
. (Join-Path $PSScriptRoot 'scanner/QuoteIndependentReviewVerificationRuntime.ps1')
function View { $r=Api $emails[1] GET $path;Check $r 200;return $r.body }
function Denied([string]$Label,[string]$Reason){$v=View;Assert-Demo ($Reason -in $v.blockers) 'B64_GUARD_REASON' "Missing authoritative blocker $Reason";$b=ReviewBody $v;Check (Api $emails[1] POST "$path/decisions" $b "b64-$Label-$run" $v.resourceVersion) 409}
try{
 $template=InitializeReview $FixtureResult
 $saved=NewManualSourceFixture $template 'review-guards' -Ready;$saved=AddConsent $saved
 $path="/quotes/$($saved.quoteId)/acceptance-cases/$($saved.caseId)/independent-review";$identity="/quotes/$($saved.quoteId)/acceptance-cases/$($saved.caseId)/identity-authority"
 $v=View;$b=ReviewBody $v
 # Genuine retained invalid PDF, unchanged parser/scanner policy and quota.
 $base="/quotes/$($saved.quoteId)/acceptance-cases/$($saved.caseId)";$c=Api $emails[0] GET $base;Check $c 200
 $bad=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/active.pdf'))
 $u=Upload $saved.quoteId $saved.caseId $bad "b64-unsafe-$run" $c.body.resourceVersion -Name 'synthetic-active-content.pdf';Check $u 201;$badProof=$u.body.result.proof
 $null=WaitScan "$base/proofs/$($badProof.proofId)" 'INVALID_PDF' 180
 $v=View;$b=ReviewBody $v;$b.findings[4].proofIds=@($badProof.proofId);$r=Api $emails[1] POST "$path/decisions" $b "b64-unclean-$run" $v.resourceVersion;Check $r 409 'QUOTE_INDEPENDENT_REVIEW_BLOCKED' 'PROOF_EFFECTIVE_CLEAN_REQUIRED'
 $old=Api $emails[0] GET $identity;Check $old 200;$binding=$old.body.binding
 $input=@{contactId=$binding.party.contactId;relationshipId=$binding.party.relationshipId;identityProofIds=@($binding.identityProofs.proofId);authorityProofIds=@($binding.authorityProofs.proofId);expectedSubjectId=$binding.subjectId;sourceId=$binding.sourceId;sourceVersion=$binding.sourceVersion;authorityClaim=$binding.authorityClaim;reason='SYNTHETIC review guards'}
 foreach($kind in @('amount','currency','expired-authority')){
  $latest=Api $emails[0] GET $identity;Check $latest 200;$change=$input.Clone();$change.authorityClaim=$binding.authorityClaim.Clone()
  switch($kind){'amount'{$change.authorityClaim.amountLimit='1'}'currency'{$change.authorityClaim.currency='USD'}'expired-authority'{$change.authorityClaim.effectiveUntil=[DateTime]::UtcNow.AddMinutes(-1).ToString('yyyy-MM-ddTHH:mm:ssZ')}}
  Check (Api $emails[0] PUT "$identity/binding" $change "b64-$kind-binding-$run" $latest.body.resourceVersion) 200
  Denied $kind $(if($kind -eq 'expired-authority'){'CLAIMED_AUTHORITY_OUTSIDE_VALIDITY'}else{'CLAIMED_AUTHORITY_SCOPE_LIMIT'})
 }
 $latest=Api $emails[0] GET $identity;Check $latest 200;Check (Api $emails[0] PUT "$identity/binding" $input "b64-restore-binding-$run" $latest.body.resourceVersion) 200
 $sources="/quotes/$($saved.quoteId)/acceptance-evidence-sources";$r=Api $emails[0] GET $sources;Check $r 200;$source=$r.body.items|Where-Object sourceId -ceq $saved.sourceId;$e=$source.evidence
 $revise=@{name=$source.name;sourceType=$source.sourceType;issuerReference=$source.issuerReference;verificationMethod=$source.verificationMethod;provenanceReference=$source.provenanceReference;requirements=$source.requirements;reason='SYNTHETIC source version guard';evidence=@{caseId=$e.caseId;issuerOrganizationId=$e.issuerOrganizationId;issuerProofIds=@($e.issuerProofs.proofId);independentChannelProofIds=@($e.independentChannelProofs.proofId);channelOriginKind=$e.channelOriginKind;channelOriginDescription=$e.channelOriginDescription;independentChannel=$e.independentChannel;effectiveFrom=$e.effectiveFrom;effectiveUntil=$e.effectiveUntil}}
 Check (Api $emails[0] PUT "$sources/$($source.sourceId)" $revise "b64-revise-source-$run" $source.resourceVersion) 200;Denied 'source-version' 'SOURCE_VERSION_CHANGED'
 $r=Api $emails[1] POST "$sources/$($source.sourceId)/approve" @{reason='SYNTHETIC independent admission of new source version'} "b64-reapprove-source-$run" 3;Check $r 200
 $latest=Api $emails[0] GET $identity;Check $latest 200;$input.sourceVersion=4;Check (Api $emails[0] PUT "$identity/binding" $input "b64-source-v4-bind-$run" $latest.body.resourceVersion) 200
 SetPolicy 'b64-changed-approval-policy';Stop-DemoHost;StartHost;LoginAll;Denied 'approval-policy' 'APPROVAL_BINDING_STALE'
 SetPolicy 'b63b-approval-v1';Stop-DemoHost;StartHost;LoginAll
 # Reuse the existing owner-approved Development lifecycle clock only for this isolated Workspace.
 $q=Api $emails[0] GET "/quotes/$($saved.quoteId)";Check $q 200
 EnvValue 'QuoteLifecycleVerification__Enabled' 'true';EnvValue 'QuoteLifecycleVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteLifecycleVerification__UtcNow' (([DateTime]::Parse($q.body.validUntil)).AddDays(2).ToString('yyyy-MM-ddTHH:mm:ssZ'));Stop-DemoHost;StartHost;LoginAll
 $r=Api $emails[0] POST "/quotes/$($saved.quoteId)/expire" @{reason='SYNTHETIC Development lifecycle time simulation for expired review guard'} "b64-expire-$run" $q.body.resourceVersion;Check $r 200
 Assert-Demo ($r.body.result.quote.status -ceq 'EXPIRED') 'B64_EXPIRED' 'Explicit owner command persisted expiry';Denied 'expired-quote' 'QUOTE_STATE_INVALID'
 EnvValue 'QuoteLifecycleVerification__Enabled' 'false';Stop-DemoHost;StartHost;LoginAll
 $role=Api $emails[1] POST '/access/roles' @{name="B64 isolated archive guard $run";capabilities=@('quotes.read','quotes.delete');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@()} "b64-delete-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[0];$member=$profile.membershipId
 $originalRoles=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member}).RoleId)
 $version=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$member/access" @{roleIds=@($originalRoles)+@($role.body.aggregateId);teamIds=@()} "b64-delete-assign-$run" $version) 200
 try{$q=Api $emails[0] GET "/quotes/$($saved.quoteId)";Check $q 200;Check (Api $emails[0] POST "/quotes/$($saved.quoteId)/archive" @{reason='SYNTHETIC archive of expired guard quote after owner retention verification'} "b64-archive-$run" $q.body.resourceVersion) 200;Denied 'archived-quote' 'QUOTE_STATE_INVALID'}
 finally{$version=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member}).Rows[0].Version;Check (Api $emails[1] POST "/access/members/$member/access" @{roleIds=$originalRoles;teamIds=@()} "b64-delete-restore-$run" $version) 200}
 # Fresh READY case for final browser capture; no counter reset or old fixture deletion.
 if(!$SkipBrowserFixture){$browser=NewManualSourceFixture $template 'review-browser-final' -Ready;$browser=AddConsent $browser;$browser|ConvertTo-Json -Depth 15|Set-Content (Join-Path $script:stateDirectory 'browser-fixture-result.json')}
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 80|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.4 authoritative guards $script:stateDirectory"
}
