#requires -Version 7.0
[CmdletBinding()]
param([string]$Phase='entry')
$ErrorActionPreference='Stop'
if($Phase -notmatch '^[a-zA-Z0-9-]+$'){throw 'Invalid evidence phase'}
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Existing verified Development database required'
$server=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine,DB_NAME() DatabaseName").Rows[0]
Assert-Demo ($server.Machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL must be local'
$directory=Join-Path (Split-Path $repo) "task-runtime/quote-lifecycle/$Phase"
[void](New-Item -ItemType Directory -Force -Path $directory)
function Rows([string]$Sql){
    $data=Invoke-DemoSql $Sql
    return ,@(foreach($row in $data.Rows){$item=@{};foreach($column in $row.Table.Columns){$item[$column.ColumnName]=if($row[$column.ColumnName] -is [DBNull]){$null}else{$row[$column.ColumnName]}};$item})
}
$script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
Get-DemoCounts|ConvertTo-Json -Depth 12|Set-Content (Join-Path $directory 'crm-counts-hashes.json')
Rows 'SELECT WorkspaceId,Status,COUNT(*) Total FROM quotes.Quotes GROUP BY WorkspaceId,Status ORDER BY WorkspaceId,Status'|ConvertTo-Json -Depth 8|Set-Content (Join-Path $directory 'quote-counts.json')
Rows 'SELECT WorkspaceId,QuoteId,QuoteNumber,QuoteRevision,RootQuoteId,RevisionOfQuoteId,Status,ResourceVersion,SourceDealId,ApprovalStatus,ApprovalPolicyVersion,ApprovalContentFingerprint,ValidUntil,ArchivedAt FROM quotes.Quotes ORDER BY WorkspaceId,QuoteId'|ConvertTo-Json -Depth 12|Set-Content (Join-Path $directory 'quotes.json')
Rows 'SELECT WorkspaceId,DealId,PipelineId,StageCode,StageCategory,Version,WinEvidenceType,WinEvidenceSourceId FROM deals.Deals ORDER BY WorkspaceId,DealId'|ConvertTo-Json -Depth 12|Set-Content (Join-Path $directory 'deals.json')
$hashes=@(foreach($spec in @(@{name='Quotes';table='quotes.Quotes';order='WorkspaceId,QuoteId'},@{name='Pipelines';table='deals.Pipelines';order='WorkspaceId,PipelineId'},@{name='PipelineStages';table='deals.PipelineStages';order='WorkspaceId,PipelineId,StageId'})){
    $value=(Invoke-DemoSql "SELECT COUNT(*) Total,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM $($spec.table) ORDER BY $($spec.order) FOR JSON PATH)),2) RowHash FROM $($spec.table)").Rows[0]
    @{table=$spec.name;count=[int]$value.Total;hash=[string]$value.RowHash}
})
$hashes|ConvertTo-Json -Depth 8|Set-Content (Join-Path $directory 'quote-pipeline-hashes.json')
$counts=@(foreach($table in 'quotes.CommandRecords','quotes.OutboxMessages','quotes.ReadAuditRecords','deals.CommandRecords','deals.OutboxMessages'){
    if((Invoke-DemoSql 'SELECT OBJECT_ID(@name) Id' @{'@name'=$table}).Rows[0].Id -is [DBNull]){continue}
    @{table=$table;rows=(Rows "SELECT WorkspaceId,COUNT(*) Total FROM $table GROUP BY WorkspaceId ORDER BY WorkspaceId")}
})
$counts|ConvertTo-Json -Depth 12|Set-Content (Join-Path $directory 'evidence-counts.json')
$histories=(Invoke-DemoSql "SELECT s.name SchemaName FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.name='__EFMigrationsHistory' ORDER BY s.name").Rows
$migrations=@(foreach($row in $histories){
    $schema=[string]$row.SchemaName
    if($schema -notmatch '^[a-zA-Z0-9_]+$'){throw 'Unexpected migration schema'}
    @{schema=$schema;migrations=(Rows "SELECT MigrationId,ProductVersion FROM [$schema].[__EFMigrationsHistory] ORDER BY MigrationId")}
})
$migrations|ConvertTo-Json -Depth 12|Set-Content (Join-Path $directory 'applied-migrations.json')
@{machine=[string]$server.Machine;database=[string]$server.DatabaseName;phase=$Phase;quoteCount=($hashes|Where-Object table -eq 'Quotes').count;readOnly=$true}|ConvertTo-Json|Set-Content (Join-Path $directory 'result.json')
Write-Host "PASS read-only SQL lifecycle baseline: $Phase; Quotes $(($hashes|Where-Object table -eq 'Quotes').count)"
