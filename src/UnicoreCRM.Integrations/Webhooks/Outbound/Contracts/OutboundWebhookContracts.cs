namespace UnicoreCRM.Integrations.Webhooks.Outbound.Contracts;
public sealed record IntegrationEventCatalogItem(string EventType,int SchemaVersion,string Name,string Description,string SourceCategory);
public sealed record OutboundWebhookSubscriptionDocument(string SubscriptionId,string WorkspaceId,string Name,string EventType,string EndpointUrl,string Status,long Version,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,DateTimeOffset? ArchivedAt);
public sealed record CreateOutboundWebhookSubscriptionRequest(string Name,string EventType,string EndpointUrl);
public sealed record UpdateOutboundWebhookSubscriptionRequest(string Name,string EventType,string EndpointUrl);
public sealed record OutboundWebhookMutationResponse(OutboundWebhookSubscriptionDocument Subscription,string Outcome,string? SigningSecret=null);
public sealed record OutboundWebhookDeliveryDocument(string DeliveryId,string SubscriptionId,string EventId,string EventType,string Status,int AttemptCount,DateTimeOffset NextAttemptAt,int? LastHttpStatus,string? LastErrorCode,DateTimeOffset CreatedAt,DateTimeOffset? SucceededAt,DateTimeOffset? DeadLetteredAt,bool Replayable);
public sealed record EmptyWebhookCommand;
