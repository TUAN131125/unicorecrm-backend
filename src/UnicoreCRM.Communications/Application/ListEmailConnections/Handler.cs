using UnicoreCRM.Communications.Application.Common;
using UnicoreCRM.Communications.Contracts;

namespace UnicoreCRM.Communications.Application.ListEmailConnections;

internal sealed class Handler(ICommunicationsPersistence persistence)
{
    internal async Task<EmailConnectionListResponse> HandleAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var connections =
            await persistence.ReadEmailConnectionsAsync(workspaceId, cancellationToken);

        return new EmailConnectionListResponse(
            connections.Select(CommunicationProjection.EmailConnection).ToArray(),
            DateTimeOffset.UtcNow.ToString("O"));
    }
}
