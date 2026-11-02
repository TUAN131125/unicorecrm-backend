namespace UnicoreCRM.Sales.Quotes.Application.Common;

/// <summary>Product decision D1: issuance binds commercial content, never an internal approval decision.
/// The legacy document/delivery policy-version slot remains readable for historical compatibility.
/// No approval state is synthesized or changed by this policy.</summary>
internal static class QuoteDirectIssuancePolicy
{
    internal const string Version = "direct-issuance-v1";
}
