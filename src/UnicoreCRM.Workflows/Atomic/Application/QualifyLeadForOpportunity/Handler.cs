using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForNurture;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

namespace UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForOpportunity;

/// <summary>
/// Deterministic, forward-only WF-10 OPPORTUNITY coordinator. Each owner commits locally in the
/// order Contact, optional Task, Deal, Lead. Owner participant idempotency plus the Workflows anchor
/// make every retry converge without Workflows opening another owner's persistence.
/// </summary>
internal sealed class Handler(
    WorkflowsDbContext dbContext,
    ICurrentWorkspace currentWorkspace,
    ILeadOpportunityQualificationParticipant leads,
    IContactQualificationParticipant contacts,
    ILeadQualificationTaskParticipant tasks,
    ILeadQualificationDealParticipant deals,
    TimeProvider timeProvider) : ILeadOpportunityQualificationWorkflow
{
    internal const string Workflow = "qualifyLeadForOpportunity";
    private const int MaxAttempts = 3;

    public async Task<LeadOpportunityQualificationResult> ExecuteAsync(
        LeadOpportunityQualificationCommand command,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(command, cancellationToken);
            }
            catch (Exception exception) when (IsContention(exception) && attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (Exception exception) when (IsContention(exception))
            {
                return Error("INTERNAL_ERROR", 500);
            }
        }
        return Error("INTERNAL_ERROR", 500);
    }

    private async Task<LeadOpportunityQualificationResult> ExecuteOnceAsync(
        LeadOpportunityQualificationCommand command,
        CancellationToken cancellationToken)
    {
        var fields = OpportunityRequestValidation.Validate(command);
        if (fields.Count != 0)
            return Error("LEAD_OPPORTUNITY_INPUT_INVALID", 422, fields);
        if (!currentWorkspace.IsResolved)
            return Error("ACCESS_DENIED", 403);
        var trusted = currentWorkspace.Require();

        var access = await leads.AuthorizeOpportunityAsync(
            new(command.LeadId, command.RequestId, command.CorrelationId), cancellationToken);
        if (!access.IsSuccess)
            return Error(access.ErrorCode!, access.ErrorStatus!.Value);
        if (!string.Equals(access.TrustedWorkspace!.WorkspaceId, trusted.WorkspaceId, StringComparison.Ordinal))
            return Error("WORKSPACE_MISMATCH", 403);

        var scopeKey = ScopeKey(trusted.WorkspaceId, command);
        var fingerprint = Fingerprint(command);
        var anchor = await dbContext.LeadQualificationAnchors
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
        if (anchor is not null && anchor.IntentVersion != 1)
            return Error("INTERNAL_ERROR", 500);
        if (anchor is not null && !string.Equals(anchor.Fingerprint, fingerprint, StringComparison.Ordinal))
            return Error("IDEMPOTENCY_KEY_REUSED", 409) with { IdempotencyKey = command.IdempotencyKey };
        if (anchor is { Stage: LeadQualificationStage.Completed })
            return Replayed(anchor);

        var prepared = await leads.PrepareOpportunityAsync(
            new(command.LeadId, command.RequestId, command.CorrelationId, command.ExpectedVersion),
            cancellationToken);
        if (!prepared.IsSuccess)
        {
            // If Deal and relationship are durable, the Lead may be CLOSED because its close
            // committed and only the anchor completion write was lost. Re-drive the Lead's own
            // idempotent close below; otherwise refuse before creating more foreign state.
            if (anchor is not { ContactId: not null, DealId: not null })
            {
                return Error(prepared.ErrorCode!, prepared.ErrorStatus!.Value, prepared.FieldErrors)
                    with { ExpectedVersion = command.ExpectedVersion, CurrentVersion = prepared.Version };
            }
        }
        else if (!string.Equals(prepared.TrustedWorkspace!.WorkspaceId, trusted.WorkspaceId, StringComparison.Ordinal))
        {
            return Error("WORKSPACE_MISMATCH", 403);
        }

        var effectiveCloseDate = command.ExpectedCloseDate ?? prepared.SuggestedOpportunityCloseDate;
        var effectiveAmount = command.EstimatedValue ?? (prepared.EstimatedValueAmount is not null
            && prepared.EstimatedValueCurrency is not null
                ? new LeadQualificationMoneyInput(prepared.EstimatedValueAmount, prepared.EstimatedValueCurrency)
                : null);
        if (anchor?.DealId is null && (effectiveCloseDate is null || effectiveAmount is null))
            return Error("INTERNAL_ERROR", 500);

        // Preflight every grantable downstream capability and owner reference before Contact NEW can
        // commit. Actual participants repeat their checks at the authoritative mutation boundary.
        if (anchor?.ContactId is null)
        {
            var dealValidation = await deals.ValidateOpportunityAsync(
                DealCommand(command, null, effectiveCloseDate!, effectiveAmount!, null, null), cancellationToken);
            if (!dealValidation.IsSuccess)
                return MapDealFailure(dealValidation);

            if (command.FollowUpTask is { } followUp)
            {
                var taskValidation = await tasks.ValidateOpportunityFollowUpAsync(
                    TaskCommand(command, "contact_pending", followUp, command.OwnerId, scopeKey, trusted.MemberId),
                    cancellationToken);
                if (!taskValidation.IsSuccess)
                    return taskValidation.ErrorStatus == 403
                        ? Error("LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED", 403)
                        : Error("LEAD_OPPORTUNITY_INPUT_INVALID", taskValidation.ErrorStatus!.Value, taskValidation.FieldErrors);
            }
        }

        anchor ??= await StartAsync(scopeKey, trusted, command, fingerprint, cancellationToken);
        if (!string.Equals(anchor.Fingerprint, fingerprint, StringComparison.Ordinal))
            return Error("IDEMPOTENCY_KEY_REUSED", 409) with { IdempotencyKey = command.IdempotencyKey };
        if (anchor.Stage == LeadQualificationStage.Completed)
            return Replayed(anchor);

        return await ConvergeAsync(
            command, anchor, trusted, prepared.OwnerId,
            new LeadCommunicationRestrictions(prepared.DoNotCall, prepared.DoNotEmail),
            effectiveCloseDate, effectiveAmount, cancellationToken);
    }

    private async Task<LeadOpportunityQualificationResult> ConvergeAsync(
        LeadOpportunityQualificationCommand command,
        LeadQualificationAnchor anchor,
        TrustedWorkspaceContext trusted,
        string? leadOwnerId,
        LeadCommunicationRestrictions restrictions,
        string? effectiveCloseDate,
        LeadQualificationMoneyInput? effectiveAmount,
        CancellationToken cancellationToken)
    {
        if (anchor.ParticipantMemberId is null || anchor.CorrelationId is null)
            return Error("INTERNAL_ERROR", 500);

        if (anchor.ContactId is null)
        {
            if (leadOwnerId is null)
                return Error("INTERNAL_ERROR", 500);
            var resolution = await contacts.ResolveAsync(
                new ResolveQualificationContactCommand(
                    trusted,
                    command.Contact.Mode == LeadNurtureRelationshipMode.New
                        ? ContactQualificationMode.New
                        : ContactQualificationMode.Existing,
                    command.Contact.SelectedContactId,
                    new ContactQualificationInput(
                        command.Contact.DisplayName ?? string.Empty,
                        command.Contact.Email,
                        command.Contact.Phone,
                        command.Contact.Title),
                    leadOwnerId,
                    anchor.ScopeKey,
                    command.RequestId,
                    command.CorrelationId,
                    restrictions.DoNotCall,
                    restrictions.DoNotEmail),
                cancellationToken);
            if (!resolution.IsSuccess)
            {
                if (resolution.Rejection == ContactQualificationRejection.ConcurrentConflict)
                    throw new ParticipantContentionException("Contacts");
                return resolution.Rejection == ContactQualificationRejection.DuplicateEmail
                    ? Error("LEAD_QUALIFICATION_RELATIONSHIP_INVALID", 422, DuplicateEmailField)
                    : Error("LEAD_QUALIFICATION_RELATIONSHIP_INVALID", 422);
            }
            if (resolution.ContactId is null || resolution.ContactVersion is null || resolution.DisplayName is null)
                return Error("INTERNAL_ERROR", 500);
            anchor.RecordContact(
                resolution.ContactId, resolution.ContactVersion.Value,
                resolution.WasCreated, resolution.DisplayName, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (command.FollowUpTask is { } followUp && anchor.TaskId is null)
        {
            var createdTask = await tasks.CreateOpportunityFollowUpAsync(
                TaskCommand(
                    command, anchor.ContactId!, followUp, command.OwnerId,
                    anchor.ScopeKey, anchor.ParticipantMemberId),
                cancellationToken);
            if (!createdTask.IsSuccess)
            {
                return createdTask.ErrorStatus == 403
                    ? Error("LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED", 403)
                    : Error("LEAD_OPPORTUNITY_INPUT_INVALID", createdTask.ErrorStatus!.Value, createdTask.FieldErrors);
            }
            if (createdTask.TaskId is null || createdTask.TaskVersion is null)
                return Error("INTERNAL_ERROR", 500);
            anchor.RecordTask(createdTask.TaskId, createdTask.TaskVersion.Value, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (anchor.DealId is null)
        {
            if (effectiveCloseDate is null || effectiveAmount is null)
                return Error("INTERNAL_ERROR", 500);
            var createdDeal = await deals.CreateOpportunityAsync(
                DealCommand(
                    command,
                    anchor.ContactId,
                    effectiveCloseDate,
                    effectiveAmount,
                    anchor.TaskId,
                    command.FollowUpTask,
                    anchor.ScopeKey,
                    anchor.ParticipantMemberId),
                cancellationToken);
            if (!createdDeal.IsSuccess)
                return MapDealFailure(createdDeal);
            if (createdDeal.DealId is null || createdDeal.DealVersion is null)
                return Error("INTERNAL_ERROR", 500);
            anchor.RecordDeal(createdDeal.DealId, createdDeal.DealVersion.Value, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var closure = await leads.CloseForOpportunityAsync(
            new LeadOpportunityQualificationCloseCommand(
                anchor.LeadId,
                anchor.ContactId!,
                anchor.DealId!,
                command.RequestId,
                anchor.CorrelationId,
                command.IdempotencyKey,
                command.ExpectedVersion,
                anchor.ParticipantMemberId),
            cancellationToken);
        if (!closure.IsSuccess)
        {
            return Error(closure.ErrorCode!, closure.ErrorStatus!.Value, closure.FieldErrors)
                with { ExpectedVersion = closure.ExpectedVersion, CurrentVersion = closure.CurrentVersion };
        }
        if (anchor.ContactVersion is null || anchor.ContactWasCreated is null || anchor.ContactDisplayName is null
            || anchor.DealVersion is null || closure.CommandId is null || closure.OccurredAt is null
            || closure.CorrelationId is null || closure.Outcome is null
            || closure.EmittedEventIds is null || closure.AuditEvidenceIds is null)
            return Error("INTERNAL_ERROR", 500);

        var response = ComposeResponse(anchor, closure);
        anchor.Complete(closure.Version!.Value, JsonSerializer.Serialize(response, ResponseJson), timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(
            true, closure.Outcome, anchor.LeadId, closure.Version,
            anchor.ContactId, anchor.TaskId, anchor.DealId, "OPPORTUNITY",
            null, null, null, response);
    }

    private static LeadQualificationWorkflowResponse ComposeResponse(
        LeadQualificationAnchor anchor,
        LeadQualificationClosure closure)
    {
        var resources = new List<LeadQualificationCreatedResource>();
        if (anchor.ContactWasCreated == true)
            resources.Add(new("CONTACT", anchor.ContactId!, anchor.ContactVersion!.Value));
        if (anchor.TaskId is not null)
            resources.Add(new("TASK", anchor.TaskId, anchor.TaskVersion!.Value));
        resources.Add(new("DEAL", anchor.DealId!, anchor.DealVersion!.Value));
        return new(
            closure.CommandId!,
            closure.CorrelationId!,
            anchor.LeadId,
            "Lead",
            closure.Version!.Value,
            closure.OccurredAt!,
            closure.Outcome!,
            new LeadQualificationWorkflowResult(
                anchor.LeadId,
                closure.Version.Value,
                "OPPORTUNITY",
                new LeadQualificationResolvedRelationship(
                    new QualificationRelationshipRef("CONTACT", anchor.ContactId!),
                    anchor.ContactDisplayName!)
                {
                    ContactId = anchor.ContactId
                },
                resources)
            {
                ContactId = anchor.ContactId,
                TaskId = anchor.TaskId,
                DealId = anchor.DealId
            },
            [],
            closure.EmittedEventIds!,
            closure.AuditEvidenceIds!);
    }

    private async Task<LeadQualificationAnchor> StartAsync(
        string scopeKey,
        TrustedWorkspaceContext trusted,
        LeadOpportunityQualificationCommand command,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var anchor = new LeadQualificationAnchor(
            scopeKey,
            trusted.WorkspaceId,
            Workflow,
            command.LeadId,
            command.IdempotencyKey,
            fingerprint,
            command.ExpectedVersion,
            trusted.MemberId,
            command.OwnerId,
            command.CorrelationId,
            timeProvider.GetUtcNow());
        dbContext.LeadQualificationAnchors.Add(anchor);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return anchor;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return await dbContext.LeadQualificationAnchors.SingleAsync(
                item => item.ScopeKey == scopeKey, cancellationToken);
        }
    }

    private static LeadOpportunityDealCommand DealCommand(
        LeadOpportunityQualificationCommand command,
        string? contactId,
        string expectedCloseDate,
        LeadQualificationMoneyInput amount,
        string? taskId,
        LeadQualificationFollowUpTaskInput? followUp,
        string? workflowScopeKey = null,
        string? idempotencyScopeActorId = null) => new(
            command.LeadId,
            contactId,
            command.Name,
            command.OwnerId,
            expectedCloseDate,
            command.InterestedProductIds,
            new DealMoney(amount.Amount, amount.Currency),
            command.NeedSummary,
            followUp?.DueAt,
            followUp?.Title,
            taskId,
            command.RequestId,
            command.CorrelationId,
            DealIdempotencyKey(workflowScopeKey ?? ScopeKeyForParticipant(command)),
            idempotencyScopeActorId);

    private static LeadOpportunityTaskCommand TaskCommand(
        LeadOpportunityQualificationCommand command,
        string contactId,
        LeadQualificationFollowUpTaskInput followUp,
        string assigneeId,
        string scopeKey,
        string actorId) => new(
            command.LeadId,
            contactId,
            assigneeId,
            followUp.DueAt!,
            followUp.Title!,
            followUp.Description,
            command.RequestId,
            command.CorrelationId,
            TaskIdempotencyKey(scopeKey),
            actorId);

    private static LeadOpportunityQualificationResult MapDealFailure(LeadOpportunityDealResult result) =>
        result.ErrorStatus switch
        {
            403 => Error("LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED", 403),
            409 => Error("LIFECYCLE_CONFLICT", 409),
            422 => Error("LEAD_OPPORTUNITY_INPUT_INVALID", 422, result.FieldErrors),
            _ => Error(result.ErrorCode ?? "INTERNAL_ERROR", result.ErrorStatus ?? 500, result.FieldErrors)
        };

    private static LeadOpportunityQualificationResult Replayed(LeadQualificationAnchor anchor)
    {
        var stored = anchor.ResponseJson is { Length: > 0 } json
            ? JsonSerializer.Deserialize<LeadQualificationWorkflowResponse>(json, ResponseJson)
            : null;
        if (stored is null)
            return Error("INTERNAL_ERROR", 500);
        stored = stored with { Outcome = "REPLAYED" };
        return new(
            true, "REPLAYED", anchor.LeadId, anchor.LeadVersion,
            anchor.ContactId, anchor.TaskId, anchor.DealId, "OPPORTUNITY",
            null, null, null, stored);
    }

    private static readonly Dictionary<string, string[]> DuplicateEmailField = new(StringComparer.Ordinal)
    {
        ["relationship.contact.email"] =
            ["A contact with this email address already exists in this workspace. Qualify with mode EXISTING instead."]
    };

    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    private static LeadOpportunityQualificationResult Error(
        string code,
        int status,
        IReadOnlyDictionary<string, string[]>? fields = null) =>
        new(false, null, null, null, null, null, null, null, code, status, fields);

    private static bool IsContention(Exception exception) => exception switch
    {
        ParticipantContentionException => true,
        DbUpdateConcurrencyException => true,
        SqlException sql => sql.Number is 1205 or 1222,
        _ => exception.InnerException is not null && IsContention(exception.InnerException)
    };

    private static string TaskIdempotencyKey(string scopeKey) => $"wf-opportunity-task-{scopeKey}";
    private static string DealIdempotencyKey(string scopeKey) => $"wf-opportunity-deal-{scopeKey}";
    private static string ScopeKeyForParticipant(LeadOpportunityQualificationCommand command) =>
        Hash($"{Workflow}\n{command.LeadId}\n{command.IdempotencyKey}");
    private static string ScopeKey(string workspaceId, LeadOpportunityQualificationCommand command) =>
        Hash($"{workspaceId}\n{Workflow}\n{command.LeadId}\n{command.IdempotencyKey}");
    private static string Fingerprint(LeadOpportunityQualificationCommand command) =>
        Hash(JsonSerializer.Serialize(new
        {
            Operation = Workflow,
            command.LeadId,
            command.Name,
            command.NeedSummary,
            command.OwnerId,
            command.ExpectedCloseDate,
            command.InterestedProductIds,
            command.EstimatedValue,
            command.DecisionProcess,
            command.BuyingWindow,
            command.FollowUpTask,
            Mode = command.Contact.Mode.ToString(),
            command.Contact.SelectedContactId,
            command.Contact.DisplayName,
            command.Contact.Email,
            command.Contact.Phone,
            command.Contact.Title
        }));
    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..48];
}
