using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Invoices.Contracts;
using UnicoreCRM.Billing.Invoices.Domain;

namespace UnicoreCRM.Billing.Invoices.Application.Common;

/// <summary>
/// Projects Invoices-owned durable state onto the adopted <c>InvoiceDocument</c> contract.
///
/// <para>Every constraint applied here is transcribed from the adopted OpenAPI schema - required
/// members, enum vocabularies, identifier and currency patterns, decimal-string money format, UTC
/// timestamps, date format, string bounds, uniqueness and array minimums. Nothing is inferred from
/// a field name and no accounting or lifecycle rule is invented: money is emitted exactly as
/// persisted and is never recomputed on a read.</para>
///
/// <para>Persisted state that does not satisfy the contract throws, so the operation fails closed
/// and no partial or contract-invalid Invoice is emitted.</para>
/// </summary>
internal static partial class InvoiceProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly HashSet<string> BuyerTypes = ["CONTACT", "ORGANIZATION_ACCOUNT"];
    private static readonly HashSet<string> LifecycleStates = ["DRAFT", "ISSUING", "ISSUED", "ISSUE_FAILED", "DISCARDED", "VOIDED"];
    private static readonly HashSet<string> DeliveryStates = ["NOT_SENT", "SENDING", "SENT", "DELIVERY_FAILED"];
    private static readonly HashSet<string> ExchangeRateSources = ["MANUAL", "CONNECTED_PROVIDER"];
    private static readonly HashSet<string> EvidenceTypes = ["PAYMENT_PROOF", "COD_REMITTANCE", "INVOICE_ISSUE_RESULT", "DELIVERY_POD", "RETURN_INSPECTION", "REFUND", "REPLACEMENT_DELIVERY", "OTHER"];
    private static readonly HashSet<string> EvidenceVerificationStates = ["UNVERIFIED", "VERIFIED", "REJECTED"];

    internal static InvoiceDocument Document(Invoice invoice)
    {
        EntityId(invoice.WorkspaceId, nameof(invoice.WorkspaceId));
        EntityId(invoice.InvoiceId, nameof(invoice.InvoiceId));
        OptionalText(invoice.InvoiceNumber, 0, 160, nameof(invoice.InvoiceNumber));
        Enum(invoice.BuyerType, BuyerTypes, nameof(invoice.BuyerType));
        EntityId(invoice.BuyerId, nameof(invoice.BuyerId));
        Enum(invoice.LifecycleState, LifecycleStates, nameof(invoice.LifecycleState));
        Enum(invoice.DeliveryState, DeliveryStates, nameof(invoice.DeliveryState));
        Currency(invoice.Currency, nameof(invoice.Currency));
        OptionalText(invoice.PaymentTerms, 0, 500, nameof(invoice.PaymentTerms));
        OptionalText(invoice.CreationIntentId, 0, 160, nameof(invoice.CreationIntentId));
        OptionalText(invoice.IssueFailureCode, 0, 160, nameof(invoice.IssueFailureCode));
        OptionalText(invoice.VoidReason, 0, 1000, nameof(invoice.VoidReason));
        Text(invoice.IdempotencyKey, 8, 128, nameof(invoice.IdempotencyKey));
        if (invoice.ResourceVersion < 0)
            Invalid("Invoice version is outside the read contract.");

        var sellerSnapshot = Read<InvoiceLegalPartySnapshot>(invoice.SellerSnapshotJson, "sellerSnapshot");
        ValidateLegalParty(sellerSnapshot, "sellerSnapshot");
        var buyerSnapshot = Read<InvoiceLegalPartySnapshot>(invoice.BuyerSnapshotJson, "buyerSnapshot");
        ValidateLegalParty(buyerSnapshot, "buyerSnapshot");

        var lines = Read<InvoiceLineDocument[]>(invoice.LinesJson, "lines");
        if (lines.Length < 1)
            Invalid("lines does not satisfy the read contract minimum.");
        foreach (var line in lines) ValidateLine(line);

        var totals = Read<InvoiceTotals>(invoice.TotalsJson, "totals");
        ValidateTotals(totals);

        var sourceLinks = Read<InvoiceSourceLinks>(invoice.SourceLinksJson, "sourceLinks");
        ValidateSourceLinks(sourceLinks);

        InvoiceExchangeRateSnapshot? exchangeRate = null;
        if (invoice.ExchangeRateSnapshotJson is not null)
        {
            exchangeRate = Read<InvoiceExchangeRateSnapshot>(invoice.ExchangeRateSnapshotJson, "exchangeRateSnapshot");
            ValidateExchangeRate(exchangeRate);
        }

        IReadOnlyList<InvoiceEvidenceItem>? issueEvidence = null;
        if (invoice.IssueEvidenceJson is not null)
        {
            issueEvidence = Read<InvoiceEvidenceItem[]>(invoice.IssueEvidenceJson, "issueEvidence");
            foreach (var item in issueEvidence) ValidateEvidence(item);
        }

        return new InvoiceDocument(
            invoice.InvoiceId,
            new InvoiceBuyerReference(invoice.BuyerType, invoice.BuyerId),
            sellerSnapshot,
            buyerSnapshot,
            invoice.LifecycleState,
            invoice.DeliveryState,
            invoice.Currency,
            lines,
            totals,
            sourceLinks,
            invoice.ResourceVersion,
            invoice.IdempotencyKey,
            Timestamp(invoice.CreatedAt),
            Timestamp(invoice.UpdatedAt))
        {
            WorkspaceId = invoice.WorkspaceId,
            InvoiceNumber = invoice.InvoiceNumber,
            IssueDate = Date(invoice.IssueDate),
            DueDate = Date(invoice.DueDate),
            ExchangeRateSnapshot = exchangeRate,
            PaymentTerms = invoice.PaymentTerms,
            CreationIntentId = invoice.CreationIntentId,
            IssuedAt = OptionalTimestamp(invoice.IssuedAt),
            IssueFailureCode = invoice.IssueFailureCode,
            IssueEvidence = issueEvidence,
            DiscardedAt = OptionalTimestamp(invoice.DiscardedAt),
            VoidedAt = OptionalTimestamp(invoice.VoidedAt),
            VoidReason = invoice.VoidReason
        };
    }

    private static void ValidateLegalParty(InvoiceLegalPartySnapshot party, string name)
    {
        if (party is null) Invalid($"{name} is required.");
        Text(party.DisplayName, 1, 240, $"{name}.displayName");
        if (party.AddressLines is null || party.AddressLines.Count > 12)
            Invalid($"{name}.addressLines is invalid.");
        foreach (var line in party.AddressLines) Text(line, 0, 500, $"{name}.addressLines");
        OptionalText(party.LegalName, 0, 240, $"{name}.legalName");
        OptionalText(party.TaxId, 0, 80, $"{name}.taxId");
        OptionalText(party.Phone, 0, 80, $"{name}.phone");
        if (party.Email is not null && (party.Email.Length > 320 || !EmailPattern().IsMatch(party.Email)))
            Invalid($"{name}.email is invalid.");
        if (party.CountryCode is not null && !CountryPattern().IsMatch(party.CountryCode))
            Invalid($"{name}.countryCode is invalid.");
    }

    private static void ValidateLine(InvoiceLineDocument line)
    {
        if (line is null) Invalid("lines contains a null entry.");
        EntityId(line.Id, "lines.id");
        Text(line.Description, 1, 1000, "lines.description");
        DecimalAmount(line.Quantity, "lines.quantity");
        ValidateMoney(line.UnitPrice, "lines.unitPrice");
        ValidateMoney(line.DiscountAmount, "lines.discountAmount");
        ValidateMoney(line.TaxAmount, "lines.taxAmount");
        ValidateMoney(line.LineTotal, "lines.lineTotal");
        OptionalEntityId(line.SourceOrderLineId, "lines.sourceOrderLineId");
        OptionalEntityId(line.OrderLineId, "lines.orderLineId");
        OptionalEntityId(line.ProductId, "lines.productId");
        OptionalText(line.SkuSnapshot, 0, 160, "lines.skuSnapshot");
        OptionalText(line.UnitOfMeasure, 0, 80, "lines.unitOfMeasure");
        OptionalDecimalAmount(line.SourceOrderQuantity, "lines.sourceOrderQuantity");
        OptionalDecimalAmount(line.AlreadyInvoicedQuantity, "lines.alreadyInvoicedQuantity");
        OptionalDecimalAmount(line.InvoiceableQuantity, "lines.invoiceableQuantity");
        OptionalDecimalAmount(line.DiscountRate, "lines.discountRate");
        OptionalDecimalAmount(line.TaxRate, "lines.taxRate");
        OptionalText(line.Notes, 0, 2000, "lines.notes");
    }

    private static void ValidateTotals(InvoiceTotals totals)
    {
        if (totals is null) Invalid("totals is required.");
        ValidateMoney(totals.Subtotal, "totals.subtotal");
        ValidateMoney(totals.DiscountTotal, "totals.discountTotal");
        ValidateMoney(totals.TaxTotal, "totals.taxTotal");
        ValidateMoney(totals.GrandTotal, "totals.grandTotal");
        if (totals.RoundingAdjustment is not null)
            ValidateMoney(totals.RoundingAdjustment, "totals.roundingAdjustment");
    }

    private static void ValidateSourceLinks(InvoiceSourceLinks sourceLinks)
    {
        if (sourceLinks is null) Invalid("sourceLinks is required.");
        OptionalEntityId(sourceLinks.OrderId, "sourceLinks.orderId");
        OptionalEntityIds(sourceLinks.PaymentScheduleLineIds, "sourceLinks.paymentScheduleLineIds");
        OptionalEntityIds(sourceLinks.ShippingBookingIds, "sourceLinks.shippingBookingIds");
        OptionalEntityIds(sourceLinks.ReturnIds, "sourceLinks.returnIds");
        if (sourceLinks.MilestoneCodes is null) return;
        if (sourceLinks.MilestoneCodes.Distinct(StringComparer.Ordinal).Count() != sourceLinks.MilestoneCodes.Count)
            Invalid("sourceLinks.milestoneCodes is invalid.");
        foreach (var code in sourceLinks.MilestoneCodes) Text(code, 0, 120, "sourceLinks.milestoneCodes");
    }

    private static void ValidateExchangeRate(InvoiceExchangeRateSnapshot snapshot)
    {
        Currency(snapshot.FromCurrency, "exchangeRateSnapshot.fromCurrency");
        Currency(snapshot.ToCurrency, "exchangeRateSnapshot.toCurrency");
        DecimalAmount(snapshot.Rate, "exchangeRateSnapshot.rate");
        ParseTimestamp(snapshot.EffectiveAt, "exchangeRateSnapshot.effectiveAt");
        Enum(snapshot.Source, ExchangeRateSources, "exchangeRateSnapshot.source");
        EntityId(snapshot.RateId, "exchangeRateSnapshot.rateId");
        if (snapshot.RateVersion < 0)
            Invalid("exchangeRateSnapshot.rateVersion is invalid.");
    }

    private static void ValidateEvidence(InvoiceEvidenceItem item)
    {
        if (item is null) Invalid("issueEvidence contains a null entry.");
        EntityId(item.Id, "issueEvidence.id");
        Enum(item.Type, EvidenceTypes, "issueEvidence.type");
        ParseTimestamp(item.CapturedAt, "issueEvidence.capturedAt");
        EntityId(item.CapturedBy, "issueEvidence.capturedBy");
        Enum(item.VerificationState, EvidenceVerificationStates, "issueEvidence.verificationState");
        ParseTimestamp(item.CreatedAt, "issueEvidence.createdAt");
        OptionalText(item.FileName, 0, 500, "issueEvidence.fileName");
        OptionalText(item.MimeType, 0, 200, "issueEvidence.mimeType");
        OptionalText(item.ExternalReference, 0, 500, "issueEvidence.externalReference");
        OptionalText(item.Notes, 0, 2000, "issueEvidence.notes");
        if (item.Url is not null
            && (item.Url.Length > 2000 || !Uri.TryCreate(item.Url, UriKind.RelativeOrAbsolute, out _)))
        {
            Invalid("issueEvidence.url is invalid.");
        }
    }

    private static T Read<T>(string json, string name)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            RejectDuplicateProperties(parsed.RootElement);
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new JsonException($"{name} cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Persisted Invoices {name} does not satisfy the admitted read contract.", exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    Invalid("Persisted Invoices JSON contains a duplicate property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private static void ValidateMoney(InvoiceMoney value, string name)
    {
        if (value is null) Invalid($"{name} is required.");
        DecimalAmount(value.Amount, $"{name}.amount");
        Currency(value.Currency, $"{name}.currency");
    }

    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? OptionalTimestamp(DateTimeOffset? value) => value is null ? null : Timestamp(value.Value);

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void ParseTimestamp(string value, string name)
    {
        if (value is null
            || !value.EndsWith('Z')
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            Invalid($"{name} is not a UTC timestamp.");
        }
    }

    private static void EntityId(string value, string name)
    {
        if (value is null || !EntityIdPattern().IsMatch(value)) Invalid($"{name} is invalid.");
    }

    private static void OptionalEntityId(string? value, string name)
    {
        if (value is not null) EntityId(value, name);
    }

    private static void OptionalEntityIds(IReadOnlyList<string>? values, string name)
    {
        if (values is null) return;
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count) Invalid($"{name} is invalid.");
        foreach (var value in values) EntityId(value, name);
    }

    private static void Currency(string value, string name)
    {
        if (value is null || !CurrencyPattern().IsMatch(value)) Invalid($"{name} is invalid.");
    }

    private static void DecimalAmount(string value, string name)
    {
        if (value is null || !DecimalPattern().IsMatch(value)) Invalid($"{name} is invalid.");
    }

    private static void OptionalDecimalAmount(string? value, string name)
    {
        if (value is not null) DecimalAmount(value, name);
    }

    private static void Enum(string value, HashSet<string> values, string name)
    {
        if (value is null || !values.Contains(value)) Invalid($"{name} is invalid.");
    }

    private static void Text(string value, int min, int max, string name)
    {
        if (value is null || value.Length < min || value.Length > max) Invalid($"{name} is invalid.");
    }

    private static void OptionalText(string? value, int min, int max, string name)
    {
        if (value is not null && (value.Length < min || value.Length > max)) Invalid($"{name} is invalid.");
    }

    [DoesNotReturn]
    private static void Invalid(string message) => throw new InvalidOperationException(message);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex EntityIdPattern();
    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)] private static partial Regex CurrencyPattern();
    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)] private static partial Regex CountryPattern();
    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)] private static partial Regex DecimalPattern();
    [GeneratedRegex("^[^@\\s]+@[^@\\s]+$", RegexOptions.CultureInvariant)] private static partial Regex EmailPattern();
}
