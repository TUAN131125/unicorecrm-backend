using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.MarkDealWon;

internal static class MarkDealWonValidation
{
    internal static bool TryWinEvidence(
        DealWinEvidence? evidence,
        out string? type,
        out string? sourceId,
        out DateTimeOffset occurredAt,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        type = evidence?.Type;
        if (type is not ("QUOTE_ACCEPTED" or "ORDER_CONFIRMED"))
            fields["evidence.type"] = ["evidence.type must be QUOTE_ACCEPTED or ORDER_CONFIRMED."];
        sourceId = DealValidation.Entity(evidence?.SourceId, "evidence.sourceId", true, fields);
        occurredAt = DealValidation.Utc(evidence?.OccurredAt, "evidence.occurredAt", true, fields) ?? default;
        errors = fields;
        return fields.Count == 0;
    }
}
