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
 $q=$saved.quoteId;$c=$saved.caseId;$path="/quotes/$q/acceptance-cases/$c/identity-authority";$proofPath="/quotes/$q/acceptance-cases/$c/proofs/$($saved.proofId)"
 $v=Api $emails[0] GET $path;Check $v 200;$view=$v.body
 $contactId=$view.binding.party.contactId
 $input=@{contactId=$contactId;expectedSubjectId=$view.binding.subjectId;identityProofIds=@($saved.proofId);authorityProofIds=@($saved.proofId);reason='Synthetic effective CLEAN guards'}
 $caseRead=Api $emails[0] GET "/quotes/$q/acceptance-cases/$c";Check $caseRead 200
 $http=[Net.Http.HttpClient]::new();try{$eicar=$http.GetByteArrayAsync('https://secure.eicar.org/eicar.com.txt').GetAwaiter().GetResult()}finally{$http.Dispose()}
 Assert-Demo ($eicar.Length -eq 68 -and [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($eicar)) -ceq '275A021BBFB6489E54D471899F7DB9D1663FC695EC2FE2A2C4538AABF651FD0F') 'B63_DETECTION_FIXTURE' 'Official harmless detection test required'
 $uploaded=Upload $q $c ([byte[]]((DetectionPdf $eicar)+[Text.Encoding]::ASCII.GetBytes("`n% SYNTHETIC $run`n"))) "b63-unsafe-$run" $caseRead.body.resourceVersion;Check $uploaded 201;$unsafe=$uploaded.body.result.proof
 $unsafePath="/quotes/$q/acceptance-cases/$c/proofs/$($unsafe.proofId)";$null=WaitScan $unsafePath 'UNSAFE'
 $input.identityProofIds=@($unsafe.proofId);Check (Api $emails[0] PUT "$path/binding" $input "b63-unsafe-bind-$run" $view.resourceVersion) 409
 $input.identityProofIds=@($saved.proofId)
 Stop-DemoHost;ScanPolicy 'b63-rescan-guard-v2';EnvValue 'QuoteProofScanVerification__Enabled' 'true';EnvValue 'QuoteProofScanVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteProofScanVerification__PauseAfterClaimMilliseconds' '12000';StartHost;LoginAll
 Check (Api $emails[0] PUT "$path/binding" $input "b63-stale-clean-$run" $view.resourceVersion) 409
 Stop-DemoHost;ScanPolicy 'b62-adjusted-v1';EnvValue 'QuoteProofScanVerification__Enabled' 'false';StartHost;LoginAll
 $null=WaitScan $proofPath 'CLEAN' 180;Check (ProofContent $proofPath) 200;Check (ProofContent $proofPath $emails[1]) 403
 $latency=@();1..10|ForEach-Object{$timer=[Diagnostics.Stopwatch]::StartNew();$r=Api $emails[0] GET $path;$timer.Stop();Check $r 200;$latency+=$timer.Elapsed.TotalMilliseconds}
 @{samples=10;milliseconds=$latency;minimum=($latency|Measure-Object -Minimum).Minimum;maximum=($latency|Measure-Object -Maximum).Maximum;mean=($latency|Measure-Object -Average).Average}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'read-latency.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;directory=$script:stateDirectory;unsafeProofId=$unsafe.proofId}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3 effective CLEAN guards $script:stateDirectory"
}
