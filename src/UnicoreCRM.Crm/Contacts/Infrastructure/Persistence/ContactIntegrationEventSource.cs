using System.Data;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class ContactIntegrationEventSource(ContactsDbContext db, TimeProvider clock) : IIntegrationEventSource
{
    public string SourceOwner => "Contacts";
    public IReadOnlyList<IntegrationEventDescriptor> EventDescriptors { get; } = [new(ContactIntegrationEvents.ContactChanged,1,"Contact changed","A Contact was created, updated, or archived.","CRM"),new(ContactIntegrationEvents.RelationshipChanged,1,"Relationship changed","A Contact relationship changed.","CRM")];
    public async Task<IntegrationEventLease?> ClaimAsync(int batchSize, TimeSpan leaseDuration, CancellationToken ct)
    {
        var now=clock.GetUtcNow(); await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var rows=await db.OutboxMessages.Where(x=>x.IntegrationEnvelopeJson!=null && ((x.ExportState=="PENDING"&&(x.NextEligibleAt==null||x.NextEligibleAt<=now))||(x.ExportState=="LEASED"&&x.LeaseExpiresAt<=now))).OrderBy(x=>x.OccurredAt).Take(batchSize).ToListAsync(ct);
        if(rows.Count==0){await tx.CommitAsync(ct);return null;} var id=Guid.NewGuid().ToString("N");var expires=now.Add(leaseDuration);foreach(var row in rows)row.Lease(id,expires);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return new(SourceOwner,id,expires,rows.Select(x=>new IntegrationEventDraft(x.EventId,x.IntegrationEnvelopeJson!)).ToArray());
    }
    public async Task AcknowledgeAsync(string id,IReadOnlyCollection<string> eventIds,CancellationToken ct){var rows=await db.OutboxMessages.Where(x=>eventIds.Contains(x.EventId)&&x.RelayAttemptId==id).ToListAsync(ct);foreach(var row in rows)row.Publish(id,clock.GetUtcNow());if(rows.Count!=eventIds.Count)throw new InvalidOperationException("Relay acknowledgement does not own every lease.");await db.SaveChangesAsync(ct);}
    public async Task ReleaseAsync(string id,IReadOnlyCollection<string> eventIds,string error,DateTimeOffset next,CancellationToken ct){var rows=await db.OutboxMessages.Where(x=>eventIds.Contains(x.EventId)&&x.RelayAttemptId==id).ToListAsync(ct);foreach(var row in rows)row.Release(id,error,next);await db.SaveChangesAsync(ct);}
}
