namespace UnicoreCRM.Platform.AccessControl.Contracts;

/// <summary>
/// Narrow availability check for the Quotes owner. Creator/requester and record-owner facts must
/// come from owner persistence. This does not authorize an approval mutation or expose a directory.
/// </summary>
public interface IQuoteApprovalAvailability
{
    Task<bool> HasEligibleApproverAsync(string workspaceId, string? quoteOwnerId, string creatorId,
        string requesterId, IReadOnlyList<string> requiredWriteFields, IReadOnlyList<string> requiredReadFields,
        CancellationToken cancellationToken);
}
