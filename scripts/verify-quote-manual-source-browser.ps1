#requires -Version 7.0
param([Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='read'
. (Join-Path $PSScriptRoot 'scanner/QuoteManualSourceVerificationRuntime.ps1')
try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'DEVELOPMENT_SIMULATION_PASS' -and $saved.workspaceKey.StartsWith('unicore-manual-source-')) 'B63B_FIXTURE' 'Isolated simulation fixture required'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b63b-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-adjusted-v1'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1';QuoteIdentityPolicies='b63b-manual-simulation-v1'}.GetEnumerator()){EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value}
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__ManualDevelopmentSimulation" 'true';StartHost;LoginAll
 foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_APPROVER_EMAIL=$emails[1];UNICORECRM_TEST_APPROVER_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_CHECKER_EMAIL=$emails[2];UNICORECRM_TEST_CHECKER_PASSWORD=$passwords[$emails[2]];UNICORECRM_TEST_WORKSPACE_KEY=$securityKey;UNICORECRM_TEST_LOW_QUOTE_ID=$saved.quoteId;UNICORECRM_TEST_DOCUMENT_ID=$saved.documentId;UNICORECRM_TEST_SOURCE_ID=$saved.browserSourceId;UNICORECRM_TEST_CONFIRMATION_PROOF_ID=$saved.browserConfirmationProofId;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 [void](New-Item -ItemType Directory -Force (Join-Path $script:stateDirectory 'browser'))
 Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
 try{npx playwright test --config playwright.quote-manual-source.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
 Get-ChildItem -LiteralPath $script:stateDirectory -Recurse -File -Filter 'error-context.md'|ForEach-Object{$text=[IO.File]::ReadAllText($_.FullName);[IO.File]::WriteAllText($_.FullName,[regex]::Replace($text,'B1-[A-Fa-f0-9]{64}!a9','[REDACTED_SYNTHETIC_CREDENTIAL]'))}
 Assert-Demo ($browserExit -eq 0) 'B63B_BROWSER' 'Connected manual source browser tests failed'
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3B Connected browser $script:stateDirectory"
}
