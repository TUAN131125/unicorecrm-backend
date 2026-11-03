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
 $fresh=NewManualSourceFixture $saved 'browser-final'
 $fresh.browserSourceId=$fresh.sourceId;$fresh.browserConfirmationProofId=$fresh.proofs.confirmation.proofId
 $fresh|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'browser-fixture-result.json')
 $status='DEVELOPMENT_SIMULATION_PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 70|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;actualSourceVerification='BLOCKED_CONTRACT';workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3B negative evidence guards $script:stateDirectory"
}
