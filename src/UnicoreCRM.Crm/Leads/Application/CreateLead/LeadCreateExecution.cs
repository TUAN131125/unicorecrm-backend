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
    internal async Task<LeadOperationResult<LeadMutationResponse>> ExecuteAsync(
        TrustedWorkspaceContext trusted,
        CreateLeadRequest request,
        LeadCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!LeadValidation.TryProfile(request, out var profile, out var fields))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(fields));
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile!.OwnerId, cancellationToken))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."] }));

        var fingerprint = LeadCommandSupport.Fingerprint(profile);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = LeadCommandSupport.ScopeKey(trusted, "createLead", "WORKSPACE", metadata);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = LeadCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? LeadOperationResult<LeadMutationResponse>.Success(LeadCommandSupport.Replay(existing))
                : LeadOperationResult<LeadMutationResponse>.Failure(replayError);
        }

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
        return LeadOperationResult<LeadMutationResponse>.Success(response);
    }
}
