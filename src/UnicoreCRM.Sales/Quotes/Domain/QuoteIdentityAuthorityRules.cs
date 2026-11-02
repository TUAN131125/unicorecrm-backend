namespace UnicoreCRM.Sales.Quotes.Domain;

internal sealed record QuoteIdentityCaseBinding(string WorkspaceId,string CaseId,string QuoteId,string RootQuoteId,int QuoteRevision,
    string DocumentId,string IssuedPdfSha256,string CommercialFingerprint,string BuyerType,string BuyerId);
internal sealed record QuoteIdentityProofReference(QuoteIdentityCaseBinding Binding,string ProofId,string ProofSha256,bool EffectiveClean);

/// <summary>
/// Applicability predicates only. Passing these checks does not establish source authenticity,
/// identity, signer authority or customer acceptance. Inputs must come from their owner contracts.
/// No public endpoint or source adapter consumes these predicates until its boundary is admitted.
/// </summary>
internal static class QuoteIdentityAuthorityRules
{
    internal static bool MatchesBuyer(string expectedType,string expectedId,string actualType,string actualId)=>
        expectedType is "CONTACT" or "ORGANIZATION_ACCOUNT" && !string.IsNullOrWhiteSpace(expectedId)
        && expectedType==actualType && expectedId==actualId;

    internal static bool MatchesCleanProof(QuoteIdentityCaseBinding expected,QuoteIdentityProofReference proof)=>
        ValidBinding(expected) && proof.EffectiveClean && !string.IsNullOrWhiteSpace(proof.ProofId) && Hash(proof.ProofSha256)
        && expected==proof.Binding;

    internal static bool CoversAmount(string quoteCurrency,decimal grandTotal,string? authorityCurrency,decimal? authorityLimit,
        IReadOnlyDictionary<string,decimal> workflowCurrencyLimits)=>
        grandTotal>=0 && authorityLimit is >=0 && authorityCurrency==quoteCurrency
        && workflowCurrencyLimits.TryGetValue(quoteCurrency,out var workflowLimit) && workflowLimit>=0
        && grandTotal<=workflowLimit && grandTotal<=authorityLimit.Value;

    // Unknown revocation/validity never becomes "not revoked" or unlimited authority.
    internal static bool IsEffective(DateTimeOffset? effectiveFrom,DateTimeOffset? effectiveUntil,bool? revoked,DateTimeOffset now)=>
        revoked==false && effectiveFrom is {} start && effectiveUntil is {} end && start<end && start<=now && now<end;

    // Freshness is a pre-decision predicate, never a timer that invalidates historical acceptance.
    internal static bool IsFresh(DateTimeOffset? checkedAt,int maximumHours,DateTimeOffset now)=>
        maximumHours is >=1 and <=24 && checkedAt is {} checkedTime && checkedTime<=now && checkedTime>=now.AddHours(-maximumHours);

    private static bool ValidBinding(QuoteIdentityCaseBinding b)=>b.QuoteRevision>=1
        && new[]{b.WorkspaceId,b.CaseId,b.QuoteId,b.RootQuoteId,b.DocumentId,b.BuyerId}.All(v=>!string.IsNullOrWhiteSpace(v))
        && Hash(b.IssuedPdfSha256) && Hash(b.CommercialFingerprint) && MatchesBuyer(b.BuyerType,b.BuyerId,b.BuyerType,b.BuyerId);
    private static bool Hash(string value)=>value is {Length:64} && value.All(Uri.IsHexDigit);
}
