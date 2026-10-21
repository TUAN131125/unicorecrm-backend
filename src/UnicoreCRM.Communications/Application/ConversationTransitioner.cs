using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Application;

internal sealed class ConversationTransitioner(CommunicationsDbContext db, IRecordAccessEvaluator evaluator,
    IWorkspaceMemberReferenceValidator members, TimeProvider clock)
{
    internal async Task<ConversationTransitionDocument> TransitionAsync(long id, string target, long expectedVersion,
        RecordAccessRequestContext request, CancellationToken ct)
    {
        var access = await evaluator.AuthorizeResourceAsync("communications", "communications.transition",
            ["id", "status", "version"], RecordAccessRepresentation.Full, request, ct);
        if (!access.IsAllowed || !access.HoldsResourceRead || access.TrustedWorkspace is not { } trusted
            || access.UnenforceableFieldKeys.Count != 0 || !access.CanWrite("status")
            || access.ScopeFilter is not (RecordAccessScopeFilter.Workspace or RecordAccessScopeFilter.OwnedByMember))
            throw new CommunicationReadException("ACCESS_DENIED", 403);
        if (!await members.IsActiveMemberAsync(trusted.WorkspaceId, trusted.MemberId, ct))
            throw new CommunicationReadException("ACCESS_DENIED", 403);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.Conversations.FromSqlInterpolated($"SELECT * FROM communications.Conversations WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={trusted.WorkspaceId} AND Id={id} AND AssigneeMemberId={trusted.MemberId}").SingleOrDefaultAsync(ct);
        var decision = await evaluator.AuthorizeRecordAsync(access, id.ToString(CultureInfo.InvariantCulture),
            row is null ? RecordAccessFacts.NotFound : RecordAccessFacts.Found(row.AssigneeMemberId),
            "transitionConversation", request, ct);
        if (row is null || !decision.IsAllowed) throw new CommunicationReadException("COMMUNICATION_NOT_FOUND", 404);
        if (row.Version != expectedVersion) throw new CommunicationReadException("COMMUNICATION_VERSION_CONFLICT", 409);
        var previous = row.Status;
        bool changed;
        try { changed = row.Transition(target); }
        catch (InvalidOperationException) { throw new CommunicationReadException("COMMUNICATION_TRANSITION_CONFLICT", 409); }
        CommunicationAudit? audit = null;
        if (changed)
        {
            audit = new(Guid.NewGuid(), trusted.WorkspaceId, row.Id, null, clock.GetUtcNow(),
                "CONVERSATION_TRANSITIONED", trusted.MemberId, CorrelationId: request.CorrelationId,
                PreviousStatus: previous, NewStatus: row.Status);
            db.Audit.Add(audit);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new CommunicationReadException("COMMUNICATION_VERSION_CONFLICT", 409); }
        }
        await transaction.CommitAsync(ct);
        return new(id.ToString(CultureInfo.InvariantCulture), row.Status, row.Version, changed, audit?.Id.ToString(),
            audit?.RecordedAt ?? clock.GetUtcNow(), request.CorrelationId);
    }
}
