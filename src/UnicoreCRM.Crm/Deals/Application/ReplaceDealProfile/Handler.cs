using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.ReplaceDealProfile;

internal sealed record Command(string DealId, ReplaceDealProfileRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, DealMutationExecution execution)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(command.DealId))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());

        var fingerprint = DealCommandSupport.Fingerprint(new { command.DealId, command.Request, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "updateDealCommand",
            "DEAL_PROFILE_REPLACED",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) =>
            {
                ReplaceDealProfileValidation.TryProfile(
                    command.Request,
                    deal.Profile.OpportunityScore,
                    deal.Profile.OwnerId,
                    deal.Profile.ExpectedCloseDate,
                    out var profile,
                    out var fields);
                if (fields.Count != 0)
                {
                    return fields.ContainsKey("lineItems")
                        ? DealErrors.FieldValidation(fields)
                        : DealErrors.Validation(fields);
                }
                var progressive = DealValidation.ProgressiveProfileErrors(profile!, deal.StageCode, deal.ForecastCategory);
                if (progressive.Count != 0)
                    return DealErrors.ProgressiveProfile(progressive);

                // The requested profile is compared against the stored one, so only a field the
                // replacement actually changes is treated as a write. Repeating a READ_ONLY value
                // unchanged is not a write and is not refused.
                var fieldError = DealFieldSecurity.GuardProfileWrite(access.Value!.Authorization, deal.Profile, profile!);
                if (fieldError is not null)
                    return fieldError;

                return deal.ReplaceProfile(profile!, now) ? null : DealErrors.LifecycleConflict(deal.DealId);
            },
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "replaceDealProfile", metadata, cancellationToken),
            // Field-write authorization is applied inside the mutation callback, which runs only
            // on the new-execution path, so no separate guard is needed here.
            null,
            cancellationToken);
    }
}
