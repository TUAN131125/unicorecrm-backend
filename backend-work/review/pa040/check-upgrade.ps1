$ErrorActionPreference = 'Stop'
$connectionString = 'Server=(localdb)\MSSQLLocalDB;Database=UnicoreCRM_Proactive_Verifier_PA040_20260926;Trusted_Connection=True;TrustServerCertificate=True'
function Snapshot {
    $connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = "SELECT (SELECT * FROM platform_ai.ProactiveItems ORDER BY ItemId FOR JSON PATH) + (SELECT * FROM platform_ai.ProactiveAudits ORDER BY AuditId FOR JSON PATH) + (SELECT * FROM platform_ai.AiExecutions ORDER BY ExecutionId FOR JSON PATH) + (SELECT * FROM platform_ai.AiProviderAttempts ORDER BY AttemptId FOR JSON PATH) + (SELECT * FROM tasks.Tasks ORDER BY TaskId FOR JSON PATH) + (SELECT * FROM tasks.IdempotencyRecords ORDER BY IdempotencyKey FOR JSON PATH) + (SELECT * FROM tasks.AuditRecords ORDER BY AuditId FOR JSON PATH) + (SELECT * FROM tasks.OutboxMessages ORDER BY EventId FOR JSON PATH)"
        $data = [Text.Encoding]::UTF8.GetBytes([string]$command.ExecuteScalar())
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data))
    } finally { $connection.Dispose() }
}
$before = Snapshot
& dotnet ef database update --project src/UnicoreCRM.PlatformOperations --context AiExecutionDbContext --no-build --connection $connectionString
if ($LASTEXITCODE -ne 0) { throw 'Migration update failed.' }
& dotnet ef database update --project src/UnicoreCRM.Operations --context TasksDbContext --no-build --connection $connectionString
if ($LASTEXITCODE -ne 0) { throw 'Tasks migration update failed.' }
$after = Snapshot
if ($before -ne $after) { throw 'No-op upgrade changed accepted data.' }
Write-Output "BASELINE_MIGRATION_NOOP_PASS before=$before after=$after"


