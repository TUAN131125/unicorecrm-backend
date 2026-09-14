using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Integrations.Infrastructure.Persistence;
using UnicoreCRM.Integrations.Webhooks.Outbound.Domain;
using UnicoreCRM.Integrations.Webhooks.Outbound.Infrastructure;
using UnicoreCRM.PlatformOperations.Outbox;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

if (args.Length is < 1 or > 2) throw new ArgumentException("Pass one isolated SQL Server connection string and optional upgrade-data mode.");
var connection = args[0];
var clock = new MutableClock(DateTimeOffset.Parse("2026-09-13T08:00:00Z"));
var eventCatalog = new TestCatalog();
var contactsOptions = new DbContextOptionsBuilder<ContactsDbContext>().UseSqlServer(connection).Options;
var journalOptions = new DbContextOptionsBuilder<IntegrationEventJournalDbContext>().UseSqlServer(connection).Options;
var integrationsOptions = new DbContextOptionsBuilder<IntegrationsDbContext>().UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "integration")).Options;
var workflowsOptions = new DbContextOptionsBuilder<WorkflowsDbContext>().UseSqlServer(connection).Options;

if (args.Length == 2 && args[1] == "upgrade-data")
{
    await using var upgradeDb = new IntegrationsDbContext(integrationsOptions);
    await upgradeDb.Database.MigrateAsync();
    var pending = await upgradeDb.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == "upgrade_pending");
    var retry = await upgradeDb.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == "upgrade_retry");
    var dead = await upgradeDb.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == "upgrade_dead");
    Check(pending.RetryCycleAttemptCount == 0, "upgrade preserves pending zero-attempt cycle");
    Check(retry.RetryCycleAttemptCount == 5, "upgrade restores retry-cycle count from lifetime attempts");
    Check(dead.Status == "DEAD_LETTER" && dead.RetryCycleAttemptCount == 6, "upgrade preserves terminal dead-letter state and bounded cycle");
    retry.Lease("upgrade_attempt_6", clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(2)); retry.Fail("upgrade_attempt_6", clock.GetUtcNow(), 503, "HTTP_503", true); await upgradeDb.SaveChangesAsync();
    Check(retry.Status == "DEAD_LETTER" && retry.AttemptCount == 6, "post-upgrade sender cycle still dead-letters at attempt six");
    Console.WriteLine("Event/Webhook retry-cycle upgrade-data verifier completed.");
    return;
}

var contact = new ContactOutboxMessage("CONTACT_CREATED", "contact_1", "ws_1", "corr_12345678", "{\"contactId\":\"contact_1\",\"changeType\":\"CREATED\",\"resourceVersion\":1}", clock.GetUtcNow());
var relationship = new ContactOutboxMessage("CONTACT_ORGANIZATION_RELATIONSHIP_CREATED", "contact_1", "ws_1", "corr_12345678", "{\"relationshipKind\":\"CONTACT_ORGANIZATION\",\"changeType\":\"CREATED\",\"contactId\":\"contact_1\",\"relationshipId\":\"rel_1\",\"targetType\":\"ORGANIZATION\",\"targetId\":\"org_1\",\"contactVersion\":2}", clock.GetUtcNow());
var internalOnly = new ContactOutboxMessage("CONTACT_INTERNAL_ONLY", "contact_1", "ws_1", "corr_12345678", "{}", clock.GetUtcNow());
Check(contact.ExportState == "PENDING" && contact.IntegrationEnvelopeJson!.Contains(IntegrationEventCatalog.ContactChanged), "owner mutation snapshot is admitted and catalog-mapped");
Check(relationship.IntegrationEnvelopeJson!.Contains("rel_1") && relationship.IntegrationEnvelopeJson.Contains("org_1"), "relationship immutable target snapshot retained");
Check(internalOnly.ExportState is null && internalOnly.IntegrationEnvelopeJson is null, "non-catalog owner event excluded");

