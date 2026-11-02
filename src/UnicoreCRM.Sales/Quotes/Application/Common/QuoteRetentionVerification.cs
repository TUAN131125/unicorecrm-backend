using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal static class QuoteRetentionVerification
{
    internal static bool Available(IEnumerable<IQuoteRetentionVerifier>? verifiers)
    {
        var owners = verifiers?.Select(v => v.Owner).ToArray() ?? [];
        return owners.Length == 3 && Enum.GetValues<QuoteRetentionOwner>().All(o => owners.Count(x => x == o) == 1);
    }
}
