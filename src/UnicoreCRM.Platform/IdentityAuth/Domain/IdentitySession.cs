namespace UnicoreCRM.Platform.IdentityAuth.Domain;

internal sealed class IdentitySession
{
    private IdentitySession() { }

    internal IdentitySession(
        string accountId,
        string refreshTokenHash,
        string deviceLabel,
        string? userAgent,
        DateTimeOffset now,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt)
    {
        SessionId = IdentityIds.New("ses");
        AccountId = accountId;
        RefreshTokenHash = refreshTokenHash;
        DeviceId = IdentityIds.New("dev");
        DeviceLabel = deviceLabel;
        UserAgent = userAgent;
        Status = SessionStatus.Active;
        IssuedAt = now;
        LastSeenAt = now;
        IdleExpiresAt = idleExpiresAt;
        AbsoluteExpiresAt = absoluteExpiresAt;
    }

    public string SessionId { get; private set; } = null!;
    public string AccountId { get; private set; } = null!;
    public string RefreshTokenHash { get; private set; } = null!;
    public int RefreshCounter { get; private set; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset IdleExpiresAt { get; private set; }
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokeReason { get; private set; }
    public string DeviceId { get; private set; } = null!;
    public string DeviceLabel { get; private set; } = null!;
    public string? UserAgent { get; private set; }

    internal bool CanRefresh(DateTimeOffset now) =>
        Status == SessionStatus.Active && now < IdleExpiresAt && now < AbsoluteExpiresAt;

    internal void SetInitialRefreshHash(string refreshTokenHash) => RefreshTokenHash = refreshTokenHash;

    internal void Rotate(DateTimeOffset now, TimeSpan idleLifetime)
    {
        if (!CanRefresh(now))
        {
            throw new InvalidOperationException("An inactive session cannot be refreshed.");
        }

        RefreshCounter++;
        LastSeenAt = now;
        var proposedIdleExpiry = now.Add(idleLifetime);
        IdleExpiresAt = proposedIdleExpiry < AbsoluteExpiresAt ? proposedIdleExpiry : AbsoluteExpiresAt;
    }

    internal void SetCurrentRefreshHash(string refreshTokenHash) => RefreshTokenHash = refreshTokenHash;

    internal void Revoke(DateTimeOffset now, string? reason)
    {
        if (Status == SessionStatus.Revoked)
        {
            return;
        }

        Status = SessionStatus.Revoked;
        RevokedAt = now;
        RevokeReason = reason;
    }
}

internal enum SessionStatus
{
    Active,
    Revoked
}
