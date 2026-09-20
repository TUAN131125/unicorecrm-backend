using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;

internal sealed class EfWorkspaceAiConfigurationStore(AiExecutionDbContext db) : IWorkspaceAiConfigurationStore
{
    public async Task<WorkspaceAiConfigurationState?> FindAsync(string workspaceId, CancellationToken cancellationToken)
        => Project(await db.Configurations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken));

    public async Task<string?> FindProtectedCredentialAsync(string workspaceId, bool fallback, CancellationToken cancellationToken)
    {
        var row = await db.Configurations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken);
        return fallback ? row?.FallbackProtectedCredential : row?.PrimaryProtectedCredential;
    }

    public async Task<ActiveWorkspaceAiPolicy?> FindActivePolicyAsync(string workspaceId, CancellationToken cancellationToken)
    {
        var row = await db.Configurations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken);
        if (row?.ActivePolicyJson is null) return null;
        var policy = JsonSerializer.Deserialize<WorkspaceAiConfigurationDraft>(row.ActivePolicyJson);
        return policy is null ? null : new(policy, row.ActivePrimaryProtectedCredential, row.ActiveFallbackProtectedCredential);
    }

    public async Task RecordTestOutcomeAsync(string workspaceId, string memberId, bool succeeded, string provider, string model,
        string correlationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.ConfigurationAudits.Add(new AiConfigurationAuditRow { AuditId = Guid.NewGuid().ToString("N"), WorkspaceId = workspaceId,
            MemberId = memberId, Action = succeeded ? "TEST_SUCCEEDED" : "TEST_FAILED", Version = 0, CorrelationId = correlationId,
            OccurredAt = now, SafeSummaryJson = JsonSerializer.Serialize(new { Provider = provider, Model = model, Succeeded = succeeded }) });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AiConfigurationCommit> SaveDraftAsync(string workspaceId, string memberId, WorkspaceAiConfigurationDraft draft,
        long expectedVersion, string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        => CommitAsync("SAVE_DRAFT", workspaceId, memberId, expectedVersion, idempotencyKey, fingerprint, correlationId, now, row =>
        {
            if (!string.Equals(row.PrimaryProvider, draft.PrimaryProvider, StringComparison.Ordinal))
                row.PrimaryProtectedCredential = null;
            if (!string.Equals(row.FallbackProvider, draft.FallbackProvider, StringComparison.Ordinal))
                row.FallbackProtectedCredential = null;
            row.Status = AiConfigurationValues.Draft; row.PrimaryProvider = draft.PrimaryProvider; row.PrimaryModel = draft.PrimaryModel;
            row.PrimaryCredentialSource = draft.PrimaryCredentialSource; row.FallbackEnabled = draft.FallbackEnabled;
            row.FallbackProvider = draft.FallbackProvider; row.FallbackModel = draft.FallbackModel;
            row.FallbackCredentialSource = draft.FallbackCredentialSource; row.RetryRateLimited = draft.RetryRateLimited;
            row.IsValidated = false;
        }, cancellationToken);

    public Task<AiConfigurationCommit> SetCredentialAsync(string workspaceId, string memberId, bool fallback, string protectedCredential,
        long expectedVersion, string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        => CommitAsync(fallback ? "SET_FALLBACK_CREDENTIAL" : "SET_PRIMARY_CREDENTIAL", workspaceId, memberId, expectedVersion,
            idempotencyKey, fingerprint, correlationId, now, row =>
            {
                if (fallback) row.FallbackProtectedCredential = protectedCredential; else row.PrimaryProtectedCredential = protectedCredential;
                row.IsValidated = false;
                if (row.ActivePolicyJson is not null) row.Status = AiConfigurationValues.Draft;
            }, cancellationToken);

    public Task<AiConfigurationCommit> SetValidationAsync(string workspaceId, string memberId, bool succeeded, long expectedVersion,
        string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        => CommitAsync(succeeded ? "VALIDATION_SUCCEEDED" : "VALIDATION_FAILED", workspaceId, memberId, expectedVersion,
            idempotencyKey, fingerprint, correlationId, now, row => row.IsValidated = succeeded, cancellationToken);

    public Task<AiConfigurationCommit> ActivateAsync(string workspaceId, string memberId, long expectedVersion, string idempotencyKey,
        string fingerprint, string correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        => CommitAsync("ACTIVATE", workspaceId, memberId, expectedVersion, idempotencyKey, fingerprint, correlationId, now, row =>
        {
            if (!row.IsValidated) throw new InvalidOperationException("AI configuration must be validated before activation.");
            row.Status = AiConfigurationValues.Active; row.ActivatedAt = now;
            row.ActivePolicyJson = JsonSerializer.Serialize(new WorkspaceAiConfigurationDraft(row.PrimaryProvider, row.PrimaryModel,
                row.PrimaryCredentialSource, row.FallbackEnabled, row.FallbackProvider, row.FallbackModel,
                row.FallbackCredentialSource, row.RetryRateLimited));
            row.ActivePrimaryProtectedCredential = row.PrimaryProtectedCredential;
            row.ActiveFallbackProtectedCredential = row.FallbackProtectedCredential;
        }, cancellationToken);

    public Task<AiConfigurationCommit> DisableAsync(string workspaceId, string memberId, long expectedVersion, string idempotencyKey,
        string fingerprint, string correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        => CommitAsync("DISABLE", workspaceId, memberId, expectedVersion, idempotencyKey, fingerprint, correlationId, now,
            row =>
            {
                var pendingDraftExists = row.Status == AiConfigurationValues.Draft;
                row.Status = pendingDraftExists ? AiConfigurationValues.Draft : AiConfigurationValues.Disabled;
                row.ActivatedAt = null; row.ActivePolicyJson = null;
                row.ActivePrimaryProtectedCredential = null; row.ActiveFallbackProtectedCredential = null;
            }, cancellationToken);

    private async Task<AiConfigurationCommit> CommitAsync(string operation, string workspaceId, string memberId, long expectedVersion,
        string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now, Action<WorkspaceAiConfigurationRow> mutation,
        CancellationToken cancellationToken)
    {
        var scopeKey = $"{workspaceId}:{memberId}:{operation}:{idempotencyKey}";
        var prior = await db.ConfigurationCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, cancellationToken);
        if (prior is not null)
            return prior.Fingerprint == fingerprint
                ? new(AiConfigurationCommitStatus.Replayed, JsonSerializer.Deserialize<WorkspaceAiConfigurationState>(prior.ResultStateJson))
                : new(AiConfigurationCommitStatus.IdempotencyKeyReused);

        var row = await db.Configurations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken);
        if (row is null)
        {
            if (expectedVersion != 0) return new(AiConfigurationCommitStatus.VersionConflict);
            row = new WorkspaceAiConfigurationRow { WorkspaceId = workspaceId, Version = 0, CreatedAt = now, UpdatedAt = now };
            db.Configurations.Add(row);
        }
        else if (row.Version != expectedVersion) return new(AiConfigurationCommitStatus.VersionConflict);

        mutation(row); row.Version++; row.UpdatedAt = now;
        var resultState = Project(row)!;
        db.ConfigurationCommands.Add(new AiConfigurationCommandRow
        {
            ScopeKey = scopeKey, WorkspaceId = workspaceId, MemberId = memberId, Operation = operation,
            IdempotencyKey = idempotencyKey, Fingerprint = fingerprint, ResultVersion = row.Version,
            ResultStateJson = JsonSerializer.Serialize(resultState), CreatedAt = now
        });
        db.ConfigurationAudits.Add(new AiConfigurationAuditRow
        {
            AuditId = Guid.NewGuid().ToString("N"), WorkspaceId = workspaceId, MemberId = memberId, Action = operation,
            Version = row.Version, CorrelationId = correlationId, OccurredAt = now,
            SafeSummaryJson = JsonSerializer.Serialize(new { row.Status, row.PrimaryProvider, row.PrimaryModel, row.PrimaryCredentialSource,
                row.FallbackEnabled, row.FallbackProvider, row.FallbackModel, row.FallbackCredentialSource, row.RetryRateLimited, row.IsValidated })
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return new(AiConfigurationCommitStatus.VersionConflict); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var completed = await db.ConfigurationCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, cancellationToken);
            if (completed is null) throw;
            return completed.Fingerprint == fingerprint
                ? new(AiConfigurationCommitStatus.Replayed, JsonSerializer.Deserialize<WorkspaceAiConfigurationState>(completed.ResultStateJson))
                : new(AiConfigurationCommitStatus.IdempotencyKeyReused);
        }
        return new(AiConfigurationCommitStatus.Committed, resultState);
    }

    private static WorkspaceAiConfigurationState? Project(WorkspaceAiConfigurationRow? row)
    {
        if (row is null) return null;
        WorkspaceAiActiveConfigurationState? active = null;
        if (row.ActivePolicyJson is not null)
        {
            var policy = JsonSerializer.Deserialize<WorkspaceAiConfigurationDraft>(row.ActivePolicyJson);
            if (policy is not null)
                active = new(policy.PrimaryProvider, policy.PrimaryModel, policy.PrimaryCredentialSource,
                    row.ActivePrimaryProtectedCredential is not null, policy.FallbackEnabled, policy.FallbackProvider,
                    policy.FallbackModel, policy.FallbackCredentialSource, row.ActiveFallbackProtectedCredential is not null,
                    policy.RetryRateLimited, row.ActivatedAt ?? row.UpdatedAt);
        }
        return new(row.WorkspaceId, row.Status, row.PrimaryProvider, row.PrimaryModel, row.PrimaryCredentialSource,
            row.PrimaryProtectedCredential is not null, row.FallbackEnabled, row.FallbackProvider, row.FallbackModel,
            row.FallbackCredentialSource, row.FallbackProtectedCredential is not null, row.RetryRateLimited, row.Version,
            row.IsValidated, row.CreatedAt, row.UpdatedAt, row.ActivatedAt, active);
    }
}
