#requires -Version 7.0
param([Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='security'
. (Join-Path $PSScriptRoot 'scanner/QuoteManualSourceVerificationRuntime.ps1')
function ParallelSource([string]$Method,[string]$Path,$Body,[long]$Version,[string[]]$Actors,[string]$Label){
 $client=[Net.Http.HttpClient]::new();$pending=@();try{
  for($i=0;$i -lt $Actors.Count;$i++){$email=$Actors[$i];$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),"http://127.0.0.1:5098$Path")
   foreach($h in @{Authorization="Bearer $($script:tokens[$email])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="$Label-$i-$run";'X-Correlation-Id'="$Label-$i-$run";'Idempotency-Key'="$Label-$i-$run";'If-Match'=('"'+$Version+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
   $request.Content=[Net.Http.StringContent]::new(($Body|ConvertTo-Json -Depth 30 -Compress),[Text.Encoding]::UTF8,'application/json');$pending+=@{request=$request;task=$client.SendAsync($request)}
  }
  $responses=@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$r=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{scenario=$Label;response=$r});$r}finally{$response.Dispose()}})
  Assert-Demo ((@($responses.status|Sort-Object)-join ',') -ceq '200,412') 'B63B_CONCURRENCY' 'Exactly one commit and one stale response required';return ,$responses
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
function FreshConfirmation([string]$Label){
 $current=Api $emails[0] GET "$base/$($saved.caseId)";Check $current 200
 $bytes=[byte[]]([IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf'))+[Text.Encoding]::ASCII.GetBytes("`n% SYNTHETIC $Label $run`n"))
 $u=Upload $saved.quoteId $saved.caseId $bytes "b63b-$Label-$run" $current.body.resourceVersion -Name "synthetic-$Label.pdf";Check $u 201;$proof=$u.body.result.proof
 $null=WaitScan "$base/$($saved.caseId)/proofs/$($proof.proofId)" 'CLEAN' 180;return $proof
}
try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'DEVELOPMENT_SIMULATION_PASS' -and $saved.workspaceKey.StartsWith('unicore-manual-source-')) 'B63B_FIXTURE' 'Isolated simulation fixture required'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b63b-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-adjusted-v1'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1';QuoteIdentityPolicies='b63b-manual-simulation-v1'}.GetEnumerator()){EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value}
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ManualDevelopmentSimulation" 'true';StartHost;LoginAll
 $saved=NewManualSourceFixture $saved 'security' -Ready
 $cases|ConvertTo-Json -Depth 70|Set-Content (Join-Path $script:stateDirectory 'fixture-cases.json')
 $saved|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'fixture-result.json')
 $base="/quotes/$($saved.quoteId)/acceptance-cases";$path="$base/$($saved.caseId)/identity-authority";$sources="/quotes/$($saved.quoteId)/acceptance-evidence-sources"
 $r=Api $emails[0] GET $path;Check $r 200;$view=$r.body
 $sourcePage=Api $emails[1] GET $sources;Check $sourcePage 200;$main=$sourcePage.body.items|Where-Object sourceId -ceq $saved.sourceId
 $e=$main.evidence;$request=@{name='SYNTHETIC concurrency source';sourceType=$main.sourceType;issuerReference=$main.issuerReference;verificationMethod=$main.verificationMethod;provenanceReference=$main.provenanceReference;requirements=$main.requirements;reason='SYNTHETIC new source';evidence=@{caseId=$e.caseId;issuerOrganizationId=$e.issuerOrganizationId;issuerProofIds=@($e.issuerProofs.proofId);independentChannelProofIds=@($e.independentChannelProofs.proofId);channelOriginKind=$e.channelOriginKind;channelOriginDescription=$e.channelOriginDescription;independentChannel=$e.independentChannel;effectiveFrom=$e.effectiveFrom;effectiveUntil=$e.effectiveUntil}}
 $bad=$request.Clone();$bad.provenanceReference='';Check (Api $emails[0] POST $sources $bad "b63b-no-provenance-$run" 0) 422
 $bad=$request.Clone();$bad.evidence=$request.evidence.Clone();$bad.evidence.independentChannelProofIds=@($saved.proofs.issuer.proofId);Check (Api $emails[0] POST $sources $bad "b63b-no-independent-proof-$run" 0) 409
 $bad=$request.Clone();$bad.status='APPROVED';Check (Api $emails[0] POST $sources $bad "b63b-forged-approved-$run" 0) 422
 $created=Api $emails[0] POST $sources $request "b63b-concurrent-source-$run" 0;Check $created 200;$secondary=$created.body.result
 $race=ParallelSource POST "$sources/$($secondary.sourceId)/approve" @{reason='SYNTHETIC independent concurrent approval'} 1 @($emails[1],$emails[2]) 'b63b-source-race';$secondary=($race|Where-Object status -eq 200).body.result
 $request.name='SYNTHETIC revised-source separation test'
 $created=Api $emails[0] POST $sources $request "b63b-editor-source-$run" 0;Check $created 200;$edited=$created.body.result
 $request.reason='SYNTHETIC source evidence revised by another actor';$r=Api $emails[1] PUT "$sources/$($edited.sourceId)" $request "b63b-editor-revision-$run" 1;Check $r 200;$edited=$r.body.result
 Check (Api $emails[1] POST "$sources/$($edited.sourceId)/approve" @{reason='Own revision cannot be approved'} "b63b-editor-self-$run" 2) 403
 $newProof=FreshConfirmation 'concurrent-observation'
 $manual=@{reportedOutcome='CONFIRMED';observedAt=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');confirmationProofIds=@($newProof.proofId);independentChannelUsed=$main.evidence.independentChannel;confirmationSummary='SYNTHETIC new independently supported observation';confirmedScope=$view.binding.authorityClaim}
 $checkBody=@{subjectId=$view.binding.subjectId;manualCheck=$manual}
 $race=ParallelSource POST "$path/source-checks" $checkBody $view.resourceVersion @($emails[2],$emails[2]) 'b63b-check-race';$view=($race|Where-Object status -eq 200).body.result
 $foreign=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=('unicore-b6-isolation-'+$securityKey.Split('-')[-1])}).Rows[0].WorkspaceId
 $own=$script:workspace;$script:workspace=$foreign;Check (Api $emails[0] GET $path) 403;$script:workspace=$own
 $role=Api $emails[1] POST '/access/roles' @{name="B63B hidden total $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.recordAcceptanceSourceCheck','quotes.assessAcceptanceIdentityAuthority','quotes.manageAcceptanceEvidenceSources','contacts.read','organizations.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='grandTotal';access='HIDDEN'})} "b63b-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 $originalRoles=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).RoleId)
 $rv=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b63b-restrict-$run" $rv) 200
 Check (Api $emails[2] GET $path) 403;Check (Api $emails[2] POST "$path/source-checks" $checkBody "b63b-restrict-check-$run" $view.resourceVersion) 403
 $rv=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=$originalRoles;teamIds=@()} "b63b-restore-$run" $rv) 200
 $binding=@{contactId=$view.binding.party.contactId;relationshipId=$view.binding.party.relationshipId;identityProofIds=@($view.binding.identityProofs.proofId);authorityProofIds=@($view.binding.authorityProofs.proofId);authorityClaim=$view.binding.authorityClaim;expectedSubjectId=$view.binding.subjectId;sourceId=$secondary.sourceId;sourceVersion=$secondary.resourceVersion;reason='SYNTHETIC revocation test binding'}
 $r=Api $emails[0] PUT "$path/binding" $binding "b63b-secondary-bind-$run" $view.resourceVersion;Check $r 200;$view=$r.body.result
 Check (Api $emails[1] POST "$sources/$($secondary.sourceId)/revoke" @{reason='SYNTHETIC source revocation test'} "b63b-revoke-$run" $secondary.resourceVersion) 200
 $r=Api $emails[2] GET $path;Check $r 200;Assert-Demo (!$r.body.assessment.readyForIndependentReview -and $r.body.assessment.reasons -contains 'SOURCE_REVOKED') 'B63B_REVOKED' 'Revoked source cannot confer readiness'
 $firstCases=Get-Content -LiteralPath (Join-Path $script:stateDirectory 'fixture-cases.json') -Raw|ConvertFrom-Json -AsHashtable
 $oldCheck=$firstCases|Where-Object {$_.path -like '*/source-checks' -and $_.response.status -eq 200 -and $_.response.body.outcome -eq 'COMMITTED'}|Select-Object -First 1
 $r=Api $emails[2] POST "$path/source-checks" $oldCheck.body $oldCheck.key $oldCheck.version;Check $r 200;Assert-Demo (!$r.body.result.assessment.readyForIndependentReview -and $r.body.outcome -ceq 'REPLAYED') 'B63B_REPLAY_CURRENT_FACTS' 'Historical receipt cannot replay stale READY'
 $binding.sourceId=$main.sourceId;$binding.sourceVersion=$main.resourceVersion;$binding.reason='SYNTHETIC restore current approved source'
 $r=Api $emails[0] PUT "$path/binding" $binding "b63b-main-bind-$run" $view.resourceVersion;Check $r 200;$view=$r.body.result
 $rollbackProof=FreshConfirmation 'rollback-observation';$manual.confirmationProofIds=@($rollbackProof.proofId);$manual.observedAt=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
 $before=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.IdentityAuthorityVersions WHERE WorkspaceId=@ws) IdentityVersions,(SELECT COUNT(*) FROM quotes.AcceptanceEvidenceSourceVersions WHERE WorkspaceId=@ws) SourceVersions,(SELECT COUNT(*) FROM quotes.CommandRecords WHERE WorkspaceId=@ws) Commands,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws) Outbox' @{'@ws'=$script:workspace}
 Stop-DemoHost;EnvValue 'QuoteIdentityVerification__Enabled' 'true';EnvValue 'QuoteIdentityVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteIdentityVerification__FailAfterFlush' 'true';StartHost;LoginAll
 Check (Api $emails[2] POST "$path/source-checks" $checkBody "b63b-rollback-check-$run" $view.resourceVersion) 500
 Stop-DemoHost;EnvValue 'QuoteIdentityVerification__Enabled' 'false';StartHost;LoginAll
 $after=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.IdentityAuthorityVersions WHERE WorkspaceId=@ws) IdentityVersions,(SELECT COUNT(*) FROM quotes.AcceptanceEvidenceSourceVersions WHERE WorkspaceId=@ws) SourceVersions,(SELECT COUNT(*) FROM quotes.CommandRecords WHERE WorkspaceId=@ws) Commands,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws) Outbox' @{'@ws'=$script:workspace}
 Assert-Demo (($before|ConvertTo-Json -Compress) -ceq ($after|ConvertTo-Json -Compress)) 'B63B_ROLLBACK' 'No partial ledger/receipt/outbox after flush failure';@{before=$before;after=$after}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'rollback-sql.json')
 $r=Api $emails[2] POST "$path/source-checks" $checkBody "b63b-rollback-check-$run" $view.resourceVersion;Check $r 200;$view=$r.body.result
 Stop-DemoHost;EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ManualDevelopmentSimulation" 'false';StartHost;LoginAll
 $r=Api $emails[2] GET $path;Check $r 200;Assert-Demo (!$r.body.assessment.readyForIndependentReview) 'B63B_MODE_GUARD' 'Workspace without simulation cannot inherit simulated readiness'
 Stop-DemoHost;EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ManualDevelopmentSimulation" 'true';EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Version" 'b63b-policy-change';StartHost;LoginAll
 $r=Api $emails[2] GET $path;Check $r 200;Assert-Demo (!$r.body.assessment.readyForIndependentReview) 'B63B_POLICY_GUARD' 'Changed documentary policy invalidates prior source/check bindings'
 Stop-DemoHost;EnvValue "QuoteVerifiedAcceptancePolicies__Workspaces__$($script:workspace)__Version" 'b6-approved-documentary-v1';StartHost;LoginAll
 $r=Api $emails[2] GET $path;Check $r 200;Assert-Demo ($r.body.assessment.readyForIndependentReview) 'B63B_RESTORED_POLICY' 'Original policy and fresh actual simulation facts restore readiness'
 $browser=NewManualSourceFixture $saved 'browser'
 $browser|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'browser-fixture-result.json')
 $browserSource=@{sourceId=$browser.sourceId};$browserProof=$browser.proofs.confirmation
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 if($originalRoles -and $member -and $script:hostProcess -and !$script:hostProcess.HasExited){$assigned=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).RoleId);if((@($assigned|Sort-Object)-join ',') -cne (@($originalRoles|Sort-Object)-join ',')){$rv=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version;Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=$originalRoles;teamIds=@()} "b63b-finally-restore-$run" $rv) 200}}
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 70|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$browser.quoteId;caseId=$browser.caseId;documentId=$browser.documentId;browserSourceId=$browserSource.sourceId;browserConfirmationProofId=$browserProof.proofId;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3B security/concurrency/rollback $script:stateDirectory"
}
