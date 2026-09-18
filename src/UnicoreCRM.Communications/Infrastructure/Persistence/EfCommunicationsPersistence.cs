using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Application.Common;
using UnicoreCRM.Communications.Domain;

namespace UnicoreCRM.Communications.Infrastructure.Persistence;

internal sealed class EfCommunicationsPersistence(CommunicationsDbContext dbContext)
    : ICommunicationsPersistence
{
    public Task<SenderProfile?> ReadSenderProfileAsync(
        string workspaceId,
        string senderProfileId,
        CancellationToken cancellationToken) =>
        dbContext.SenderProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId
                        && item.SenderProfileId == senderProfileId,
                cancellationToken);

    public Task<SenderProfile?> LoadSenderProfileAsync(
        string workspaceId,
        string senderProfileId,
        CancellationToken cancellationToken) =>
        dbContext.SenderProfiles
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId
                        && item.SenderProfileId == senderProfileId,
                cancellationToken);

    public async Task<IReadOnlyList<SenderProfile>> ReadSenderProfilesAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.SenderProfiles
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.DisplayName)
            .ThenBy(item => item.SenderProfileId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<SenderProfile>> ReadEnabledSenderProfilesAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.SenderProfiles
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.IsEnabled)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.DisplayName)
            .ThenBy(item => item.SenderProfileId)
            .ToArrayAsync(cancellationToken);

    public Task<EmailConnection?> ReadEmailConnectionAsync(
        string workspaceId,
        string emailConnectionId,
        CancellationToken cancellationToken) =>
        dbContext.EmailConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId
                        && item.EmailConnectionId == emailConnectionId,
                cancellationToken);

    public Task<EmailConnection?> LoadEmailConnectionAsync(
        string workspaceId,
        string emailConnectionId,
        CancellationToken cancellationToken) =>
        dbContext.EmailConnections
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId
                        && item.EmailConnectionId == emailConnectionId,
                cancellationToken);

    public async Task<IReadOnlyList<EmailConnection>> ReadEmailConnectionsAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.EmailConnections
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .OrderBy(item => item.EmailAddress)
            .ThenBy(item => item.EmailConnectionId)
            .ToArrayAsync(cancellationToken);

    public Task<EmailMessage?> ReadEmailMessageAsync(
        string workspaceId,
        string emailMessageId,
        CancellationToken cancellationToken) =>
        dbContext.EmailMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId
                        && item.EmailMessageId == emailMessageId,
                cancellationToken);

    public Task<EmailMessage?> LoadEmailMessageAsync(
        string workspaceId,
        string emailMessageId,
        CancellationToken cancellationToken) =>
        dbContext.EmailMessages
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId
                        && item.EmailMessageId == emailMessageId,
                cancellationToken);

    public async Task<IReadOnlyList<EmailRecipient>> ReadRecipientsAsync(
        string workspaceId,
        string emailMessageId,
        CancellationToken cancellationToken) =>
        await dbContext.EmailRecipients
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId
                           && item.EmailMessageId == emailMessageId)
            .OrderBy(item => item.Type)
            .ThenBy(item => item.EmailAddress)
            .ToArrayAsync(cancellationToken);

    public void AddSenderProfile(SenderProfile senderProfile) =>
        dbContext.SenderProfiles.Add(senderProfile);

    public void AddEmailConnection(EmailConnection emailConnection) =>
        dbContext.EmailConnections.Add(emailConnection);

    public void AddEmailMessage(EmailMessage emailMessage) =>
        dbContext.EmailMessages.Add(emailMessage);

    public void AddEmailRecipient(EmailRecipient recipient) =>
        dbContext.EmailRecipients.Add(recipient);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CommunicationsPersistenceConcurrencyException();
        }
        catch (DbUpdateException exception)
            when (ContainsSqlError(exception, 2601) || ContainsSqlError(exception, 2627))
        {
            throw new CommunicationsPersistenceConflictException();
        }
        catch (Exception exception) when (ContainsSqlError(exception, 1205))
        {
            throw new CommunicationsPersistenceConcurrencyException();
        }
    }

    private static bool ContainsSqlError(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number == number)
                return true;
        }

        return false;
    }
}
