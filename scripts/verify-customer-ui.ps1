#requires -Version 7.0
# SELECT-only evidence for the previously authorized local Development fixture.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$config=Read-DemoConfiguration
$script:connectionString=$config['ConnectionStrings:UnicoreCRM']
$identity=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
Assert-Demo ($identity.DataSource -ceq 'DESKTOP-ICH225R' -and $identity.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Only the previously authorized Development fixture is admitted'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) MachineName").Rows[0].MachineName
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL Server must be local'
$workspace='ws_f2332a12d56a421d8a722bd2f65c5efb'
$table=Invoke-DemoSql @'
SELECT c.CustomerId,c.CustomerCode,c.RelationshipType,c.RelationshipId,
 COALESCE(p.FullName,o.DisplayName) SubjectName
FROM customers.Customers c
LEFT JOIN contacts.Contacts p ON c.RelationshipType='CONTACT' AND p.WorkspaceId=c.WorkspaceId AND p.ContactId=c.RelationshipId
LEFT JOIN organizations.Organizations o ON c.RelationshipType='ORGANIZATION_ACCOUNT' AND o.WorkspaceId=c.WorkspaceId AND o.OrganizationId=c.RelationshipId
WHERE c.WorkspaceId=@workspace ORDER BY c.CreatedAt DESC,c.CustomerId
'@ @{'@workspace'=$workspace}
$rows=@(foreach($row in $table.Rows){@{customerId=[string]$row.CustomerId;code=[string]$row.CustomerCode;subjectType=[string]$row.RelationshipType;subjectId=[string]$row.RelationshipId;subjectName=[string]$row.SubjectName}})
Assert-Demo ($rows.Count -eq 70) 'UNEXPECTED_FIXTURE' 'Expected existing 70 Customers'
Assert-Demo (@($rows|Where-Object{[string]::IsNullOrWhiteSpace($_.subjectName)}).Count -eq 0) 'ORPHAN_SUBJECT' 'Every Customer must have a same-workspace subject'
$directory=Join-Path $repo 'artifacts/customer-ui-tests'
[void](New-Item -ItemType Directory -Path $directory -Force)
@{status='PASS';server=$identity.DataSource;database=$identity.InitialCatalog;workspaceId=$workspace;count=$rows.Count;rows=$rows;verifiedAt=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $directory 'sql-identity.json')
Write-Host 'PASS SELECT-only SQL identity verification: 70 Customers, no missing same-workspace subjects'
