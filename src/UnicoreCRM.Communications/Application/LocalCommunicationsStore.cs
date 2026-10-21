using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;

namespace UnicoreCRM.Communications.Application;

internal sealed record ConversationView(long Id, DateTimeOffset LastActivityAt, long Version);
internal sealed record MessageView(Guid Id, long ConversationId, long Sequence, string Text, DateTimeOffset OccurredAt);
internal sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

// Internal ingestion boundary. User reads use CommunicationsReader, never these fixture query helpers.
internal sealed class LocalCommunicationsStore(CommunicationsDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("communications.local-pagination.v1");
    private sealed record Cursor(string Workspace, string Stream, int Limit, long Position, DateTimeOffset At);

    internal async Task AppendAsync(string workspace, long conversationId, Guid participantId,
        string identity, string text, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var conversation = await db.Conversations.FromSqlInterpolated($"SELECT * FROM communications.Conversations WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={workspace} AND Id={conversationId}").SingleOrDefaultAsync(ct);
        var participant = await db.Participants.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.Id == participantId, ct);
        if (conversation is null || participant is null || participant.AccountId != conversation.AccountId)
            throw new InvalidOperationException("COMMUNICATION_BINDING_INVALID");
        CommunicationValue.Required(identity, 128);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 10000) throw new ArgumentException("COMMUNICATION_VALIDATION");
        var duplicate = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace
            && x.AccountId == conversation.AccountId && x.LocalIdentity == identity, ct);
        if (duplicate is not null)
        {
            if (duplicate.ConversationId != conversationId || duplicate.ParticipantId != participantId
                || duplicate.Text != text || duplicate.OccurredAt != occurredAt.ToUniversalTime())
                throw new InvalidOperationException("COMMUNICATION_IDENTITY_CONFLICT");
            return; // persisted exact synthetic replay is not a new inbound
        }
        Append(conversation, participant, identity, text, occurredAt);
        await db.SaveChangesAsync(ct); // message, version/order and audit are one EF transaction
        await transaction.CommitAsync(ct);
    }

    // Caller must hold a verified account lock and own the admission/checkpoint transaction.
    // Provider DTOs never cross this boundary; canonical account is loaded by the coordinator.
    internal async Task AdmitInTransactionAsync(ChannelAccount account, string threadIdentity, string participantIdentity,
        string messageIdentity, string text, DateTimeOffset at, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("COMMUNICATION_TRANSACTION_REQUIRED");
        var conversation = await db.Conversations.FromSqlInterpolated($"SELECT * FROM communications.Conversations WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={account.WorkspaceId} AND AccountId={account.Id} AND LocalIdentity={threadIdentity}").SingleOrDefaultAsync(ct);
        var participant = await db.Participants.SingleOrDefaultAsync(x => x.WorkspaceId == account.WorkspaceId
            && x.AccountId == account.Id && x.LocalIdentity == participantIdentity, ct);
        participant ??= new ExternalParticipant(account, participantIdentity);
        var duplicate = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == account.WorkspaceId
            && x.AccountId == account.Id && x.LocalIdentity == messageIdentity, ct);
        if (duplicate is not null)
        {
            if (conversation is null || duplicate.ConversationId != conversation.Id || duplicate.ParticipantId != participant.Id
                || duplicate.Text != text || duplicate.OccurredAt != at.ToUniversalTime())
                throw new InvalidOperationException("COMMUNICATION_IDENTITY_CONFLICT");
            return;
        }
        if (db.Entry(participant).State == EntityState.Detached) db.Participants.Add(participant);
        if (conversation is null)
        {
            conversation = new Conversation(account, threadIdentity, at);
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct); // generated Conversation ID, still inside caller transaction
        }
        Append(conversation, participant, messageIdentity, text, at);
    }

    private void Append(Conversation conversation, ExternalParticipant participant, string identity, string text, DateTimeOffset occurredAt)
    {
        var previousStatus = conversation.Status;
        var message = conversation.Append(participant, identity, text, occurredAt);
        db.Messages.Add(message);
        db.Audit.Add(new(Guid.NewGuid(), conversation.WorkspaceId, conversation.Id, message.Id, clock.GetUtcNow()));
        if (previousStatus != conversation.Status)
            db.Audit.Add(new(Guid.NewGuid(), conversation.WorkspaceId, conversation.Id, message.Id, clock.GetUtcNow(),
                "CONVERSATION_AUTO_REOPENED", PreviousStatus: previousStatus, NewStatus: conversation.Status));
    }

    internal async Task<ConversationView?> GetAsync(string workspace, long id, CancellationToken ct = default) =>
        await db.Conversations.AsNoTracking().Where(x => x.WorkspaceId == workspace && x.Id == id)
            .Select(x => new ConversationView(x.Id, x.LastActivityAt, x.Version)).SingleOrDefaultAsync(ct);

    internal async Task<Page<ConversationView>> ListAsync(string workspace, int limit, string? cursor = null, CancellationToken ct = default)
    {
        var after = Decode(workspace, "conversations", limit, cursor);
        var query = db.Conversations.AsNoTracking().Where(x => x.WorkspaceId == workspace);
        if (after is not null) query = query.Where(x => x.LastActivityAt < after.At || (x.LastActivityAt == after.At && x.Id < after.Position));
        var rows = await query.OrderByDescending(x => x.LastActivityAt).ThenByDescending(x => x.Id).Take(limit + 1)
            .Select(x => new ConversationView(x.Id, x.LastActivityAt, x.Version)).ToListAsync(ct);
        var items = rows.Take(limit).ToArray();
        return new(items, rows.Count > limit ? Encode(new(workspace, "conversations", limit, items[^1].Id, items[^1].LastActivityAt)) : null);
    }

    internal async Task<Page<MessageView>> MessagesAsync(string workspace, long conversationId, int limit,
        string? cursor = null, CancellationToken ct = default)
    {
        var stream = $"messages:{conversationId}";
        var after = Decode(workspace, stream, limit, cursor);
        var query = db.Messages.AsNoTracking().Where(x => x.WorkspaceId == workspace && x.ConversationId == conversationId);
        if (after is not null) query = query.Where(x => x.Sequence > after.Position);
        var rows = await query.OrderBy(x => x.Sequence).Take(limit + 1)
            .Select(x => new MessageView(x.Id, x.ConversationId, x.Sequence, x.Text, x.OccurredAt)).ToListAsync(ct);
        var items = rows.Take(limit).ToArray();
        return new(items, rows.Count > limit ? Encode(new(workspace, stream, limit, items[^1].Sequence, default)) : null);
    }

    private string Encode(Cursor cursor) => protector.Protect(JsonSerializer.Serialize(cursor));
    private Cursor? Decode(string workspace, string stream, int limit, string? value)
    {
        CommunicationValue.Required(workspace, 128);
        if (limit is < 1 or > 100) throw new ArgumentException("COMMUNICATION_VALIDATION");
        if (value is null) return null;
        if (value.Length > 4096) throw new ArgumentException("COMMUNICATION_CURSOR_INVALID");
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(protector.Unprotect(value));
            if (cursor is null || cursor.Workspace != workspace || cursor.Stream != stream || cursor.Limit != limit || cursor.Position < 1)
                throw new ArgumentException("COMMUNICATION_CURSOR_INVALID");
            return cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            throw new ArgumentException("COMMUNICATION_CURSOR_INVALID");
        }
    }
}
