using Microsoft.EntityFrameworkCore;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;

internal sealed class AiExecutionRow
{
    public string ExecutionId { get; set; } = ""; public string WorkspaceId { get; set; } = ""; public string MemberId { get; set; } = "";
    public string Operation { get; set; } = ""; public string Provider { get; set; } = ""; public string Model { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } public DateTimeOffset CompletedAt { get; set; } public string Status { get; set; } = "";
    public long DurationMilliseconds { get; set; } public string ContextTypesJson { get; set; } = "[]"; public string EvidenceIdentifiersJson { get; set; } = "[]";
    public int? InputTokens { get; set; } public int? OutputTokens { get; set; } public string? ProviderRequestId { get; set; } public string? ErrorCode { get; set; }
}
internal sealed class AiProviderAttemptRow
{
    public string AttemptId { get; set; } = ""; public string ExecutionId { get; set; } = ""; public string WorkspaceId { get; set; } = "";
    public int AttemptNumber { get; set; } public string AttemptKind { get; set; } = ""; public string Provider { get; set; } = ""; public string Model { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } public DateTimeOffset CompletedAt { get; set; } public long DurationMilliseconds { get; set; }
    public string Status { get; set; } = ""; public string? ProviderRequestId { get; set; } public int? InputTokens { get; set; } public int? OutputTokens { get; set; }
    public string? ErrorCategory { get; set; } public string? SafeDiagnostic { get; set; }
}

