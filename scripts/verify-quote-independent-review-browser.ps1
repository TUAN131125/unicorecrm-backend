#requires -Version 7.0
param([Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='read'
. (Join-Path $PSScriptRoot 'scanner/QuoteIndependentReviewVerificationRuntime.ps1')
try{
 $saved=InitializeReview $FixtureResult
 foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_APPROVER_EMAIL=$emails[1];UNICORECRM_TEST_APPROVER_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_CHECKER_EMAIL=$emails[2];UNICORECRM_TEST_CHECKER_PASSWORD=$passwords[$emails[2]];UNICORECRM_TEST_WORKSPACE_KEY=$saved.workspaceKey;UNICORECRM_TEST_LOW_QUOTE_ID=$saved.quoteId;UNICORECRM_TEST_DOCUMENT_ID=$saved.documentId;UNICORECRM_TEST_SOURCE_ID=$saved.sourceId;UNICORECRM_TEST_PROOF_ID=$saved.consentProofId;UNICORECRM_TEST_CONSENT_ACQUIRED_AT=$saved.consentAcquiredAt;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 [void](New-Item -ItemType Directory -Force (Join-Path $script:stateDirectory 'browser'))
 Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
 try{
  npx playwright test --config playwright.quote-independent-review.config.ts --grep 'restricted actors|independent reviewer examines';$first=$LASTEXITCODE
  Assert-Demo ($first -eq 0) 'B64_BROWSER_FIRST' 'Connected review/permissions browser failed'
  Stop-DemoHost;StartHost;LoginAll
  npx playwright test --config playwright.quote-independent-review.config.ts --grep 'review persistence|source revocation';$second=$LASTEXITCODE
  Assert-Demo ($second -eq 0) 'B64_BROWSER_SECOND' 'Connected restart/revocation browser failed'
 }finally{Pop-Location}
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 Get-ChildItem -LiteralPath $script:stateDirectory -Recurse -File -Filter 'error-context.md'|ForEach-Object{$text=[IO.File]::ReadAllText($_.FullName);[IO.File]::WriteAllText($_.FullName,[regex]::Replace($text,'B1-[A-Fa-f0-9]{64}!a9','[REDACTED_SYNTHETIC_CREDENTIAL]'))}
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.4 Connected browser $script:stateDirectory"
}
