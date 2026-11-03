#requires -Version 7.0
param([Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='advanced'
. (Join-Path $PSScriptRoot 'scanner/QuoteManualSourceVerificationRuntime.ps1')
try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'DEVELOPMENT_SIMULATION_PASS' -and $saved.workspaceKey.StartsWith('unicore-manual-source-')) 'B63B_FIXTURE' 'Isolated simulation fixture required'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b63b-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-adjusted-v1'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1';QuoteIdentityPolicies='b63b-manual-simulation-v1'}.GetEnumerator()){EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value}
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ManualDevelopmentSimulation" 'true';StartHost;LoginAll
 $base="/quotes/$($saved.quoteId)/acceptance-cases";$path="$base/$($saved.caseId)/identity-authority"
 $r=Api $emails[2] GET $path;Check $r 200;$view=$r.body
 $sources=Api $emails[2] GET "/quotes/$($saved.quoteId)/acceptance-evidence-sources";Check $sources 200;$source=$sources.body.items|Where-Object sourceId -ceq $view.binding.sourceId
 $manual=@{reportedOutcome='CONFIRMED';observedAt=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ');confirmationProofIds=@($view.lastSourceCheck.manualCheck.confirmationProofs.proofId);independentChannelUsed=$source.evidence.independentChannel;confirmationSummary='SYNTHETIC negative guard verification';confirmedScope=$view.binding.authorityClaim}
 $wrong=$manual.Clone();$wrong.confirmedScope=$manual.confirmedScope.Clone();$wrong.confirmedScope.amountLimit='999';Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$view.binding.subjectId;manualCheck=$wrong} "b63b-scope-mismatch-$run" $view.resourceVersion) 409
 $wrong=$manual.Clone();$wrong.independentChannelUsed='';Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$view.binding.subjectId;manualCheck=$wrong} "b63b-no-channel-$run" $view.resourceVersion) 409
 $wrong=$manual.Clone();$wrong.confirmationProofIds=@($view.binding.authorityProofs.proofId);Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$view.binding.subjectId;manualCheck=$wrong} "b63b-not-independent-$run" $view.resourceVersion) 409
 Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$view.binding.subjectId;manualCheck=$manual;isVerified=$true} "b63b-forged-trust-$run" $view.resourceVersion) 422
 $binding=@{contactId=$view.binding.party.contactId;relationshipId='contact_org_relationship_missing';identityProofIds=@($view.binding.identityProofs.proofId);authorityProofIds=@($view.binding.authorityProofs.proofId);authorityClaim=$view.binding.authorityClaim;expectedSubjectId=$view.binding.subjectId;sourceId=$view.binding.sourceId;sourceVersion=$view.binding.sourceVersion;reason='Synthetic wrong affiliation denial'}
 Check (Api $emails[0] PUT "$path/binding" $binding "b63b-affiliation-deny-$run" $view.resourceVersion) 403
 $case=Api $emails[0] GET "$base/$($saved.caseId)";Check $case 200
 $invalid=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/active.pdf'));$u=Upload $saved.quoteId $saved.caseId $invalid "b63b-invalid-pdf-$run" $case.body.resourceVersion -Name 'synthetic-unsupported.pdf';Check $u 201;$badProof=$u.body.result.proof
 $null=WaitScan "$base/$($saved.caseId)/proofs/$($badProof.proofId)" 'INVALID_PDF' 180
 $wrong=$manual.Clone();$wrong.confirmationProofIds=@($badProof.proofId);Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$view.binding.subjectId;manualCheck=$wrong} "b63b-unclean-denied-$run" $view.resourceVersion) 409
 Check (ProofContent "$base/$($saved.caseId)/proofs/$($badProof.proofId)" $emails[2]) 409
 $r=Api $emails[2] GET $path;Check $r 200;Assert-Demo ($r.body.assessment.readyForIndependentReview -and !$r.body.assessment.actualSourceVerification) 'B63B_NEGATIVE_NO_MUTATION' 'Rejected attempts do not replace the valid bound simulation facts'
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 70|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3B negative evidence guards $script:stateDirectory"
}
