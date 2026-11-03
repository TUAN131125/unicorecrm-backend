#requires -Version 7.0
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-customer-decision/guards-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-approval-synthetic.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Verification helper parse failure'}
$helpers=@('EnvValue','OverrideArray','DisableBootstraps','StartHost','LoginAll','Api','Check','Snapshot','SQLRows')
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($definition.Name -in $helpers){. ([ScriptBlock]::Create($definition.Extent.Text))}}
$configuration=Read-DemoConfiguration;$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Verified Development database required'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine").Rows[0].Machine
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL must be local'
$script:workspace='ws_096302f33ec94b3fb6ab9a41422d5531'
$emails=@('quote.creator@example.test','quote.approver@example.test');$passwords=@{}
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing process-only credential derivation required'
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null;$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$status='FAIL'
try{
    $before=Snapshot
    $beforeEvidence=(Invoke-DemoSql 'SELECT (SELECT COUNT(*) FROM quotes.CommandRecords) Receipts,(SELECT COUNT(*) FROM quotes.OutboxMessages) Events').Rows[0]
    foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
    foreach($item in @{Version='b3-user-approved-v1';AllowReportedAcceptance='true';AllowRejection='true'}.GetEnumerator()){EnvValue ("QuoteCustomerDecisionPolicies__Workspaces__$($script:workspace)__"+$item.Key) $item.Value}
    StartHost;LoginAll
    $quotes=SQLRows "SELECT QuoteId,Status,ResourceVersion FROM quotes.Quotes WHERE WorkspaceId=@ws AND ArchivedAt IS NULL ORDER BY QuoteId" @{'@ws'=$script:workspace}
    $sent=$quotes|Where-Object Status -ceq 'SENT'|Select-Object -First 1
    Assert-Demo (!!$sent.QuoteId) 'GUARD_FIXTURE' 'Read-only existing SENT fixture required'
    $path="/workflows/quote-acceptance/$($sent.QuoteId)/accept-and-close-deal"
    Check (Api $emails[0] POST $path @{} "b3-no-evidence-$run" $sent.ResourceVersion) 409 'QUOTE_ACCEPTANCE_BLOCKED' 'CUSTOMER_EVIDENCE_VERIFIER_UNAVAILABLE'
    foreach($field in 'verified','accepted','verifiedBy','acceptedAt','customerIdentityVerified','buyerRef','quoteRevision','commercialContentFingerprint','evidenceId'){
        Check (Api $emails[0] POST $path @{$field='client-claim'} "b3-spoof-$field-$run" $sent.ResourceVersion) 422 'VALIDATION_FAILED'
    }
    Check (Api $emails[0] POST $path @{} "b3-stale-accept-$run" ([long]$sent.ResourceVersion-1)) 412 'VERSION_CONFLICT'
    Check (Api $emails[1] POST $path @{} "b3-no-update-$run" $sent.ResourceVersion) 403 'ACCESS_DENIED'
    foreach($state in 'DRAFT','REVIEW'){
        $quote=$quotes|Where-Object Status -ceq $state|Select-Object -First 1
        if($quote){Check (Api $emails[0] POST "/workflows/quote-acceptance/$($quote.QuoteId)/accept-and-close-deal" @{} "b3-state-$state-$run" $quote.ResourceVersion) 409 'QUOTE_ACCEPTANCE_BLOCKED' 'QUOTE_STATE_INVALID'}
    }
    Check (Api $emails[0] POST "/quotes/$($sent.QuoteId)/reject" @{} "b3-missing-reason-$run" $sent.ResourceVersion) 422 'VALIDATION_FAILED'
    Check (Api $emails[0] POST "/quotes/$($sent.QuoteId)/reject" @{reason='Synthetic denial probe';verified='true'} "b3-reject-spoof-$run" $sent.ResourceVersion) 422 'VALIDATION_FAILED'
    Check (Api $emails[0] POST "/quotes/$($sent.QuoteId)/reject" @{reason='Synthetic stale probe'} "b3-reject-stale-$run" ([long]$sent.ResourceVersion-1)) 412 'VERSION_CONFLICT'
    Check (Api $emails[1] POST "/quotes/$($sent.QuoteId)/reject" @{reason='Synthetic permission probe'} "b3-reject-no-cap-$run" $sent.ResourceVersion) 403 'ACCESS_DENIED'
    $script:workspace='ws_11f7ec273a2849a7b8d7e1aef5e53ed4'
    Check (Api $emails[0] POST $path @{} "b3-foreign-$run" $sent.ResourceVersion) 404 'RESOURCE_NOT_FOUND'
    $script:workspace='ws_096302f33ec94b3fb6ab9a41422d5531'
    $afterEvidence=(Invoke-DemoSql 'SELECT (SELECT COUNT(*) FROM quotes.CommandRecords) Receipts,(SELECT COUNT(*) FROM quotes.OutboxMessages) Events').Rows[0]
    Assert-Demo ($beforeEvidence.Receipts -eq $afterEvidence.Receipts -and $beforeEvidence.Events -eq $afterEvidence.Events) 'B3_NEGATIVE_WRITES' 'Negative-only verification must create no command/outbox records'
    $status='PASS'
}finally{
    Stop-DemoHost
    if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
    foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
    $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
    $script:httpEvidence|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
    @{status=$status;requests=$cases.Count;negativeOnly=$true;newQuoteWrites=0;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
    Write-Host "$status B3 negative-only API guards: $($cases.Count) requests; no Quote/domain data changes. $script:stateDirectory"
}
