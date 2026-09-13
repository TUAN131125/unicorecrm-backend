using UnicoreCRM.BuildingBlocks;
namespace UnicoreCRM.Integrations.Webhooks.Outbound.Contracts;
public sealed record IntegrationEventCatalogItem(string EventType,int SchemaVersion,string Name,string Description,string SourceCategory);
public sealed record OutboundWebhookSubscriptionDocument(string SubscriptionId,string WorkspaceId,string Name,string EventType,string EndpointUrl,string Status,long Version,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,DateTimeOffset? ArchivedAt);
public sealed record CreateOutboundWebhookSubscriptionRequest(string Name,string EventType,string EndpointUrl);
public sealed record UpdateOutboundWebhookSubscriptionRequest(string Name,string EventType,string EndpointUrl);
public sealed record OutboundWebhookMutationResponse(OutboundWebhookSubscriptionDocument Subscription,string Outcome,string? SigningSecret=null);
public sealed record OutboundWebhookDeliveryDocument(string DeliveryId,string SubscriptionId,string EventId,string EventType,string Status,int AttemptCount,DateTimeOffset NextAttemptAt,int? LastHttpStatus,string? LastErrorCode,DateTimeOffset CreatedAt,DateTimeOffset? SucceededAt,DateTimeOffset? DeadLetteredAt,bool Replayable);
public sealed record EmptyWebhookCommand;

internal static class OutboundEventCatalogDocuments{internal static readonly IReadOnlyList<IntegrationEventCatalogItem> All=[
 new(IntegrationEventCatalog.ContactChanged,1,"Contact changed","A Contact was created, updated, or archived.","CRM"),new(IntegrationEventCatalog.OrganizationChanged,1,"Organization changed","An Organization was created, updated, or archived.","CRM"),new(IntegrationEventCatalog.CustomerChanged,1,"Customer changed","A Customer profile or lifecycle changed.","CRM"),new(IntegrationEventCatalog.RelationshipChanged,1,"Relationship changed","A Contact relationship changed.","CRM"),new(IntegrationEventCatalog.LeadCustomerConverted,1,"Lead converted to customer","A Lead-to-Customer workflow completed.","Workflow"),new(IntegrationEventCatalog.ActivityLogged,1,"Activity logged","A CRM activity was logged.","Operations")];}
