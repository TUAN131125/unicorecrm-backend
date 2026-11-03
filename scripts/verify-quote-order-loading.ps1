#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Before','After')][string]$Phase='Before',[switch]$Browser)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot;$root=Split-Path $repo
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$evidence=Join-Path $root 'task-runtime/quote-order-loading-p0'
$script:stateDirectory=Join-Path $evidence $Phase.ToLowerInvariant()
[void](New-Item -ItemType Directory -Path $script:stateDirectory -Force)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$configuration=Read-DemoConfiguration;$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$target=[Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($target.DataSource -ceq 'DESKTOP-ICH225R' -and $target.InitialCatalog -ceq 'UnicoreCRM_Development') 'TARGET_CHANGED' 'Use only the inspected local database'
$identityEmail=$configuration['DevelopmentDemoBootstrap:Email'];$workspaceKey=$configuration['DevelopmentDemoBootstrap:Workspace:Key']
$identityPassword=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$identityPassword){$identityPassword=$configuration['DevelopmentDemoBootstrap:Password']}
$manifestPath=Join-Path $repo 'artifacts/realistic-demo/realistic-v1/manifest.json'
if(Test-Path $manifestPath){
 $manifest=Get-Content $manifestPath -Raw|ConvertFrom-Json
 Assert-Demo ($manifest.server -ceq $target.DataSource -and $manifest.database -ceq $target.InitialCatalog) 'MANIFEST_TARGET_CHANGED' 'Existing realistic demo identity provenance must match the actual database'
 $identityEmail=$manifest.email;$workspaceKey=$manifest.workspaceKey
}
$script:httpEvidence=[Collections.Generic.List[object]]::new();$script:token=$null;$script:workspace=$null;$script:hostProcess=$null;$vite=$null
function SetRuntime([string]$Name,[string]$Value){[Environment]::SetEnvironmentVariable($Name,$Value)}
function Login{
 $script:token=$null;$script:workspace=$null
 $email=$identityEmail;$password=$identityPassword
 $r=Invoke-DemoApi POST '/auth/sessions' @{email=$email;password=$password} ('loading-login-'+[guid]::NewGuid().ToString('N'))
 Assert-Demo ($r.status -eq 200) 'LOGIN_FAILED' "Existing configured identity login returned $($r.status); no seed or permission changes performed"
 $script:token=$r.body.accessToken
 $workspaces=Invoke-DemoApi GET '/workspaces'
 Assert-Demo ($workspaces.status -eq 200) 'WORKSPACE_FAILED' 'Existing memberships are required'
 $key=$workspaceKey
 $selected=@($workspaces.body.items|Where-Object workspaceKey -eq $key)
 Assert-Demo ($selected.Count -eq 1) 'WORKSPACE_FAILED' 'Configured demo Workspace requires an existing active membership'
 $script:workspace=$selected[0].workspaceId
 @{workspaceId=$script:workspace;workspaceKey=$key;database=$target.InitialCatalog;server=$target.DataSource;environment='Development';api='http://127.0.0.1:5098';workers='DISABLED_BY_EXISTING_DEMO_SEED_HOST_MODE'}|ConvertTo-Json|Set-Content (Join-Path $evidence 'context.json')
}
function SecurityAndQueries{
 $savedToken=$script:token;$savedWorkspace=$script:workspace
 $script:token=$null
 foreach($path in @('/quotes?limit=1','/orders?limit=1')){$r=Invoke-DemoApi GET $path;Assert-Demo ($r.status -eq 401) 'AUTHENTICATION_REGRESSION' 'Anonymous list must remain denied'}
 $script:token=$savedToken
 $foreign=(Invoke-DemoSql "SELECT TOP(1) WorkspaceId FROM workspace.Workspaces WHERE [Key] NOT IN (@demo,@control) ORDER BY WorkspaceId" @{'@demo'=$workspaceKey;'@control'=($workspaceKey+'-control')}).Rows[0].WorkspaceId
 $script:workspace=$foreign
 foreach($path in @('/quotes?limit=1','/orders?limit=1')){$r=Invoke-DemoApi GET $path;Assert-Demo ($r.status -eq 403) 'WORKSPACE_REGRESSION' 'Foreign Workspace list must remain denied'}
 $script:workspace=$savedWorkspace
 $first=Invoke-DemoApi GET '/quotes?limit=1&sortBy=updatedAt&sortDirection=desc'
 Assert-Demo ($first.status -eq 200) 'PAGING_FAILED' 'Cursor first page must load'
 if($first.body.pageInfo.hasNextPage){
  $cursor=[Uri]::EscapeDataString($first.body.pageInfo.nextCursor)
  $next=Invoke-DemoApi GET ('/quotes?limit=1&sortBy=updatedAt&sortDirection=desc&cursor='+$cursor)
  Assert-Demo ($next.status -eq 200 -and $next.body.items[0].id -ne $first.body.items[0].id) 'PAGING_FAILED' 'Next cursor must load a distinct historical Quote'
 }
 $fields=@{resourceKey='quotes';recordId=$first.body.items[0].id;requestedFields=@('status','grandTotal','lineItems','salesAcceptance');requestedCommands=@('quote.update')}
 $access=Invoke-DemoApi POST '/access/records/evaluate' $fields
 Assert-Demo ($access.status -eq 200 -and $access.body.authority -eq 'backend' -and $access.body.workspaceId -eq $savedWorkspace) 'FLS_AUTHORITY_FAILED' 'Field access must come from current Backend policy in the trusted Workspace'
 foreach($field in $fields.requestedFields){Assert-Demo ($access.body.fieldAccess.ContainsKey($field)) 'FLS_FIELDS_MISSING' 'Every requested field needs an explicit Backend access decision'}
 @{status=$access.status;workspaceId=$access.body.workspaceId;resourceKey=$access.body.resourceKey;canRead=$access.body.canRead;fieldAccess=$access.body.fieldAccess;authority=$access.body.authority}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'effective-field-access.json')
 $forged=$fields.Clone();$forged.workspaceId=$foreign
 $denied=Invoke-DemoApi POST '/access/records/evaluate' $forged
 Assert-Demo ($denied.status -eq 422) 'AUTHORITY_SPOOFING' 'Client cannot inject a Workspace fact into effective field access evaluation'
 $filtered=Invoke-DemoApi GET '/quotes?limit=1&status=DRAFT&archived=ACTIVE'
 Assert-Demo ($filtered.status -eq 200 -and @($filtered.body.items|Where-Object status -ne 'DRAFT').Count -eq 0) 'FILTER_FAILED' 'Status filtering must retain server semantics'
 $orderFiltered=Invoke-DemoApi GET '/orders?limit=1&state=DRAFT&search=loading-read-verification'
 Assert-Demo ($orderFiltered.status -eq 200 -and $orderFiltered.body.items.Count -eq 0) 'EMPTY_FAILED' 'Empty filtered Order query must succeed'
}
function ProbeLists([string]$Label){
 foreach($path in @('/quotes?limit=250','/orders?limit=250')){
  $r=Invoke-DemoApi GET $path
  $name=if($path.StartsWith('/quotes')){'quotes'}else{'orders'}
  $safe=if($r.status -eq 200){@{items=@($r.body.items|ForEach-Object{@{id=$_.id;status=$_.status;state=$_.state;resourceVersion=$_.resourceVersion}});pageInfo=$r.body.pageInfo}}else{$r.body}
  @{path=$path;status=$r.status;response=$safe}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $script:stateDirectory ($Label+'-'+$name+'.json'))
  if($Phase -eq 'After'){Assert-Demo ($r.status -eq 200) 'LIST_FAILED' "$path returned $($r.status)"}
 }
}
try{
 SetRuntime 'ASPNETCORE_ENVIRONMENT' 'Development';SetRuntime 'DOTNET_ENVIRONMENT' 'Development'
 SetRuntime 'ConnectionStrings__UnicoreCRM' $script:connectionString
 SetRuntime 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3048'
 Start-DemoHost;Login;ProbeLists 'initial'
 if($Phase -eq 'After'){SecurityAndQueries}
 if($Browser){
  foreach($entry in @{LOADING_TEST_API='http://127.0.0.1:5098';LOADING_TEST_EMAIL=$identityEmail;LOADING_TEST_PASSWORD=$identityPassword;LOADING_TEST_WORKSPACE=$workspaceKey;LOADING_TEST_WORKSPACE_ID=$script:workspace;LOADING_TEST_PHASE=$Phase;LOADING_TEST_EVIDENCE=(Join-Path $script:stateDirectory 'browser');VITE_RUNTIME_MODE='connected';VITE_API_BASE_URL='http://127.0.0.1:5098'}.GetEnumerator()){SetRuntime $entry.Key $entry.Value}
  $frontend=Join-Path $root 'frontend/unicorecrm-web'
  Assert-Demo (!(Get-NetTCPConnection -LocalPort 3048 -State Listen -ErrorAction SilentlyContinue)) 'PORT_IN_USE' 'Task Frontend port must be free'
  $vite=Start-Process node -ArgumentList @('node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','3048','--strictPort') -WorkingDirectory $frontend -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:stateDirectory 'vite.log') -RedirectStandardError (Join-Path $script:stateDirectory 'vite-error.log')
  for($i=0;$i -lt 60;$i++){if($vite.HasExited){throw 'Task Vite exited'};try{if((Invoke-WebRequest 'http://127.0.0.1:3048' -NoProxy).StatusCode -eq 200){break}}catch{};Start-Sleep -Milliseconds 500}
  & node (Join-Path $PSScriptRoot 'verify-quote-order-loading-browser.mjs')
  Assert-Demo ($LASTEXITCODE -eq 0) 'CHROMIUM_FAILED' 'Inspect connected browser evidence'
 }
 if($Phase -eq 'After'){
  Stop-DemoHost;Start-DemoHost;Login;ProbeLists 'restart'
  if($Browser){
   SetRuntime 'LOADING_TEST_PHASE' 'AfterRestart';SetRuntime 'LOADING_TEST_EVIDENCE' (Join-Path $script:stateDirectory 'browser-restart')
   & node (Join-Path $PSScriptRoot 'verify-quote-order-loading-browser.mjs')
   Assert-Demo ($LASTEXITCODE -eq 0) 'RESTART_CHROMIUM_FAILED' 'Lists must remain readable through Chromium after ApiHost restart'
  }
 }
}finally{
 $script:httpEvidence|ConvertTo-Json -Depth 15|Set-Content (Join-Path $script:stateDirectory 'http.json')
 Stop-DemoHost
 if($vite -and !$vite.HasExited){Stop-Process -Id $vite.Id -Force}
}
