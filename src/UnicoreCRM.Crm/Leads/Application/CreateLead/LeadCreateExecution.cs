using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.CreateLead;

internal sealed class LeadCreateExecution(
    ILeadsPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Creates a Lead under one of the closed admitted admission models. Business validation,
    /// idempotency, persistence, audit and outbox are shared; the security model is not shared and
    /// is not optional - <paramref name="admission"/> is required and states which one applies.
    /// </summary>
    /// <param name="admission">
    /// Which admitted security model governs this creation. There is no null case and no boolean:
    /// an interactive creation must carry a real AccessControl decision, and a delegated ingress must
    /// carry proof that the delegated capability evaluation allowed it.
    /// </param>
    internal async Task<LeadOperationResult<LeadMutationResponse>> ExecuteAsync(
        LeadCreateAdmission admission,
        CreateLeadRequest request,
        LeadCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var trusted = admission.Trusted;
        if (!LeadValidation.TryProfile(request, out var profile, out var fields))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(fields));

        var fingerprint = LeadCommandSupport.Fingerprint(profile);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = LeadCommandSupport.ScopeKey(trusted, "createLead", "WORKSPACE", metadata);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // Answered from stored evidence alone, so an owner deactivated after the original commit
            // cannot retroactively invalidate the replay or create a second Lead.
            var replayError = LeadCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? LeadOperationResult<LeadMutationResponse>.Success(admission.Project(LeadCommandSupport.Replay(existing)))
                : LeadOperationResult<LeadMutationResponse>.Failure(replayError);
        }

        // A committed replay writes nothing and is answered from its original authority evidence.
        // New delegated execution must still bind both owner assignment and audit provenance to the
        // member carried by the server-issued admission proof.
        var bindingError = admission.GuardExecutionBinding(profile!, metadata);
        if (bindingError is not null)
            return LeadOperationResult<LeadMutationResponse>.Failure(bindingError);

        // Creation is a resource-level question, so no record scope applies, but the admitted model's
        // field-write policy still does: a field the caller may not write must not be written on the
        // way in either. It follows the replay branch, so a committed creation stays replayable after
        // a field turns READ_ONLY or HIDDEN - the replay writes nothing.
        var createWriteError = admission.GuardCreateWrite(profile!);
        if (createWriteError is not null)
            return LeadOperationResult<LeadMutationResponse>.Failure(createWriteError);

        // Only a genuinely new command evaluates current mutable owner/member state.
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile!.OwnerId, cancellationToken))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."] }));

        var now = timeProvider.GetUtcNow();
        var lead = new Lead(trusted.WorkspaceId, profile, now);
        persistence.AddLead(lead);
        var response = LeadCommandSupport.RecordCommit(
            persistence,
            lead,
            trusted,
            metadata,
            "createLead",
            "LEAD_CREATED",
            scopeKey,
            "WORKSPACE",
            fingerprint,
            null,
            now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return LeadOperationResult<LeadMutationResponse>.Success(admission.Project(response));
    }
}
