using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal static partial class DealValidation
{
    internal static bool TryCreate(
        CreateDealRequest request,
        out DealProfile? profile,
        out DealStageDefinition? stage,
        out DealForecastCategory forecastCategory,
        out DateTimeOffset? nextActionAt,
        out string? nextActionSummary,
        out string? nextActionTaskId,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = Text(request.Name, "name", 1, 240, true, fields);
        var buyer = Buyer(request.BuyerRef, fields);
        var stageCode = Text(request.StageCode, "stageCode", 1, 120, true, fields);
        stage = stageCode is not null && DealStages.TryGet(stageCode, out var found) ? found : null;
        var amount = Money(request.Amount, "amount", fields);
        var score = Percentage(request.OpportunityScore, "opportunityScore", true, fields);
        var ownerId = Entity(request.OwnerId, "ownerId", true, fields);
        var closeDate = BusinessDate(request.ExpectedCloseDate, "expectedCloseDate", true, fields);
        var interestedProductIds = EntityList(request.InterestedProductIds, "interestedProductIds", true, 250, fields);
        RejectLineItems(request.LineItems, fields);
        var contactId = Entity(request.ContactId, "contactId", false, fields);
        var sourceLeadId = Entity(request.SourceLeadId, "sourceLeadId", false, fields);
        var notes = Text(request.Notes, "notes", 0, 4000, false, fields);
        forecastCategory = ParseForecastCategory(request.ForecastCategory, "forecastCategory", fields) ?? DealForecastCategory.Pipeline;
        nextActionAt = Utc(request.NextActionAt, "nextActionAt", false, fields);
        nextActionSummary = Text(request.NextActionSummary, "nextActionSummary", 0, 1000, false, fields);
        nextActionTaskId = Entity(request.NextActionTaskId, "nextActionTaskId", false, fields);

        errors = fields;
        if (fields.Count != 0 || stage is null)
        {
            profile = null;
            return fields.Count == 0;
        }

        forecastCategory = request.ForecastCategory is null
            ? DealStages.DeriveForecast(stage.Code, score!)
            : forecastCategory;
        profile = new DealProfile(
            name!, buyer!, amount!, score!, ownerId!, closeDate!.Value,
            contactId, sourceLeadId, interestedProductIds, notes);
        return true;
    }

    internal static bool TryProfile(
        ReplaceDealProfileRequest request,
        string opportunityScore,
        string ownerId,
        DateOnly expectedCloseDate,
        out DealProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = Text(request.Name, "name", 1, 240, true, fields);
        var buyer = Buyer(request.BuyerRef, fields);
        var amount = Money(request.Amount, "amount", fields);
        var interestedProductIds = EntityList(request.InterestedProductIds, "interestedProductIds", true, 250, fields);
        RejectLineItems(request.LineItems, fields);
        var contactId = Entity(request.ContactId, "contactId", false, fields);
        var sourceLeadId = Entity(request.SourceLeadId, "sourceLeadId", false, fields);
        var notes = Text(request.Notes, "notes", 0, 4000, false, fields);
        errors = fields;
        profile = fields.Count == 0
            ? new DealProfile(name!, buyer!, amount!, opportunityScore, ownerId, expectedCloseDate, contactId, sourceLeadId, interestedProductIds, notes)
            : null;
        return fields.Count == 0;
    }

    internal static IReadOnlyDictionary<string, string[]> ProgressiveProfileErrors(
        DealProfile profile,
        string stageCode,
        DealForecastCategory? forecastCategory)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (profile.Name.Length == 0)
            fields["name"] = ["name is required for this Deal stage."];
        if (profile.BuyerRef.Id.Length == 0)
            fields["buyerRef"] = ["buyerRef is required for this Deal stage."];
        if (profile.OwnerId.Length == 0)
            fields["ownerId"] = ["ownerId is required for this Deal stage."];
        if (stageCode is "PROPOSAL" or "NEGOTIATION")
        {
            if (DealDecimal.ParseScaled(profile.Amount.Amount) <= 0)
                fields["amount"] = ["amount must be greater than zero for PROPOSAL or NEGOTIATION."];
            if (forecastCategory is null)
                fields["forecastCategory"] = ["forecastCategory is required for PROPOSAL or NEGOTIATION."];
        }
        return fields;
    }

    internal static bool TryForecast(
        UpdateDealForecastRequest request,
        Deal deal,
        out DateOnly expectedCloseDate,
        out string opportunityScore,
        out DealForecastCategory forecastCategory,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ExpectedCloseDate is null && request.OpportunityScore is null && request.ForecastCategory is null)
            fields["body"] = ["At least one forecast field is required."];
        expectedCloseDate = request.ExpectedCloseDate is null
            ? deal.Profile.ExpectedCloseDate
            : BusinessDate(request.ExpectedCloseDate, "expectedCloseDate", true, fields) ?? deal.Profile.ExpectedCloseDate;
        opportunityScore = request.OpportunityScore is null
            ? deal.Profile.OpportunityScore
            : Percentage(request.OpportunityScore, "opportunityScore", true, fields) ?? deal.Profile.OpportunityScore;
        forecastCategory = ParseForecastCategory(request.ForecastCategory, "forecastCategory", fields) ?? deal.ForecastCategory;
        errors = fields;
        return fields.Count == 0;
    }

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
        sourceId = Entity(evidence?.SourceId, "evidence.sourceId", true, fields);
        occurredAt = Utc(evidence?.OccurredAt, "evidence.occurredAt", true, fields) ?? default;
        errors = fields;
        return fields.Count == 0;
    }

    internal static bool TryLost(
        MarkDealLostRequest request,
        out string? reason,
        out string? note,
        out DealRecycleDecision recycleDecision,
        out DateTimeOffset? revisitAt,
        out DealOperationError? semanticError)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        reason = Text(request.Reason, "reason", 1, 500, true, fields);
        note = Text(request.Note, "note", 0, 2000, false, fields);
        recycleDecision = request.RecycleDecision switch
        {
            "RECYCLE" => DealRecycleDecision.Recycle,
            "CONDITIONAL" => DealRecycleDecision.Conditional,
            "DO_NOT_RECYCLE" => DealRecycleDecision.DoNotRecycle,
            _ => InvalidRecycle(fields)
        };
        revisitAt = Utc(request.RevisitAt, "revisitAt", false, fields);
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

    internal static bool IsEntityId(string? value) => value is not null && EntityIdPattern().IsMatch(value);

    internal static bool TryCursor(string? cursor, IDictionary<string, string[]> fields, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor))
            return true;
        if (cursor.Length > 512)
        {
            fields["cursor"] = ["cursor must contain at most 512 characters."];
            return false;
        }
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            if (bytes.Length != sizeof(int))
                throw new FormatException();
            offset = BitConverter.ToInt32(bytes);
            if (offset < 0)
                throw new FormatException();
            return true;
        }
        catch (FormatException)
        {
            fields["cursor"] = ["cursor is invalid."];
            return false;
        }
    }

    internal static string Cursor(int offset) => WebEncoders.Base64UrlEncode(BitConverter.GetBytes(offset));

    internal static string? RequiredText(
        string? input,
        string field,
        int maximum,
        IDictionary<string, string[]> fields) => Text(input, field, 1, maximum, true, fields);

    internal static string? OptionalText(
        string? input,
        string field,
        int maximum,
        IDictionary<string, string[]> fields) => Text(input, field, 0, maximum, false, fields);

    internal static string? OptionalEntity(
        string? input,
        string field,
        IDictionary<string, string[]> fields) => Entity(input, field, false, fields);

    internal static DateTimeOffset? RequiredUtc(
        string? input,
        string field,
        IDictionary<string, string[]> fields) => Utc(input, field, true, fields);

    internal static DealForecastCategory? ForecastCategory(
        string? input,
        string field,
        IDictionary<string, string[]> fields) => ParseForecastCategory(input, field, fields);

    private static DealBuyer? Buyer(DealBuyerReference? input, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            fields["buyerRef"] = ["buyerRef is required."];
            return null;
        }
        if (input.Type is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["buyerRef.type"] = ["buyerRef.type must be CONTACT or ORGANIZATION_ACCOUNT."];
        var id = Entity(input.Id, "buyerRef.id", true, fields);
        return id is null || fields.ContainsKey("buyerRef.type") ? null : new DealBuyer(input.Type!, id);
    }

    private static DealMoneyValue? Money(DealMoney? input, string field, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            fields[field] = [$"{field} is required."];
            return null;
        }
        if (input.Amount is null || !DecimalPattern().IsMatch(input.Amount))
            fields[$"{field}.amount"] = ["amount must be a base-10 decimal string with at most 6 fractional digits."];
        if (input.Currency is null || !CurrencyPattern().IsMatch(input.Currency))
            fields[$"{field}.currency"] = ["currency must be a three-letter uppercase code."];
        return fields.ContainsKey($"{field}.amount") || fields.ContainsKey($"{field}.currency")
            ? null
            : new DealMoneyValue(input.Amount!, input.Currency!);
    }

    private static IReadOnlyList<string> EntityList(
        IReadOnlyList<string>? input,
        string field,
        bool required,
        int maximum,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return [];
        }
        if (input.Count > maximum)
            fields[field] = [$"{field} cannot contain more than {maximum} entries."];
        var values = new List<string>(input.Count);
        for (var index = 0; index < input.Count; index++)
        {
            var value = Entity(input[index], $"{field}[{index}]", true, fields);
            if (value is not null)
                values.Add(value);
        }
        if (values.Count != values.Distinct(StringComparer.Ordinal).Count())
            fields[field] = [$"{field} cannot contain duplicate identifiers."];
        return values;
    }

    private static void RejectLineItems(IReadOnlyList<DealLineInput>? input, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            fields["lineItems"] = ["lineItems is required."];
            return;
        }
        if (input.Count > 250)
            fields["lineItems"] = ["lineItems cannot contain more than 250 entries."];
        else if (input.Count != 0)
            fields["lineItems"] = ["lineItems require an admitted Products snapshot and pricing contract and are not available in B06 Deals Core."];
    }

    private static string? Percentage(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!PercentagePattern().IsMatch(input))
            fields[field] = [$"{field} must be a decimal percentage from 0 through 100 with at most 6 fractional digits."];
        return input;
    }

    private static DealForecastCategory? ParseForecastCategory(
        string? input,
        string field,
        IDictionary<string, string[]> fields) =>
        input switch
        {
            null => null,
            "COMMIT" => DealForecastCategory.Commit,
            "BEST_CASE" => DealForecastCategory.BestCase,
            "PIPELINE" => DealForecastCategory.Pipeline,
            _ => InvalidForecast(field, fields)
        };

    private static DateOnly? BusinessDate(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input))
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            fields[field] = [$"{field} must be a business date in yyyy-MM-dd format."];
            return null;
        }
        return value;
    }

    private static DateTimeOffset? Utc(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input))
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!input.EndsWith('Z')
            || !DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            fields[field] = [$"{field} must be a UTC date-time ending in Z."];
            return null;
        }
        return value.ToUniversalTime();
    }

    private static string? Text(
        string? input,
        string field,
        int minimum,
        int maximum,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        var value = input.Trim();
        if (value.Length < minimum || value.Length > maximum)
            fields[field] = [$"{field} must contain between {minimum} and {maximum} characters."];
        return value.Length == 0 && !required ? null : value;
    }

    private static string? Entity(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, 1, 128, required, fields);
        if (value is not null && !EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
        return value;
    }

    private static DealRecycleDecision InvalidRecycle(IDictionary<string, string[]> fields)
    {
        fields["recycleDecision"] = ["recycleDecision must be RECYCLE, CONDITIONAL, or DO_NOT_RECYCLE."];
        return DealRecycleDecision.DoNotRecycle;
    }

    private static DealForecastCategory? InvalidForecast(string field, IDictionary<string, string[]> fields)
    {
        fields[field] = [$"{field} must be COMMIT, BEST_CASE, or PIPELINE."];
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();

    [GeneratedRegex("^(?:(?:0|[1-9][0-9]?)(?:\\.[0-9]{1,6})?|100(?:\\.0{1,6})?)$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentagePattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
