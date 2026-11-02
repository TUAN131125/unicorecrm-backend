using System.Text.Json.Serialization;
namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteRevisionRequest(string? Reason = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteExpirationRequest(string? Reason = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteArchiveRequest(string Reason);

public sealed record QuoteValidityReadModel(string State, string EvaluatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TimeZone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LocalDate { get; init; }
}
