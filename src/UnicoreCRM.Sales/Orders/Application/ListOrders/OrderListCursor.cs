using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using UnicoreCRM.Sales.Orders.Application.Common;
using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Application.ListOrders;

internal static class OrderListCursor
{
    private const int CurrentVersion = 1;
    private const int MaximumCursorLength = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string QueryFingerprint(OrderListQueryBinding query)
    {
        var json = JsonSerializer.Serialize(query, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    internal static bool TryParse(
        string? cursor,
        string sortBy,
        bool descending,
        string queryFingerprint,
        IDictionary<string, string[]> fields,
        out OrderListContinuation? continuation)
    {
        continuation = null;
        if (string.IsNullOrEmpty(cursor))
            return true;
        if (cursor.Length > MaximumCursorLength)
        {
            Invalid(fields);
            return false;
        }

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            var payload = JsonSerializer.Deserialize<CursorPayload>(bytes, JsonOptions);
            if (payload is null
                || payload.Version != CurrentVersion
                || string.IsNullOrEmpty(payload.SortBy)
                || string.IsNullOrEmpty(payload.SortDirection)
                || string.IsNullOrEmpty(payload.LastPrimary)
                || string.IsNullOrEmpty(payload.LastOrderId)
                || payload.LastOrderId.Length > 128
                || string.IsNullOrEmpty(payload.QueryFingerprint)
                || payload.QueryFingerprint.Length != 64)
            {
                throw new FormatException();
            }

            var direction = descending ? "desc" : "asc";
            if (!string.Equals(payload.SortBy, sortBy, StringComparison.Ordinal)
                || !string.Equals(payload.SortDirection, direction, StringComparison.Ordinal)
                || !string.Equals(payload.QueryFingerprint, queryFingerprint, StringComparison.Ordinal))
            {
                throw new FormatException();
            }

            continuation = ParseContinuation(payload, sortBy);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            Invalid(fields);
            return false;
        }
    }

    internal static string Encode(
        Order lastOrder,
        string sortBy,
        bool descending,
        string queryFingerprint)
    {
        var payload = new CursorPayload(
            CurrentVersion,
            sortBy,
            descending ? "desc" : "asc",
            Primary(lastOrder, sortBy),
            lastOrder.OrderId,
            queryFingerprint);
        return WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
    }

    private static OrderListContinuation ParseContinuation(CursorPayload payload, string sortBy) => sortBy switch
    {
        "updatedAt" or "createdAt" when DateTimeOffset.TryParseExact(
            payload.LastPrimary,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var timestamp) => new OrderTimestampContinuation(timestamp, payload.LastOrderId!),
        "orderDate" when DateOnly.TryParseExact(
            payload.LastPrimary,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date) => new OrderDateContinuation(date, payload.LastOrderId!),
        "grandTotal" when decimal.TryParse(
            payload.LastPrimary,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var amount) => new OrderAmountContinuation(amount, payload.LastOrderId!),
        "orderNumber" => new OrderTextContinuation(payload.LastPrimary!, payload.LastOrderId!),
        _ => throw new FormatException()
    };

    private static string Primary(Order order, string sortBy) => sortBy switch
    {
        "createdAt" => order.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        "orderDate" => order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "grandTotal" => order.GrandTotalAmount.ToString("G29", CultureInfo.InvariantCulture),
        "orderNumber" => order.OrderNumber,
        _ => order.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)
    };

    private static void Invalid(IDictionary<string, string[]> fields) =>
        fields["cursor"] = ["cursor is invalid."];

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        string? SortBy,
        string? SortDirection,
        string? LastPrimary,
        string? LastOrderId,
        string? QueryFingerprint);
}

internal sealed record OrderListQueryBinding(
    string? Search,
    IReadOnlyList<string> SearchableFields,
    string? State,
    string? SourceQuoteId,
    string? SourceDealId,
    string? BuyerType,
    string? BuyerId,
    string SortBy,
    string SortDirection);
