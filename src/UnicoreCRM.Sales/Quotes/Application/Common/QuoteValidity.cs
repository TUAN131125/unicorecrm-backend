using System.Globalization;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal static class QuoteValidity
{
    internal static QuoteValidityReadModel Evaluate(DateOnly? validUntil, string? timeZone, DateTimeOffset now)
    {
        TimeZoneInfo zone;
        var at = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? ""); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { return new("TIMEZONE_UNAVAILABLE", at); }
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return new(validUntil is null ? "NO_EXPIRY" : date > validUntil.Value ? "OVERDUE" : "VALID", at)
        { TimeZone = timeZone, LocalDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
    }
}
