#requires -Version 7.0
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Existing exact local Development database required'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine").Rows[0].Machine
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL must be local'
$before=[Environment]::GetEnvironmentVariable('ConnectionStrings__UnicoreCRM')
try{
 [Environment]::SetEnvironmentVariable('ConnectionStrings__UnicoreCRM',$script:connectionString)
 Push-Location $repo
 try{
  dotnet ef database update --context QuotesDbContext --project src/UnicoreCRM.Sales --startup-project src/UnicoreCRM.Sales
  if($LASTEXITCODE -ne 0){throw 'Quote manual source admission migration failed'}
 }finally{Pop-Location}
}finally{[Environment]::SetEnvironmentVariable('ConnectionStrings__UnicoreCRM',$before)}
