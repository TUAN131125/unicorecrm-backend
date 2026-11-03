#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) 'task-runtime/quote-lifecycle/b1-api'
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$script:httpEvidence=[Collections.Generic.List[object]]::new()
$cases=[Collections.Generic.List[object]]::new()
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Verified local SQL target required'
$script:workspace='ws_772b487b87e84456b5020631fbaedd27'
$password=$configuration['UNICORE_DEV_SEED_PASSWORD']; if(!$password){$password=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$password) 'BLOCKED_ENVIRONMENT' 'Existing test identity credentials required'
$original=@{}
function EnvironmentValue([string]$Name,[string]$Value){$original[$Name]=[Environment]::GetEnvironmentVariable($Name);[Environment]::SetEnvironmentVariable($Name,$Value)}
function Api([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
    $response=Invoke-DemoApi $Method $Path $Body $Key $Version
    if($response.body -is [array] -and $response.body.Count -gt 0 -and $response.body[0] -is [long]){
        $response.body=[Text.Encoding]::UTF8.GetString([byte[]]$response.body)|ConvertFrom-Json -AsHashtable
    }
    if($Path -notlike '/auth/*'){$snapshot=if($null -ne $Body){$Body|ConvertTo-Json -Depth 40|ConvertFrom-Json -AsHashtable}else{$null};$cases.Add(@{method=$Method;path=$Path;body=$snapshot;key=$Key;version=$Version;response=$response})}
    return $response
}
function Check($Response,[int]$Status,[string]$Code=''){
    Assert-Demo ($Response.status -eq $Status -and (!$Code -or $Response.body.code -ceq $Code)) 'B1_API_FAILED' ($Response|ConvertTo-Json -Depth 25 -Compress)
}
function Start-ApprovalHost {
    Assert-Demo (!(Get-NetTCPConnection -State Listen -LocalPort 5098 -ErrorAction SilentlyContinue)) 'BLOCKED_ENVIRONMENT' 'Port 5098 must be free'
    $script:hostProcess=Start-Process -FilePath (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -WorkingDirectory $script:hostDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:stateDirectory 'host.log') -RedirectStandardError (Join-Path $script:stateDirectory 'host-error.log')
    for($i=0;$i -lt 60;$i++){
        Assert-Demo (!$script:hostProcess.HasExited) 'BLOCKED_ENVIRONMENT' 'Normal ApiHost exited'
        try{$null=Invoke-DemoApi GET '/auth/session';return}catch{Start-Sleep -Milliseconds 500}
    }
    throw 'ApiHost readiness timeout'
}
function Login {
    $script:token=$null
    $response=Api POST '/auth/sessions' @{email='pipeline.provisioning@example.com';password=$password} "b1-login-$([guid]::NewGuid().ToString('N'))"
    Check $response 200
    $script:token=$response.body.accessToken
}
function ParallelApproval([string]$QuoteId,[long]$Version,[string]$Run){
    $client=[Net.Http.HttpClient]::new()
    $pending=@()
    try{
        foreach($index in 1,2){
            $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$QuoteId/request-approval")
            $request.Headers.Add('Authorization',"Bearer $script:token");$request.Headers.Add('X-Workspace-Id',$script:workspace)
            $request.Headers.Add('X-Request-Id',"b1-race-$Run-$index");$request.Headers.Add('X-Correlation-Id',"b1-race-$Run-$index")
            $request.Headers.Add('Idempotency-Key',"b1-race-$Run-$index");$request.Headers.Add('If-Match','"'+$Version+'"')
            $request.Content=[Net.Http.StringContent]::new('{}',[Text.Encoding]::UTF8,'application/json')
            $pending+=@{request=$request;task=$client.SendAsync($request)}
        }
        $results=@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)}}finally{$response.Dispose()}})
        foreach($response in $results){$cases.Add(@{method='POST';path="/quotes/$QuoteId/request-approval";version=$Version;concurrent=$true;response=$response})}
        return ,$results
    }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
