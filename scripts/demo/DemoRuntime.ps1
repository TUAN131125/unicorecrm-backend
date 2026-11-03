function Read-DemoConfiguration {
    $values = @{}
    function Flatten($node, [string]$prefix) {
        foreach($key in $node.Keys) {
            $path=if($prefix){"${prefix}:$key"}else{$key}
            if($node[$key] -is [System.Collections.IDictionary]){Flatten $node[$key] $path}
            elseif($node[$key] -is [array]){for($i=0;$i -lt $node[$key].Count;$i++){$values["${path}:$i"]=$node[$key][$i]}}
            else{$values[$path]=$node[$key]}
        }
    }
    foreach($file in @('appsettings.json','appsettings.Development.json','appsettings.Development.Local.json')) {
        $path=Join-Path $script:hostDirectory $file
        if(Test-Path $path){Flatten (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable) ''}
    }
    $secrets=Join-Path $env:APPDATA 'Microsoft/UserSecrets/unicorecrm-apihost-development/secrets.json'
    if(Test-Path $secrets){Flatten (Get-Content -LiteralPath $secrets -Raw | ConvertFrom-Json -AsHashtable) ''}
    foreach($entry in Get-ChildItem Env:){$values[$entry.Name.Replace('__',':')]=$entry.Value}
    return $values
}
function Invoke-DemoSql([string]$Query, [hashtable]$Parameters=@{}) {
    $connection=[System.Data.SqlClient.SqlConnection]::new($script:connectionString)
    try {
        $connection.Open();$command=$connection.CreateCommand();$command.CommandText=$Query;$command.CommandTimeout=30
        foreach($key in $Parameters.Keys){[void]$command.Parameters.AddWithValue($key,$Parameters[$key])}
        $table=[System.Data.DataTable]::new();$table.Load($command.ExecuteReader())
        return ,$table
    } finally {$connection.Dispose()}
}
function Get-DemoCounts {
    $rows=@(foreach($module in $script:tables.Keys | Sort-Object) {
        $table=Invoke-DemoSql "SELECT WorkspaceId,COUNT(*) RecordCount FROM $($script:tables[$module]) GROUP BY WorkspaceId"
        foreach($row in $table.Rows){
            $id=@{Lead='LeadId';Contact='ContactId';Organization='OrganizationId';Customer='CustomerId';Deal='DealId';Task='TaskId';Activity='ActivityId';Product='ProductId';Support='CaseId'}[$module]
            $hash=(Invoke-DemoSql "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM $($script:tables[$module]) WHERE WorkspaceId=@ws ORDER BY $id FOR JSON PATH)),2) RowHash" @{'@ws'=[string]$row.WorkspaceId}).Rows[0].RowHash
            @{module=$module;workspaceId=[string]$row.WorkspaceId;count=[int]$row.RecordCount;hash=[string]$hash}
        }
    })
    return ,$rows
}
function Save-DemoManifest {
    $json=$script:manifest | ConvertTo-Json -Depth 60
    $temporary="$script:manifestPath.tmp"
    [IO.File]::WriteAllText($temporary,$json,[Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $script:manifestPath -Force
}
function Invoke-DemoApi([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null) {
    $correlation="demo-$([Guid]::NewGuid().ToString('N'))"
    $headers=@{'X-Request-Id'=$correlation;'X-Correlation-Id'=$correlation}
    if($script:token){$headers.Authorization="Bearer $script:token"}
    if($script:workspace){$headers['X-Workspace-Id']=$script:workspace}
    if($Key){$headers['Idempotency-Key']=$Key}
    if($null -ne $Version){$headers['If-Match']='"'+$Version+'"'}
    $request=@{Method=$Method;Uri="http://127.0.0.1:5098$Path";Headers=$headers;SkipHttpErrorCheck=$true;TimeoutSec=60}
    if($null -ne $Body){$request.Body=$Body|ConvertTo-Json -Depth 40 -Compress;$request.ContentType='application/json; charset=utf-8'}
    $response=Invoke-WebRequest @request
    $value=if($response.Content){$response.Content|ConvertFrom-Json -AsHashtable}else{@{}}
    # Authentication responses (including tokens) never enter evidence or manifest.
    $script:httpEvidence.Add(@{method=$Method;path=$Path;status=[int]$response.StatusCode;code=$value.code;correlationId=$correlation})
    return @{status=[int]$response.StatusCode;body=$value}
}
function Assert-Demo([bool]$Condition,[string]$Code,[string]$Message) {
    if(!$Condition){throw "$Code`: $Message"}
}
function Invoke-DemoCommand([string]$Name,[string]$Path,$Body,$Version=$null,[string]$Method='POST') {
    if(!$script:manifest.commands.Contains($Name)) {
        $script:manifest.commands[$Name]=@{path=$Path;method=$Method;workspaceId=$script:workspace;body=$Body;version=$Version;key="realistic-v1-$Name";response=$null}
        Save-DemoManifest
    }
    $intent=$script:manifest.commands[$Name]
    Assert-Demo (!$intent.workspaceId -or $intent.workspaceId -eq $script:workspace) 'INTENT_WORKSPACE_MISMATCH' $Name
    if($null -ne $intent.response){return $intent.response}
    # The saved intent, including the original expected version, is authoritative for replay.
    $verb=if($intent.method){$intent.method}else{'POST'}
    $result=Invoke-DemoApi $verb $intent.path $intent.body $intent.key $intent.version
    if($result.status -notin 200,201,202){$intent.lastError=@{status=$result.status;problem=$result.body};Save-DemoManifest}
    Assert-Demo ($result.status -in 200,201,202) 'COMMAND_FAILED' "$Name HTTP $($result.status) $($result.body.code) $($result.body.fieldErrors|ConvertTo-Json -Compress)"
    Assert-Demo ($result.body.outcome -in 'COMMITTED','REPLAYED') 'COMMAND_INCOMPLETE' "$Name requires a committed terminal outcome"
    $script:committedThisRun++
    if($script:StopAfterCommit -gt 0 -and $script:committedThisRun -eq $script:StopAfterCommit){throw 'SIMULATED_INTERRUPTION: response deliberately not saved; next run must replay this committed command'}
    $intent.response=$result.body
    Save-DemoManifest
    if($script:committedThisRun % 50 -eq 0){Write-Host "Committed $script:committedThisRun commands this run; last=$Name"}
    return $result.body
}
function Start-DemoHost {
    Assert-Demo (!(Get-NetTCPConnection -State Listen -LocalPort 5098 -ErrorAction SilentlyContinue)) 'BLOCKED_ENVIRONMENT' 'Port 5098 must be free; an existing host is never reused'
    $script:hostProcess=Start-Process -FilePath (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -ArgumentList '--demo-seed-host' -WorkingDirectory $script:hostDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:stateDirectory 'host.log') -RedirectStandardError (Join-Path $script:stateDirectory 'host-error.log')
    for($i=0;$i -lt 60;$i++) {
        Assert-Demo (!$script:hostProcess.HasExited) 'BLOCKED_ENVIRONMENT' 'Seed ApiHost exited; inspect local host logs'
        try{$null=Invoke-DemoApi GET '/auth/session';return}catch{Start-Sleep -Milliseconds 500}
    }
    throw 'BLOCKED_ENVIRONMENT: Seed ApiHost readiness timeout'
}
function Stop-DemoHost {if($script:hostProcess -and !$script:hostProcess.HasExited){Stop-Process -Id $script:hostProcess.Id;$script:hostProcess.WaitForExit()};$script:hostProcess=$null}
function Connect-DemoIdentity {
    $script:token=$null;$script:workspace=$null
    $login=Invoke-DemoApi POST '/auth/sessions' @{email=$script:manifest.email;password=$script:password} "login-$([Guid]::NewGuid().ToString('N'))"
    Assert-Demo ($login.status -eq 200) 'AUTHENTICATION_FAILED' "HTTP $($login.status)"
    $script:token=$login.body.accessToken
    $session=Invoke-DemoApi GET '/auth/session'
    $script:member=$session.body.principal.memberId
    $list=Invoke-DemoApi GET '/workspaces'
    Assert-Demo ($list.status -eq 200) 'WORKSPACE_LIST_FAILED' 'Authenticated workspace list required'
    $demo=@($list.body.items|Where-Object workspaceKey -eq $script:manifest.workspaceKey)
    $control=@($list.body.items|Where-Object workspaceKey -eq $script:manifest.controlKey)
    Assert-Demo ($demo.Count -eq 1 -and $control.Count -eq 1) 'WORKSPACE_AUTHORITY_FAILED' 'Both new workspaces require real active memberships'
    Assert-Demo ((!$script:manifest.workspaceId -or $script:manifest.workspaceId -eq $demo[0].workspaceId) -and (!$script:manifest.controlId -or $script:manifest.controlId -eq $control[0].workspaceId)) 'WORKSPACE_IDENTITY_CHANGED' 'Original workspace IDs cannot be replaced by matching keys'
    $script:workspace=$demo[0].workspaceId
    $script:manifest.workspaceId=$script:workspace;$script:manifest.controlId=$control[0].workspaceId
    Save-DemoManifest
}
