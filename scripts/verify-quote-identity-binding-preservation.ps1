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
 $case=Api $emails[0] GET "/quotes/$q/acceptance-cases/$c";Check $case 200
 $bytes=[byte[]]([IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf'))+[Text.Encoding]::ASCII.GetBytes("`n% SYNTHETIC MULTIPROOF $run`n"))
 $new=Upload $q $c $bytes "b63-multiproof-$run" $case.body.resourceVersion;Check $new 201;$newProof=$new.body.result.proof
 $null=WaitScan "/quotes/$q/acceptance-cases/$c/proofs/$($newProof.proofId)" 'CLEAN' 180
 1..21|ForEach-Object{$input=@{name="SYNTHETIC pagination descriptor $_ $run";sourceType='INTERNAL_PROCEDURE';issuerReference='SYNTHETIC NOT ACTUAL AUTHORITY';verificationMethod='UNADMITTED';provenanceReference='Synthetic pagination regression only';requirements='No actual source. Never approve.'};Check (Api $emails[1] POST $sourcesPath $input "b63-page-source-$_-$run" 0) 200}
 $all=Api $emails[1] GET "$sourcesPath`?limit=50";Check $all 200;$source=$all.body.items[20]
 $current=Api $emails[0] GET $path;Check $current 200;$view=$current.body
 $ids=@($saved.proofId,$newProof.proofId)
 $input=@{contactId=$view.binding.party.contactId;expectedSubjectId=$view.binding.subjectId;identityProofIds=$ids;authorityProofIds=$ids;sourceId=$source.sourceId;sourceVersion=$source.resourceVersion;reason='SYNTHETIC multi-proof and out-of-page source preservation'}
 $r=Api $emails[0] PUT "$path/binding" $input "b63-multi-binding-$run" $view.resourceVersion;Check $r 200
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;sourceId=$source.sourceId;proofIds=$ids;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3 pagination and multiproof fixture $script:stateDirectory"
}