try{
    EnvironmentValue 'ASPNETCORE_ENVIRONMENT' 'Development';EnvironmentValue 'DOTNET_ENVIRONMENT' 'Development';EnvironmentValue 'ASPNETCORE_URLS' 'http://127.0.0.1:5098'
    $prefix="QuoteApprovalPolicies__Workspaces__$($script:workspace)__"
    foreach($entry in @{Version='b1-approved-v1';LineDiscountPercent='10';TotalDiscountPercent='10';PostpaidDays='30';AdvancedPaymentRequiresApproval='true';AlwaysRequireApproval='false';GrandTotalLimits__VND='100000000'}.GetEnumerator()){EnvironmentValue ($prefix+$entry.Key) $entry.Value}
    Start-ApprovalHost;Login
    $buyerRow=(Invoke-DemoSql 'SELECT TOP(1) BuyerType,BuyerId FROM quotes.Quotes WHERE WorkspaceId=@ws ORDER BY QuoteId' @{'@ws'=$script:workspace}).Rows[0]
    $run=[guid]::NewGuid().ToString('N')
    $draft=@{buyerRef=@{type=[string]$buyerRow.BuyerType;id=[string]$buyerRow.BuyerId};sourcePath='DIRECT_SALE';title="B1 API acceptance $run";currency='VND';lineItems=@(@{productNameSnapshot='B1 synthetic manual service';quantity='1';unitPrice=@{amount='100000000';currency='VND'};discountRate='0';taxMode='NONE'})}
    $create=Api POST '/quotes' $draft "b1-create-$run";Check $create 201
    $low=$create.body.result.quote
    $assessment=Api POST "/quotes/$($low.id)/request-approval" @{} "b1-assess-$run" $low.resourceVersion;Check $assessment 200
    Assert-Demo ($assessment.body.result.quote.approvalStatus -ceq 'NOT_REQUIRED' -and $assessment.body.result.quote.approvalRequired -eq $false) 'B1_EQUALITY' 'Grand Total equal to limit is not greater than limit'
    $replay=Api POST "/quotes/$($low.id)/request-approval" @{} "b1-assess-$run" $low.resourceVersion;Check $replay 200
    Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.version -eq $assessment.body.version) 'B1_REPLAY' 'Stored commitment required'
    Check (Api POST "/quotes/$($low.id)/request-approval" @{note='different'} "b1-assess-$run" $low.resourceVersion) 409 'IDEMPOTENCY_KEY_REUSED'
    Check (Api POST "/quotes/$($low.id)/approve" @{} "b1-self-$run" $assessment.body.version) 403 'ACCESS_DENIED'
    Check (Api POST "/quotes/$($low.id)/request-approval" @{approvedBy='caller'} "b1-spoof-$run" $assessment.body.version) 422 'VALIDATION_FAILED'
    $race=ParallelApproval $low.id $assessment.body.version $run
    Assert-Demo (($race.status|Sort-Object) -join ',' -ceq '200,412') 'B1_RACE' 'Same-version requests must commit exactly once'
    $current=(Api GET "/quotes/$($low.id)").body
    $draft.title="B1 edited commercial content $run"
    $edit=Api POST "/quotes/$($low.id)/update-draft" $draft "b1-edit-$run" $current.resourceVersion;Check $edit 200
    Assert-Demo (!$edit.body.result.quote.ContainsKey('approvalContentFingerprint') -and !$edit.body.result.quote.ContainsKey('approvalStatus')) 'B1_INVALIDATION' 'Commercial replacement must clear prior binding'
    $draft.lineItems[0].unitPrice.amount='100000001'
    $highCreate=Api POST '/quotes' $draft "b1-high-create-$run";Check $highCreate 201
    $high=$highCreate.body.result.quote
    $blocked=Api POST "/quotes/$($high.id)/request-approval" @{} "b1-no-approver-$run" $high.resourceVersion;Check $blocked 409 'QUOTE_APPROVAL_REQUIRED'
    Assert-Demo ($blocked.body.detail -ceq 'NO_ELIGIBLE_APPROVER') 'B1_NO_APPROVER' 'Independent approver must not be silently bypassed'
    $sql=(Invoke-DemoSql 'SELECT QuoteId,Status,ApprovalStatus,ApprovalRequired,ApprovalContentFingerprint,ApprovalPolicyVersion,ResourceVersion FROM quotes.Quotes WHERE WorkspaceId=@ws AND QuoteId IN (@low,@high)' @{'@ws'=$script:workspace;'@low'=$low.id;'@high'=$high.id}).Rows
    $sqlEvidence=@(foreach($row in $sql){@{quoteId=[string]$row.QuoteId;status=[string]$row.Status;approvalStatus=[string]$row.ApprovalStatus;version=[long]$row.ResourceVersion}})
    if($Browser){
        EnvironmentValue 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3044'
        Stop-DemoHost;Start-ApprovalHost;Login
        $fixture=Get-Content -Raw (Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline/backend/provisioning-acceptance.json')|ConvertFrom-Json -AsHashtable
        foreach($entry in @{UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL='pipeline.provisioning@example.com';UNICORECRM_TEST_PASSWORD=$password;UNICORECRM_TEST_WORKSPACE_KEY=$fixture.workspaceKey;UNICORECRM_TEST_LOW_QUOTE_ID=$low.id;UNICORECRM_TEST_HIGH_QUOTE_ID=$high.id;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')}.GetEnumerator()){EnvironmentValue $entry.Key $entry.Value}
        Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
        try {& npx playwright test --config playwright.quote-approval.config.ts; $exit=$LASTEXITCODE}finally{Pop-Location}
        Assert-Demo ($exit -eq 0) 'B1_BROWSER_FAILED' 'Inspect isolated connected Chromium evidence'
    }
    Stop-DemoHost
    [Environment]::SetEnvironmentVariable($prefix+'Version',$null)
    Start-ApprovalHost;Login
    $read=Api GET "/quotes/$($high.id)";Check $read 200
    Check (Api POST "/quotes/$($high.id)/request-approval" @{} "b1-missing-policy-$run" $high.resourceVersion) 503 'INTEGRATION_UNAVAILABLE'
    @{status='PASS_MEASURED_CASES';positiveIndependentApproval='BLOCKED_ENVIRONMENT_NO_SECOND_APPROVER';workspaceId=$script:workspace;lowQuoteId=$low.id;highQuoteId=$high.id;sql=$sqlEvidence;cases=$cases}|ConvertTo-Json -Depth 40|Set-Content (Join-Path $script:stateDirectory 'result.json')
    Write-Host 'PASS: real API policy equality, replay, conflict, no approver, self denial, strict input, invalidation, concurrent request and missing-policy/read preservation. Independent positive approval is BLOCKED_ENVIRONMENT.'
}finally{
    Stop-DemoHost
    $cases|ConvertTo-Json -Depth 40|Set-Content (Join-Path $script:stateDirectory 'cases.json')
    $script:httpEvidence|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'http-evidence.json')
    foreach($entry in $original.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
}