await using (var workflowDb = new WorkflowsDbContext(workflowsOptions))
{
    var anchor = new LeadCustomerConversionAnchor("scope_ev", "ws_1", "lead_1", "idem_ev", "request", "business", 1, "account_1", "member_1", "membership_1", "member_1", "corr_workflow_1", "request_workflow_1", "CONTACT", "contact_1", 1, "member_1", clock.GetUtcNow());
    anchor.AcquireLease("a1", "member_1", clock.GetUtcNow(), TimeSpan.FromMinutes(2)); anchor.RecordLeadReservation("a1", 2, [], [], clock.GetUtcNow());
    anchor.AcquireLease("a2", "member_1", clock.GetUtcNow(), TimeSpan.FromMinutes(2)); anchor.RecordCustomer("a2", "customer_1", 1, "CREATED", [], [], clock.GetUtcNow());
    anchor.AcquireLease("a3", "member_1", clock.GetUtcNow(), TimeSpan.FromMinutes(2)); anchor.RecordLead("a3", 3, [], [], clock.GetUtcNow());
    workflowDb.LeadCustomerConversionAnchors.Add(anchor); await workflowDb.SaveChangesAsync();
    Check(!await workflowDb.IntegrationOutboxMessages.AnyAsync(), "conversion event absent at LeadConversionRecorded");
    anchor.AcquireLease("a4", "member_1", clock.GetUtcNow(), TimeSpan.FromMinutes(2)); anchor.RecordCustomerFinalization("a4", 2, [], [], clock.GetUtcNow());
    workflowDb.IntegrationOutboxMessages.Add(new WorkflowIntegrationOutboxMessage(anchor, clock.GetUtcNow())); anchor.Complete("{}", clock.GetUtcNow()); await workflowDb.SaveChangesAsync();
    Check(await workflowDb.IntegrationOutboxMessages.CountAsync() == 1 && anchor.Stage == LeadCustomerConversionStage.Completed, "conversion event is committed only with Completed transition");
}

IntegrationEventLease reclaimedLease;
await using (var db = new ContactsDbContext(contactsOptions))
{
    db.OutboxMessages.AddRange(contact, relationship, internalOnly); await db.SaveChangesAsync();
    var source = new ContactIntegrationEventSource(db, clock);
    var first = await source.ClaimAsync(10, TimeSpan.FromMinutes(1), default);
    Check(first is { Events.Count: 2 }, "historical/internal row excluded from real owner claim");
    db.ChangeTracker.Clear();
    Check((await db.OutboxMessages.Where(x => x.IntegrationEnvelopeJson != null).ToArrayAsync()).All(x => x.ExportAttemptCount == 1), "owner relay attempt count survives SQL reload after claim");
    Check(await source.ClaimAsync(10, TimeSpan.FromMinutes(1), default) is null, "concurrent claim cannot own active lease");
    var stale = first!.RelayAttemptId;
    clock.Advance(TimeSpan.FromMinutes(2));
    var reclaimed = await source.ClaimAsync(10, TimeSpan.FromMinutes(1), default);
    Check(reclaimed is { Events.Count: 2 } && reclaimed.RelayAttemptId != stale, "expired owner lease is recoverable");
    db.ChangeTracker.Clear();
    Check((await db.OutboxMessages.Where(x => x.IntegrationEnvelopeJson != null).ToArrayAsync()).All(x => x.ExportAttemptCount == 2), "owner relay attempt count survives SQL reload after reclaim");
    reclaimedLease = reclaimed!;
    try { await source.AcknowledgeAsync(stale, reclaimedLease.Events.Select(x => x.EventId).ToArray(), default); Check(false, "stale acknowledgement rejected"); }
    catch (InvalidOperationException) { Check(true, "stale acknowledgement rejected"); }
}

IntegrationEventDraft draft;
await using (var db = new ContactsDbContext(contactsOptions))
{
    var source = new ContactIntegrationEventSource(db, clock);
    var lease = reclaimedLease;
    draft = lease.Events[0];
    await using var journalDb = new IntegrationEventJournalDbContext(journalOptions);
    var journal = new IntegrationEventJournal(journalDb, eventCatalog, clock);
    await journal.AdmitAsync(lease.Events, default);
    Check(await journal.GetTailSequenceAsync(default) == 2, "owner events admitted to actual immutable journal");
    await journal.AdmitAsync(lease.Events, default);
    Check(await journal.GetTailSequenceAsync(default) == 2, "journal replay converges by EventId");
    await source.AcknowledgeAsync(lease.RelayAttemptId, lease.Events.Select(x => x.EventId).ToArray(), default);
}
await using (var db = new IntegrationEventJournalDbContext(journalOptions))
{
    var journal = new IntegrationEventJournal(db, eventCatalog, clock);
    var changed = new IntegrationEventDraft(draft.EventId, draft.CanonicalEnvelopeJson.Replace("contact_1", "contact_changed"));
    try { await journal.AdmitAsync([changed], default); Check(false, "different immutable replay fails closed"); }
    catch (InvalidOperationException) { Check(true, "different immutable replay fails closed"); }
}

