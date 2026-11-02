using System.Text.Json.Serialization;

namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordQuoteSendEvidenceRequest(string DeliveryId, string Channel, string EvidenceType,
    string? RecipientEmail = null, string? Recipient = null, string? Note = null, string? SentAt = null, string? FileName = null);
