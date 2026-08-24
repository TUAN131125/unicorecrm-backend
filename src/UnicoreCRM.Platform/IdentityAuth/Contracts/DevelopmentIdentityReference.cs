namespace UnicoreCRM.Platform.IdentityAuth.Contracts;

internal interface IDevelopmentIdentityReferenceLookup
{
    Task<DevelopmentIdentityReference?> FindActiveByEmailAsync(string email, CancellationToken cancellationToken);
}

internal sealed record DevelopmentIdentityReference(string AccountId, string MemberId);
