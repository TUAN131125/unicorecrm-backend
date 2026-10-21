using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Application;

internal sealed class ConversationClaimer(CommunicationsDbContext db, IRecordAccessEvaluator evaluator,
    IWorkspaceMemberReferenceValidator members, TimeProvider time)
{
    internal async Task<RecordAccessAuthorization?> AuthorityAsync(RecordAccessRequestContext request, CancellationToken ct)
    {
        var claim = await evaluator.AuthorizeResourceAsync("communications.queue", "communications.claim",
            ["id", "lastActivityAt", "ownerId"], RecordAccessRepresentation.Full, request, ct);
        if (!Supported(claim) || !claim.CanReadUnassigned || !claim.CanWrite("ownerId")) return null;
        var read = await evaluator.AuthorizeResourceAsync("communications", "communications.read",
            ConversationAccessFacts.Fields.Keys.ToArray(), RecordAccessRepresentation.Full, request, ct);
        if (!Supported(read) || read.TrustedWorkspace != claim.TrustedWorkspace) return null;
        var trusted = claim.TrustedWorkspace!;
        return await members.IsActiveMemberAsync(trusted.WorkspaceId, trusted.MemberId, ct) ? claim : null;
    }

    private static bool Supported(RecordAccessAuthorization access) => access.IsAllowed
        && access.TrustedWorkspace is not null && access.UnenforceableFieldKeys.Count == 0
        && access.ScopeFilter is RecordAccessScopeFilter.Workspace or RecordAccessScopeFilter.OwnedByMember;

    internal async Task<ConversationClaimDocument> ClaimAsync(long id, RecordAccessRequestContext request, CancellationToken ct)
    {
        var access = await AuthorityAsync(request, ct) ?? throw new CommunicationReadException("ACCESS_DENIED", 403);
        var trusted = access.TrustedWorkspace!;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Same row lock for every actor; SQL, not a process lock, serializes Claim and retry.
        var row = await db.Conversations.FromSqlInterpolated($"SELECT * FROM communications.Conversations WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={trusted.WorkspaceId} AND Id={id}")
            .SingleOrDefaultAsync(ct);
        if (row is null) throw new CommunicationReadException("COMMUNICATION_NOT_FOUND", 404);
        // The queue decision uses actual ownership for unassigned/self. Another assignee is
        // represented as unavailable; no identity or content is returned to the losing actor.
        var facts = row.AssigneeMemberId is null || row.AssigneeMemberId == trusted.MemberId
            ? RecordAccessFacts.Found(row.AssigneeMemberId) : RecordAccessFacts.NotFound;
        var decision = await evaluator.AuthorizeRecordAsync(access, id.ToString(CultureInfo.InvariantCulture),
            facts, "claimConversation", request, ct);
        if (row.AssigneeMemberId is not null && row.AssigneeMemberId != trusted.MemberId)
            throw new CommunicationReadException("COMMUNICATION_CLAIM_CONFLICT", 409);
        if (!decision.IsAllowed) throw new CommunicationReadException("ACCESS_DENIED", 403);
        var changed = row.Claim(trusted.MemberId);
        CommunicationAudit? audit;
        if (changed)
        {
            audit = new CommunicationAudit(Guid.NewGuid(), trusted.WorkspaceId, id, null,
                time.GetUtcNow(), "CONVERSATION_CLAIMED", trusted.MemberId, null, trusted.MemberId, request.CorrelationId);
            db.Audit.Add(audit);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new CommunicationReadException("COMMUNICATION_CLAIM_CONFLICT", 409); }
        }
        else
        {
            audit = await db.Audit.AsNoTracking().Where(x => x.WorkspaceId == trusted.WorkspaceId
                && x.ConversationId == id && x.Operation == "CONVERSATION_CLAIMED" && x.ActorMemberId == trusted.MemberId)
                .OrderByDescending(x => x.RecordedAt).FirstOrDefaultAsync(ct);
            if (audit is null) throw new CommunicationReadException("COMMUNICATION_CLAIM_CONFLICT", 409);
        }
        await transaction.CommitAsync(ct);
        return new(id.ToString(CultureInfo.InvariantCulture), row.Version, audit.Id.ToString(), audit.RecordedAt,
            !changed, request.CorrelationId);
    }
}
