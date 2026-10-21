using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;

namespace UnicoreCRM.Communications.Application;

internal static class TelegramConnectionLifecycle
{
    // Account row must be locked first. This helper participates in the caller's audit/config transaction.
    internal static async Task InvalidateAsync(CommunicationsDbContext db, ChannelAccount account, bool remove, bool disconnect, CancellationToken ct)
    {
        var connection = await db.TelegramConnections.SingleOrDefaultAsync(x => x.WorkspaceId == account.WorkspaceId && x.AccountId == account.Id, ct);
        if (connection is not null)
        {
            connection.VerifiedGeneration = null; connection.VerifiedConfigurationVersion = null; connection.LastError = null;
            if (remove) { connection.Ciphertext = null; connection.CredentialGeneration = checked(connection.CredentialGeneration + 1); }
            if (disconnect) connection.Disconnected = true;
        }
        var state = await db.TelegramPolling.SingleOrDefaultAsync(x => x.WorkspaceId == account.WorkspaceId && x.AccountId == account.Id, ct);
        if (state is not null)
        {
            state.Active = false;
            state.Fence = checked(state.Fence + 1); state.LeaseOwner = null; state.LeaseExpiry = null;
            state.RetryAfter = DateTimeOffset.MaxValue;
        }
    }
}
