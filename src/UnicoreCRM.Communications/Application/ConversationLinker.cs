using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Application;

internal sealed class ConversationLinker(CommunicationsDbContext db, IRecordAccessEvaluator evaluator,
    IWorkspaceMemberReferenceValidator members, ILeadLinkReader leads, IContactLinkReader contacts, TimeProvider clock)
{
    private async Task<RecordAccessAuthorization> Authorize(bool write, RecordAccessRequestContext request, CancellationToken ct)
    {
        var access = await evaluator.AuthorizeResourceAsync("communications", write ? "communications.link" : "communications.read",
            ["id", "version", "crmLink", "crmContext"], RecordAccessRepresentation.Full, request, ct);
        if (!access.IsAllowed || !access.HoldsResourceRead || access.TrustedWorkspace is not { } trusted
            || access.UnenforceableFieldKeys.Count != 0
            || access.ScopeFilter is not (RecordAccessScopeFilter.Workspace or RecordAccessScopeFilter.OwnedByMember)
            || write && !access.CanWrite("crmLink")
            || !await members.IsActiveMemberAsync(trusted.WorkspaceId, trusted.MemberId, ct))
            throw new CommunicationReadException("ACCESS_DENIED", 403);
        return access;
    }

    private async Task<Conversation> Record(long id, RecordAccessAuthorization access, bool locked, RecordAccessRequestContext request, CancellationToken ct)
    {
        var trusted = access.TrustedWorkspace!;
        var query = locked ? db.Conversations.FromSqlInterpolated($"SELECT * FROM communications.Conversations WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={trusted.WorkspaceId} AND Id={id} AND AssigneeMemberId={trusted.MemberId}")
            : db.Conversations.AsNoTracking().Where(x => x.WorkspaceId == trusted.WorkspaceId && x.Id == id && x.AssigneeMemberId == trusted.MemberId);
        var row = await query.SingleOrDefaultAsync(ct);
        var decision = await evaluator.AuthorizeRecordAsync(access, id.ToString(CultureInfo.InvariantCulture),
            row is null ? RecordAccessFacts.NotFound : RecordAccessFacts.Found(row.AssigneeMemberId),
            locked ? "changeConversationCrmLink" : "readConversationCrmContext", request, ct);
        if (row is null || !decision.IsAllowed) throw new CommunicationReadException("COMMUNICATION_NOT_FOUND", 404);
        return row;
    }

    private async Task<CrmLinkTargetDocument?> Resolve(string type, string id, RecordAccessRequestContext request, CancellationToken ct)
    {
        if (type == "LEAD")
        {
            var row = await leads.ReadAsync(id, request.RequestId, request.CorrelationId, ct);
            return row is null ? null : new(type, row.Id, row.Label, row.Eligible);
        }
        var contact = await contacts.ReadAsync(id, request.RequestId, request.CorrelationId, ct);
        return contact is null ? null : new(type, contact.Id, contact.Label, contact.Eligible);
    }

    internal async Task<ConversationCrmContextDocument> ReadAsync(long id, RecordAccessRequestContext request, CancellationToken ct)
    {
        var access = await Authorize(false, request, ct);
        var row = await Record(id, access, false, request, ct);
        var canLink = access.Holds("communications.link") && access.CanWrite("crmLink");
        if (!access.CanRead("crmLink") || !access.CanRead("crmContext")) return new("UNAVAILABLE", row.Version, canLink, null, null);
        var link = await db.RecordLinks.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == row.WorkspaceId && x.ConversationId == id && x.UnlinkedAt == null, ct);
        if (link is null) return new("NONE", row.Version, canLink, null, null);
        var target = await Resolve(link.TargetType, link.TargetId, request, ct);
        if (target is null) return new("UNAVAILABLE", row.Version, canLink, null, null);
        CrmLinkTargetDocument? successor = null;
        if (link.TargetType == "LEAD")
        {
            var lead = await leads.ReadAsync(link.TargetId, request.RequestId, request.CorrelationId, ct);
            if (lead?.SuccessorContactId is { } contactId)
            {
                var candidate = await Resolve("CONTACT", contactId, request, ct);
                if (candidate?.Eligible == true) successor = candidate;
            }
        }
        // Recheck assignment after owner dereferences; no late content after reassignment.
        await Record(id, access, false, request, ct);
        return new("AVAILABLE", row.Version, canLink, target, successor);
    }

    internal async Task<IReadOnlyList<CrmLinkTargetDocument>> SearchAsync(long id, string type, string search, RecordAccessRequestContext request, CancellationToken ct)
    {
        var access = await Authorize(true, request, ct);
        await Record(id, access, false, request, ct);
        if (!access.CanRead("crmLink") || !access.CanRead("crmContext")) throw new CommunicationReadException("ACCESS_DENIED", 403);
        IReadOnlyList<CrmLinkTargetDocument> result = type == "LEAD"
            ? (await leads.SearchAsync(search, request.RequestId, request.CorrelationId, ct)).Select(x => new CrmLinkTargetDocument(type, x.Id, x.Label, x.Eligible)).ToArray()
            : (await contacts.SearchAsync(search, request.RequestId, request.CorrelationId, ct)).Select(x => new CrmLinkTargetDocument(type, x.Id, x.Label, x.Eligible)).ToArray();
        await Record(id, access, false, request, ct);
        return result;
    }

    internal async Task<ConversationLinkMutationDocument> ChangeAsync(long id, string? type, string? targetId, long expectedVersion,
        RecordAccessRequestContext request, CancellationToken ct)
    {
        var access = await Authorize(true, request, ct);
        // Gate Conversation before target validation, so another assignee cannot probe CRM targets.
        var current = await Record(id, access, false, request, ct);
        if (current.Version != expectedVersion) throw new CommunicationReadException("COMMUNICATION_VERSION_CONFLICT", 409);
        if (type is not null)
        {
            var resolved = await Resolve(type, targetId!, request, ct);
            if (resolved?.Eligible != true) throw new CommunicationReadException("COMMUNICATION_TARGET_UNAVAILABLE", 404);
            targetId = resolved.TargetId; // preserve the owner's canonical identity, including casing
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await Record(id, access, true, request, ct);
        if (row.Version != expectedVersion) throw new CommunicationReadException("COMMUNICATION_VERSION_CONFLICT", 409);
        var previous = await db.RecordLinks.SingleOrDefaultAsync(x => x.WorkspaceId == row.WorkspaceId && x.ConversationId == id && x.UnlinkedAt == null, ct);
        if (previous?.TargetType == type && previous?.TargetId == targetId)
        {
            await transaction.CommitAsync(ct);
            return new(id.ToString(CultureInfo.InvariantCulture), row.Version, false, null, clock.GetUtcNow(), request.CorrelationId);
        }
        var now = clock.GetUtcNow();
        previous?.End(access.TrustedWorkspace!.MemberId, now);
        row.RecordLinkChange();
        var audit = new CommunicationAudit(Guid.NewGuid(), row.WorkspaceId, id, null, now, "CONVERSATION_CRM_LINK_CHANGED",
            access.TrustedWorkspace!.MemberId, CorrelationId: request.CorrelationId,
            PreviousTargetType: previous?.TargetType, PreviousTargetId: previous?.TargetId, NewTargetType: type, NewTargetId: targetId);
        db.Audit.Add(audit);
        try
        {
            // End the old row before inserting its replacement (filtered unique index).
            await db.SaveChangesAsync(ct);
            if (type is not null)
            {
                db.RecordLinks.Add(new(row, type, targetId!, access.TrustedWorkspace.MemberId, now));
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new CommunicationReadException("COMMUNICATION_VERSION_CONFLICT", 409); }
        return new(id.ToString(CultureInfo.InvariantCulture), row.Version, true, audit.Id.ToString(), now, request.CorrelationId);
    }
}