var protectorProvider = DataProtectionProvider.Create(Path.Combine(Path.GetTempPath(), "unicore-ev-verifier-keys"));
var protector = protectorProvider.CreateProtector("UnicoreCRM.Integrations.Webhooks.Outbound.SigningSecret.v1");
const string plain = "test-secret-never-persist";
var active = new OutboundWebhookSubscription("ws_1", "active", IntegrationEventCatalog.ContactChanged, "https://example.com/hook", protector.Protect(plain), "member_1", clock.GetUtcNow()); active.Activate("member_1", clock.GetUtcNow());
var paused = new OutboundWebhookSubscription("ws_1", "paused", IntegrationEventCatalog.ContactChanged, "https://example.com/hook", protector.Protect(plain), "member_1", clock.GetUtcNow()); paused.Activate("member_1", clock.GetUtcNow()); paused.Pause("member_1", clock.GetUtcNow());
var draftSubscription = new OutboundWebhookSubscription("ws_1", "draft", IntegrationEventCatalog.ContactChanged, "https://example.com/hook", protector.Protect(plain), "member_1", clock.GetUtcNow());
var wrongWorkspace = new OutboundWebhookSubscription("ws_2", "wrong", IntegrationEventCatalog.ContactChanged, "https://example.com/hook", protector.Protect(plain), "member_1", clock.GetUtcNow()); wrongWorkspace.Activate("member_1", clock.GetUtcNow());
var wrongType = new OutboundWebhookSubscription("ws_1", "wrong-type", IntegrationEventCatalog.CustomerChanged, "https://example.com/hook", protector.Protect(plain), "member_1", clock.GetUtcNow()); wrongType.Activate("member_1", clock.GetUtcNow());
await using (var db = new IntegrationsDbContext(integrationsOptions)) { db.OutboundWebhookSubscriptions.AddRange(active, paused, draftSubscription, wrongWorkspace, wrongType); await db.SaveChangesAsync(); Check(!active.ProtectedSecret.Contains(plain), "signing secret is protected at rest"); }

await using (var journalDb = new IntegrationEventJournalDbContext(journalOptions))
await using (var db = new IntegrationsDbContext(integrationsOptions))
{
    var feed = new IntegrationEventJournal(journalDb, eventCatalog, clock); var consumer = new OutboundWebhookConsumerProcessor(db, feed, clock);
    await consumer.ProcessOnceAsync(default);
    Check((await db.OutboundWebhookConsumerCursors.SingleAsync()).LastProcessedSequence == await feed.GetTailSequenceAsync(default), "initial cursor begins at current journal tail without backfill");
    var nextJson = IntegrationEventSerialization.CreateEnvelope("event_future", IntegrationEventCatalog.ContactChanged, "ws_1", "Contacts", "CONTACT", "contact_2", 1, clock.GetUtcNow(), "corr_future_1", new { contactId = "contact_2", changeType = "CREATED", resourceVersion = 1 });
    await feed.AdmitAsync([new("event_future", nextJson)], default); await consumer.ProcessOnceAsync(default); db.ChangeTracker.Clear();
    var deliveries = await db.OutboundWebhookDeliveries.ToArrayAsync();
    Check(deliveries.Length == 2 && deliveries.Any(x => x.SubscriptionId == active.SubscriptionId) && deliveries.Any(x => x.SubscriptionId == paused.SubscriptionId), "ACTIVE and PAUSED fan-out with DRAFT/workspace/type exclusion");
    await consumer.ProcessOnceAsync(default); Check(await db.OutboundWebhookDeliveries.CountAsync() == 2, "duplicate feed consumption converges");
}

var transport = new CaptureTransport((503, null));
await using (var db = new IntegrationsDbContext(integrationsOptions))
{
    var sender = new OutboundWebhookSenderProcessor(db, protectorProvider, transport, clock, NullLogger.Instance);
    await sender.ProcessOnceAsync(default); db.ChangeTracker.Clear();
    var sent = await db.OutboundWebhookDeliveries.SingleAsync(x => x.SubscriptionId == active.SubscriptionId);
    var pausedDelivery = await db.OutboundWebhookDeliveries.SingleAsync(x => x.SubscriptionId == paused.SubscriptionId);
    Check(sent.Status == "RETRY_SCHEDULED" && sent.AttemptCount == 1 && pausedDelivery.Status == "PENDING", "retryable response schedules retry while PAUSED does not transmit");
    Check(transport.Payload == sent.CanonicalPayloadJson && transport.EventId == sent.EventId && transport.DeliveryId == sent.DeliveryId, "body/EventId/DeliveryId remain canonical and stable");
    var expected = SafeOutboundWebhookTransport.CreateSignature(clock.GetUtcNow().ToUnixTimeSeconds().ToString(), sent.DeliveryId, Encoding.UTF8.GetBytes(sent.CanonicalPayloadJson), plain);
    Check(transport.SignatureMaterialDigest == expected, "production HMAC material is reproducible");
    var pausedRow = await db.OutboundWebhookSubscriptions.SingleAsync(x => x.SubscriptionId == paused.SubscriptionId);
    pausedRow.Resume("member_1", clock.GetUtcNow()); await db.SaveChangesAsync();
}

