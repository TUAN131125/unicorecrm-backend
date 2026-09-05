using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.CreateLeadNurtureFollowUp;

/// <summary>
/// Composes the frozen NURTURE follow-up Task and delegates to the ordinary <c>createTask</c>
/// execution, so authorization, validation, idempotency, audit and outbox behaviour are exactly the
/// public operation's and are not re-implemented or relaxed here.
/// </summary>
internal sealed class Handler(
    CreateTask.Handler createTask,
    TaskAuthorization authorization,
    ICurrentWorkspace currentWorkspace,
    IWorkspaceMemberReferenceValidator memberValidator) : ILeadQualificationTaskParticipant
{
    private const int MaxTitleLength = 300;
    private const string LeadSourceType = "LEAD";

    public async Task<LeadNurtureTaskAssigneeValidationResult> ValidateNurtureAssigneeAsync(
        string assigneeId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return new LeadNurtureTaskAssigneeValidationResult(false, "ACCESS_DENIED", 403, null);

        var trusted = currentWorkspace.Require();
        if (await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, assigneeId, cancellationToken))
            return new LeadNurtureTaskAssigneeValidationResult(true, null, null, null);

        return new LeadNurtureTaskAssigneeValidationResult(
            false,
            "VALIDATION_FAILED",
            422,
            new Dictionary<string, string[]>
            {
                ["assigneeId"] = ["assigneeId must reference an active member of the trusted workspace."]
            });
    }

    public async Task<LeadNurtureTaskResult> CreateNurtureFollowUpAsync(
        LeadNurtureTaskCommand command,
        CancellationToken cancellationToken)
    {
        var request = new CreateTaskRequest(
            Title: Title(command.Reason),
            AssigneeId: command.AssigneeId,
            DueAt: command.RevisitAt,
            Description: command.Note,
            Priority: null,
            // Scalar references only. Tasks asserts nothing about the Contact or the Lead and reads
            // neither owner's persistence; both are recorded as evidence, exactly as the public
            // createTask contract already admits. The source reference also carries the complete
            // qualification reason as its evidence, so no part of an admitted caller fact is lost.
            RelationshipRef: new BuyerReference("CONTACT", command.ContactId),
            RecordRef: null,
            SourceRef: new TaskSourceReference(LeadSourceType, command.LeadId, command.Reason.Trim()),
            DedupeKey: null);

        var result = await createTask.HandleAsync(
            new CreateTask.Command(
                request,
                new TaskCommandMetadata(
                    command.RequestId,
                    command.CorrelationId,
                    command.IdempotencyKey,
                    null,
                    command.IdempotencyScopeActorId)),
            cancellationToken);

        if (!result.IsSuccess)
        {
            var error = result.Error!;
            return new LeadNurtureTaskResult(false, null, null, error.Code, error.Status, error.FieldErrors);
        }

        var response = result.Value!;
        return new LeadNurtureTaskResult(
            true,
            response.Result.Task.Id,
            response.Outcome,
            null,
            null,
            null,
            response.Result.Task.ResourceVersion);
    }

    public async Task<LeadNurtureTaskAssigneeValidationResult> ValidateOpportunityFollowUpAsync(
        LeadOpportunityTaskCommand command,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return new(false, "ACCESS_DENIED", 403, null);
        var access = await authorization.AuthorizeAsync(
            TaskCapabilities.Create,
            new TaskRequestMetadata(command.RequestId, command.CorrelationId),
            cancellationToken);
        if (!access.IsSuccess)
            return new(false, access.Error!.Code, access.Error.Status, access.Error.FieldErrors);

        var request = OpportunityRequest(command);
        if (!CreateTask.CreateTaskValidation.TryCreate(request, out var input, out var fields))
            return new(false, "VALIDATION_FAILED", 422, fields);
        var fieldError = TaskFieldSecurity.GuardCreateWrite(
            access.Value!.Authorization, input!.Description, input.References);
        if (fieldError is not null)
            return new(false, fieldError.Code, fieldError.Status, fieldError.FieldErrors);
        if (!await memberValidator.IsActiveMemberAsync(
                access.Value.Trusted.WorkspaceId, input.AssigneeId, cancellationToken))
        {
            return new(false, "VALIDATION_FAILED", 422, new Dictionary<string, string[]>
            {
                ["assigneeId"] = ["assigneeId must reference an active member of the trusted workspace."]
            });
        }
        return new(true, null, null, null);
    }

    public async Task<LeadNurtureTaskResult> CreateOpportunityFollowUpAsync(
        LeadOpportunityTaskCommand command,
        CancellationToken cancellationToken)
    {
        var result = await createTask.HandleAsync(
            new CreateTask.Command(
                OpportunityRequest(command),
                new TaskCommandMetadata(
                    command.RequestId,
                    command.CorrelationId,
                    command.IdempotencyKey,
                    null,
                    command.IdempotencyScopeActorId)),
            cancellationToken);
        if (!result.IsSuccess)
        {
            var error = result.Error!;
            return new(false, null, null, error.Code, error.Status, error.FieldErrors);
        }
        var response = result.Value!;
        return new(
            true,
            response.Result.Task.Id,
            response.Outcome,
            null,
            null,
            null,
            response.Result.Task.ResourceVersion);
    }

    private static CreateTaskRequest OpportunityRequest(LeadOpportunityTaskCommand command) => new(
        Title: Title(command.Title),
        AssigneeId: command.AssigneeId,
        DueAt: command.DueAt,
        Description: command.Description,
        Priority: null,
        RelationshipRef: new BuyerReference("CONTACT", command.ContactId),
        RecordRef: null,
        SourceRef: new TaskSourceReference(LeadSourceType, command.LeadId, command.Title.Trim()),
        DedupeKey: null);

    /// <summary>
    /// The follow-up Task's title is a <b>bounded derived summary</b> of the NURTURE reason, not the
    /// reason itself. The adopted contract admits a reason of 1-1000 characters while
    /// <c>createTask.title</c> stops at 300, so the title can never be the reason's canonical home;
    /// the complete reason travels in <c>sourceRef.evidence</c>, whose contract bound is exactly the
    /// reason's own 1000. Widening the Task title or refusing a contract-valid Lead qualification on
    /// a downstream field length would both be worse answers, and neither is admitted.
    ///
    /// The derivation is a pure function of the immutable caller intent, so a re-drive after a lost
    /// acknowledgment composes byte-identical Task input and reaches this owner's existing
    /// idempotency record rather than producing a second, differently worded Task.
    /// </summary>
    private static string Title(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength];
    }
}
