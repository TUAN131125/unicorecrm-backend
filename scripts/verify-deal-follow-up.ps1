param(
 [Parameter(Mandatory)][string] $ApiBaseUrl,
 [Parameter(Mandatory)][string] $DatabaseName,
 [Parameter(Mandatory)][string] $RunId,
 [Parameter(Mandatory)][string] $WorkspaceId,
 [Parameter(Mandatory)][string] $DealId,
 [Parameter(Mandatory)][string] $TaskId
)
$ErrorActionPreference = 'Stop'
if ($RunId -notmatch '^[a-f0-9]{32}$' -or $DatabaseName -cne "UniCoreCRM_ConnectedAcceptance_$RunId") { throw 'Task-owned database required.' }
foreach ($id in @($WorkspaceId,$DealId,$TaskId)) { if ($id -notmatch '^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$') { throw 'Invalid fixture identity.' } }
if ($ApiBaseUrl -notmatch '^http://127\.0\.0\.1:[0-9]+$' -or -not $env:UNICORE_ACCEPTANCE_TOKEN) { throw 'Local authorized ApiHost required.' }
$marker = & sqlcmd -S localhost -E -C -d $DatabaseName -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'UniCoreCRM_ConnectedAcceptance_RunId'"
if ($LASTEXITCODE -ne 0 -or ($marker -join '').Trim() -cne $RunId) { throw 'Database marker mismatch.' }
$headers = @{ Authorization = "Bearer $env:UNICORE_ACCEPTANCE_TOKEN"; 'X-Workspace-Id' = $WorkspaceId; 'X-Request-Id' = [Guid]::NewGuid().ToString('N'); 'X-Correlation-Id' = [Guid]::NewGuid().ToString('N') }
$task = Invoke-RestMethod -Uri "$ApiBaseUrl/tasks/$TaskId" -Headers $headers
if ($task.id -cne $TaskId -or $task.recordRef.moduleKey -cne 'deals' -or $task.recordRef.recordId -cne $DealId) { throw 'Canonical Task reference mismatch.' }
$json = & sqlcmd -S localhost -E -C -d $DatabaseName -b -y 0 -w 65535 -Q "SET NOCOUNT ON; SELECT TaskId,RecordModuleKey,RecordId,AssigneeId,DueAt,Status FROM tasks.Tasks WHERE WorkspaceId=N'$WorkspaceId' AND TaskId=N'$TaskId' FOR JSON PATH"
if ($LASTEXITCODE -ne 0) { throw 'SQL assertion failed.' }
$rows = ($json -join '') | ConvertFrom-Json
if (@($rows).Count -ne 1 -or $rows[0].RecordId -cne $DealId -or $rows[0].RecordModuleKey -cne 'deals' -or $rows[0].AssigneeId -cne $task.assigneeId) { throw 'Persisted Task mismatch.' }
@{ status='PASS'; execution='REAL_AUTHORIZED_HTTP_AND_SQL'; taskId=$TaskId; dealId=$DealId; assigneeId=$task.assigneeId; dueAt=$task.dueAt; taskStatus=$task.status } | ConvertTo-Json
