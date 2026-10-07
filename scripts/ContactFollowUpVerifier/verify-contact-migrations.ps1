[CmdletBinding()]
param([string]$SqlServer = '(localdb)\MSSQLLocalDB')
$ErrorActionPreference = 'Stop'
$database = 'UnicoreCRM_ContactMigrationVerifier_' + [Guid]::NewGuid().ToString('N')
$oldConnection = $env:ConnectionStrings__UnicoreCRM
$connection = "Server=$SqlServer;Database=$database;Trusted_Connection=True;TrustServerCertificate=True"
function Sql([string]$query) {
    $cn = [System.Data.SqlClient.SqlConnection]::new($connection)
    try { $cn.Open(); $command = $cn.CreateCommand(); $command.CommandText = $query; return $command.ExecuteScalar() }
    finally { $cn.Dispose() }
}
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw $name }; Write-Output "PASS $name"
}
function Migrate([string]$target, [string]$project = 'src/UnicoreCRM.Crm', [string]$context = 'ContactsDbContext') {
    & dotnet ef database update $target --project $project --context $context --no-build
    if ($LASTEXITCODE -ne 0) { throw "Migration failed: $target" }
}
Push-Location (Resolve-Path "$PSScriptRoot/../..")
try {
    $env:ConnectionStrings__UnicoreCRM = $connection
    Migrate '20260913134425_PersistContactExportAttemptCount'
    [void](Sql "INSERT INTO contacts.Contacts (ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile) VALUES (N'existing',N'ws_test',N'member',N'Existing Contact',N'active',7,'2026-10-06T00:00:00+00:00','2026-10-06T01:00:00+00:00',N'{}'); SELECT 1")
    Migrate '20261006144337_ContactListReadProjectionIndexes'
    Check ((Sql "SELECT COUNT(*) FROM contacts.Contacts WHERE ContactId=N'existing' AND FullName=N'Existing Contact' AND Version=7") -eq 1) 'current upgrade preserves existing Contact data'
    Check ((Sql "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'contacts.Contacts') AND name IN (N'IX_Contacts_WorkspaceId_UpdatedAt_ContactId',N'IX_Contacts_WorkspaceId_FullName_ContactId')") -eq 2) 'both Contact keyset indexes exist'
    Check ((Sql "SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'contacts.Contacts') AND i.name IN (N'IX_Contacts_WorkspaceId_UpdatedAt_ContactId',N'IX_Contacts_WorkspaceId_FullName_ContactId') AND (SELECT STRING_AGG(c.name,',') WITHIN GROUP (ORDER BY k.key_ordinal) FROM sys.index_columns k JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id WHERE k.object_id=i.object_id AND k.index_id=i.index_id AND k.key_ordinal>0) IN ('WorkspaceId,UpdatedAt,ContactId','WorkspaceId,FullName,ContactId')") -eq 2) 'index key order matches both list sorts'
    Check ((Sql "SELECT COUNT(*) FROM sys.objects WHERE schema_id=SCHEMA_ID(N'tasks')") -eq 0) 'Contacts migration creates no Tasks table or view'
    Migrate '20260913134425_PersistContactExportAttemptCount'
    Check ((Sql "SELECT COUNT(*) FROM contacts.Contacts WHERE ContactId=N'existing' AND Version=7") -eq 1) 'index rollback preserves existing Contact'
    Migrate '20261006144337_ContactListReadProjectionIndexes'
    Migrate '0'
    Migrate '20261006144337_ContactListReadProjectionIndexes'
    Check ((Sql 'SELECT COUNT(*) FROM contacts.Contacts') -eq 0) 'clean complete Contacts migration chain succeeds'
    Migrate '20261006150000_ContactFollowUpReadProjection' 'src/UnicoreCRM.Operations' 'TasksDbContext'
    Check ((Sql "SELECT COUNT(*) FROM sys.views WHERE object_id=OBJECT_ID(N'tasks.ContactFollowUpReadProjection')") -eq 1) 'Tasks migration independently owns the projection view'
    & dotnet ef migrations has-pending-model-changes --project src/UnicoreCRM.Crm --context ContactsDbContext --no-build
    Check ($LASTEXITCODE -eq 0) 'Contacts snapshot matches current mapped view and indexes'
}
finally {
    # Drop only the literal GUID-named disposable database allocated by this script.
    $master = [System.Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=master;Trusted_Connection=True;TrustServerCertificate=True")
    try {
        $master.Open(); $cmd = $master.CreateCommand()
        $cmd.CommandText = "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END"
        [void]$cmd.ExecuteNonQuery()
    } finally { $master.Dispose(); $env:ConnectionStrings__UnicoreCRM = $oldConnection; Pop-Location }
}
