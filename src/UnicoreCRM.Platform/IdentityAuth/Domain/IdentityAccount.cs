namespace UnicoreCRM.Platform.IdentityAuth.Domain;

internal sealed class IdentityAccount
{
    private IdentityAccount() { }

    internal IdentityAccount(string email, string displayName, DateTimeOffset now, bool emailVerified = false)
    {
        AccountId = IdentityIds.New("acc");
        MemberId = IdentityIds.New("mem");
        Email = email;
        NormalizedEmail = email.ToUpperInvariant();
        DisplayName = displayName;
        Status = emailVerified ? AccountStatus.Active : AccountStatus.PendingVerification;
        CreatedAt = now;
        EmailVerifiedAt = emailVerified ? now : null;
    }

    public string AccountId { get; private set; } = null!;
    public string MemberId { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public AccountStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    /// <summary>
    /// Activates an account whose email ownership has just been proved. Only a
    /// <see cref="AccountStatus.PendingVerification"/> account may be activated this way: a suspended
    /// account is never reinstated by email verification, and an already active account is never
    /// re-stamped.
    /// </summary>
    internal void MarkEmailVerified(DateTimeOffset now)
    {
        if (Status != AccountStatus.PendingVerification)
        {
            throw new InvalidOperationException("Only an account awaiting verification can be activated by email verification.");
        }

        Status = AccountStatus.Active;
        EmailVerifiedAt = now;
    }
}

internal enum AccountStatus
{
    PendingVerification,
    Active,
    Suspended
}

internal static class IdentityIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
