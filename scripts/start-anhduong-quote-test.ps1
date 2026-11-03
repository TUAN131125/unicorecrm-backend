#requires -Version 7.0
[CmdletBinding()]
param([switch]$ApiOnly)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$evidence=Join-Path $root 'task-runtime/shared-smtp-quote/anhduong'
$context=Get-Content (Join-Path $evidence 'database-context.json') -Raw|ConvertFrom-Json
if($context.database -notmatch '^UnicoreCRM_QuoteGmailTest_[a-f0-9]{32}$' -or $context.workspace.WorkspaceId -cne 'ws_f2332a12d56a421d8a722bd2f65c5efb'){throw 'Unexpected database or Workspace.'}
$proposal=Get-Content (Join-Path $root 'task-runtime/shared-smtp-quote/anhduong-policy-proposal.json') -Raw|ConvertFrom-Json -AsHashtable
if($proposal.state -cne 'OWNER_APPROVED_TEST_ONLY'){throw 'Owner-approved test policy is required.'}
$sourceConnection=$null
foreach($f in @((Join-Path $root 'backend/src/UnicoreCRM.ApiHost/appsettings.json'),(Join-Path $root 'backend/src/UnicoreCRM.ApiHost/appsettings.Development.json'),(Join-Path $root 'backend/src/UnicoreCRM.ApiHost/appsettings.Development.Local.json'),(Join-Path $env:APPDATA 'Microsoft/UserSecrets/unicorecrm-apihost-development/secrets.json'))){
 if(Test-Path -LiteralPath $f){$cfg=Get-Content -LiteralPath $f -Raw|ConvertFrom-Json -AsHashtable;if($cfg.ConnectionStrings.UnicoreCRM){$sourceConnection=$cfg.ConnectionStrings.UnicoreCRM};if($cfg['ConnectionStrings:UnicoreCRM']){$sourceConnection=$cfg['ConnectionStrings:UnicoreCRM']}}
}
if($env:ConnectionStrings__UnicoreCRM){$sourceConnection=$env:ConnectionStrings__UnicoreCRM}
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($sourceConnection);$builder['Initial Catalog']=$context.database
$env:ConnectionStrings__UnicoreCRM=$builder.ConnectionString
foreach($name in @('IdentityAuth','Workspace','AccessControl')){[Environment]::SetEnvironmentVariable($name+'__DevelopmentBootstrap__Enabled','false')}
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:UNICORE_DEV_SEED_ENABLED='false'
if($ApiOnly){
 $previous=Get-Content (Join-Path $evidence 'verified-hosts.json') -Raw|ConvertFrom-Json
 $port=([Uri]$previous.apiUrl).Port
 $running=Get-Process -Id $previous.apiPid -ErrorAction SilentlyContinue
 if($running){
  $expectedExe=[IO.Path]::GetFullPath((Join-Path $root 'backend/artifacts/quote-local-host/bin/UnicoreCRM.QuoteLocalTestHost.exe'))
  if($running.Path -cne $expectedExe){throw 'Refusing to stop an unexpected process'}
  Stop-Process -Id $running.Id -Force;$running.WaitForExit(10000)|Out-Null
 }
}else{$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()}
$env:VITE_API_BASE_URL='http://127.0.0.1:'+$port;$env:VITE_RUNTIME_MODE='connected'
$front=$null;$api=$null
try{
 if($ApiOnly){$front=Get-Process -Id $previous.frontendPid -ErrorAction Stop}
 else{$front=Start-Process node -WorkingDirectory (Join-Path $root 'frontend/unicorecrm-web') -ArgumentList @((Join-Path $PSScriptRoot 'run-anhduong-test-vite.mjs')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'frontend.log') -RedirectStandardError (Join-Path $evidence 'frontend-error.log')}
 $frontend=$null
 for($i=0;$i -lt 100;$i++){
  if($front.HasExited){throw 'Frontend exited'}
  if(Test-Path (Join-Path $evidence 'frontend.json')){$candidate=Get-Content (Join-Path $evidence 'frontend.json') -Raw|ConvertFrom-Json;if($candidate.pid -eq $front.Id){$frontend=$candidate;break}}
  Start-Sleep -Milliseconds 300
 }
 if(!$frontend){throw 'Frontend address unavailable'}
 $uri=[Uri]$frontend.url;if(!$uri.IsLoopback -or $uri.Scheme -cne 'http'){throw 'Only loopback test frontend is permitted'}
 if((Invoke-WebRequest -Uri $uri.AbsoluteUri -NoProxy -TimeoutSec 15).StatusCode -ne 200){throw 'Frontend health verification failed'}
 $publicBase=$frontend.url.TrimEnd('/')
 # ApiHost captures CORS origins while composing services, before factory overrides run.
 $env:Frontend__AllowedOrigins__0=$publicBase
 $ws=$context.workspace.WorkspaceId
 $settings=@{
  QuoteSalesAcceptance=@{PublicBaseUrl=$publicBase;Workspaces=@{$ws=$proposal.proposedPolicy}}
  QuoteGmail=@{Workspaces=@{$ws=@{Enabled=$true}}}
  Frontend=@{AllowedOrigins=@($publicBase)}
 }
 if($proposal.documentPolicy.ownerApproved -eq $true){$settings.QuoteDocumentPolicies=@{Workspaces=@{$ws=@{Enabled=$true;Version=$proposal.documentPolicy.version}}}}
 $settingsPath=Join-Path $evidence 'approved-settings.json'
 $settings|ConvertTo-Json -Depth 12|Set-Content $settingsPath
 $exe=Join-Path $root 'backend/artifacts/quote-local-host/bin/UnicoreCRM.QuoteLocalTestHost.exe'
 $api=Start-Process $exe -WorkingDirectory (Join-Path $root 'backend/src/UnicoreCRM.ApiHost') -ArgumentList @($settingsPath,$evidence,[string]$port) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'api.log') -RedirectStandardError (Join-Path $evidence 'api-error.log')
 $actual=$null
 for($i=0;$i -lt 120;$i++){
  if($api.HasExited){throw 'API exited'}
  if(Test-Path (Join-Path $evidence 'host.json')){$candidate=Get-Content (Join-Path $evidence 'host.json') -Raw|ConvertFrom-Json;if($candidate.pid -eq $api.Id){$actual=$candidate;break}}
  Start-Sleep -Milliseconds 300
 }
 if(!$actual -or $actual.database -cne $context.database -or ([Uri]$actual.apiUrl).Port -ne $port){throw 'API identity/address verification failed'}
 $health=Invoke-WebRequest -Uri ($actual.apiUrl.TrimEnd('/')+'/auth/session') -SkipHttpErrorCheck -NoProxy -TimeoutSec 15
 if($health.StatusCode -ne 401){throw 'Unexpected API authentication health response'}
 @{frontendUrl=$publicBase;apiUrl=$actual.apiUrl;database=$context.database;workspaceId=$ws;frontendHealth=200;apiAnonymousHealth=401;frontendPid=$front.Id;apiPid=$api.Id;state='VERIFIED_LOOPBACK_TEST_HOSTS'}|ConvertTo-Json|Set-Content (Join-Path $evidence 'verified-hosts.json')
 Write-Output ('Verified local test frontend '+$publicBase+'; isolated database '+$context.database)
}catch{
 if($api -and !$api.HasExited){Stop-Process -Id $api.Id -Force}
 if(!$ApiOnly -and $front -and !$front.HasExited){Stop-Process -Id $front.Id -Force}
 Write-Output ('Local test startup failed: '+$_.Exception.GetType().Name+'; line '+$_.InvocationInfo.ScriptLineNumber);exit 1
}