Check(!WebhookDestinationPolicy.IsPublic(IPAddress.Loopback) && !WebhookDestinationPolicy.IsPublic(IPAddress.Parse("10.0.0.1")) && WebhookDestinationPolicy.IsPublic(IPAddress.Parse("8.8.8.8")), "SSRF address policy rejects loopback/private destinations");
Check(!await WebhookDestinationPolicy.IsSafeAsync(new Uri("https://example.test"), new StaticResolver(IPAddress.Loopback), default), "create-time resolver SSRF validation rejects private resolution");
var blockedTransport = new SafeOutboundWebhookTransport(new StaticResolver(IPAddress.Loopback), clock);
var blockedResult = await blockedTransport.SendAsync(new Uri("https://example.test/hook"), "delivery_blocked", "event_blocked", IntegrationEventCatalog.ContactChanged, "{}", plain, default);
Check(blockedResult.Item2 == "DESTINATION_BLOCKED", "connect-time resolved-address SSRF validation cannot be bypassed");
var replaySubscription = new OutboundWebhookSubscription("ws_1", "replay", IntegrationEventCatalog.ContactChanged, "https://example.com/hook", protector.Protect(plain), "member_1", clock.GetUtcNow()); replaySubscription.Activate("member_1", clock.GetUtcNow());
var retryDelivery = new OutboundWebhookDelivery(replaySubscription, "event_retry", 99, IntegrationEventCatalog.ContactChanged, "{\"eventId\":\"event_retry\"}", clock.GetUtcNow());
for (var attemptNumber = 1; attemptNumber <= 6; attemptNumber++)
{
    var attemptId = $"attempt_{attemptNumber}"; retryDelivery.Lease(attemptId, clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(2));
    retryDelivery.Fail(attemptId, clock.GetUtcNow(), 503, "HTTP_503", true);
    if (attemptNumber < 6) clock.Advance(retryDelivery.NextAttemptAt - clock.GetUtcNow());
}
Check(retryDelivery.Status == "DEAD_LETTER" && retryDelivery.AttemptCount == 6, "six retryable failures reach the fixed dead-letter ceiling");
var stableDeliveryId = retryDelivery.DeliveryId; retryDelivery.Replay(clock.GetUtcNow());
Check(retryDelivery.Status == "PENDING" && retryDelivery.DeliveryId == stableDeliveryId && retryDelivery.EventId == "event_retry", "manual replay preserves DeliveryId and EventId");
await using (var db = new IntegrationsDbContext(integrationsOptions))
{
    foreach (var subscription in await db.OutboundWebhookSubscriptions.ToArrayAsync())
        if (subscription.Status == "ACTIVE") subscription.Pause("member_1", clock.GetUtcNow());
    db.OutboundWebhookSubscriptions.Add(replaySubscription);
    db.OutboundWebhookDeliveries.Add(retryDelivery);
    for (var number = 1; number <= 6; number++) { var evidence = new OutboundWebhookDeliveryAttempt($"historic_{number}", retryDelivery.DeliveryId, number, clock.GetUtcNow()); evidence.Complete(clock.GetUtcNow(), number == 6 ? "DEAD_LETTER" : "RETRY_SCHEDULED", 503, "HTTP_503"); db.OutboundWebhookDeliveryAttempts.Add(evidence); }
    await db.SaveChangesAsync();
}
var replayTransport = new CaptureTransport((200, null));
await using (var db = new IntegrationsDbContext(integrationsOptions))
{
    await new OutboundWebhookSenderProcessor(db, protectorProvider, replayTransport, clock, NullLogger.Instance).ProcessOnceAsync(default);
    db.ChangeTracker.Clear();
    var persisted = await db.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == stableDeliveryId);
    var attempts = await db.OutboundWebhookDeliveryAttempts.Where(x => x.DeliveryId == stableDeliveryId).OrderBy(x => x.AttemptNumber).ToArrayAsync();
    Check(persisted.Status == "SUCCEEDED" && attempts.Length == 7 && attempts[^1].AttemptNumber == 7, "dead-letter replay appends attempt 7 and executes through the real sender");
}
var raceDelivery = new OutboundWebhookDelivery(replaySubscription, "event_race", 102, IntegrationEventCatalog.ContactChanged, "{}", clock.GetUtcNow());
await using (var workerA = new IntegrationsDbContext(integrationsOptions))
{
    workerA.OutboundWebhookDeliveries.Add(raceDelivery); raceDelivery.Lease("lease_A", clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(1)); await workerA.SaveChangesAsync();
    clock.Advance(TimeSpan.FromMinutes(2));
    await using var workerB = new IntegrationsDbContext(integrationsOptions);
    var current = await workerB.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == raceDelivery.DeliveryId); current.Lease("lease_B", clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(2)); await workerB.SaveChangesAsync();
    raceDelivery.Succeed("lease_A", clock.GetUtcNow(), 200);
    try { await workerA.SaveChangesAsync(); Check(false, "stale independent sender is rejected by SQL concurrency"); } catch (DbUpdateConcurrencyException) { Check(true, "stale independent sender is rejected by SQL concurrency"); workerA.ChangeTracker.Clear(); }
    workerB.ChangeTracker.Clear(); current = await workerB.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == raceDelivery.DeliveryId);
    Check(current.Owns("lease_B"), "stale sender cannot overwrite the newer persisted lease"); current.Succeed("lease_B", clock.GetUtcNow(), 200); await workerB.SaveChangesAsync();
    Check((await workerB.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == raceDelivery.DeliveryId)).Status == "SUCCEEDED", "authoritative reclaimed sender completes normally");
}
var terminal = new OutboundWebhookDelivery(active, "event_400", 100, IntegrationEventCatalog.ContactChanged, "{}", clock.GetUtcNow()); terminal.Lease("terminal_attempt", clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(2)); terminal.Fail("terminal_attempt", clock.GetUtcNow(), 400, "HTTP_400", false);
Check(terminal.Status == "DEAD_LETTER", "deterministic 4xx dead-letters immediately");
var staleDelivery = new OutboundWebhookDelivery(active, "event_stale", 101, IntegrationEventCatalog.ContactChanged, "{}", clock.GetUtcNow()); staleDelivery.Lease("new_attempt", clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(2));
try { staleDelivery.Succeed("old_attempt", clock.GetUtcNow(), 200); Check(false, "stale sender execution rejected"); } catch (InvalidOperationException) { Check(true, "stale sender execution rejected"); }
Console.WriteLine("Event/Webhook persistence and transport verifier completed.");

