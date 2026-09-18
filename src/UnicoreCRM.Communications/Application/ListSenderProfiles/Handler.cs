using UnicoreCRM.Communications.Application.Common;
using UnicoreCRM.Communications.Contracts;

namespace UnicoreCRM.Communications.Application.ListSenderProfiles;

internal sealed class Handler(ICommunicationsPersistence persistence)
{
    internal async Task<SenderProfileListResponse> HandleAsync(
        string workspaceId,
        bool enabledOnly,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var senders = enabledOnly
            ? await persistence.ReadEnabledSenderProfilesAsync(workspaceId, cancellationToken)
            : await persistence.ReadSenderProfilesAsync(workspaceId, cancellationToken);

        return new SenderProfileListResponse(
            senders.Select(CommunicationProjection.SenderProfile).ToArray(),
            DateTimeOffset.UtcNow.ToString("O"));
    }
}
