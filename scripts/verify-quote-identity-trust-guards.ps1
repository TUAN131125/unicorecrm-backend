#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='read'
. (Join-Path $PSScriptRoot 'scanner/QuoteIdentityVerificationRuntime.ps1')
try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'PARTIAL_FUNCTIONAL_PASS' -and $saved.workspaceKey.StartsWith('unicore-identity-authority-')) 'B62_FIXTURE' 'Only an existing B6.2 synthetic fixture is admitted'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-adjusted-v1'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__Version" 'b63-approved-v1'
 StartHost;LoginAll
 $q=$saved.quoteId;$c=$saved.caseId;$path="/quotes/$q/acceptance-cases/$c/identity-authority";$sourcesPath="/quotes/$q/acceptance-evidence-sources"
 $current=Api $emails[0] GET $path;Check $current 200;$view=$current.body;$binding=$view.binding
 Check (Api $emails[1] POST $sourcesPath @{name='SYNTHETIC forged';sourceType='INTERNAL_PROCEDURE';issuerReference='test';verificationMethod='test';provenanceReference='test';requirements='test';status='APPROVED'} "b63-source-forged-$run" 0) 422
 Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$binding.subjectId;actorId='forged_actor';checkedAt='2026-10-26T01:00:00Z';outcome='SOURCE_CHECKED'} "b63-check-forged-$run" $view.resourceVersion) 422
 Check (Api $emails[0] PUT "$path/binding" @{contactId=$binding.party.contactId;identityProofIds=@();authorityProofIds=@();expectedSubjectId=$binding.subjectId;reason='Synthetic contact-only insufficiency test';authorityClaim=@{email='staff-claim@example.test'}} "b63-email-claim-$run" $view.resourceVersion) 422
 $input=@{contactId=$binding.party.contactId;identityProofIds=@();authorityProofIds=@();expectedSubjectId=$binding.subjectId;reason='Synthetic contact-only insufficiency test'}
 $r=Api $emails[0] PUT "$path/binding" $input "b63-contact-only-$run" $view.resourceVersion;Check $r 200
 Assert-Demo (!$r.body.result.assessment.readyForIndependentReview -and $r.body.result.assessment.reasons -contains 'IDENTITY_PROOF_REQUIRED' -and $r.body.result.assessment.reasons -contains 'AUTHORITY_PROOF_REQUIRED') 'B63_CONTACT_NOT_AUTHORITY' 'A CRM contact is not authority evidence'
 Check (Api $emails[2] POST "$path/source-checks" @{subjectId=$binding.subjectId} "b63-no-proof-check-$run" $r.body.result.resourceVersion) 409
 $input.identityProofIds=@($binding.identityProofs.proofId);$input.authorityProofIds=@($binding.authorityProofs.proofId);$input.authorityClaim=$binding.authorityClaim;$input.sourceId=$binding.sourceId;$input.sourceVersion=$binding.sourceVersion;$input.reason='Restore full synthetic binding after negative test'
 Check (Api $emails[0] PUT "$path/binding" $input "b63-restore-binding-$run" $r.body.result.resourceVersion) 200
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3 forged trust and contact-only guards $script:stateDirectory"
}