static void Check(bool value, string name) { if (!value) throw new InvalidOperationException($"FAIL: {name}"); Console.WriteLine($"OK: {name}"); }

sealed class MutableClock(DateTimeOffset value) : TimeProvider { private DateTimeOffset current = value; public override DateTimeOffset GetUtcNow() => current; public void Advance(TimeSpan by) => current += by; }
static class IntegrationEventCatalog { internal const string ContactChanged="crm.contact.changed"; internal const string CustomerChanged="crm.customer.changed"; }
sealed class TestCatalog : IIntegrationEventCatalog { public IReadOnlyList<IntegrationEventDescriptor> Events { get; }=[new("crm.contact.changed",1,"Contact changed","Contact changed","CRM"),new("crm.relationship.changed",1,"Relationship changed","Relationship changed","CRM")]; public bool Admits(string eventType,int schemaVersion)=>Events.Any(x=>x.EventType==eventType&&x.SchemaVersion==schemaVersion); }
sealed class StaticResolver(IPAddress address) : IWebhookHostResolver { public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(new[] { address }); }
sealed class CaptureTransport((int? Status, string? Error) result) : IOutboundWebhookTransport
{
    public string? Payload { get; private set; } public string? EventId { get; private set; } public string? DeliveryId { get; private set; } public string? SignatureMaterialDigest { get; private set; }
    public Task<(int? Status, string? Error)> SendAsync(Uri endpoint, string deliveryId, string eventId, string eventType, string payload, string secret, CancellationToken ct)
    { Payload = payload; EventId = eventId; DeliveryId = deliveryId; var timestamp = DateTimeOffset.Parse("2026-09-13T08:02:00Z").ToUnixTimeSeconds(); SignatureMaterialDigest = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}\n{deliveryId}\n{payload}"))).ToLowerInvariant(); return Task.FromResult(result); }
}
