using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

namespace UnicoreCRM.Workflows.Atomic.Application.HandoverLead;

internal interface ILeadHandoverRecoveryRunner
{
    Task<int> ResumeDueAsync(CancellationToken ct);
}
internal enum LeadHandoverFaultPoint { AfterTasksCommit, AfterLeadCommit }
internal interface ILeadHandoverFaultInjector
{
    Task AfterParticipantCommitAsync(LeadHandoverFaultPoint point, CancellationToken ct);
}
internal sealed class NoopLeadHandoverFaultInjector : ILeadHandoverFaultInjector
{
    public Task AfterParticipantCommitAsync(LeadHandoverFaultPoint point, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class Handler(WorkflowsDbContext db, ILeadHandoverParticipant leads,
    ILeadHandoverTaskParticipant tasks, ILeadHandoverPolicyReader policies, IServiceAccessAuthorizer services,
    ILeadHandoverFaultInjector faults, TimeProvider time, ILogger<Handler> logger)
    : ILeadHandoverWorkflow, ILeadHandoverRecoveryRunner
{
    internal const string RecoveryPrincipal = "svc_lead_handover_recovery";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<LeadHandoverOperationResult> ExecuteAsync(LeadHandoverCommand command, CancellationToken ct)
    {
        var errors = Validate(command.Request);
        if (errors.Count > 0) return new(false, null, "VALIDATION_FAILED", 422, errors);
        var request = command.Request with { NextOwnerId = command.Request.NextOwnerId!.Trim(), Reason = command.Request.Reason!.Trim() };
        var preparationCommand = new PrepareLeadHandoverCommand(command.LeadId, command.RequestId,
            command.CorrelationId, command.ExpectedVersion, request.NextOwnerId!);
        var access = await leads.AuthorizeAsync(preparationCommand, ct);
        if (!access.IsSuccess) return Fail(access.ErrorCode!, access.ErrorStatus!.Value);
        var trusted = access.TrustedWorkspace!;
        var fingerprint = Hash(JsonSerializer.Serialize(new { command.LeadId, command.ExpectedVersion,
            request.NextOwnerId, request.Reason }, Json));
        var scope = Hash($"{trusted.WorkspaceId}\nhandoverLeadWithTasks\n{trusted.MemberId}\n{command.LeadId}\n{command.IdempotencyKey}");
        var prior = await db.LeadHandoverAnchors.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope, ct);
        if (prior is not null)
        {
            if (!MatchesIntent(prior, command.ExpectedVersion, request)) return new(false, null, "IDEMPOTENCY_KEY_REUSED", 409, IdempotencyKey: command.IdempotencyKey);
            return await ProjectAsync(await ResumeAsync(scope, trusted.MemberId, true, ct), command, access, true, ct);
        }
        var prepared = await leads.PrepareAsync(preparationCommand, ct, access);
        if (!prepared.IsSuccess) return new(false, null, prepared.ErrorCode, prepared.ErrorStatus,
            ExpectedVersion: command.ExpectedVersion, CurrentVersion: prepared.Version);
        var policy = await policies.FindAsync(trusted.WorkspaceId, ct);
        if (policy is null || policy.AcceptanceSlaHours is < 1 or > 168) return Fail("INTEGRATION_UNAVAILABLE", 503);
        var now = time.GetUtcNow();
        var anchor = new LeadHandoverAnchor(scope, trusted.WorkspaceId, command.LeadId, command.IdempotencyKey, fingerprint,
            command.ExpectedVersion, trusted.AccountId, trusted.MemberId, trusted.MembershipId, command.CorrelationId,
            command.RequestId, prepared.OwnerId!, request.NextOwnerId!, request.Reason!, policy.AcceptanceSlaHours, now);
        var taskAdmission = await tasks.ValidateAsync(TaskCommand(anchor, trusted.MemberId), ct);
        if (!taskAdmission.IsSuccess) return new(false, null, taskAdmission.ErrorCode, taskAdmission.ErrorStatus, taskAdmission.FieldErrors);
        db.LeadHandoverAnchors.Add(anchor);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            prior = await db.LeadHandoverAnchors.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope, ct);
            if (prior is not null && MatchesIntent(prior, command.ExpectedVersion, request))
                return await ProjectAsync(await ResumeAsync(scope, trusted.MemberId, true, ct), command, access, true, ct);
            return Fail("LEAD_HANDOVER_IN_PROGRESS", 409);
        }
        return await ProjectAsync(await ResumeAsync(scope, trusted.MemberId, false, ct), command, prepared, false, ct);
    }

    private async Task<LeadHandoverOperationResult> ProjectAsync(LeadHandoverOperationResult result, LeadHandoverCommand command, LeadHandoverPreparation admission, bool replay, CancellationToken ct)
    {
        if (!result.IsSuccess) return result;
        // A new execution uses its captured admission. Ownership changes cannot revoke
        // its success response. Later requests recheck resource/field permissions only.
        if (replay)
        {
            var anchor = await db.LeadHandoverAnchors.AsNoTracking().SingleAsync(
                x => x.HandoverId == result.Response!.CommandId && x.LeadId == command.LeadId, ct);
            var disclosure = await tasks.AuthorizeReplayAsync(TaskCommand(anchor, anchor.OriginalPrincipalId), ct);
            if (!disclosure.IsSuccess) return Fail(disclosure.ErrorCode!, disclosure.ErrorStatus!.Value);
        }
        var document = leads.Project(result.Response!.Result.Lead, admission);
        return result with {
            Response = result.Response with { Result = result.Response.Result with { Lead = document } } };
    }

    public async Task<int> ResumeDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var ids = await db.LeadHandoverAnchors.AsNoTracking()
            .Where(x => x.Stage != LeadHandoverStage.Completed && x.Stage != LeadHandoverStage.ManualReview
                && (x.NextRetryAt == null || x.NextRetryAt <= now))
            .OrderBy(x => x.UpdatedAt).Select(x => new { x.ScopeKey, x.WorkspaceId, x.CorrelationId }).Take(10).ToArrayAsync(ct);
        var count = 0;
        foreach (var item in ids)
        {
            var grant = await services.AuthorizeAsync(item.WorkspaceId, RecoveryPrincipal,
                AccessRequirement.ForCanonicalCapability("leads.handover.recover"), item.CorrelationId, ct);
            if (!grant.IsAllowed)
            {
                logger.LogWarning("Handover recovery denied in workspace {WorkspaceId}", item.WorkspaceId);
                continue;
            }
            if ((await ResumeAsync(item.ScopeKey, RecoveryPrincipal, true, ct)).IsSuccess) count++;
        }
        return count;
    }

    private async Task<LeadHandoverOperationResult> ResumeAsync(string scope, string executor, bool replay, CancellationToken ct)
    {
        var attempt = $"handover_execution_{Guid.NewGuid():N}";
        while (true)
        {
            db.ChangeTracker.Clear();
            var anchor = await db.LeadHandoverAnchors.SingleAsync(x => x.ScopeKey == scope, ct);
            if (anchor.Stage == LeadHandoverStage.Completed)
                return new(true, JsonSerializer.Deserialize<LeadHandoverResponse>(anchor.ResponseJson!, Json)! with { Outcome = "REPLAYED" });
            if (anchor.Stage == LeadHandoverStage.ManualReview) return Fail(anchor.LastErrorCode!, 409);
            var now = time.GetUtcNow();
            if (anchor.HasActiveLease(now, attempt)) return Fail("LEAD_HANDOVER_IN_PROGRESS", 409);
            anchor.AcquireLease(attempt, executor, now, Lease);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Fail("LEAD_HANDOVER_IN_PROGRESS", 409); }
            if (anchor.Stage == LeadHandoverStage.Created)
            {
                var reservation = await leads.ReserveAsync(LeadCommand(anchor, "reserve", executor), ct);
                if (!reservation.IsSuccess) return await StopBeforeTasksAsync(anchor, attempt, reservation.ErrorCode!, reservation.ErrorStatus!.Value, executor, ct);
                anchor.RecordStage(attempt, LeadHandoverStage.LeadReserved, null,
                    reservation.Response!.EmittedEventIds, reservation.Response.AuditEvidenceIds, time.GetUtcNow());
            }
            else if (anchor.Stage == LeadHandoverStage.LeadReserved)
            {
                // A participant may have committed before the prior executor persisted this stage.
                // Resolve that evidence under service authority before checking current human grants.
                var committed = await tasks.ExecuteAsync(TaskCommand(anchor, RecoveryPrincipal), ct);
                if (committed.IsSuccess)
                {
                    anchor.RecordStage(attempt, LeadHandoverStage.TasksCommitted,
                        JsonSerializer.Serialize(committed, Json), committed.EmittedEventIds,
                        committed.AuditEvidenceIds, time.GetUtcNow());
                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException) { return Fail("LEAD_HANDOVER_IN_PROGRESS", 409); }
                    continue;
                }
                if (committed.ErrorCode == "HANDOVER_TASKS_FENCED")
                    return await StopBeforeTasksAsync(anchor, attempt, committed.ErrorCode, 409, executor, ct);
                if (committed.ErrorCode != "HANDOVER_TASKS_NOT_COMMITTED")
                    return await RetryAsync(anchor, attempt, committed.ErrorCode!, ct);
                if (executor != RecoveryPrincipal)
                {
                    var admission = await leads.AuthorizeAsync(new(anchor.LeadId, anchor.RequestId,
                        anchor.CorrelationId, anchor.ExpectedLeadVersion, anchor.NextOwnerId, RequiresOwnerWrite: true), ct);
                    if (!admission.IsSuccess) return await StopBeforeTasksAsync(anchor, attempt,
                        admission.ErrorCode!, admission.ErrorStatus!.Value, executor, ct);
                }
                var result = await tasks.ExecuteAsync(TaskCommand(anchor, executor), ct);
                if (!result.IsSuccess)
                {
                    if (result.ErrorStatus >= 500) return await RetryAsync(anchor, attempt, result.ErrorCode!, ct);
                    return await StopBeforeTasksAsync(anchor, attempt, result.ErrorCode!, result.ErrorStatus!.Value, executor, ct);
                }
                await faults.AfterParticipantCommitAsync(LeadHandoverFaultPoint.AfterTasksCommit, ct);
                anchor.RecordStage(attempt, LeadHandoverStage.TasksCommitted, JsonSerializer.Serialize(result, Json), result.EmittedEventIds, result.AuditEvidenceIds, time.GetUtcNow());
            }
            else if (anchor.Stage == LeadHandoverStage.TasksCommitted)
            {
                // From the irreversible Tasks commit onward, service authority owns completion.
                var grant = await services.AuthorizeAsync(anchor.WorkspaceId, RecoveryPrincipal,
                    AccessRequirement.ForCanonicalCapability("leads.handover.recover"), anchor.CorrelationId, ct);
                if (!grant.IsAllowed) return await RetryAsync(anchor, attempt, "ACCESS_DENIED", ct);
                var result = await leads.CompleteAsync(LeadCommand(anchor, "complete", RecoveryPrincipal), ct);
                if (!result.IsSuccess) return await RetryAsync(anchor, attempt, result.ErrorCode!, ct);
                await faults.AfterParticipantCommitAsync(LeadHandoverFaultPoint.AfterLeadCommit, ct);
                anchor.RecordStage(attempt, LeadHandoverStage.LeadCommitted, JsonSerializer.Serialize(result.Response, Json),
                    result.Response!.EmittedEventIds, result.Response.AuditEvidenceIds, time.GetUtcNow());
            }
            else if (anchor.Stage == LeadHandoverStage.LeadCommitted)
            {
                var lead = JsonSerializer.Deserialize<LeadMutationResponse>(anchor.LeadResultJson!, Json)!;
                var task = JsonSerializer.Deserialize<LeadHandoverTaskResult>(anchor.TasksResultJson!, Json)!;
                var response = new LeadHandoverResponse(anchor.HandoverId, anchor.CorrelationId, anchor.LeadId, "LEAD",
                    lead.Version, anchor.HandoverOccurredAt.UtcDateTime.ToString("O"), replay ? "REPLAYED" : "COMMITTED",
                    new(lead.Result, task.ReassignedTaskIds, task.HandoverTaskId!, task.HandoverTaskVersion!.Value,
                        anchor.TakeoverDueAt.UtcDateTime.ToString("O"), anchor.ResolvedSlaHours), [],
                    JsonSerializer.Deserialize<string[]>(anchor.EmittedEventIdsJson)!, JsonSerializer.Deserialize<string[]>(anchor.AuditEvidenceIdsJson)!);
                db.IntegrationOutboxMessages.Add(new WorkflowIntegrationOutboxMessage(anchor, lead.Version, time.GetUtcNow()));
                anchor.Complete(JsonSerializer.Serialize(response with { Outcome = "COMMITTED" }, Json), time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                return new(true, response);
            }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Fail("LEAD_HANDOVER_IN_PROGRESS", 409); }
        }
    }

    private async Task<LeadHandoverOperationResult> StopBeforeTasksAsync(LeadHandoverAnchor anchor, string attempt,
        string code, int status, string executor, CancellationToken ct)
    {
        if (anchor.Stage == LeadHandoverStage.Created)
        {
            var reservation = await leads.ResolveReservationOrFenceAsync(LeadCommand(anchor, "reserve", RecoveryPrincipal), ct);
            if (reservation.IsSuccess)
            {
                anchor.RecordStage(attempt, LeadHandoverStage.LeadReserved, null,
                    reservation.Response!.EmittedEventIds, reservation.Response.AuditEvidenceIds, time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                return Fail("LEAD_HANDOVER_IN_PROGRESS", 503);
            }
            if (reservation.ErrorCode != "HANDOVER_RESERVATION_FENCED")
                return await RetryAsync(anchor, attempt, reservation.ErrorCode!, ct);
        }
        if (anchor.Stage == LeadHandoverStage.LeadReserved)
        {
            // The Tasks key-range lock decides between a committed result and a durable
            // cancellation fence. No late executor may commit after reservation release.
            var resolved = await tasks.ResolveOrFenceAsync(TaskCommand(anchor, RecoveryPrincipal), ct);
            if (resolved.IsSuccess)
            {
                anchor.RecordStage(attempt, LeadHandoverStage.TasksCommitted,
                    JsonSerializer.Serialize(resolved, Json), resolved.EmittedEventIds,
                    resolved.AuditEvidenceIds, time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                return Fail("LEAD_HANDOVER_IN_PROGRESS", 503);
            }
            if (resolved.ErrorCode != "HANDOVER_TASKS_FENCED")
                return await RetryAsync(anchor, attempt, resolved.ErrorCode!, ct);
            var released = await leads.ReleaseAsync(LeadCommand(anchor, "release", executor), ct);
            if (!released.IsSuccess) return await RetryAsync(anchor, attempt, released.ErrorCode!, ct);
        }
        anchor.ManualReview(attempt, code, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Fail(code, status);
    }
    private async Task<LeadHandoverOperationResult> RetryAsync(LeadHandoverAnchor anchor, string attempt, string code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        anchor.Retry(attempt, code, now.AddMinutes(1), now);
        await db.SaveChangesAsync(ct);
        return Fail(code, 503);
    }
    private static LeadHandoverParticipantCommand LeadCommand(LeadHandoverAnchor a, string stage, string executor) =>
        new(new(a.WorkspaceId, a.OriginalAccountId, a.OriginalMemberId, a.OriginalMembershipId), a.LeadId, a.HandoverId,
            a.PreviousOwnerId, a.NextOwnerId, a.Reason, a.ExpectedLeadVersion, $"{a.HandoverId}:{stage}",
            a.RequestId, a.CorrelationId, a.OriginalPrincipalId, executor);
    private static LeadHandoverTaskCommand TaskCommand(LeadHandoverAnchor a, string executor) =>
        new(new(a.WorkspaceId, a.OriginalAccountId, a.OriginalMemberId, a.OriginalMembershipId), a.LeadId, a.HandoverId,
            a.NextOwnerId, a.Reason, a.TakeoverDueAt, a.RequestId, a.CorrelationId,
            a.OriginalPrincipalId, executor == RecoveryPrincipal ? executor : null);
    // The anchor already stores every canonical intent field. Compare those fields
    // rather than a historical wire-format hash, so an upgraded completed command
    // remains replayable without admitting any obsolete public field or policy.
    private static bool MatchesIntent(LeadHandoverAnchor anchor, long expectedVersion, LeadHandoverRequest request) =>
        anchor.ExpectedLeadVersion == expectedVersion
        && string.Equals(anchor.NextOwnerId, request.NextOwnerId, StringComparison.Ordinal)
        && string.Equals(anchor.Reason, request.Reason, StringComparison.Ordinal);

    internal static Dictionary<string, string[]> Validate(LeadHandoverRequest request)
    {
        var fields = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NextOwnerId) || request.NextOwnerId.Trim().Length > 128) fields["nextOwnerId"] = ["A valid member is required."];
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000) fields["reason"] = ["Reason must contain 1..1000 characters."];
        return fields;
    }
    private static LeadHandoverOperationResult Fail(string code, int status) => new(false, null, code, status);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
