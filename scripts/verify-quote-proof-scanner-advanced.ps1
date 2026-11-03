#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser,[Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='advanced'
. (Join-Path $PSScriptRoot 'scanner/QuoteProofScannerVerificationRuntime.ps1')

try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'PASS' -and $saved.workspaceKey.StartsWith('unicore-proof-scan-')) 'B62_FIXTURE' 'Only an existing successful B6.2 synthetic fixture is admitted'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 $identity=(Invoke-DemoSql 'SELECT [Key] FROM workspace.Workspaces WHERE WorkspaceId=@ws' @{'@ws'=$script:workspace}).Rows[0].Key
 Assert-Demo ($identity -ceq $securityKey) 'B62_FIXTURE' 'Fixture Workspace must match owner data'
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-recovered-v4'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 StartHost;LoginAll
 $quote=@{id=$saved.quoteId};$case=@{caseId=$saved.caseId};$document=@{documentId=$saved.documentId};$base="/quotes/$($quote.id)/acceptance-cases"
 $bytes=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf'))
 $recoveryPath="$base/$($case.caseId)/proofs/$($saved.recoveryProofId)"
 Check (ProofContent $recoveryPath) 200
 if($Browser){
  foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_RESTRICTED_EMAIL=$emails[1];UNICORECRM_TEST_RESTRICTED_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_WORKSPACE_KEY=$securityKey;UNICORECRM_TEST_LOW_QUOTE_ID=$quote.id;UNICORECRM_TEST_DOCUMENT_ID=$document.documentId;UNICORECRM_TEST_CASE_ID=$case.caseId;UNICORECRM_TEST_CLEAN_PROOF_ID=$saved.recoveryProofId;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
  [void](New-Item -ItemType Directory -Force (Join-Path $script:stateDirectory 'browser'))
  Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
  try{npx playwright test --config playwright.quote-scanner.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
  Get-ChildItem -LiteralPath $script:stateDirectory -Recurse -File -Filter 'error-context.md'|ForEach-Object{$text=[IO.File]::ReadAllText($_.FullName);[IO.File]::WriteAllText($_.FullName,[regex]::Replace($text,'B1-[A-Fa-f0-9]{64}!a9','[REDACTED_SYNTHETIC_CREDENTIAL]'))}
  Assert-Demo ($browserExit -eq 0) 'B62_BROWSER' 'Connected browser scanner acceptance failed'
 }
 Stop-DemoHost
 EnvValue 'QuoteProofScanVerification__Enabled' 'true';EnvValue 'QuoteProofScanVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteProofScanVerification__PauseAfterClaimMilliseconds' '120000'
 StartHost;LoginAll
 $restartProof=UploadFresh 'restart';$restartPath="$base/$($case.caseId)/proofs/$($restartProof.proofId)"
 $null=WaitScan $restartPath 'SCANNING';$claimed=ScanRows $restartProof.proofId
 Assert-Demo ($claimed.Attempts -eq 1 -and $claimed.Results -eq 0 -and $claimed.LifetimeAttempts -eq 1) 'B62_CLAIM' 'Exactly one durable claim, no result before work'
 Stop-DemoHost;EnvValue 'QuoteProofScanVerification__PauseAfterClaimMilliseconds' '0';StartHost;LoginAll
 Check (ProofContent $restartPath) 409
 $null=WaitScan $restartPath 'CLEAN' 140
 $restarted=ScanRows $restartProof.proofId
 Assert-Demo ($restarted.Attempts -eq 2 -and $restarted.Results -eq 2) 'B62_RESTART_RECOVERY' 'Expired lease keeps failed result; new lease performs real scan'
 $h=Api $emails[1] GET ($restartPath+'/scans');Check $h 200
 Assert-Demo (@($h.body.items|Where-Object reasonCode -ceq 'SCAN_LEASE_EXPIRED').Count -eq 1) 'B62_EXPIRED_HISTORY' 'Expired scan remains visible'
 Stop-DemoHost;EnvValue 'QuoteProofScanVerification__FailAfterResultFlush' 'true';StartHost;LoginAll
 $rollbackProof=UploadFresh 'rollback';$rollbackPath="$base/$($case.caseId)/proofs/$($rollbackProof.proofId)"
 $null=WaitScan $rollbackPath 'SCANNING';Start-Sleep -Seconds 7
 $rolled=ScanRows $rollbackProof.proofId
 Assert-Demo ($rolled.Status -ceq 'SCANNING' -and $rolled.Attempts -eq 1 -and $rolled.Results -eq 0) 'B62_ROLLBACK' 'Result and job completion rolled back together'
 Check (ProofContent $rollbackPath) 409
 Stop-DemoHost;EnvValue 'QuoteProofScanVerification__FailAfterResultFlush' 'false';StartHost;LoginAll
 $null=WaitScan $rollbackPath 'CLEAN' 140
 Check (ProofContent $rollbackPath) 200
 $rolledRecovered=ScanRows $rollbackProof.proofId
 Assert-Demo ($rolledRecovered.Attempts -eq 2 -and $rolledRecovered.Results -eq 2) 'B62_ROLLBACK_RECOVERY' 'Only committed results can release bytes'
 @{claim=$claimed;restart=$restarted;rollback=$rolled;rollbackRecovered=$rolledRecovered}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'recovery-sql.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;restartProofId=$restartProof.proofId;rollbackProofId=$rollbackProof.proofId;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.2 advanced API/SQL/browser $script:stateDirectory"
}
