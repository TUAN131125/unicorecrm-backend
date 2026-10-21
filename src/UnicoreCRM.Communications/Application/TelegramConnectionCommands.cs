using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Telegram;

namespace UnicoreCRM.Communications.Application;

internal sealed partial class TelegramConnectionManagement
{
    internal async Task<TelegramConnectionDocument> CommandAsync(Guid id, string operation, long expectedVersion,
        string key, string correlation, CancellationToken ct)
    {
        var authority = await channels.Authorize(correlation, "credential", ct);
        if (operation is not ("verify" or "activate" or "deactivate" or "reconnect") || expectedVersion < 1
            || key.Length is < 1 or > 128 || key.Any(char.IsControl)) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        var workspace = authority.WorkspaceId;
        var requestKey = Hash(key);
        var fingerprint = Hash(JsonSerializer.Serialize(new { operation, id, expectedVersion }));
        if (operation != "deactivate" && !admission.Allowed) throw new CommunicationReadException("LIVE_NOT_ADMITTED", 503);
        var resource = $"communications:telegram:command:{id:N}";
        var intentResource = "communications:channel:" + Hash(workspace + "|" + authority.MemberId + "|" + requestKey);
        await db.Database.OpenConnectionAsync(ct);
        var locked = false;
        var intentLocked = false;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={intentResource},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; IF @r<0 THROW 51002,'CHANNEL_REQUEST_BUSY',1;", ct);
            intentLocked = true;
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; IF @r<0 THROW 51002,'CHANNEL_REQUEST_BUSY',1;", ct);
            locked = true;
            var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.Id == id, ct)
                ?? throw new CommunicationReadException("CHANNEL_NOT_FOUND", 404);
            if (account.ProviderKey != "telegram") throw new CommunicationReadException("CHANNEL_PROVIDER_UNSUPPORTED", 400);
            var replay = await db.ChannelMutations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.ActorMemberId == authority.MemberId && x.RequestKey == requestKey, ct);
            if (replay is not null)
            {
                if (replay.Fingerprint != fingerprint || replay.Version != account.ConfigurationVersion)
                    throw new CommunicationReadException("CHANNEL_IDEMPOTENCY_CONFLICT", 409);
                await Reauthorize();
                return await Project(workspace, id, ct); // current safe metadata; never resurrect stored state
            }
            if (account.ConfigurationVersion != expectedVersion) throw new CommunicationReadException("CHANNEL_VERSION_CONFLICT", 409);
            async Task Reauthorize()
            {
                var current = await channels.Authorize(correlation, "credential", ct);
                if (current != authority) throw new CommunicationReadException("ACCESS_DENIED", 403);
            }
            TelegramConnectionDocument? response = null;
            async Task Record(ChannelAccount current, TelegramConnection? row, TelegramPollingState? state)
            {
                await Reauthorize();
                var wasVerified = row is not null && row.VerifiedGeneration == row.CredentialGeneration && row.VerifiedConfigurationVersion == current.ConfigurationVersion;
                current.InvalidateConnection(clock.GetUtcNow());
                if (wasVerified) row!.VerifiedConfigurationVersion = current.ConfigurationVersion;
                if (state is not null)
                {
                    state.ConnectionGeneration = current.ConfigurationVersion;
                    state.Fence = checked(state.Fence + 1); state.LeaseOwner = null; state.LeaseExpiry = null;
                    state.Active = operation is "activate" or "reconnect";
                    if (state.Active) { state.ActivatedAt = clock.GetUtcNow(); state.LastSuccessfulPollAt = null; state.RetryAfter = null; state.FailureClassification = null; state.FailureCount = 0; }
                }
                await db.SaveChangesAsync(ct);
                response = await Project(workspace, id, ct);
                db.ChannelMutations.Add(new() { WorkspaceId = workspace, AccountId = id, ActorMemberId = authority.MemberId,
                    RequestKey = requestKey, Fingerprint = fingerprint, Operation = "TELEGRAM_" + operation.ToUpperInvariant(),
                    Version = current.ConfigurationVersion, RecordedAt = clock.GetUtcNow(), CorrelationId = correlation,
                    ResponseJson = JsonSerializer.Serialize(response, Json) });
            }
            if (operation == "deactivate")
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var current = await db.Accounts.FromSqlInterpolated($"SELECT * FROM communications.ChannelAccounts WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={workspace} AND Id={id}").SingleAsync(ct);
                if (current.ConfigurationVersion != expectedVersion) throw new CommunicationReadException("CHANNEL_VERSION_CONFLICT", 409);
                await Record(current, await db.TelegramConnections.SingleOrDefaultAsync(x => x.AccountId == id && x.WorkspaceId == workspace, ct),
                    await db.TelegramPolling.SingleOrDefaultAsync(x => x.AccountId == id && x.WorkspaceId == workspace, ct));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            else
            {
                if (!vault.Ready) throw new CommunicationReadException("SECURITY_NOT_READY", 503);
                if (account.Status != "DRAFT") throw new CommunicationReadException("ACCOUNT_INELIGIBLE", 409);
                if (operation is "activate" or "reconnect" && !pollingOptions.Enabled) throw new CommunicationReadException("RUNTIME_NOT_READY", 503);
                var credential = await vault.ResolveForVerificationAsync(workspace, id, ct, operation == "reconnect") ?? throw new TelegramFailure("CREDENTIAL_UNAVAILABLE");
                if (credential.ConfigurationVersion != expectedVersion) throw new TelegramFailure("CONNECTION_GENERATION_STALE");
                if (operation == "activate")
                {
                    var row = await db.TelegramConnections.AsNoTracking().SingleAsync(x => x.AccountId == id && x.WorkspaceId == workspace, ct);
                    var state = await db.TelegramPolling.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == id && x.WorkspaceId == workspace, ct);
                    if (row.VerifiedGeneration != row.CredentialGeneration || row.VerifiedConfigurationVersion != expectedVersion || state is null || state.Synthetic != admission.Synthetic)
                        throw new CommunicationReadException("VERIFICATION_REQUIRED", 409);
                }
                try
                {
                    await Reauthorize();
                    var me = await api.ReadAsync(credential, "getMe", 0, ct);
                    var bot = TelegramUpdateNormalizer.Id(me, "id");
                    if (!me.TryGetProperty("is_bot", out var flag) || flag.ValueKind != JsonValueKind.True) throw new TelegramFailure("BOT_BINDING_INVALID");
                    await Reauthorize();
                    var fresh = await vault.ResolveForVerificationAsync(workspace, id, ct, operation == "reconnect");
                    if (fresh is null || fresh.ConfigurationVersion != credential.ConfigurationVersion || fresh.CredentialGeneration != credential.CredentialGeneration || fresh.CredentialId != credential.CredentialId)
                        throw new TelegramFailure("CONNECTION_GENERATION_STALE");
                    var webhook = await api.ReadAsync(credential, "getWebhookInfo", 0, ct);
                    if (webhook.ValueKind != JsonValueKind.Object || !webhook.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) throw new TelegramFailure("RESPONSE_INVALID");
                    if (!string.IsNullOrEmpty(url.GetString())) throw new TelegramFailure("WEBHOOK_CONFLICT");
                    await CommitVerificationAsync(workspace, id, credential, bot, ct, Record, operation == "reconnect");
                }
                catch (TelegramFailure error)
                {
                    await Reauthorize();
                    await RecordFailureAsync(workspace, id, credential, error.Code, ct);
                    throw;
                }
            }
            return response ?? throw new CommunicationReadException("CHANNEL_REQUEST_BUSY", 503);
        }
        catch (SqlException e) when (e.Number == 51002) { throw new CommunicationReadException("CHANNEL_REQUEST_BUSY", 503); }
        finally
        {
            if (locked) await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource},@LockOwner='Session';", CancellationToken.None);
            if (intentLocked) await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={intentResource},@LockOwner='Session';", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
