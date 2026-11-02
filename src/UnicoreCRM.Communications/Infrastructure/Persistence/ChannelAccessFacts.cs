using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Infrastructure.Persistence;

internal sealed class ChannelAccessFacts(CommunicationsDbContext db) : IRecordAccessFactProvider
{
    public RecordAccessResourceDescriptor Descriptor { get; } = RecordAccessResourceDescriptor.Create(
        "communications.channels", "communications.channels.manage", updateCapability: "communications.channels.manage", enforceableFields: CommunicationsAccessFields.Channels);
    public async Task<RecordAccessFacts> ReadFactsAsync(TrustedWorkspaceContext trustedWorkspace, string recordId,
        RecordAccessRequestContext requestContext, CancellationToken cancellationToken) =>
        Guid.TryParse(recordId, out var id) && await db.Accounts.AsNoTracking().AnyAsync(x => x.WorkspaceId == trustedWorkspace.WorkspaceId && x.Id == id, cancellationToken)
            ? RecordAccessFacts.Found(null) : RecordAccessFacts.NotFound;
}
