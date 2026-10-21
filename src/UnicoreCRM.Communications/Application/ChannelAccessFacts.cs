using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Application;

internal sealed class ChannelAccessFacts(CommunicationsDbContext db) : IRecordAccessFactProvider
{
    internal static readonly IReadOnlyDictionary<string, bool> Fields = new[]
    {
        "id", "providerKey", "channelType", "displayName", "status", "configurationVersion", "createdAt", "updatedAt", "externalAccountKey", "lastVerifiedAt"
    }.ToDictionary(x => x, _ => true);
    public RecordAccessResourceDescriptor Descriptor { get; } = RecordAccessResourceDescriptor.Create(
        "communications.channels", "communications.channels.manage", updateCapability: "communications.channels.manage", enforceableFields: Fields);
    public async Task<RecordAccessFacts> ReadFactsAsync(TrustedWorkspaceContext trustedWorkspace, string recordId,
        RecordAccessRequestContext requestContext, CancellationToken cancellationToken) =>
        Guid.TryParse(recordId, out var id) && await db.Accounts.AsNoTracking().AnyAsync(x => x.WorkspaceId == trustedWorkspace.WorkspaceId && x.Id == id, cancellationToken)
            ? RecordAccessFacts.Found(null) : RecordAccessFacts.NotFound;
}
