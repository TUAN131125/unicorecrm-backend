using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.ReadDealSummary;

/// <summary>
/// The minimized Deal projection AI reads through. It carried its own copy of the record-scope and
/// field-visibility rules, which made it a second authorization authority over the same stored
/// policy; it now goes through the canonical AccessControl boundary like every other Deals use case,
/// so one authority decides and this reader only applies the result.
/// </summary>
internal sealed class DealSummaryReader(
    DealAuthorization authorization,
    IDealsPersistence persistence,
    TimeProvider timeProvider) : IDealSummaryReader
{
    /// <summary>The fields this minimized contract exposes, all of which it declares optional.</summary>
    private static readonly string[] SummaryFieldKeys =
        ["name", "stageCode", "stageCategory", "opportunityScore", "expectedCloseDate", "nextActionAt", "nextActionSummary"];

    public async Task<DealSummaryReadResult> ReadAsync(
        string dealId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(requestId, correlationId);
        // Every field of the minimized summary contract is optional, so this operation can return
        // any of them absent. The full Deal read model makes some of them required, but that
        // declaration governs the full representation, not this one.
        var access = await authorization.AuthorizeAsync(
            DealCapabilities.Read, metadata, cancellationToken, SummaryFieldKeys);
        if (!access.IsSuccess)
        {
            return new(access.Error!.Code == "WORKSPACE_MISMATCH"
                ? DealSummaryReadStatus.WorkspaceMismatch
                : DealSummaryReadStatus.AccessDenied);
        }

        if (!DealValidation.IsEntityId(dealId))
            return new(DealSummaryReadStatus.InvalidReference);

        var trusted = access.Value!.Trusted;
        var deal = await persistence.ReadDealAsync(trusted.WorkspaceId, dealId, cancellationToken);
        if (deal is null)
            return new(DealSummaryReadStatus.NotFound);
        if (await authorization.EnforceRecordAsync(access.Value!, deal, "readDealSummary", metadata, cancellationToken) is not null)
            return new(DealSummaryReadStatus.NotFound);

        var policy = access.Value!.Authorization;
        var document = DealFieldSecurity.Project(DealProjection.Document(deal), policy);
        var summary = new DealSummaryProjection(
            deal.DealId,
            policy.CanRead("name") ? document.Name : null,
            policy.CanRead("stageCode") ? document.StageCode : null,
            policy.CanRead("stageCategory") ? document.StageCategory : null,
            policy.CanRead("opportunityScore") ? document.OpportunityScore : null,
            policy.CanRead("expectedCloseDate") ? document.ExpectedCloseDate : null,
            policy.CanRead("nextActionAt") ? document.NextActionAt : null,
            policy.CanRead("nextActionSummary") ? document.NextActionSummary : null);

        persistence.AddAudit(new DealAuditRecord(
            "readDealSummary",
            trusted.WorkspaceId,
            trusted.MemberId,
            deal.DealId,
            requestId,
            correlationId,
            "READ",
            deal.Version,
            deal.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return new(DealSummaryReadStatus.Succeeded, summary);
    }
}
