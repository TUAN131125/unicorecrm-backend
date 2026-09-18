namespace UnicoreCRM.Communications.Domain;

/// <summary>
/// Communications-owned record of a composed and eventually delivered email.
/// CustomerId and SupportCaseId are opaque foreign-owner references only; this owner never
/// creates foreign keys or navigation properties into CRM or Operations persistence.
/// </summary>
internal sealed class EmailMessage
{
    private EmailMessage() { }

    internal EmailMessage(
        string workspaceId,
        string senderProfileId,
        string actorMembershipId,
        string subject,
        string body,
        string? customerId,
        string? supportCaseId,
        DateTimeOffset now)
    {
        EmailMessageId = CommunicationIds.New("email");
        WorkspaceId = Require(workspaceId, nameof(workspaceId));
        SenderProfileId = Require(senderProfileId, nameof(senderProfileId));
        ActorMembershipId = Require(actorMembershipId, nameof(actorMembershipId));
        Subject = Require(subject, nameof(subject));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        CustomerId = NullIfWhiteSpace(customerId);
        SupportCaseId = NullIfWhiteSpace(supportCaseId);
        Status = EmailMessageStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string EmailMessageId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string SenderProfileId { get; private set; } = null!;
    public string ActorMembershipId { get; private set; } = null!;
    public string? CustomerId { get; private set; }
    public string? SupportCaseId { get; private set; }
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public EmailMessageStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public long Version { get; private set; }

    internal void MarkSending(DateTimeOffset now)
    {
        Status = EmailMessageStatus.Sending;
        FailureCode = null;
        FailureMessage = null;
        Touch(now);
    }

    internal void MarkSent(
        string providerMessageId,
        string? providerThreadId,
        DateTimeOffset now)
    {
        ProviderMessageId = Require(providerMessageId, nameof(providerMessageId));
        ProviderThreadId = NullIfWhiteSpace(providerThreadId);
        Status = EmailMessageStatus.Sent;
        FailureCode = null;
        FailureMessage = null;
        SentAt = now;
        Touch(now);
    }

    internal void MarkFailed(string failureCode, string? failureMessage, DateTimeOffset now)
    {
        FailureCode = Require(failureCode, nameof(failureCode));
        FailureMessage = NullIfWhiteSpace(failureMessage);
        Status = EmailMessageStatus.Failed;
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

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class EmailRecipient
{
    private EmailRecipient() { }

    internal EmailRecipient(
        string workspaceId,
        string emailMessageId,
        EmailRecipientType type,
        string emailAddress,
        string? displayName,
        DateTimeOffset now)
    {
        EmailRecipientId = CommunicationIds.New("recipient");
        WorkspaceId = Require(workspaceId, nameof(workspaceId));
        EmailMessageId = Require(emailMessageId, nameof(emailMessageId));
        Type = type;
        EmailAddress = NormalizeEmail(emailAddress, nameof(emailAddress));
        DisplayName = NullIfWhiteSpace(displayName);
        CreatedAt = now;
    }

    public string EmailRecipientId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string EmailMessageId { get; private set; } = null!;
    public EmailRecipientType Type { get; private set; }
    public string EmailAddress { get; private set; } = null!;
    public string? DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();

    private static string NormalizeEmail(string value, string parameterName) =>
        Require(value, parameterName).ToLowerInvariant();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
