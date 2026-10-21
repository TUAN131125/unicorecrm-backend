using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Communications.Application;

internal sealed class CommunicationReadException(string code, int status) : Exception(code)
{
    internal string Code => Message;
    internal int Status => status;
}

// Production reads intentionally do not reuse the synthetic LocalCommunicationsStore.
internal sealed class CommunicationsReader(CommunicationsDbContext db, IRecordAccessEvaluator evaluator, IDataProtectionProvider protection,
    ConversationClaimer claimer)
{
    private readonly IDataProtector protector = protection.CreateProtector("communications.authorized-read.v1");
    private sealed record Cursor(string Scope, int Limit, long Position, DateTimeOffset At);
    private static readonly string[] ConversationFields = ["id", "lastActivityAt", "version", "status"];
    private static readonly string[] MessageFields = ["id", "conversationId", "sequence", "occurredAt", "text"];

    private async Task<RecordAccessAuthorization> Authorize(bool queue, IReadOnlyList<string> fields,
        RecordAccessRequestContext request, CancellationToken ct)
    {
        var resource = queue ? "communications.queue" : "communications";
        var access = await evaluator.AuthorizeResourceAsync(resource, resource + ".read", fields,
            RecordAccessRepresentation.Full, request, ct);
        if (!access.IsAllowed || access.TrustedWorkspace is null || access.UnenforceableFieldKeys.Count != 0
            || access.ScopeFilter is not (RecordAccessScopeFilter.Workspace or RecordAccessScopeFilter.OwnedByMember))
            throw new CommunicationReadException("ACCESS_DENIED", 403);
        return access;
    }

    private IQueryable<Conversation> Assigned(RecordAccessAuthorization access) => db.Conversations.AsNoTracking()
        .Where(x => x.WorkspaceId == access.TrustedWorkspace!.WorkspaceId && x.AssigneeMemberId == access.TrustedWorkspace.MemberId);

    private static string Scope(RecordAccessAuthorization access, string stream)
    {
        var trusted = access.TrustedWorkspace!;
        return JsonSerializer.Serialize(new[] { trusted.WorkspaceId, trusted.AccountId, trusted.MembershipId,
            trusted.MemberId, access.PolicyFingerprint, stream });
    }

    private Cursor? Decode(string scope, int limit, string? token)
    {
        if (limit is < 1 or > 100) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        if (token is null) return null;
        if (token.Length is < 1 or > 4096) throw new CommunicationReadException("COMMUNICATION_CURSOR_INVALID", 400);
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(protector.Unprotect(token));
            if (cursor is null || cursor.Scope != scope || cursor.Limit != limit || cursor.Position < 1)
                throw new CommunicationReadException("COMMUNICATION_CURSOR_INVALID", 400);
            return cursor;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        { throw new CommunicationReadException("COMMUNICATION_CURSOR_INVALID", 400); }
    }
    private string Encode(string scope, int limit, long position, DateTimeOffset at) =>
        protector.Protect(JsonSerializer.Serialize(new Cursor(scope, limit, position, at)));

    internal async Task<object> ListAsync(bool queue, int limit, string? cursor, RecordAccessRequestContext request, CancellationToken ct, string statusView = "active")
    {
        if (statusView is not ("active" or "closed") || queue && statusView != "active")
            throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        var access = await Authorize(queue, queue ? ["id", "lastActivityAt"] : ConversationFields, request, ct);
        var trusted = access.TrustedWorkspace!;
        var scope = Scope(access, queue ? "queue:active" : "assigned:" + statusView);
        var after = Decode(scope, limit, cursor);
        var query = queue ? db.Conversations.AsNoTracking().Where(x => x.WorkspaceId == trusted.WorkspaceId && x.AssigneeMemberId == null) : Assigned(access);
        query = query.Where(x => statusView == "closed" ? x.Status == "CLOSED" : x.Status != "CLOSED");
        var canTransition = !queue && access.Holds("communications.transition") && access.CanWrite("status");
        if (after is not null) query = query.Where(x => x.LastActivityAt < after.At || (x.LastActivityAt == after.At && x.Id < after.Position));
        // Scope is in SQL before Take; no body/participant/account/CRM fields are selected.
        var rows = await query.OrderByDescending(x => x.LastActivityAt).ThenByDescending(x => x.Id).Take(limit + 1)
            .Select(x => new ConversationDocument(x.Id.ToString(), x.LastActivityAt, queue ? 0 : x.Version,
                queue ? "" : x.Status, canTransition)).ToListAsync(ct);
        var items = rows.Take(limit).ToArray();
        var next = rows.Count > limit ? Encode(scope, limit, long.Parse(items[^1].Id, CultureInfo.InvariantCulture), items[^1].LastActivityAt) : null;
        if (queue) return new CommunicationsPage<QueueConversationDocument>(items.Select(x => new QueueConversationDocument(x.Id, x.LastActivityAt)).ToArray(), next,
            await claimer.AuthorityAsync(request, ct) is not null);
        return new CommunicationsPage<ConversationDocument>(items, next);
    }

    private async Task<(RecordAccessAuthorization Access, Conversation Conversation)> RecordAsync(long id, bool messages,
        RecordAccessRequestContext request, CancellationToken ct)
    {
        var access = await Authorize(false, messages ? MessageFields : ConversationFields, request, ct);
        var conversation = await Assigned(access).SingleOrDefaultAsync(x => x.Id == id, ct);
        var decision = await evaluator.AuthorizeRecordAsync(access, id.ToString(CultureInfo.InvariantCulture),
            conversation is null ? RecordAccessFacts.NotFound : RecordAccessFacts.Found(conversation.AssigneeMemberId),
            messages ? "listConversationMessages" : "getConversation", request, ct);
        if (conversation is null || !decision.IsAllowed) throw new CommunicationReadException("COMMUNICATION_NOT_FOUND", 404);
        return (access, conversation);
    }

    internal async Task<ConversationDocument> GetAsync(long id, RecordAccessRequestContext request, CancellationToken ct)
    {
        var (access, row) = await RecordAsync(id, false, request, ct);
        return new(row.Id.ToString(CultureInfo.InvariantCulture), row.LastActivityAt, row.Version, row.Status,
            access.Holds("communications.transition") && access.CanWrite("status"));
    }

    internal async Task<CommunicationsPage<ConversationMessageDocument>> MessagesAsync(long id, int limit, string? cursor,
        RecordAccessRequestContext request, CancellationToken ct)
    {
        var (access, _) = await RecordAsync(id, true, request, ct);
        var scope = Scope(access, $"messages:{id}");
        var after = Decode(scope, limit, cursor);
        var trusted = access.TrustedWorkspace!;
        var showText = access.CanRead("text");
        // Reapply current ownership in the actual content SQL, not just in a prior lookup.
        var query = db.Messages.AsNoTracking().Where(m => m.WorkspaceId == trusted.WorkspaceId && m.ConversationId == id
            && db.Conversations.Any(c => c.Id == m.ConversationId && c.WorkspaceId == trusted.WorkspaceId && c.AssigneeMemberId == trusted.MemberId));
        if (after is not null) query = query.Where(m => m.Sequence > after.Position);
        var rows = await query.OrderBy(m => m.Sequence).Take(limit + 1)
            .Select(m => new ConversationMessageDocument(m.Id.ToString(), m.ConversationId.ToString(), m.Sequence, m.OccurredAt, showText ? m.Text : null)).ToListAsync(ct);
        var items = rows.Take(limit).ToArray();
        return new(items, rows.Count > limit ? Encode(scope, limit, items[^1].Sequence, default) : null);
    }
}
