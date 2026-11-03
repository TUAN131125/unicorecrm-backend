#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Audit','Backup','Apply','Verify')][string]$Phase='Audit')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot;$root=Split-Path $repo
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$evidence=Join-Path $root 'task-runtime/quote-order-loading-p0'
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
if($Phase -eq 'Audit'){
 $matrix=Matrix;$matrix|ConvertTo-Json -Depth 12|Set-Content (Join-Path $evidence 'migration-matrix-before.json');$matrix|ForEach-Object {[pscustomobject]@{Context=$_.context;LatestSource=$_.latestSource;LatestApplied=$_.latestApplied;Pending=$_.pending -join ',';Result=$_.result}}|Format-Table
 return
}
if($Phase -eq 'Backup'){
 $tableNames=Rows @'
SELECT s.name SchemaName,t.name TableName FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name IN ('workspace','leads','contacts','organizations','customers','deals','products','quotes','orders','payments','invoices','tasks','support','workflow','commercial_evidence')
AND t.name<>'__EFMigrationsHistory' AND t.name NOT LIKE '%ReadAudit%' AND t.name NOT LIKE '%AccessAudit%'
ORDER BY s.name,t.name
'@
 $inventory=@(foreach($table in $tableNames){
  $parameters=@{'@schema'=$table.SchemaName;'@table'=$table.TableName}
  $columns=@(Rows 'SELECT c.name Name FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@schema AND t.name=@table AND c.is_computed=0 ORDER BY c.column_id' $parameters|ForEach-Object {$_['Name']})
  $keys=@(Rows 'SELECT c.name Name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE i.is_primary_key=1 AND s.name=@schema AND t.name=@table ORDER BY ic.key_ordinal' $parameters|ForEach-Object {$_['Name']})
  @{schema=$table.SchemaName;name=$table.TableName;columns=$columns;primaryKey=$keys}
 })
 $inventory|ConvertTo-Json -Depth 15|Set-Content (Join-Path $evidence 'original-column-inventory.json')
 Snapshot $inventory|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidence 'business-before.json')
 $backupDirectory=@(Rows "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(1000)) Path")[0].Path
 Assert-Demo (![string]::IsNullOrWhiteSpace($backupDirectory)) 'BACKUP_PATH_MISSING' 'SQL Server default backup directory required'
 $backupPath=Join-Path $backupDirectory ('UnicoreCRM_Development_quote_order_loading_'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss')+'_'+[guid]::NewGuid().ToString('N').Substring(0,8)+'.bak')
 $connection=[Data.SqlClient.SqlConnection]::new($script:connectionString);$connection.Open()
 try{
  $command=$connection.CreateCommand();$command.CommandTimeout=600
  $command.CommandText='BACKUP DATABASE [UnicoreCRM_Development] TO DISK=@path WITH COPY_ONLY,CHECKSUM;';[void]$command.Parameters.AddWithValue('@path',$backupPath);[void]$command.ExecuteNonQuery()
  $command.CommandText='RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM;';[void]$command.ExecuteNonQuery()
 }finally{$connection.Dispose()}
 @{server=$actual.ServerName;database=$actual.DatabaseName;path=$backupPath;copyOnly=$true;checksum=$true;restoreVerifyOnly='PASS';verifiedAt=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $evidence 'backup.json')
 Write-Output 'Native COPY_ONLY backup with CHECKSUM and RESTORE VERIFYONLY: PASS'
 return
}
if($Phase -eq 'Apply'){
 $backup=Get-Content (Join-Path $evidence 'backup.json') -Raw|ConvertFrom-Json
 Assert-Demo ($backup.database -ceq $actual.DatabaseName -and $backup.server -ceq $actual.ServerName -and $backup.restoreVerifyOnly -ceq 'PASS') 'BACKUP_REQUIRED' 'Verified backup must match this exact target'
 $matrix=Matrix;$pending=@($matrix|ForEach-Object { @($_.pending) })
 $expected=@('20261102014225_QuoteAutomaticConfirmationIntent','20261102014228_OrderPolicyConfirmationOrigin')
 Assert-Demo ($pending.Count -eq 2 -and @($pending|Where-Object {$_ -notin $expected}).Count -eq 0) 'MIGRATIONS_CHANGED' 'Only the two reviewed additive migrations may be pending'
 foreach($file in @('quotes-pending.sql','orders-pending.sql')){Assert-Demo ((Get-Content (Join-Path $evidence $file) -Raw) -notmatch '\b(DROP|TRUNCATE|DELETE|UPDATE|MERGE)\b') 'DESTRUCTIVE_SQL' 'Reviewed forward SQL must remain additive'}
 $inventory=Get-Content (Join-Path $evidence 'original-column-inventory.json') -Raw|ConvertFrom-Json
 $before=Get-Content (Join-Path $evidence 'business-before.json') -Raw|ConvertFrom-Json
 $current=Snapshot $inventory
 foreach($row in $before){$now=@($current|Where-Object {$_.schema -eq $row.schema -and $_.table -eq $row.table})[0];Assert-Demo ($now.count -eq $row.count -and $now.sha256 -ceq $row.sha256) 'BASELINE_CHANGED' 'Business data changed after backup; stop and inspect before migrating'}
 $old=[Environment]::GetEnvironmentVariable('ConnectionStrings__UnicoreCRM')
 try{
  [Environment]::SetEnvironmentVariable('ConnectionStrings__UnicoreCRM',$script:connectionString)
  Push-Location $repo
  try{foreach($owner in $owners|Where-Object schema -in @('quotes','orders')){
   & dotnet ef database update $expected[$(if($owner.schema -eq 'quotes'){0}else{1})] --context $owner.context --project $owner.project --no-build *> (Join-Path $evidence ($owner.schema+'-migration-apply.log'))
   if($LASTEXITCODE -ne 0){throw "EF migration failed for $($owner.context)"}
  }}finally{Pop-Location}
 }finally{[Environment]::SetEnvironmentVariable('ConnectionStrings__UnicoreCRM',$old)}
}
if($Phase -in @('Apply','Verify')){
 $inventory=Get-Content (Join-Path $evidence 'original-column-inventory.json') -Raw|ConvertFrom-Json
 $before=Get-Content (Join-Path $evidence 'business-before.json') -Raw|ConvertFrom-Json
 $after=Snapshot $inventory;$after|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidence 'business-after.json')
 $auditCutoff=[DateTimeOffset](Get-Item (Join-Path $evidence 'business-before.json')).LastWriteTimeUtc
 $readAuditOperations=@{'deals.AuditRecords'=@('listDeals');'workspace.AccessRecords'=@('getWorkspaceBootstrap','listMyWorkspaces')}
 $comparison=@(foreach($row in $before){
  $now=@($after|Where-Object {$_.schema -eq $row.schema -and $_.table -eq $row.table})[0]
  $name=$row.schema+'.'+$row.table;$same=($now.count -eq $row.count -and $now.sha256 -ceq $row.sha256)
  $record=@{table=$name;beforeCount=$row.count;afterCount=$now.count;hashUnchanged=($now.sha256 -ceq $row.sha256);passed=$same;verification='FULL_TABLE_UNCHANGED'}
  if(!$same -and $readAuditOperations.ContainsKey($name) -and $now.count -ge $row.count){
   $definition=@($inventory|Where-Object {$_.schema -eq $row.schema -and $_.name -eq $row.table})[0]
   $sqlName=(Identifier $row.schema)+'.'+(Identifier $row.table)
   $cols=(@($definition.columns|ForEach-Object {Identifier $_}) -join ',');$order=(@($definition.primaryKey|ForEach-Object {Identifier $_}) -join ',')
   $old=@(Rows "SELECT COUNT_BIG(*) RecordCount,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $cols FROM $sqlName WHERE OccurredAt<=@cutoff ORDER BY $order FOR JSON PATH,INCLUDE_NULL_VALUES)),2) DataHash FROM $sqlName WHERE OccurredAt<=@cutoff" @{'@cutoff'=$auditCutoff})[0]
   $newRows=@(Rows "SELECT Operation,Outcome,COUNT_BIG(*) RecordCount FROM $sqlName WHERE OccurredAt>@cutoff GROUP BY Operation,Outcome" @{'@cutoff'=$auditCutoff})
   $permitted=@($newRows|Where-Object {$_.Outcome -cne 'READ' -or $_.Operation -cnotin $readAuditOperations[$name]}).Count -eq 0
   $record.passed=($old.RecordCount -eq $row.count -and $old.DataHash -ceq $row.sha256 -and $permitted)
   $record.verification='ORIGINAL_ROWS_HASH_IDENTICAL_PLUS_DECLARED_READ_AUDIT_APPENDS'
   $record.originalCountAfter=$old.RecordCount;$record.originalHashUnchanged=($old.DataHash -ceq $row.sha256);$record.appendedReadRows=$now.count-$row.count;$record.newReadOperations=$newRows
  }
  $record
 })
 $comparison|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidence 'preservation.json')
 Assert-Demo (@($comparison|Where-Object {-not $_.passed}).Count -eq 0) 'PRESERVATION_FAILED' 'Original business columns/counts must match; no automatic data repair is allowed'
 $matrix=Matrix;$matrix|ConvertTo-Json -Depth 12|Set-Content (Join-Path $evidence 'migration-matrix-after.json')
 Assert-Demo (@($matrix|Where-Object {$_.pending.Count -gt 0}).Count -eq 0) 'MIGRATIONS_PENDING' 'All four related owners must be aligned'
 Write-Output "Preservation PASS: $($comparison.Count) original business tables; four owner migration chains aligned."
}
