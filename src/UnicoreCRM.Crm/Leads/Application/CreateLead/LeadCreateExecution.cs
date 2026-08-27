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
    /// <param name="access">
    /// The caller's AccessControl decision, when the request arrived through the authenticated Leads
    /// boundary. The delegated inbound-webhook path has no interactive membership and therefore no
    /// field-security decision to enforce; it passes <c>null</c> and is authorized separately through
    /// the delegated authorizer before reaching here.
    /// </param>
    internal async Task<LeadOperationResult<LeadMutationResponse>> ExecuteAsync(
        TrustedWorkspaceContext trusted,
        LeadAccess? access,
        CreateLeadRequest request,
        LeadCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!LeadValidation.TryProfile(request, out var profile, out var fields))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(fields));

        // Creation is a resource-level question, so no record scope applies, but field security
        // still does: a field the caller may not write must not be written on the way in either.
        if (access is not null)
        {
            var createWriteError = LeadFieldSecurity.GuardCreateWrite(access.Authorization, profile!);
            if (createWriteError is not null)
                return LeadOperationResult<LeadMutationResponse>.Failure(createWriteError);
        }

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
                ? LeadOperationResult<LeadMutationResponse>.Success(Project(LeadCommandSupport.Replay(existing), access))
                : LeadOperationResult<LeadMutationResponse>.Failure(replayError);
        }

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
        return LeadOperationResult<LeadMutationResponse>.Success(Project(response, access));
    }

    private static LeadMutationResponse Project(LeadMutationResponse response, LeadAccess? access) =>
        access is null
            ? response
            : response with { Result = LeadFieldSecurity.Project(response.Result, access.Authorization) };
}
