#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser,[switch]$Security)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-delivery/api-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
# Reuse only named helper definitions, never execute B1 fixture provisioning or its tests.
$parseErrors=$null;$parseTokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-approval-synthetic.ps1'),[ref]$parseTokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'B1 verification helpers could not be parsed'}
$helpers=@('EnvValue','OverrideArray','DisableBootstraps','SetPolicy','StartHost','LoginAll','Api','Check','Draft','RequestApproval','SQLRows','Snapshot')
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
    if($definition.Name -in $helpers){. ([ScriptBlock]::Create($definition.Extent.Text))}
}
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Exact Development database required'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine").Rows[0].Machine
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'Local SQL required'
$emails=@('quote.creator@example.test','quote.approver@example.test','quote.approver2@example.test')
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing process-only credential derivation secret required'
$passwords=@{};$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null
$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$status='FAIL'
$prior=Get-Content (Join-Path (Split-Path $repo) 'task-runtime/quote-lifecycle/b1-synthetic/result.json') -Raw|ConvertFrom-Json -AsHashtable
$script:workspace=$prior.workspaceId
$script:buyer=$prior.buyerId
if(!$script:buyer){$script:buyer=(Invoke-DemoSql 'SELECT TOP(1) ContactId FROM contacts.Contacts WHERE WorkspaceId=@ws ORDER BY ContactId' @{'@ws'=$script:workspace}).Rows[0].ContactId}
function CreateB2([string]$Name,[string]$Amount='1000',$Extra=@{}){
    $body=Draft "B2 $Name $run" $Amount
    foreach($key in $Extra.Keys){$body[$key]=$Extra[$key]}
    $response=Api $emails[0] POST '/quotes' $body "b2-create-$Name-$run";Check $response 201
    return $response.body.result.quote
}
function Evidence([string]$Name){return @{deliveryId="b2_delivery_$Name`_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test';sentAt='2026-01-01T00:00:00Z';note='Synthetic human confirmation; no email sent'}}
function Send($Quote,$Body,[string]$Key){return Api $emails[0] POST "/quotes/$($Quote.id)/send" $Body $Key $Quote.resourceVersion}
function ParallelSend($Quote){
    $client=[Net.Http.HttpClient]::new();$pending=@()
    try{
        foreach($name in 'one','two'){
            $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$($Quote.id)/send")
            foreach($header in @{Authorization="Bearer $($script:tokens[$emails[0]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b2-race-$name-$run";'X-Correlation-Id'="b2-race-$name-$run";'Idempotency-Key'="b2-race-$name-$run";'If-Match'=('"'+$Quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($header.Key,$header.Value)}
            $request.Content=[Net.Http.StringContent]::new(((Evidence "race-$name")|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json')
            $pending+=@{request=$request;task=$client.SendAsync($request)}
        }
        return ,@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$result=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{method='POST';path="/quotes/$($Quote.id)/send";concurrent=$true;response=$result});$result}finally{$response.Dispose()}})
    }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
function ProvisionSecurityMember([string]$Email,[string[]]$Capabilities){
    DisableBootstraps
    EnvValue 'Workspace__DevelopmentBootstrap__Enabled' 'true';EnvValue 'Workspace__DevelopmentBootstrap__IdentityEmail' $Email
    EnvValue 'Workspace__DevelopmentBootstrap__EnsureStudioConfiguration' 'true'
    foreach($target in @(@{Section='MemberWorkspace';Key=$securityKey;Name='Quote Delivery Security Test'},@{Section='NonMemberWorkspace';Key='unicore-quote-approval-isolation';Name='Quote Approval Isolation Companion'})){
        $prefix='Workspace__DevelopmentBootstrap__'+$target.Section+'__'
        foreach($entry in @{Key=$target.Key;Name=$target.Name;LogoText='B2';Locale='vi';TimeZone='Asia/Ho_Chi_Minh';BaseCurrency='VND'}.GetEnumerator()){EnvValue ($prefix+$entry.Key) $entry.Value}
        OverrideArray ($prefix+'Capabilities') $Capabilities;OverrideArray ($prefix+'EnabledModuleKeys') @('contacts','quotes');OverrideArray ($prefix+'AvailableProductSpaces') @('crm')
    }
    EnvValue 'AccessControl__DevelopmentBootstrap__Enabled' 'true';EnvValue 'AccessControl__DevelopmentBootstrap__IdentityEmail' $Email
    EnvValue 'AccessControl__DevelopmentBootstrap__WorkspaceKey' $securityKey;EnvValue 'AccessControl__DevelopmentBootstrap__RoleName' ("B2 security baseline "+$Email)
    OverrideArray 'AccessControl__DevelopmentBootstrap__Capabilities' $Capabilities
    # IdentityAuth stays disabled: reuse existing identities without rewriting credentials.
    $process=Start-Process -FilePath (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -ArgumentList '--seed-demo' -WorkingDirectory $script:hostDirectory -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $script:stateDirectory ("security-provision-"+$Email+".log")) -RedirectStandardError (Join-Path $script:stateDirectory ("security-provision-"+$Email+"-error.log"))
    Assert-Demo ($process.ExitCode -eq 0) 'B2_SECURITY_PROVISION' 'Official Development owner bootstrap failed'
    DisableBootstraps
}
try{
    $before=Snapshot
    $before|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'entry-row-hashes.json')
    EnvValue 'ASPNETCORE_ENVIRONMENT' 'Development';EnvValue 'DOTNET_ENVIRONMENT' 'Development';EnvValue 'ASPNETCORE_URLS' 'http://127.0.0.1:5098'
    EnvValue 'UNICORE_DEV_SEED_ENABLED' 'false';EnvValue 'Development__ApplyMigrations' 'false';EnvValue 'Workflows__InitialWorkspaceProvisioning__ResumeEnabled' 'false'
    OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044')
    SetPolicy 'b1-synthetic-approved-v2';StartHost;LoginAll
    $raw=CreateB2 'missing';Check (Send $raw (Evidence 'missing') "b2-missing-$run") 409 'QUOTE_APPROVAL_STALE'
    $low=RequestApproval (CreateB2 'low') 'b2-low'
    $body=Evidence 'low';$sent=Send $low $body "b2-send-$run";Check $sent 200
    Assert-Demo ($sent.body.result.quote.status -ceq 'SENT' -and $sent.body.version -eq ($low.resourceVersion+1)) 'B2_STATE' 'Server must commit SENT and increment version'
    Check (Send $low $body "b2-send-$run") 200
    $changed=$body.Clone();$changed.note='different';Check (Send $low $changed "b2-send-$run") 409 'IDEMPOTENCY_KEY_REUSED'
    Check (Send $sent.body.result.quote $changed "b2-id-reused-$run") 409 'QUOTE_DELIVERY_EVIDENCE_INVALID'
    Check (Send $sent.body.result.quote $body "b2-id-replay-$run") 200
    Check (Send $sent.body.result.quote (Evidence 'reused-alias-key') "b2-id-replay-$run") 409 'IDEMPOTENCY_KEY_REUSED'
    Check (Send $low (Evidence 'stale') "b2-stale-$run") 412 'VERSION_CONFLICT'
    $resent=Send $sent.body.result.quote (Evidence 'resend') "b2-resend-$run";Check $resent 200
    Assert-Demo ($resent.body.result.quote.deliveryHistory.Count -eq 2) 'B2_RESEND' 'Actual resend must append a new record'
    $pending=RequestApproval (CreateB2 'high' '100000001') 'b2-high'
    Check (Send $pending (Evidence 'pending') "b2-pending-$run") 409 'QUOTE_SEND_BLOCKED'
    Check (Api $emails[0] POST "/quotes/$($pending.id)/approve" @{} "b2-self-$run" $pending.resourceVersion) 403 'ACCESS_DENIED'
    $approved=Api $emails[1] POST "/quotes/$($pending.id)/approve" @{} "b2-approve-$run" $pending.resourceVersion;Check $approved 200
    $highSent=Send $approved.body.result.quote (Evidence 'approved') "b2-approved-$run";Check $highSent 200
    foreach($kind in 'PROVIDER_ACCEPTED','PROVIDER_DELIVERED'){$fake=Evidence $kind;$fake.evidenceType=$kind;Check (Send $resent.body.result.quote $fake "b2-fake-$kind-$run") 422 'QUOTE_DELIVERY_EVIDENCE_INVALID'}
    $invalid=Evidence 'bad-email';$invalid.recipientEmail='invalid';Check (Send $resent.body.result.quote $invalid "b2-invalid-$run") 422 'VALIDATION_FAILED'
    $invalid=Evidence 'bad-channel';$invalid.channel='FAKE';Check (Send $resent.body.result.quote $invalid "b2-channel-$run") 422 'VALIDATION_FAILED'
    $invalid=Evidence 'future';$invalid.sentAt='2099-01-01T00:00:00Z';Check (Send $resent.body.result.quote $invalid "b2-future-$run") 422 'VALIDATION_FAILED'
    $invalid=Evidence 'offset';$invalid.sentAt='2026-01-01T00:00:00+00:00';Check (Send $resent.body.result.quote $invalid "b2-offset-$run") 422 'VALIDATION_FAILED'
    $invalid=Evidence 'spoof';$invalid.sentBy='caller-controlled';Check (Send $resent.body.result.quote $invalid "b2-spoof-$run") 422 'VALIDATION_FAILED'
    Check (Api $emails[0] POST "/quotes/$($resent.body.aggregateId)/send" (Evidence 'no-match') "b2-no-match-$run") 422 'VALIDATION_FAILED'
    Check (Api $emails[0] POST "/quotes/$($resent.body.aggregateId)/send" (Evidence 'no-key') '' $resent.body.version) 422 'VALIDATION_FAILED'
    $expired=RequestApproval (CreateB2 'expired' '1000' @{validUntil='2020-01-01'}) 'b2-expired'
    Check (Send $expired (Evidence 'expired') "b2-expired-$run") 409 'QUOTE_SEND_BLOCKED' 'QUOTE_EXPIRED'
    $recipient=RequestApproval (CreateB2 'recipient' '1000' @{recipientEmail='saved@example.test'}) 'b2-recipient'
    Check (Send $recipient (Evidence 'recipient') "b2-recipient-$run") 422 'VALIDATION_FAILED'
    Check (Api $emails[1] POST "/quotes/$($recipient.id)/send" (Evidence 'restricted') "b2-restricted-$run" $recipient.resourceVersion) 403 'ACCESS_DENIED'
    $ws=$script:workspace;$script:workspace='ws_7a5bfb9ca8e64598b9f60a76507a3462'
    Check (Send $recipient (Evidence 'cross') "b2-cross-$run") 403
    $script:workspace=$ws
    $edited=RequestApproval (CreateB2 'edit-invalidation' '100000001') 'b2-edit'
    $editApproval=Api $emails[1] POST "/quotes/$($edited.id)/approve" @{} "b2-edit-approve-$run" $edited.resourceVersion;Check $editApproval 200
    $editBody=Draft "B2 changed commercial content $run" '100000002'
    $edit=Api $emails[0] POST "/quotes/$($edited.id)/update-draft" $editBody "b2-edit-$run" $editApproval.body.version;Check $edit 200
    Check (Send $edit.body.result.quote (Evidence 'edited') "b2-edited-send-$run") 409 'QUOTE_APPROVAL_STALE'
    $race=RequestApproval (CreateB2 'race') 'b2-race'
    $raceResults=ParallelSend $race
    Assert-Demo ((@($raceResults.status|Sort-Object) -join ',') -ceq '200,412') 'B2_RACE' 'Concurrent distinct sends at the same version must commit only once'
    $raceSql=(Invoke-DemoSql "SELECT (SELECT COUNT(*) FROM OPENJSON(q.DeliveryHistoryJson)) History,(SELECT COUNT(*) FROM quotes.CommandRecords c WHERE c.WorkspaceId=q.WorkspaceId AND c.QuoteId=q.QuoteId AND c.Operation='recordQuoteSendEvidenceCommand') Audits,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_DELIVERY_RECORDED') Events FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@quote" @{'@ws'=$ws;'@quote'=$race.id}).Rows[0]
    Assert-Demo ($raceSql.History -eq 1 -and $raceSql.Audits -eq 1 -and $raceSql.Events -eq 1) 'B2_RACE_SQL' 'Concurrent sends must leave exactly one delivery/audit/event'
    Stop-DemoHost;SetPolicy 'b2-policy-changed';StartHost;LoginAll
    Check (Send $resent.body.result.quote (Evidence 'policy-change') "b2-policy-$run") 409 'QUOTE_APPROVAL_STALE'
    $readback=Api $emails[0] GET "/quotes/$($low.id)";Check $readback 200
    Assert-Demo ($readback.body.deliveryHistory.Count -eq 2 -and $readback.body.status -ceq 'SENT') 'B2_RESTART' 'History must survive restart/login'
    $sql=SQLRows "SELECT QuoteId,Status,ResourceVersion,DeliveryHistoryJson,(SELECT COUNT(*) FROM quotes.CommandRecords c WHERE c.WorkspaceId=q.WorkspaceId AND c.QuoteId=q.QuoteId AND Operation='recordQuoteSendEvidenceCommand') Audits,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND EventType='QUOTE_DELIVERY_RECORDED') Events FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@quote" @{'@ws'=$ws;'@quote'=$low.id}
    Assert-Demo ($sql[0].Audits -eq 3 -and $sql[0].Events -eq 2) 'B2_EXACTLY_ONCE' 'Two actual deliveries plus one new-key replay receipt must yield three immutable receipts and only two delivery events'
    $sql|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'sql-evidence.json')
    if($Security){
        Stop-DemoHost
        $securityKey='unicore-quote-delivery-security-'+$run.Substring(0,8)
        ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','contacts.read','contacts.create','access.read','access.configure')
        ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.update')
        $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
        SetPolicy 'b2-security-v1';StartHost;LoginAll
        $contact=Api $emails[0] POST '/contacts' @{fullName="B2 security buyer $run"} "b2-security-buyer-$run";Check $contact 201
        $originalBuyer=$script:buyer;$script:buyer=$contact.body.result.contact.id
        if(!$script:buyer){$script:buyer=$contact.body.id}
        $securityQuote=RequestApproval (CreateB2 'security') 'b2-security'
        $securityBody=Evidence 'security'
        $securitySend=Api $emails[1] POST "/quotes/$($securityQuote.id)/send" $securityBody "b2-security-send-$run" $securityQuote.resourceVersion;Check $securitySend 200
        $role=Api $emails[0] POST '/access/roles' @{name="B2 delivery read-only $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.update');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='deliveryHistory';access='READ_ONLY'})} "b2-security-role-$run";Check $role 200
        $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[1]
        $member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
        Assert-Demo (!!$member.membershipId) 'B2_SECURITY_MEMBERSHIP' 'Official directory must identify the new membership'
        $assignment=Api $emails[0] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b2-security-assign-$run" $member.version;Check $assignment 200
        Check (Api $emails[1] POST "/quotes/$($securityQuote.id)/send" $securityBody "b2-security-send-$run" $securityQuote.resourceVersion) 403 'ACCESS_DENIED'
        Check (Api $emails[1] POST "/quotes/$($securityQuote.id)/send" (Evidence 'security-denied') "b2-security-denied-$run" $securitySend.body.version) 403 'ACCESS_DENIED'
        $securityProof=SQLRows "SELECT QuoteId,Status,ResourceVersion,(SELECT COUNT(*) FROM OPENJSON(q.DeliveryHistoryJson)) History,(SELECT COUNT(*) FROM quotes.CommandRecords c WHERE c.WorkspaceId=q.WorkspaceId AND c.QuoteId=q.QuoteId AND c.Operation='recordQuoteSendEvidenceCommand') Receipts,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_DELIVERY_RECORDED') Events FROM quotes.Quotes q WHERE WorkspaceId=@ws AND QuoteId=@id" @{'@ws'=$script:workspace;'@id'=$securityQuote.id}
        Assert-Demo ($securityProof[0].History -eq 1 -and $securityProof[0].Receipts -eq 1 -and $securityProof[0].Events -eq 1) 'B2_SECURITY_SQL' 'Denied FLS/replay must not append or mutate'
        @{workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$securityQuote.id;roleId=$role.body.aggregateId;sql=$securityProof}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'security-evidence.json')
        $script:workspace=$ws;$script:buyer=$originalBuyer
        Stop-DemoHost;StartHost;LoginAll
    }
    if($Browser){
        Stop-DemoHost;SetPolicy 'b1-synthetic-approved-v2';StartHost;LoginAll
        $ui=CreateB2 'browser' '1000' @{recipientEmail='buyer@example.test'}
        # Existing control actor/Deal are read only. Only a fresh linked Quote is created.
        Stop-DemoHost
        $script:workspace='ws_772b487b87e84456b5020631fbaedd27';SetPolicy 'b1-approved-v1'
        $script:workspace=$ws;StartHost;LoginAll
        $controlEmail='pipeline.provisioning@example.com'
        $controlPassword=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$controlPassword){$controlPassword=$configuration['DevelopmentDemoBootstrap:Password']}
        $script:token=$null
        $controlLogin=Invoke-DemoApi POST '/auth/sessions' @{email=$controlEmail;password=$controlPassword} "b2-control-login-$run"
        Assert-Demo ($controlLogin.status -eq 200) 'B2_CONTROL_LOGIN' 'Existing control actor login required'
        $script:tokens[$controlEmail]=$controlLogin.body.accessToken
        $script:workspace='ws_772b487b87e84456b5020631fbaedd27'
        Check (Api $controlEmail GET "/workspaces/$($script:workspace)/bootstrap") 200
        $deal=(Invoke-DemoSql "SELECT TOP(1) DealId,Version FROM deals.Deals WHERE WorkspaceId=@ws AND StageCategory='Open' AND ArchivedAt IS NULL ORDER BY DealId" @{'@ws'=$script:workspace}).Rows[0]
        Assert-Demo (!!$deal.DealId) 'B2_LINKED_FIXTURE' 'Existing OPEN control Deal required'
        $linkedDraft=Draft "B2 linked browser $run" '1000';$linkedDraft.Remove('buyerRef');$linkedDraft.Remove('sourcePath');$linkedDraft.recipientEmail='buyer@example.test'
        $linked=Api $controlEmail POST "/deals/$($deal.DealId)/quotes" $linkedDraft "b2-linked-$run" $deal.Version;Check $linked 201
        $linkedAssessment=Api $controlEmail POST "/quotes/$($linked.body.aggregateId)/request-approval" @{} "b2-linked-assess-$run" $linked.body.version;Check $linkedAssessment 200
        $linkedQuote=$linkedAssessment.body.result.quote
        $linkedSend=Api $controlEmail POST "/quotes/$($linkedQuote.id)/send" (Evidence 'linked') "b2-linked-send-$run" $linkedQuote.resourceVersion;Check $linkedSend 200
        $script:workspace=$ws
        foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL=$emails[0];UNICORECRM_TEST_PASSWORD=$passwords[$emails[0]];UNICORECRM_TEST_WORKSPACE_KEY='unicore-quote-approval-test';UNICORECRM_TEST_LOW_QUOTE_ID=$ui.id;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
        foreach($entry in @{UNICORECRM_TEST_APPROVER_EMAIL=$emails[1];UNICORECRM_TEST_APPROVER_PASSWORD=$passwords[$emails[1]];UNICORECRM_TEST_DOCUMENT_QUOTE_ID=$recipient.id;UNICORECRM_TEST_UNASSESSED_QUOTE_ID=$raw.id}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
        $controlKey=(Invoke-DemoSql 'SELECT [Key] FROM workspace.Workspaces WHERE WorkspaceId=@ws' @{'@ws'='ws_772b487b87e84456b5020631fbaedd27'}).Rows[0].Key
        foreach($entry in @{UNICORECRM_TEST_CONTROL_EMAIL=$controlEmail;UNICORECRM_TEST_CONTROL_PASSWORD=$controlPassword;UNICORECRM_TEST_CONTROL_WORKSPACE_KEY=[string]$controlKey;UNICORECRM_TEST_LINKED_QUOTE_ID=$linkedQuote.id;UNICORECRM_TEST_DEAL_ID=[string]$deal.DealId}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
        Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
        try{& npx playwright test --config playwright.quote-delivery.config.ts;$browserExit=$LASTEXITCODE}finally{Pop-Location}
        Assert-Demo ($browserExit -eq 0) 'B2_BROWSER_FAILED' 'Connected delivery browser failed'
        $uiRead=Api $emails[0] GET "/quotes/$($ui.id)";Check $uiRead 200
        Assert-Demo ($uiRead.body.status -ceq 'SENT' -and $uiRead.body.deliveryHistory.Count -eq 2) 'B2_BROWSER_READBACK' 'Browser first delivery and resend must persist'
    }
    $status='PASS'
}finally{
    Stop-DemoHost
    if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($after[$table][$key] -cne $before[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
    foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
    $cases|ConvertTo-Json -Depth 80|Set-Content (Join-Path $script:stateDirectory 'cases.json')
    $script:httpEvidence|ConvertTo-Json -Depth 15|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
    @{status=$status;workspaceId=$script:workspace;run=$run;cases=$cases.Count;lowQuoteId=$low.id;highQuoteId=$pending.id;uiQuoteId=$ui.id;linkedQuoteId=$linkedQuote.id;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
    Write-Host "$status B2 real API/SQL: $($cases.Count) requests. Evidence: $script:stateDirectory"
}
