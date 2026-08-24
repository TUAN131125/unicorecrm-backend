using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.MarkDealLost;

internal static class MarkDealLostValidation
{
    internal static bool TryLost(
        MarkDealLostRequest request,
        out string? reason,
        out string? note,
        out DealRecycleDecision recycleDecision,
        out DateTimeOffset? revisitAt,
        out DealOperationError? semanticError)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        reason = DealValidation.Text(request.Reason, "reason", 1, 500, true, fields);
        note = DealValidation.Text(request.Note, "note", 0, 2000, false, fields);
        recycleDecision = request.RecycleDecision switch
        {
            "RECYCLE" => DealRecycleDecision.Recycle,
            "CONDITIONAL" => DealRecycleDecision.Conditional,
            "DO_NOT_RECYCLE" => DealRecycleDecision.DoNotRecycle,
            _ => InvalidRecycle(fields)
        };
        revisitAt = DealValidation.Utc(request.RevisitAt, "revisitAt", false, fields);
        if (fields.ContainsKey("reason"))
        {
            semanticError = DealErrors.LossReason(fields);
            return false;
        }
        if (fields.Count != 0)
        {
            semanticError = DealErrors.Validation(fields);
            return false;
        }
        if (recycleDecision is not DealRecycleDecision.DoNotRecycle && revisitAt is null)
        {
            semanticError = DealErrors.RecycleDate(new Dictionary<string, string[]> { ["revisitAt"] = ["revisitAt is required for recyclable Deal losses."] });
            return false;
        }
        semanticError = null;
        return true;
    }

    private static DealRecycleDecision InvalidRecycle(IDictionary<string, string[]> fields)
    {
        fields["recycleDecision"] = ["recycleDecision must be RECYCLE, CONDITIONAL, or DO_NOT_RECYCLE."];
        return DealRecycleDecision.DoNotRecycle;
    }
}
