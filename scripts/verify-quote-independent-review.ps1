#requires -Version 7.0
param([Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='advanced'
. (Join-Path $PSScriptRoot 'scanner/QuoteIndependentReviewVerificationRuntime.ps1')
function Current { $r=Api $emails[1] GET $path;Check $r 200;return $r.body }
function Submit($Body,[string]$Key,[string]$Actor=$emails[1]){return Api $Actor POST "$path/decisions" $Body $Key $Body.identityVersion}
function Counts {return SQLRows "SELECT (SELECT COUNT(*) FROM quotes.IdentityAuthorityVersions WHERE WorkspaceId=@ws) Versions,(SELECT COUNT(*) FROM quotes.CommandRecords WHERE WorkspaceId=@ws) Commands,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws) Outbox" @{'@ws'=$script:workspace}}
function RoleCheck([int]$Actor,[string]$Label,[string]$Scope='WORKSPACE',[object[]]$Fields=@()){
 $caps=@('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.readAcceptanceProofContent','quotes.verifyAcceptanceEvidence','contacts.read','organizations.read','access.read','access.configure')
 $role=Api $emails[1] POST '/access/roles' @{name="B64 $Label $run";capabilities=$caps;dataScopes=@(@{resourceKey='quotes';scope=$Scope});fieldSecurity=$Fields} "b64-role-$Label-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[$Actor];$member=$profile.membershipId
 $old=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member}).RoleId)
 function Assign($Roles,[string]$Key){$v=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member}).Rows[0].Version;Check (Api $emails[1] POST "/access/members/$member/access" @{roleIds=@($Roles);teamIds=@()} $Key $v) 200}
 try{
  # Retain creator's existing owner/admin roles when proving no self-review override.
  $roles=if($Actor -eq 0){@($old)+@($role.body.aggregateId)}else{@($role.body.aggregateId)};Assign $roles "b64-$Label-assign-$run"
  $b=ReviewBody $script:lastView;$expected=if($Label -eq 'own'){404}else{403};Check (Submit $b "b64-$Label-deny-$run" $emails[$Actor]) $expected
  if($Label -in @('fls','own')){Check (Api $emails[$Actor] GET $path) $expected}
 }finally{Assign $old "b64-$Label-restore-$run"}
}
function Race($Body){
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($decision in @('REQUEST_MORE_EVIDENCE','REJECT_REVIEW')){
   $b=$Body.Clone();$b.decision=$decision;$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098$path/decisions")
   foreach($h in @{Authorization="Bearer $($script:tokens[$emails[1]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b64-race-$decision-$run";'X-Correlation-Id'="b64-race-$decision-$run";'Idempotency-Key'="b64-race-$decision-$run";'If-Match'=('"'+$Body.identityVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
   $request.Content=[Net.Http.StringContent]::new(($b|ConvertTo-Json -Depth 30),[Text.Encoding]::UTF8,'application/json');$pending+=@{request=$request;task=$client.SendAsync($request)}
  }
  $responses=@(foreach($entry in $pending){$r=$entry.task.GetAwaiter().GetResult();try{$v=@{status=[int]$r.StatusCode;body=($r.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{scenario='B64_CONCURRENT_CONFLICTING_REVIEW';response=$v});$v}finally{$r.Dispose()}})
  Assert-Demo ((@($responses.status|Sort-Object)-join ',') -ceq '200,412') 'B64_RACE' 'Exactly one authoritative decision and one stale failure required'
 }finally{foreach($p in $pending){$p.request.Dispose()};$client.Dispose()}
}
try{
 $saved=InitializeReview $FixtureResult
 $saved=AddConsent $saved
 $path="/quotes/$($saved.quoteId)/acceptance-cases/$($saved.caseId)/independent-review"
 $view=Current;Assert-Demo ($view.readyForIndependentReview -and !$view.actualSourceVerification) 'B64_READY' 'Complete isolated simulation case should be reviewable'
 $script:lastView=$view;$body=ReviewBody $view
 $bad=$body.Clone();$bad.reviewBindingFingerprint='0'*64;Check (Submit $bad "b64-wrong-facts-$run") 409 'QUOTE_INDEPENDENT_REVIEW_BLOCKED' 'REVIEW_FACTS_CHANGED'
 Check (Submit $body "b64-missing-permission-$run" $emails[2]) 403
 RoleCheck 0 'owner-self';RoleCheck 2 'source-checker';RoleCheck 1 'fls' 'WORKSPACE' @(@{resourceKey='quotes';fieldKey='grandTotal';access='HIDDEN'});RoleCheck 1 'own' 'OWN'
 $own=$script:workspace;$foreign=[string](Invoke-DemoSql "SELECT TOP(1) WorkspaceId FROM workspace.Workspaces WHERE [Key] LIKE 'unicore-b6-isolation-%' AND WorkspaceId<>@ws ORDER BY WorkspaceId" @{'@ws'=$own}).Rows[0].WorkspaceId
 Assert-Demo (!!$foreign) 'B64_ISOLATION' 'Nonmember Workspace required';$script:workspace=$foreign;Check (Api $emails[0] GET $path) 403;Check (Submit $body "b64-cross-$run") 403;$script:workspace=$own
 foreach($criterion in @('D1','D2','D3','D4','D5')){$bad=$body.Clone();$bad.findings=@($body.findings|Where-Object criterion -cne $criterion);Check (Submit $bad "b64-missing-$criterion-$run") 409}
 $bad=$body.Clone();$bad.consent=$null;Check (Submit $bad "b64-no-consent-$run") 409
 $bad=$body.Clone();$bad.consent=$body.consent.Clone();$bad.consent.proofIds=@($saved.proofs.issuer.proofId);Check (Submit $bad "b64-not-consent-$run") 409
 foreach($field in @('documentId','quoteRevision','pdfSha256')){$bad=$body.Clone();$bad[$field]=switch($field){'documentId'{'qdoc_wrong'}'quoteRevision'{99}'pdfSha256'{('0'*64)}};Check (Submit $bad "b64-wrong-$field-$run") 409}
 foreach($field in @('reviewerId','isVerified','actualSourceVerification','sourceChecked','commercialCommitment')){$bad=$body.Clone();$bad[$field]=$true;Check (Submit $bad "b64-forged-$field-$run") 422}
 $bad=$body.Clone();$bad.identityVersion=$view.resourceVersion-1;Check (Submit $bad "b64-stale-$run") 412
 foreach($decision in @('REQUEST_MORE_EVIDENCE','REJECT_REVIEW')){
  $view=Current;$b=ReviewBody $view $decision;$b.Remove('consent');$b.findings=@();$r=Submit $b "b64-$decision-$run";Check $r 200
  Assert-Demo ($r.body.result.lastDecision.decision -ceq $decision) 'B64_NEGATIVE_RECORDED' 'Negative decision persisted without positive checklist'
 }
 $view=Current;$body=ReviewBody $view;$approved=Submit $body "b64-approve-$run";Check $approved 200;$review=$approved.body.result.lastDecision
 Assert-Demo ($approved.body.result.status -ceq 'REVIEW_APPROVED_SIMULATION' -and $review.assuranceLevel -ceq 'DEVELOPMENT_SIMULATION' -and !$review.actualSourceVerification) 'B64_SIMULATION_APPROVED' 'Review is simulation only, never verified acceptance'
 $replay=Submit $body "b64-approve-$run";Check $replay 200;Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.result.lastDecision.reviewDecisionId -ceq $review.reviewDecisionId) 'B64_REPLAY' 'No duplicate review'
 $bad=$body.Clone();$bad.reason='Changed replay payload';Check (Submit $bad "b64-approve-$run") 409
 $h=Api $emails[1] GET "$path/history?limit=1";Check $h 200;Assert-Demo ($h.body.items.Count -eq 1 -and $h.body.pageInfo.hasNextPage) 'B64_PAGINATION' 'Bounded review history pages';Check (Api $emails[1] GET "$path/history?limit=51") 422
 Stop-DemoHost;StartHost;LoginAll;$view=Current;Assert-Demo ($view.status -ceq 'REVIEW_APPROVED_SIMULATION' -and $view.lastDecision.reviewDecisionId -ceq $review.reviewDecisionId) 'B64_RESTART' 'Committed review survives restart'
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ReviewPolicyVersion" 'b64-review-v2';Stop-DemoHost;StartHost;LoginAll;$view=Current;Assert-Demo ($view.status -ceq 'REVIEW_INVALIDATED') 'B64_POLICY_INVALIDATION' 'Policy changes invalidate effective review';Check (Submit $body "b64-approve-$run") 200
 $staleFacts=$body.Clone();$staleFacts.identityVersion=$view.resourceVersion;Check (Submit $staleFacts "b64-stale-policy-form-$run") 409 'QUOTE_INDEPENDENT_REVIEW_BLOCKED' 'REVIEW_FACTS_CHANGED'
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ReviewPolicyVersion" 'b64-review-v1';Stop-DemoHost;StartHost;LoginAll
 $view=Current;Race (ReviewBody $view);$view=Current
 $rollback=ReviewBody $view 'REQUEST_MORE_EVIDENCE';$before=Counts
 EnvValue 'QuoteIdentityVerification__Enabled' 'true';EnvValue 'QuoteIdentityVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteIdentityVerification__FailAfterFlush' 'true';Stop-DemoHost;StartHost;LoginAll;Check (Submit $rollback "b64-rollback-$run") 500
 $after=Counts;Assert-Demo (($before|ConvertTo-Json -Compress) -ceq ($after|ConvertTo-Json -Compress)) 'B64_ROLLBACK' 'Decision/receipt/audit/outbox must roll back together'
 @{before=$before;after=$after}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'rollback.json')
 EnvValue 'QuoteIdentityVerification__FailAfterFlush' 'false';Stop-DemoHost;StartHost;LoginAll;Check (Submit $rollback "b64-rollback-$run") 200
 # Keep a fresh independent browser fixture; existing quota stays five proofs per Case.
 $browser=NewManualSourceFixture $saved 'review-browser' -Ready;$browser=AddConsent $browser
 $browser|ConvertTo-Json -Depth 15|Set-Content (Join-Path $script:stateDirectory 'browser-fixture-result.json')
 $view=Current;$body=ReviewBody $view;$r=Submit $body "b64-final-approval-$run";Check $r 200;$historical=$r.body.result.lastDecision
 $r=Api $emails[1] POST "/quotes/$($saved.quoteId)/acceptance-evidence-sources/$($saved.sourceId)/revoke" @{reason='SYNTHETIC post-review revocation'} "b64-revoke-$run" 2;Check $r 200
 $view=Current;Assert-Demo ($view.status -ceq 'REVIEW_INVALIDATED' -and $view.lastDecision.reviewDecisionId -ceq $historical.reviewDecisionId) 'B64_REVOCATION' 'Historical review retained, current authority invalidated'
 $r=Submit $body "b64-final-approval-$run";Check $r 200;Assert-Demo ($r.body.result.status -ceq 'REVIEW_INVALIDATED') 'B64_STALE_REPLAY' 'Replay cannot return stale effective approval'
 $view=Current;$new=ReviewBody $view;Check (Submit $new "b64-revoked-deny-$run") 409
 $counts=SQLRows "SELECT (SELECT COUNT(*) FROM quotes.Quotes WHERE Status='ACCEPTED') Accepted,(SELECT COUNT(*) FROM deals.Deals WHERE WonAt IS NOT NULL) Won,(SELECT COUNT(*) FROM orders.Orders) Orders,(SELECT COUNT(*) FROM payments.PaymentPlans) Payments,(SELECT COUNT(*) FROM quotes.CustomerDecisionRecords WHERE VerificationStatus<>'UNVERIFIED') VerifiedDecisions"
 Assert-Demo ($counts[0].Values|Where-Object{$_ -ne 0}|Measure-Object).Count.Equals(0) 'B64_NO_PROMOTION' 'No VERIFIED/ACCEPTED/WON/Orders/Payments'
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 80|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$saved.quoteId;caseId=$saved.caseId;directory=$script:stateDirectory;browserFixture=(Join-Path $script:stateDirectory 'browser-fixture-result.json')}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.4 independent review $script:stateDirectory"
}
