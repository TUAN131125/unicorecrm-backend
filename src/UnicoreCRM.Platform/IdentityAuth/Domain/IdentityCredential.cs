namespace UnicoreCRM.Platform.IdentityAuth.Domain;

internal sealed class IdentityCredential
{
    private IdentityCredential() { }

    internal IdentityCredential(string accountId, string passwordHash, DateTimeOffset now)
    {
        AccountId = accountId;
        PasswordHash = passwordHash;
        UpdatedAt = now;
    }

    public string AccountId { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset UpdatedAt { get; private set; }
}
