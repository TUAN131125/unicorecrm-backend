using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ReplaceLeadProfile;

internal sealed record Command(string LeadId, ReplaceLeadProfileRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(
    LeadAuthorization authorization,
    LeadMutationExecution execution,
    ILeadsPersistence persistence,
    LeadInterestedProductResolution interestedProducts,
    IWorkspaceMemberReferenceValidator memberValidator)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadMutationResponse>.Failure(access.Error!);
        if (!LeadValidation.IsEntityId(command.LeadId))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["leadId"] = ["leadId is not a valid entity identifier."] }));
        if (!LeadValidation.TryProfile(command.Request, out var profile, out var productIntents, out var fields))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(fields));

        // The fingerprint covers the caller's interested-product intent, never the resolved
        // snapshots: a snapshot is current catalog state, and binding it into the key would make a
        // replay after a Product rename conflict against its own original command.
        var fingerprint = LeadCommandSupport.Fingerprint(
            new { command.LeadId, profile, productIntents, command.Metadata.ExpectedVersion });

        // Filled by the precondition below, which runs only on a genuinely new execution.
        IReadOnlyList<Domain.LeadInterestedProduct> resolvedProducts = [];
        return await execution.ExecuteAsync(
            access.Value!,
            "replaceLeadProfile",
            "LEAD_PROFILE_REPLACED",
            command.LeadId,
            command.Metadata,
            fingerprint,
            (lead, now) =>
            {
                // The requested profile is compared against the stored one, so only a field the
                // replacement actually changes is treated as a write. Repeating a READ_ONLY value
                // unchanged is not a write and is not refused.
                // The guard inspects what will actually be written, so the resolved interested-product
                // collection is substituted before comparison rather than after it.
                var replacement = profile! with { InterestedProducts = resolvedProducts };
                var fieldError = LeadFieldSecurity.GuardProfileWrite(access.Value!.Authorization, lead.Profile, replacement);
                if (fieldError is not null)
                    return fieldError;

                lead.ReplaceProfile(replacement, now);
                return null;
            },
            async (trusted, token) =>
            {
                if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile!.OwnerId, token))
                {
                    return LeadErrors.Validation(new Dictionary<string, string[]>
                    {
                        ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."]
                    });
                }

                // The submitted collection is the desired state. An identifier the Lead already
                // carries keeps its captured snapshot; only a newly added one is resolved through
                // Products. This runs inside the command's own serializable transaction and after
                // its replay branch, so a replay calls Products not at all.
                var stored = await persistence.ReadLeadAsync(trusted.WorkspaceId, command.LeadId, token);
                var capture = await interestedProducts.ResolveForReplaceAsync(
                    productIntents,
                    stored?.Profile.InterestedProducts ?? [],
                    token);
                if (capture.Error is not null)
                    return capture.Error;

                resolvedProducts = capture.Items!;
                return null;
            },
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "replaceLeadProfile", metadata, cancellationToken),
            // Field-write authorization is applied inside the mutation callback, which runs only
            // on the new-execution path, so no separate guard is needed here.
            null,
            cancellationToken);
    }
}
