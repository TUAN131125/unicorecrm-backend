#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser,[switch]$UploadBrowser,[Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='read'
. (Join-Path $PSScriptRoot 'scanner/QuoteProofScannerVerificationRuntime.ps1')
try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'PASS' -and $saved.workspaceKey.StartsWith('unicore-proof-scan-')) 'B62_FIXTURE' 'Only an existing B6.2 synthetic fixture is admitted'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-recovered-v4'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 StartHost;LoginAll
 $quote=@{id=$saved.quoteId};$document=@{documentId=$saved.documentId};$proofId=if($saved.recoveryProofId){$saved.recoveryProofId}else{$saved.cleanProofId}
 $proofPath="/quotes/$($quote.id)/acceptance-cases/$($saved.caseId)/proofs/$proofId"
 $null=WaitScan $proofPath 'CLEAN';Check (ProofContent $proofPath) 200;Check (ProofContent $proofPath.ToUpperInvariant()) 200
 $history=Api $emails[1] GET ($proofPath+'/scans');Check $history 200
 $wire=HistoryWire $proofPath;Check $wire 200
 Assert-Demo (@($wire.body.items|Where-Object{$_.startedAt -notmatch 'Z$' -or $_.completedAt -and $_.completedAt -notmatch 'Z$'}).Count -eq 0) 'B62_UTC_WIRE' 'Public scan timestamps must use canonical UTC Z'
 Check (Api $emails[1] GET ($proofPath+'/scans?limit=1')) 200
 Check (Api $emails[1] GET ($proofPath+'/scans?limit=0')) 422
 Check (Api $emails[1] GET ($proofPath+'/scans?cursor=invalid')) 422
 Stop-DemoHost;EnvValue 'QuoteProofScan__PollMilliseconds' 'invalid';StartHost;LoginAll
 Check (Api $emails[0] GET "/quotes/$($quote.id)") 200
 Check (Api $emails[1] GET ($proofPath+'/scans')) 200
 Check (ProofContent $proofPath) 409
 Stop-DemoHost;EnvValue 'QuoteProofScan__PollMilliseconds' '2000';StartHost;LoginAll
 Check (ProofContent $proofPath) 200
 if($Browser){
  foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_RESTRICTED_EMAIL=$emails[1];UNICORECRM_TEST_RESTRICTED_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_WORKSPACE_KEY=$securityKey;UNICORECRM_TEST_LOW_QUOTE_ID=$quote.id;UNICORECRM_TEST_DOCUMENT_ID=$document.documentId;UNICORECRM_TEST_CASE_ID=$saved.caseId;UNICORECRM_TEST_CLEAN_PROOF_ID=$proofId;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
  if($UploadBrowser){EnvValue 'UNICORECRM_TEST_UPLOAD_PROOF_FIXTURE' (Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf')}
  [void](New-Item -ItemType Directory -Force (Join-Path $script:stateDirectory 'browser'))
  Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
  try{npx playwright test --config playwright.quote-scanner.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
  Get-ChildItem -LiteralPath $script:stateDirectory -Recurse -File -Filter 'error-context.md'|ForEach-Object{$text=[IO.File]::ReadAllText($_.FullName);[IO.File]::WriteAllText($_.FullName,[regex]::Replace($text,'B1-[A-Fa-f0-9]{64}!a9','[REDACTED_SYNTHETIC_CREDENTIAL]'))}
  Assert-Demo ($browserExit -eq 0) 'B62_BROWSER' 'Connected browser acceptance failed'
 }
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;directory=$script:stateDirectory;businessMutations=$false;proofUploadEnabled=[bool]$UploadBrowser}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.2 protected reads and configuration recovery $script:stateDirectory"
}
