#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-lifecycle-b4/api-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-approval-synthetic.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Verification helper parse failure'}
$helpers=@('SetPolicy','Draft','RequestApproval','EnvValue','OverrideArray','DisableBootstraps','StartHost','LoginAll','Api','Check','Snapshot','SQLRows')
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($definition.Name -in $helpers){. ([ScriptBlock]::Create($definition.Extent.Text))}}
function OverrideArray([string]$Prefix,[string[]]$Values){
 if(!$Values.Count){throw 'Empty override array is not admitted'}
 $key=$Prefix.Replace('__',':')
 $indices=@($configuration.Keys|Where-Object{$_ -match ('^'+[regex]::Escape($key)+':\d+$')}|ForEach-Object{[int]($_.Split(':')[-1])})
 $indices+=@([Environment]::GetEnvironmentVariables().Keys|Where-Object{$_ -match ('^'+[regex]::Escape($Prefix)+'__\d+$')}|ForEach-Object{[int](($_ -split '__')[-1])})
 $maximum=[Math]::Max($Values.Count-1, $(if($indices.Count){($indices|Measure-Object -Maximum).Maximum}else{0}))
 for($i=0;$i -le $maximum;$i++){EnvValue ($Prefix+'__'+$i) $Values[$i % $Values.Count]}
}
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
$fixture=Get-Content (Join-Path (Split-Path $repo) 'task-runtime/quote-lifecycle-b4/api-32c88969d33c4371878a3633c9ff3edd/result.json') -Raw|ConvertFrom-Json -AsHashtable
$script:workspace=$fixture.workspaceId;$script:buyer=$fixture.buyerId
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ("QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"+$item.Key) $item.Value}
 StartHost;LoginAll
 $created=Api $emails[0] POST '/quotes' (Draft "B4 missing retention proof $run" '1000') "b4-gap-create-$run";Check $created 201
 $quote=$created.body.result.quote
 $key="b4-gap-archive-$run"
 $response=Api $emails[0] POST "/quotes/$($quote.id)/archive" @{reason='Synthetic missing-owner-schema proof'} $key $quote.resourceVersion;Check $response 409 'QUOTE_ARCHIVE_BLOCKED' 'BLOCKED_CONTRACT'
 $sql=SQLRows "SELECT q.ArchivedAt,q.ResourceVersion,(SELECT COUNT(*) FROM quotes.CommandRecords c WHERE c.WorkspaceId=q.WorkspaceId AND c.QuoteId=q.QuoteId AND c.Operation='archiveQuoteCommand') Receipts,(SELECT COUNT(*) FROM quotes.OutboxMessages o WHERE o.WorkspaceId=q.WorkspaceId AND o.QuoteId=q.QuoteId AND o.EventType='QUOTE_ARCHIVED') Events FROM quotes.Quotes q WHERE q.WorkspaceId=@ws AND q.QuoteId=@q" @{'@ws'=$script:workspace;'@q'=$quote.id}
 Assert-Demo ($null -eq $sql[0].ArchivedAt -and $sql[0].ResourceVersion -eq 1 -and $sql[0].Receipts -eq 0 -and $sql[0].Events -eq 0) 'B4_GAP_SQL' 'Missing retention proof must leave no archive mutation, receipt or event'
 $sql|ConvertTo-Json -Depth 6|Set-Content (Join-Path $script:stateDirectory 'missing-contract-sql.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($entryKey in $before[$table].Keys){if($before[$table][$entryKey] -cne $after[$table][$entryKey]){@{table=$table;key=$entryKey}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($entryKey in $original.Keys){[Environment]::SetEnvironmentVariable($entryKey,$original[$entryKey])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;quoteId=$quote.id;archiveKey=$key;requests=$cases.Count;missingProof='Payments owner QuoteSourceId schema not deployed yet; no substitute inference from empty tables'}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B4 missing archive proof API/SQL. $script:stateDirectory"
}

