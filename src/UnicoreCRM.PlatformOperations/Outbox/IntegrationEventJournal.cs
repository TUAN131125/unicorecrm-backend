using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.PlatformOperations.Outbox;

public sealed record JournalIntegrationEvent(long Sequence, string EventId, string EventType, int SchemaVersion,
    string WorkspaceId, string SourceOwner, string SubjectType, string SubjectId, long? SubjectVersion,
    DateTimeOffset OccurredAt, string CorrelationId, string CanonicalEnvelopeJson, DateTimeOffset RecordedAt);

public interface IIntegrationEventFeed
{
    Task<long> GetTailSequenceAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<JournalIntegrationEvent>> ReadAfterAsync(long sequence, int batchSize, CancellationToken cancellationToken);
}
internal sealed class AggregatedIntegrationEventCatalog(IEnumerable<IIntegrationEventSource> sources) : IIntegrationEventCatalog
{
    public IReadOnlyList<IntegrationEventDescriptor> Events { get; } = sources.SelectMany(x => x.EventDescriptors).OrderBy(x => x.EventType, StringComparer.Ordinal).ToArray();
    public bool Admits(string eventType, int schemaVersion) => Events.Any(x => x.EventType == eventType && x.SchemaVersion == schemaVersion);
}

internal sealed class IntegrationEventJournalRecord
{
    private IntegrationEventJournalRecord() { }
    internal IntegrationEventJournalRecord(IntegrationEventEnvelope envelope, string json, DateTimeOffset now)
    { EventId = envelope.EventId; EventType = envelope.EventType; SchemaVersion = envelope.SchemaVersion; WorkspaceId = envelope.WorkspaceId; SourceOwner = envelope.SourceOwner; SubjectType = envelope.SubjectType; SubjectId = envelope.SubjectId; SubjectVersion = envelope.SubjectVersion; OccurredAt = envelope.OccurredAt; CorrelationId = envelope.CorrelationId; CanonicalEnvelopeJson = json; RecordedAt = now; }
    internal long Sequence { get; private set; }
    internal string EventId { get; private set; } = null!; internal string EventType { get; private set; } = null!; internal int SchemaVersion { get; private set; }
    internal string WorkspaceId { get; private set; } = null!; internal string SourceOwner { get; private set; } = null!; internal string SubjectType { get; private set; } = null!; internal string SubjectId { get; private set; } = null!; internal long? SubjectVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
    internal string CorrelationId { get; private set; } = null!; internal string CanonicalEnvelopeJson { get; private set; } = null!; internal DateTimeOffset RecordedAt { get; private set; }
}

internal sealed class IntegrationEventJournalDbContext(DbContextOptions<IntegrationEventJournalDbContext> options) : DbContext(options)
{
    internal DbSet<IntegrationEventJournalRecord> Events => Set<IntegrationEventJournalRecord>();
    protected override void OnModelCreating(ModelBuilder b) { b.HasDefaultSchema("ops"); b.Entity<IntegrationEventJournalRecord>(e => { e.ToTable("IntegrationEventJournal"); e.HasKey(x => x.Sequence); e.Property(x => x.Sequence).UseIdentityColumn(); e.HasIndex(x => x.EventId).IsUnique(); e.Property(x => x.EventId).HasMaxLength(128); e.Property(x => x.EventType).HasMaxLength(160); e.Property(x => x.SchemaVersion); e.Property(x => x.WorkspaceId).HasMaxLength(128); e.Property(x => x.SourceOwner).HasMaxLength(80); e.Property(x => x.SubjectType).HasMaxLength(80); e.Property(x => x.SubjectId).HasMaxLength(128); e.Property(x => x.SubjectVersion); e.Property(x => x.CorrelationId).HasMaxLength(128); e.Property(x => x.CanonicalEnvelopeJson).HasColumnType("nvarchar(max)"); e.Property(x => x.OccurredAt).HasPrecision(7); e.Property(x => x.RecordedAt).HasPrecision(7); e.HasIndex(x => new { x.WorkspaceId, x.EventType, x.Sequence }); }); }
}

internal sealed class IntegrationEventJournal(IntegrationEventJournalDbContext db, IIntegrationEventCatalog catalog, TimeProvider clock) : IIntegrationEventFeed
{
    internal async Task AdmitAsync(IReadOnlyList<IntegrationEventDraft> drafts, CancellationToken ct)
    {
        foreach (var draft in drafts) { var existing = await db.Events.SingleOrDefaultAsync(x => x.EventId == draft.EventId, ct); if (existing is not null) { if (existing.CanonicalEnvelopeJson != draft.CanonicalEnvelopeJson) throw new InvalidOperationException($"Integration Event {draft.EventId} was replayed with different immutable content."); continue; } var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(draft.CanonicalEnvelopeJson, IntegrationEventSerialization.Options) ?? throw new InvalidOperationException("Invalid Integration Event envelope."); if (envelope.EventId != draft.EventId || !catalog.Admits(envelope.EventType, envelope.SchemaVersion)) throw new InvalidOperationException("Integration Event envelope failed catalog validation."); db.Events.Add(new(envelope, draft.CanonicalEnvelopeJson, clock.GetUtcNow())); }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); foreach (var draft in drafts) { var existing = await db.Events.AsNoTracking().SingleAsync(x => x.EventId == draft.EventId, ct); if (existing.CanonicalEnvelopeJson != draft.CanonicalEnvelopeJson) throw new InvalidOperationException($"Integration Event {draft.EventId} conflicts with immutable journal content."); } }
    }
    public async Task<long> GetTailSequenceAsync(CancellationToken ct) => await db.Events.Select(x => (long?)x.Sequence).MaxAsync(ct) ?? 0;
    public async Task<IReadOnlyList<JournalIntegrationEvent>> ReadAfterAsync(long sequence, int size, CancellationToken ct) => await db.Events.AsNoTracking().Where(x => x.Sequence > sequence).OrderBy(x => x.Sequence).Take(Math.Clamp(size, 1, 500)).Select(x => new JournalIntegrationEvent(x.Sequence, x.EventId, x.EventType, x.SchemaVersion, x.WorkspaceId, x.SourceOwner, x.SubjectType, x.SubjectId, x.SubjectVersion, x.OccurredAt, x.CorrelationId, x.CanonicalEnvelopeJson, x.RecordedAt)).ToArrayAsync(ct);
}
