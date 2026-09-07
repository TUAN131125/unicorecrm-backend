using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfStudioPersistence(WorkspaceDbContext dbContext) : IStudioPersistence
{
    public Task<StudioConfiguration?> FindConfigurationAsync(string workspaceId, CancellationToken cancellationToken) =>
        dbContext.StudioConfigurations.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);

    public Task<StudioQuickSetup?> FindQuickSetupAsync(string workspaceId, CancellationToken cancellationToken) =>
        dbContext.StudioQuickSetups.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);

    public async Task<IReadOnlyList<StudioConfigurationAudit>> ListAuditAsync(string workspaceId, CancellationToken cancellationToken) =>
        await dbContext.StudioConfigurationAudits.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .OrderByDescending(item => item.OccurredAt)
            .Take(500)
            .ToArrayAsync(cancellationToken);

    public async Task RecordReadAsync(
        string operationId,
        TrustedWorkspaceContext context,
        WorkspaceRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        dbContext.AccessRecords.Add(WorkspaceAccessRecord.SuccessfulRead(
            operationId,
            context.AccountId,
            context.WorkspaceId,
            request.RequestId,
            request.CorrelationId,
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StudioConfigurationCommit> CommitConfigurationAsync(
        string workspaceId,
        StudioConfigurationChange change,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AcquireAggregateLockAsync($"configuration:{workspaceId}", transaction, cancellationToken);
        var scopeKey = ScopeKey(workspaceId, change.OperationId, change.IdempotencyKey);
        var replay = await dbContext.StudioCommandRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
        if (replay is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (!string.Equals(replay.RequestFingerprint, change.Fingerprint, StringComparison.Ordinal))
                return new(StudioCommitStatus.IdempotencyKeyReused);
            var replayed = StudioJson.Deserialize<StudioConfigurationMutationResponse>(replay.ResponseJson) with { Outcome = "REPLAYED" };
            return new(StudioCommitStatus.Replayed, replayed);
        }

        var configuration = await dbContext.StudioConfigurations.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        if (configuration is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(StudioCommitStatus.NotFound);
        }
        if (configuration.Revision != change.ExpectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(StudioCommitStatus.VersionConflict);
        }

        switch (change.Kind)
        {
            case StudioConfigurationChangeKind.BusinessInformation:
                configuration.UpdateBusinessInformation(change.BusinessInformationJson!, change.AddressesJson!, change.ActorMemberId, now);
                break;
            case StudioConfigurationChangeKind.LocaleRegion:
                configuration.UpdateLocaleRegion(change.LocaleRegionJson!, change.ActorMemberId, now);
                break;
            case StudioConfigurationChangeKind.Blueprint:
                configuration.UpdateBlueprint(change.BlueprintJson!, change.FeaturesJson!, change.ActorMemberId, now);
                break;
            case StudioConfigurationChangeKind.Features:
                configuration.UpdateFeatures(change.FeaturesJson!, change.ActorMemberId, now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        if (change.Kind == StudioConfigurationChangeKind.LocaleRegion)
        {
            var locale = StudioJson.Deserialize<WorkspaceLocaleRegionDocument>(change.LocaleRegionJson!);
            var bootstrap = await dbContext.BootstrapProjections.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
            bootstrap?.ApplyStudioLocaleRegion(locale.DefaultLocale, locale.Timezone, locale.Currencies.BaseCurrency);
        }
        if (change.Kind is StudioConfigurationChangeKind.Blueprint or StudioConfigurationChangeKind.Features)
        {
            var features = StudioJson.Deserialize<WorkspaceFeatureUsageDocument>(change.FeaturesJson!);
            var bootstrap = await dbContext.BootstrapProjections.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
            bootstrap?.ApplyStudioFeatures(StudioJson.Serialize(EnabledModuleKeys(features)));
        }

        var audit = new StudioConfigurationAudit(workspaceId, configuration.Revision, change.Action, change.ActorMemberId, now, change.CorrelationId, change.Summary);
        var outbox = new StudioOutboxEvent(
            workspaceId, change.EventType, workspaceId, configuration.Revision, change.CorrelationId,
            StudioJson.Serialize(new { WorkspaceId = workspaceId, Revision = configuration.Revision, Action = change.Action }), now);
        var response = new StudioConfigurationMutationResponse(
            WorkspaceIds.New("cmd"), change.CorrelationId, workspaceId, "WorkspaceStudioConfiguration",
            configuration.Revision, now, "COMMITTED", [], [outbox.EventId], [audit.AuditId], StudioDefaults.Project(configuration));
        dbContext.StudioConfigurationAudits.Add(audit);
        dbContext.StudioOutboxEvents.Add(outbox);
        dbContext.StudioCommandRecords.Add(new(scopeKey, workspaceId, change.OperationId, change.IdempotencyKey, change.Fingerprint, StudioJson.Serialize(response), now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(StudioCommitStatus.Committed, response);
    }

    public async Task<StudioQuickSetupCommit> CommitQuickSetupAsync(
        string workspaceId,
        StudioQuickSetupChange change,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AcquireAggregateLockAsync($"quick-setup:{workspaceId}", transaction, cancellationToken);
        var scopeKey = ScopeKey(workspaceId, change.OperationId, change.IdempotencyKey);
        var replay = await dbContext.StudioCommandRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
        if (replay is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (!string.Equals(replay.RequestFingerprint, change.Fingerprint, StringComparison.Ordinal))
                return new(StudioCommitStatus.IdempotencyKeyReused);
            var replayed = StudioJson.Deserialize<StudioQuickSetupMutationResponse>(replay.ResponseJson) with { Outcome = "REPLAYED" };
            return new(StudioCommitStatus.Replayed, replayed);
        }

        var setup = await dbContext.StudioQuickSetups.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        if (setup is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(StudioCommitStatus.NotFound);
        }
        if (setup.Revision != change.ExpectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(StudioCommitStatus.VersionConflict);
        }

        switch (change.Kind)
        {
            case StudioQuickSetupChangeKind.Open: setup.Open(now); break;
            case StudioQuickSetupChangeKind.Dismiss: setup.Dismiss(now); break;
            case StudioQuickSetupChangeKind.CompleteStep: setup.ResolveStep(change.StepId!, true, now); break;
            case StudioQuickSetupChangeKind.SkipStep: setup.ResolveStep(change.StepId!, false, now); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        var audit = new StudioConfigurationAudit(workspaceId, setup.Revision, change.Action, change.ActorMemberId, now, change.CorrelationId, change.StepId);
        var outbox = new StudioOutboxEvent(
            workspaceId, change.EventType, workspaceId, setup.Revision, change.CorrelationId,
            StudioJson.Serialize(new { WorkspaceId = workspaceId, Revision = setup.Revision, Action = change.Action, change.StepId }), now);
        var response = new StudioQuickSetupMutationResponse(
            WorkspaceIds.New("cmd"), change.CorrelationId, workspaceId, "StudioQuickSetup",
            setup.Revision, now, "COMMITTED", [audit.AuditId], StudioDefaults.Project(setup));
        dbContext.StudioConfigurationAudits.Add(audit);
        dbContext.StudioOutboxEvents.Add(outbox);
        dbContext.StudioCommandRecords.Add(new(scopeKey, workspaceId, change.OperationId, change.IdempotencyKey, change.Fingerprint, StudioJson.Serialize(response), now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(StudioCommitStatus.Committed, response);
    }

    private static string ScopeKey(string workspaceId, string operationId, string idempotencyKey) =>
        $"{workspaceId}:{operationId}:{idempotencyKey}";

    private static IReadOnlyList<string> EnabledModuleKeys(WorkspaceFeatureUsageDocument features)
    {
        var values = new List<string>(13);
        if (features.Leads) values.Add("leads");
        if (features.Customers) values.Add("customers");
        if (features.Contacts) values.Add("contacts");
        if (features.Deals) values.Add("deals");
        if (features.Quotes) values.Add("quotes");
        if (features.Orders) values.Add("orders");
        if (features.Support) values.Add("support");
        if (features.Organizations) values.Add("organizations");
        if (features.Tasks) values.Add("tasks");
        if (features.Payments) values.Add("payments");
        if (features.Invoices) values.Add("invoices");
        if (features.Shipping) values.Add("shipping");
        if (features.Returns) values.Add("returns");
        return values;
    }

    private async Task AcquireAggregateLockAsync(
        string resource,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 60000;
            SELECT @result;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = $"UnicoreCRM.Workspace.Studio:{resource}";
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0) throw new InvalidOperationException($"Could not acquire the Studio aggregate lock (sp_getapplock result {result}).");
    }
}
