using UnicoreCRM.Communications.Domain;

namespace UnicoreCRM.Communications.Application.Common;

internal interface ICommunicationsPersistence
{
    Task<SenderProfile?> ReadSenderProfileAsync(
        string workspaceId,
        string senderProfileId,
        CancellationToken cancellationToken);

    Task<SenderProfile?> LoadSenderProfileAsync(
        string workspaceId,
        string senderProfileId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SenderProfile>> ReadSenderProfilesAsync(
        string workspaceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SenderProfile>> ReadEnabledSenderProfilesAsync(
        string workspaceId,
        CancellationToken cancellationToken);

    Task<EmailConnection?> ReadEmailConnectionAsync(
        string workspaceId,
        string emailConnectionId,
        CancellationToken cancellationToken);

    Task<EmailConnection?> LoadEmailConnectionAsync(
        string workspaceId,
        string emailConnectionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailConnection>> ReadEmailConnectionsAsync(
        string workspaceId,
        CancellationToken cancellationToken);

    Task<EmailMessage?> ReadEmailMessageAsync(
        string workspaceId,
        string emailMessageId,
        CancellationToken cancellationToken);

    Task<EmailMessage?> LoadEmailMessageAsync(
        string workspaceId,
        string emailMessageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailRecipient>> ReadRecipientsAsync(
        string workspaceId,
        string emailMessageId,
        CancellationToken cancellationToken);

    void AddSenderProfile(SenderProfile senderProfile);
    void AddEmailConnection(EmailConnection emailConnection);
    void AddEmailMessage(EmailMessage emailMessage);
    void AddEmailRecipient(EmailRecipient recipient);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal sealed class CommunicationsPersistenceConcurrencyException : Exception;
internal sealed class CommunicationsPersistenceConflictException : Exception;
