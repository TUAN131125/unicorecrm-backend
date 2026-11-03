#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot;$root=Split-Path $repo
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$evidence=Join-Path $root 'task-runtime/quote-detail-unified/development-preservation'
[void](New-Item -ItemType Directory -Path $evidence -Force)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$configuration=Read-DemoConfiguration;$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$target=[Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($target.DataSource -ceq 'DESKTOP-ICH225R' -and $target.InitialCatalog -ceq 'UnicoreCRM_Development') 'TARGET_CHANGED' 'Only the inspected local target is authorized'
function Rows([string]$Query,[hashtable]$Parameters=@{}){
 $table=Invoke-DemoSql $Query $Parameters
 return @(foreach($row in $table.Rows){$value=@{};foreach($col in $table.Columns){$value[$col.ColumnName]=$(if($row[$col.ColumnName] -is [DBNull]){$null}else{$row[$col.ColumnName]})};$value})
}
$actual=@(Rows "SELECT @@SERVERNAME ServerName,DB_NAME() DatabaseName,CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) MachineName")[0]
Assert-Demo ($actual.DatabaseName -ceq $target.InitialCatalog -and $actual.MachineName -ieq $env:COMPUTERNAME) 'TARGET_CHANGED' 'Effective SQL target must be this machine and inspected catalog'
$owners=@(
 @{schema='quotes';context='QuotesDbContext';project='src/UnicoreCRM.Sales';folder='src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations'},
 @{schema='orders';context='OrdersDbContext';project='src/UnicoreCRM.Sales';folder='src/UnicoreCRM.Sales/Orders/Infrastructure/Persistence/Migrations'},
 @{schema='payments';context='PaymentsDbContext';project='src/UnicoreCRM.Billing';folder='src/UnicoreCRM.Billing/Payments/Infrastructure/Persistence/Migrations'},
 @{schema='workflow';context='WorkflowsDbContext';project='src/UnicoreCRM.Workflows';folder='src/UnicoreCRM.Workflows/Atomic/Infrastructure/Persistence/Migrations'}
)
function Matrix{
 return ,@(foreach($owner in $owners){
  $source=@(Get-ChildItem (Join-Path $repo $owner.folder) -Filter '*.cs'|Where-Object Name -Match '^\d{14}_(?!.*\.Designer)'|ForEach-Object BaseName|Sort-Object)
  $applied=@(Rows "SELECT MigrationId FROM $($owner.schema).__EFMigrationsHistory ORDER BY MigrationId"|ForEach-Object {$_['MigrationId']})
  $pending=@($source|Where-Object {$_ -notin $applied})
  @{context=$owner.context;schema=$owner.schema;latestSource=$source[-1];latestApplied=$applied[-1];pending=$pending;result=$(if($pending.Count){'PENDING'}else{'ALIGNED'})}
 })
}
function Identifier([string]$Value){return '['+$Value.Replace(']',']]')+']'}
function Snapshot([array]$Inventory){
 return ,@(foreach($table in $Inventory){
  $name=(Identifier $table.schema)+'.'+(Identifier $table.name)
  $cols=(@($table.columns|ForEach-Object {Identifier $_}) -join ',')
  $order=(@($table.primaryKey|ForEach-Object {Identifier $_}) -join ',')
  Assert-Demo (![string]::IsNullOrEmpty($order)) 'NO_STABLE_KEY' "Stable primary key required for preservation: $name"
  $r=@(Rows "SELECT COUNT_BIG(*) RecordCount,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $cols FROM $name ORDER BY $order FOR JSON PATH,INCLUDE_NULL_VALUES)),2) DataHash FROM $name")[0]
  @{schema=$table.schema;table=$table.name;count=$r.RecordCount;sha256=$r.DataHash}
 })
}

# The copied P0 baseline predates this task. Preserve its strict failure report;
# establish this task's claim from its own captured Quote versions and owner journals.
$started=(Get-Item (Join-Path $root 'task-runtime/quote-detail-unified/entry-hashes.json')).CreationTimeUtc
$now=[DateTimeOffset]::UtcNow
$initial=Get-Content (Join-Path $root 'task-runtime/quote-detail-unified/before/initial-quotes.json') -Raw|ConvertFrom-Json
$versions=@(foreach($q in $initial.response.items){
 $current=@(Rows 'SELECT ResourceVersion,Status FROM quotes.Quotes WHERE QuoteId=@id' @{'@id'=$q.id})[0]
 @{quoteId=$q.id;beforeVersion=$q.resourceVersion;afterVersion=$current.ResourceVersion;beforeStatus=$q.status;afterStatus=$current.Status;passed=($q.resourceVersion -eq $current.ResourceVersion -and $q.status -ceq $current.Status)}
})
$window=@{'@started'=$started;'@now'=$now}
$journal=@(Rows "SELECT 'QuoteCommands' Owner,COUNT_BIG(*) Changes FROM quotes.CommandRecords WHERE OccurredAt>=@started AND OccurredAt<=@now UNION ALL SELECT 'QuoteOutbox',COUNT_BIG(*) FROM quotes.OutboxMessages WHERE OccurredAt>=@started AND OccurredAt<=@now UNION ALL SELECT 'QuoteUpdated',COUNT_BIG(*) FROM quotes.Quotes WHERE UpdatedAt>=@started AND UpdatedAt<=@now UNION ALL SELECT 'Orders',COUNT_BIG(*) FROM orders.Orders" $window)
$counts=@(Rows 'SELECT COUNT_BIG(*) QuoteCount FROM quotes.Quotes')[0]
$priorBaseline=Get-Content (Join-Path $evidence 'business-before.json') -Raw|ConvertFrom-Json
$cutoff=(Get-Item (Join-Path $evidence 'business-before.json')).LastWriteTimeUtc
$preTask=@(Rows 'SELECT Operation,QuoteId,ActorId,OccurredAt,PriorVersion,ResourceVersion FROM quotes.CommandRecords WHERE OccurredAt>@cutoff AND OccurredAt<@started ORDER BY OccurredAt' @{'@cutoff'=$cutoff;'@started'=$started})
$matrix=Matrix
$result=@{startedUtc=$started;checkedAtUtc=$now;server=$actual.ServerName;database=$actual.DatabaseName;beforeQuoteVersions=$versions;quoteCount=$counts.QuoteCount;ownerJournalDuringTask=$journal;changesBetweenPriorP0BaselineAndTask=$preTask;migrations=$matrix;strictPriorBaseline='FAIL_STALE_CROSS_TASK_BASELINE';scope='V4 before-HTTP resource versions plus immutable owner journals; no schema/data mutations executed on Development by this task'}
$result.passed=(@($versions|Where-Object {-not $_.passed}).Count -eq 0 -and @($journal|Where-Object {$_.Changes -ne 0}).Count -eq 0 -and $counts.QuoteCount -eq ($priorBaseline|Where-Object {$_.schema -eq 'quotes' -and $_.table -eq 'Quotes'}).count -and @($matrix|Where-Object {$_.pending.Count -gt 0}).Count -eq 0)
$result|ConvertTo-Json -Depth 16|Set-Content (Join-Path $evidence 'task-journal-preservation.json')
Assert-Demo $result.passed 'TASK_PRESERVATION_FAILED' 'Captured Quote versions and current-task owner journals must remain unchanged'
Write-Output 'PASS: entry Quote versions preserved; zero Quote commands/outbox/updates and zero Orders during this task. Prior cross-task baseline differences remain disclosed.'