internal sealed class WorkspaceAiConfigurationRow
{
    public string WorkspaceId { get; set; } = ""; public string Status { get; set; } = AiConfigurationValues.Draft;
    public string PrimaryProvider { get; set; } = ""; public string PrimaryModel { get; set; } = "";
    public string PrimaryCredentialSource { get; set; } = ""; public string? PrimaryProtectedCredential { get; set; }
    public bool FallbackEnabled { get; set; } public string? FallbackProvider { get; set; } public string? FallbackModel { get; set; }
    public string? FallbackCredentialSource { get; set; } public string? FallbackProtectedCredential { get; set; }
    public string? ActivePolicyJson { get; set; } public string? ActivePrimaryProtectedCredential { get; set; }
    public string? ActiveFallbackProtectedCredential { get; set; }
    public bool RetryRateLimited { get; set; } public bool IsValidated { get; set; } public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } public DateTimeOffset? ActivatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class AiConfigurationCommandRow
{
    public string ScopeKey { get; set; } = ""; public string WorkspaceId { get; set; } = ""; public string MemberId { get; set; } = "";
    public string Operation { get; set; } = ""; public string IdempotencyKey { get; set; } = ""; public string Fingerprint { get; set; } = "";
    public long ResultVersion { get; set; } public string ResultStateJson { get; set; } = "{}"; public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class AiConfigurationAuditRow
{
    public string AuditId { get; set; } = ""; public string WorkspaceId { get; set; } = ""; public string MemberId { get; set; } = "";
    public string Action { get; set; } = ""; public long Version { get; set; } public string SafeSummaryJson { get; set; } = "{}";
    public string CorrelationId { get; set; } = ""; public DateTimeOffset OccurredAt { get; set; }
}

internal sealed class AiExecutionDbContext(DbContextOptions<AiExecutionDbContext> options) : DbContext(options)
{
    internal DbSet<AiExecutionRow> Executions => Set<AiExecutionRow>();
    internal DbSet<AiProviderAttemptRow> ProviderAttempts => Set<AiProviderAttemptRow>();
    internal DbSet<WorkspaceAiConfigurationRow> Configurations => Set<WorkspaceAiConfigurationRow>();
    internal DbSet<AiConfigurationCommandRow> ConfigurationCommands => Set<AiConfigurationCommandRow>();
    internal DbSet<AiConfigurationAuditRow> ConfigurationAudits => Set<AiConfigurationAuditRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row=modelBuilder.Entity<AiExecutionRow>(); row.ToTable("AiExecutions","platform_ai"); row.HasKey(x=>x.ExecutionId);
        row.Property(x=>x.ExecutionId).HasMaxLength(80); row.Property(x=>x.WorkspaceId).HasMaxLength(128); row.Property(x=>x.MemberId).HasMaxLength(128);
        row.Property(x=>x.Operation).HasMaxLength(100); row.Property(x=>x.Provider).HasMaxLength(100); row.Property(x=>x.Model).HasMaxLength(200); row.Property(x=>x.Status).HasMaxLength(80);
        row.Property(x=>x.ContextTypesJson).HasMaxLength(1000); row.Property(x=>x.EvidenceIdentifiersJson).HasMaxLength(4000); row.Property(x=>x.ProviderRequestId).HasMaxLength(256); row.Property(x=>x.ErrorCode).HasMaxLength(100);
        row.HasIndex(x=>new{x.WorkspaceId,x.StartedAt});
        var attempt = modelBuilder.Entity<AiProviderAttemptRow>(); attempt.ToTable("AiProviderAttempts", "platform_ai"); attempt.HasKey(x => x.AttemptId);
        attempt.Property(x => x.AttemptId).HasMaxLength(80); attempt.Property(x => x.ExecutionId).HasMaxLength(80); attempt.Property(x => x.WorkspaceId).HasMaxLength(128);
        attempt.Property(x => x.AttemptKind).HasMaxLength(32); attempt.Property(x => x.Provider).HasMaxLength(32); attempt.Property(x => x.Model).HasMaxLength(128);
        attempt.Property(x => x.Status).HasMaxLength(32); attempt.Property(x => x.ProviderRequestId).HasMaxLength(256); attempt.Property(x => x.ErrorCategory).HasMaxLength(64);
        attempt.Property(x => x.SafeDiagnostic).HasMaxLength(512); attempt.HasIndex(x => new { x.ExecutionId, x.AttemptNumber }).IsUnique();

        var configuration = modelBuilder.Entity<WorkspaceAiConfigurationRow>(); configuration.ToTable("WorkspaceAiConfigurations", "platform_ai"); configuration.HasKey(x => x.WorkspaceId);
        configuration.Property(x => x.WorkspaceId).HasMaxLength(128); configuration.Property(x => x.Status).HasMaxLength(32);
        configuration.Property(x => x.PrimaryProvider).HasMaxLength(32); configuration.Property(x => x.PrimaryModel).HasMaxLength(128);
        configuration.Property(x => x.PrimaryCredentialSource).HasMaxLength(32); configuration.Property(x => x.PrimaryProtectedCredential).HasMaxLength(8000);
        configuration.Property(x => x.FallbackProvider).HasMaxLength(32); configuration.Property(x => x.FallbackModel).HasMaxLength(128);
        configuration.Property(x => x.FallbackCredentialSource).HasMaxLength(32); configuration.Property(x => x.FallbackProtectedCredential).HasMaxLength(8000);
        configuration.Property(x => x.ActivePolicyJson).HasMaxLength(4000); configuration.Property(x => x.ActivePrimaryProtectedCredential).HasMaxLength(8000);
        configuration.Property(x => x.ActiveFallbackProtectedCredential).HasMaxLength(8000);
        configuration.Property(x => x.RowVersion).IsRowVersion();

        var command = modelBuilder.Entity<AiConfigurationCommandRow>(); command.ToTable("AiConfigurationCommands", "platform_ai"); command.HasKey(x => x.ScopeKey);
        command.Property(x => x.ScopeKey).HasMaxLength(512); command.Property(x => x.WorkspaceId).HasMaxLength(128); command.Property(x => x.MemberId).HasMaxLength(128);
        command.Property(x => x.Operation).HasMaxLength(80); command.Property(x => x.IdempotencyKey).HasMaxLength(128); command.Property(x => x.Fingerprint).HasMaxLength(128);
        command.Property(x => x.ResultStateJson).HasMaxLength(4000);
        command.HasIndex(x => new { x.WorkspaceId, x.MemberId, x.Operation, x.IdempotencyKey }).IsUnique();

        var audit = modelBuilder.Entity<AiConfigurationAuditRow>(); audit.ToTable("AiConfigurationAudits", "platform_ai"); audit.HasKey(x => x.AuditId);
        audit.Property(x => x.AuditId).HasMaxLength(80); audit.Property(x => x.WorkspaceId).HasMaxLength(128); audit.Property(x => x.MemberId).HasMaxLength(128);
        audit.Property(x => x.Action).HasMaxLength(80); audit.Property(x => x.SafeSummaryJson).HasMaxLength(2000); audit.Property(x => x.CorrelationId).HasMaxLength(128);
        audit.HasIndex(x => new { x.WorkspaceId, x.OccurredAt });
    }
}

