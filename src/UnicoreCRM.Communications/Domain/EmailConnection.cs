namespace UnicoreCRM.Communications.Domain;

/// <summary>
/// Provider authorization metadata for a Workspace mailbox. Secret material is deliberately
/// externalized behind CredentialReference so no OAuth refresh token is persisted in clear text
/// inside Communications tables.
/// </summary>
internal sealed class EmailConnection
{
    private EmailConnection() { }

    internal EmailConnection(
        string workspaceId,
        EmailProviderKind provider,
        string emailAddress,
        string credentialReference,
        string? ownerMembershipId,
        string? providerAccountId,
        DateTimeOffset now)
    {
        EmailConnectionId = CommunicationIds.New("mailconn");
        WorkspaceId = Require(workspaceId, nameof(workspaceId));
        Provider = provider;
        EmailAddress = NormalizeEmail(emailAddress, nameof(emailAddress));
        CredentialReference = Require(credentialReference, nameof(credentialReference));
        OwnerMembershipId = NullIfWhiteSpace(ownerMembershipId);
        ProviderAccountId = NullIfWhiteSpace(providerAccountId);
        Status = EmailConnectionStatus.Connected;
        ConnectedAt = now;
        UpdatedAt = now;
    }

    public string EmailConnectionId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public EmailProviderKind Provider { get; private set; }
    public string EmailAddress { get; private set; } = null!;
    public string CredentialReference { get; private set; } = null!;
    public string? OwnerMembershipId { get; private set; }
    public string? ProviderAccountId { get; private set; }
    public EmailConnectionStatus Status { get; private set; }
    public DateTimeOffset ConnectedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public long Version { get; private set; }

    internal void ReplaceCredentialReference(string credentialReference, DateTimeOffset now)
    {
        CredentialReference = Require(credentialReference, nameof(credentialReference));
        Status = EmailConnectionStatus.Connected;
        RevokedAt = null;
        Touch(now);
    }

    internal void Revoke(DateTimeOffset now)
    {
        Status = EmailConnectionStatus.Revoked;
        RevokedAt = now;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();

    private static string NormalizeEmail(string value, string parameterName) =>
        Require(value, parameterName).ToLowerInvariant();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
