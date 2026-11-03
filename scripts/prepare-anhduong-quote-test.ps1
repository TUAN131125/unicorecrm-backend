#requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^UnicoreCRM_QuoteGmailTest_[a-f0-9]{32}$')][string]$DatabaseName=('UnicoreCRM_QuoteGmailTest_'+[guid]::NewGuid().ToString('N')))
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$evidence=Join-Path $root 'task-runtime/shared-smtp-quote/anhduong'
[void](New-Item -ItemType Directory -Force $evidence)
$sourceConnection=$null
foreach($f in @((Join-Path $root 'backend/src/UnicoreCRM.ApiHost/appsettings.json'),(Join-Path $root 'backend/src/UnicoreCRM.ApiHost/appsettings.Development.json'),(Join-Path $root 'backend/src/UnicoreCRM.ApiHost/appsettings.Development.Local.json'),(Join-Path $env:APPDATA 'Microsoft/UserSecrets/unicorecrm-apihost-development/secrets.json'))){
 if(Test-Path -LiteralPath $f){$cfg=Get-Content -LiteralPath $f -Raw|ConvertFrom-Json -AsHashtable;if($cfg.ConnectionStrings.UnicoreCRM){$sourceConnection=$cfg.ConnectionStrings.UnicoreCRM};if($cfg['ConnectionStrings:UnicoreCRM']){$sourceConnection=$cfg['ConnectionStrings:UnicoreCRM']}}
}
if($env:ConnectionStrings__UnicoreCRM){$sourceConnection=$env:ConnectionStrings__UnicoreCRM}
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($sourceConnection)
if($builder['Initial Catalog'] -cne 'UnicoreCRM_Development'){throw 'Only the explicitly authorized Development source may be copied by this script.'}
$sourceName=$builder['Initial Catalog'];$builder['Initial Catalog']='master'
$master=[System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
function Query([string]$Sql,[hashtable]$Parameters=@{}){
 $command=$master.CreateCommand();$command.CommandText=$Sql;$command.CommandTimeout=300
 foreach($entry in $Parameters.GetEnumerator()){[void]$command.Parameters.AddWithValue($entry.Key,$entry.Value)}
 $reader=$command.ExecuteReader();try{$rows=@();while($reader.Read()){$row=@{};for($i=0;$i -lt $reader.FieldCount;$i++){$row[$reader.GetName($i)]=$reader.GetValue($i)};$rows+=[pscustomobject]$row};return $rows}finally{$reader.Dispose();$command.Dispose()}
}
function SourceQuote{
 $rows=@(Query "SELECT QuoteId,Status,ResourceVersion,GrandTotalAmount,CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),LineItemsJson+COALESCE(PaymentAgreementJson,''))),2) CommercialHash FROM [UnicoreCRM_Development].quotes.Quotes WHERE WorkspaceId=@workspace AND QuoteId=@quote" @{'@workspace'='ws_f2332a12d56a421d8a722bd2f65c5efb';'@quote'='quote_ed19adbfe15842eaa2adfbe2e6c281ef'})
 if($rows.Count -ne 1){throw 'The authorized reference Quote was not found.'};return $rows[0]
}
try{
 $stage='master-connection'
 $master.Open()
 if((Query 'SELECT COUNT(*) C FROM sys.databases WHERE name=@name' @{'@name'=$DatabaseName})[0].C -ne 0){throw 'Refusing to overwrite or reset an existing database.'}
 $stage='source-readback';$before=SourceQuote
 $directories=(Query "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath')) BackupPath,CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath')) DataPath,CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath')) LogPath")[0]
 foreach($key in @('BackupPath','DataPath','LogPath')){if([string]::IsNullOrWhiteSpace($directories.$key)){throw 'SQL Server default directories are unavailable.'}}
 $backup=Join-Path $directories.BackupPath ($DatabaseName+'.bak')
 if(Test-Path -LiteralPath $backup){throw 'Refusing to overwrite an existing backup.'}
 # COPY_ONLY reads the source without resetting, migrating or reseeding its business data.
 $stage='copy-only-backup';[void](Query 'BACKUP DATABASE [UnicoreCRM_Development] TO DISK=@backup WITH COPY_ONLY,CHECKSUM' @{'@backup'=$backup})
 $files=@(Query 'RESTORE FILELISTONLY FROM DISK=@backup' @{'@backup'=$backup})
 $moves=@();$index=0
 foreach($file in $files){
  $directory=if($file.Type -eq 'L'){$directories.LogPath}else{$directories.DataPath}
  $extension=if($file.Type -eq 'L'){'.ldf'}elseif($index -eq 0){'.mdf'}else{'.ndf'}
  $target=[IO.Path]::GetFullPath((Join-Path $directory ($DatabaseName+'_'+$index+$extension)))
  $allowedRoot=[IO.Path]::GetFullPath($directory).TrimEnd('\')+'\'
  if(!$target.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $target)){throw 'Unsafe or existing restore file target.'}
  $moves+="MOVE N'"+$file.LogicalName.Replace("'","''")+"' TO N'"+$target.Replace("'","''")+"'";$index++
 }
 if(!$moves.Count){throw 'Backup contains no database files.'}
 $stage='restore-fresh-target';[void](Query ("RESTORE DATABASE [$DatabaseName] FROM DISK=@backup WITH "+($moves -join ',')+',RECOVERY,CHECKSUM') @{'@backup'=$backup})
 $after=SourceQuote
 if(($before|ConvertTo-Json -Compress) -cne ($after|ConvertTo-Json -Compress)){throw 'The source Quote changed during the copy. Review before continuing.'}
 $workspace=(Query "SELECT WorkspaceId,Name FROM [$DatabaseName].workspace.Workspaces WHERE WorkspaceId=@workspace" @{'@workspace'='ws_f2332a12d56a421d8a722bd2f65c5efb'})[0]
 if(!$workspace){throw 'The copied Workspace was not found.'}
 @{database=$DatabaseName;server=$builder.DataSource;workspace=$workspace;sourceQuoteBefore=$before;sourceQuoteAfter=$after;backup=$backup;developmentWrites=0;sourceMigrationsRun=0;state='ISOLATED_COPY_PREPARED'}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $evidence 'database-context.json')
 Write-Output ('Prepared isolated database '+$DatabaseName+'; Development Quote unchanged.')
}catch{Write-Output ('Isolated copy preparation failed at '+$stage+' for '+$DatabaseName+': '+$_.Exception.GetType().Name+'; line '+$_.InvocationInfo.ScriptLineNumber);exit 1}finally{$master.Dispose()}
