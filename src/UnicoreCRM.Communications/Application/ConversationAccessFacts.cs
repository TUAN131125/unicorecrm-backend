using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Application;

internal sealed class ConversationAccessFacts(CommunicationsDbContext db) : IRecordAccessFactProvider
{
    internal static readonly IReadOnlyDictionary<string, bool> Fields = new Dictionary<string, bool>
    {
        ["id"] = true, ["lastActivityAt"] = true, ["version"] = true,
        ["conversationId"] = true, ["sequence"] = true, ["occurredAt"] = true, ["text"] = false, ["status"] = true,
        ["crmLink"] = false, ["crmContext"] = false
    };
    public RecordAccessResourceDescriptor Descriptor { get; } = RecordAccessResourceDescriptor.Create(
        "communications", "communications.read", enforceableFields: Fields,
        commandCapabilities: new Dictionary<string, string> { ["communications.transition"] = "communications.transition", ["communications.link"] = "communications.link" });

    public async Task<RecordAccessFacts> ReadFactsAsync(TrustedWorkspaceContext trustedWorkspace, string recordId,
        RecordAccessRequestContext requestContext, CancellationToken cancellationToken)
    {
        if (!long.TryParse(recordId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 1)
            return RecordAccessFacts.NotFound;
        // DEC-COM-019 caps even a Workspace scope: elevated content access has not been approved.
        var row = await db.Conversations.AsNoTracking().Where(x => x.WorkspaceId == trustedWorkspace.WorkspaceId
            && x.Id == id && x.AssigneeMemberId == trustedWorkspace.MemberId)
            .Select(x => new { x.AssigneeMemberId }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? RecordAccessFacts.NotFound : RecordAccessFacts.Found(row.AssigneeMemberId);
    }
}

internal sealed class QueueAccessFacts(CommunicationsDbContext db) : IRecordAccessFactProvider
{
    public RecordAccessResourceDescriptor Descriptor { get; } = RecordAccessResourceDescriptor.Create(
        "communications.queue", "communications.queue.read",
        commandCapabilities: new Dictionary<string, string> { ["communications.claim"] = "communications.claim" },
        enforceableFields: new Dictionary<string, bool>
        { ["id"] = true, ["lastActivityAt"] = true, ["ownerId"] = false },
        unassignedReadCapability: "communications.queue.read", unassignedClaimCommand: "communications.claim");
    public async Task<RecordAccessFacts> ReadFactsAsync(TrustedWorkspaceContext trustedWorkspace, string recordId,
        RecordAccessRequestContext requestContext, CancellationToken cancellationToken)
    {
        if (!long.TryParse(recordId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 1)
            return RecordAccessFacts.NotFound;
        var row = await db.Conversations.AsNoTracking().Where(x => x.WorkspaceId == trustedWorkspace.WorkspaceId
            && x.Id == id && (x.AssigneeMemberId == null || x.AssigneeMemberId == trustedWorkspace.MemberId))
            .Select(x => new { x.AssigneeMemberId }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? RecordAccessFacts.NotFound : RecordAccessFacts.Found(row.AssigneeMemberId);
    }
}