internal sealed class EfAiExecutionLedger(AiExecutionDbContext db) : IAiExecutionLedger
{
    public async Task RecordAsync(AiExecutionEvidence e,CancellationToken ct)
    {
        db.Executions.Add(new AiExecutionRow{ExecutionId=e.ExecutionId,WorkspaceId=e.WorkspaceId,MemberId=e.MemberId,Operation=e.Operation,Provider=e.Provider,Model=e.Model,StartedAt=e.StartedAt,CompletedAt=e.CompletedAt,Status=e.Status,DurationMilliseconds=e.DurationMilliseconds,ContextTypesJson=System.Text.Json.JsonSerializer.Serialize(e.ContextTypes),EvidenceIdentifiersJson=System.Text.Json.JsonSerializer.Serialize(e.EvidenceIdentifiers),InputTokens=e.InputTokens,OutputTokens=e.OutputTokens,ProviderRequestId=e.ProviderRequestId,ErrorCode=e.ErrorCode});
        await db.SaveChangesAsync(ct);
    }
    public async Task RecordAttemptAsync(AiProviderAttemptEvidence e, CancellationToken ct)
    {
        db.ProviderAttempts.Add(new AiProviderAttemptRow { AttemptId=e.AttemptId,ExecutionId=e.ExecutionId,WorkspaceId=e.WorkspaceId,AttemptNumber=e.AttemptNumber,
            AttemptKind=e.AttemptKind,Provider=e.Provider,Model=e.Model,StartedAt=e.StartedAt,CompletedAt=e.CompletedAt,DurationMilliseconds=e.DurationMilliseconds,
            Status=e.Status,ProviderRequestId=e.ProviderRequestId,InputTokens=e.InputTokens,OutputTokens=e.OutputTokens,ErrorCategory=e.ErrorCategory,SafeDiagnostic=e.SafeDiagnostic });
        await db.SaveChangesAsync(ct);
    }
    public async Task<AiWorkspaceUsageSummary> ReadWorkspaceSummaryAsync(string workspaceId, DateTimeOffset since, CancellationToken ct)
    {
        var executions = db.Executions.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.StartedAt >= since);
        var attempts = db.ProviderAttempts.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.StartedAt >= since);
        var latest = await executions.OrderByDescending(x => x.StartedAt).Select(x => new { x.StartedAt, x.Status }).FirstOrDefaultAsync(ct);
        return new(await executions.LongCountAsync(ct), await executions.LongCountAsync(x => x.Status == "SUCCEEDED", ct),
            await executions.LongCountAsync(x => x.Status != "SUCCEEDED", ct), await attempts.LongCountAsync(ct),
            await attempts.SumAsync(x => (long?)(x.InputTokens ?? 0), ct) ?? 0,
            await attempts.SumAsync(x => (long?)(x.OutputTokens ?? 0), ct) ?? 0, latest?.StartedAt, latest?.Status);
    }
}
