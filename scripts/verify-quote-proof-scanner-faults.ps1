#requires -Version 7.0
[CmdletBinding()]
param([switch]$Recover,[Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='fault'
. (Join-Path $PSScriptRoot 'scanner/QuoteProofScannerVerificationRuntime.ps1')

try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.workspaceKey.StartsWith('unicore-proof-scan-') -and $saved.status -in @('PASS','OFFLINE_VERIFIED')) 'B62_FIXTURE' 'Only a verified B6.2 synthetic fixture is admitted'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-recovered-v4'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 StartHost;LoginAll
 $quote=@{id=$saved.quoteId};$case=@{caseId=$saved.caseId};$document=@{documentId=$saved.documentId};$base="/quotes/$($quote.id)/acceptance-cases"
 $bytes=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf'))
 if($Recover){
  $offlinePath="$base/$($case.caseId)/proofs/$($saved.offlineProofId)"
  $old=Api $emails[0] GET ($offlinePath+'/security');Check $old 200
  Assert-Demo ($old.body.scanStatus -ceq 'NEEDS_INTERVENTION') 'B62_EXHAUSTED_RECOVERY' 'Runtime recovery cannot reset exhausted counters'
  $new=UploadFresh 'offline-recovery';$newPath="$base/$($case.caseId)/proofs/$($new.proofId)";$null=WaitScan $newPath 'CLEAN';Check (ProofContent $newPath) 200
  $status='PASS'
 }else{
  $source=Api $emails[0] GET "/quotes/$($saved.quoteId)";Check $source 200;$script:buyer=$source.body.buyerRef.id
  $draft=Assessed (DraftQuote 'scanner-faults') 'scanner-faults';$issued=Issue $draft 'scanner-faults';Check $issued 201;$document=$issued.body.result.document
  $sent=Api $emails[0] POST "/quotes/$($draft.id)/send" @{deliveryId="b62_faults_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic owned scanner fault test'} "b62-faults-send-$run" $issued.body.version;Check $sent 200;$quote=$sent.body.result.quote
  $base="/quotes/$($quote.id)/acceptance-cases";$binding=@{documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint}
  $created=Api $emails[0] POST $base $binding "b62-faults-case-$run" $quote.resourceVersion;Check $created 201;$case=$created.body.result.case
  $owned=Get-Content (Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/runtime/owned-clamd.json') -Raw|ConvertFrom-Json -AsHashtable -DateKind String
  Assert-Demo ($owned.scope -ceq 'B62_TASK_OWNED_DEVELOPMENT') 'B62_SCANNER_OWNERSHIP' 'Only task-owned scanner may receive a fault'
  $clamd=Get-Process -Id $owned.pid -ErrorAction Stop
  Assert-Demo ([IO.Path]::GetFullPath($clamd.Path) -ieq [IO.Path]::GetFullPath($owned.executable) -and $clamd.StartTime.ToUniversalTime() -eq [DateTimeOffset]::Parse($owned.startedAt).UtcDateTime) 'B62_SCANNER_OWNERSHIP' 'Exact scanner PID/path/start time must match'
  Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class B62OwnedScannerFault{[DllImport("ntdll.dll")]public static extern int NtSuspendProcess(IntPtr handle);[DllImport("ntdll.dll")]public static extern int NtResumeProcess(IntPtr handle);}'
  Assert-Demo ([B62OwnedScannerFault]::NtSuspendProcess($clamd.Handle) -eq 0) 'B62_SCANNER_FAULT' 'Owned scanner suspension must succeed';$suspended=$true
  $timeoutProof=UploadFresh 'real-timeout';$timeoutPath="$base/$($case.caseId)/proofs/$($timeoutProof.proofId)"
  $timedOut=WaitScan $timeoutPath 'SCAN_FAILED|NEEDS_INTERVENTION' 60
  $history=Api $emails[1] GET ($timeoutPath+'/scans');Check $history 200
  Assert-Demo (@($history.body.items|Where-Object reasonCode -cin @('SCANNER_DEADLINE_EXCEEDED','SCANNER_RUNTIME_OR_SIGNATURE_UNAVAILABLE')).Count -ge 1) 'B62_REAL_TIMEOUT' 'The real owned scanner health/scan deadline must fail closed, never return CLEAN'
  Check (ProofContent $timeoutPath) 409
  Assert-Demo ([B62OwnedScannerFault]::NtResumeProcess($clamd.Handle) -eq 0) 'B62_SCANNER_RESUME' 'Owned scanner must resume';$suspended=$false
  if($timedOut.scanStatus -ceq 'SCAN_FAILED'){$null=WaitScan $timeoutPath 'CLEAN|NEEDS_INTERVENTION' 70}
  # Recheck exact identity before terminating only this task's portable scanner.
  $clamd=Get-Process -Id $owned.pid -ErrorAction Stop
  Assert-Demo ([IO.Path]::GetFullPath($clamd.Path) -ieq [IO.Path]::GetFullPath($owned.executable) -and $clamd.StartTime.ToUniversalTime() -eq [DateTimeOffset]::Parse($owned.startedAt).UtcDateTime) 'B62_SCANNER_OWNERSHIP' 'Scanner identity changed; do not stop it'
  Stop-Process -Id $clamd.Id -ErrorAction Stop;$clamd.WaitForExit()
  $offlineProof=UploadFresh 'real-offline';$offlinePath="$base/$($case.caseId)/proofs/$($offlineProof.proofId)"
  $null=WaitScan $offlinePath 'NEEDS_INTERVENTION' 45
  Check (Api $emails[1] GET ($offlinePath+'/scans')) 200;Check (ProofContent $offlinePath) 409
  $status='OFFLINE_VERIFIED'
 }
}finally{
 if($suspended -and $clamd -and !$clamd.HasExited){[void][B62OwnedScannerFault]::NtResumeProcess($clamd.Handle)}
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;documentId=$document.documentId;caseId=$case.caseId;timeoutProofId=$timeoutProof.proofId;offlineProofId=$offlineProof.proofId;recoveryProofId=$new.proofId;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.2 real scanner fault/recovery $script:stateDirectory"
}
