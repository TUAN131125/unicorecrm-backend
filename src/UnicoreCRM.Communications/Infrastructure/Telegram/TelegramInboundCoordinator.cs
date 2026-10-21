using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;

namespace UnicoreCRM.Communications.Infrastructure.Telegram;

internal sealed class TelegramPollingOptions { public bool Enabled { get; set; } }

internal sealed class TelegramInboundCoordinator(CommunicationsDbContext db, TelegramApiClient api,
    ITelegramCredentials credentials, LocalCommunicationsStore core, TelegramPollingOptions options, TelegramProviderAdmission admission)
{
    // Internal trusted test admission only. Not an endpoint or production configuration path.
    internal async Task AdmitSyntheticAsync(string workspace, Guid id, long expectedBot, CancellationToken ct = default)
    {
        var credential = await credentials.ResolveAsync(workspace, id, ct);
        if (credential is not { Synthetic: true }) throw new TelegramFailure("SYNTHETIC_ADMISSION_DENIED");
        var me = await api.ReadAsync(credential, "getMe", 0, ct);
        if (TelegramUpdateNormalizer.Id(me, "id") != expectedBot || !me.TryGetProperty("is_bot", out var isBot) || isBot.ValueKind != System.Text.Json.JsonValueKind.True)
            throw new TelegramFailure("BOT_BINDING_INVALID");
        await CheckWebhook(credential, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await Account(workspace, id, ct);
        if (account is not { ProviderKey: "telegram", Status: "DRAFT" }) throw new TelegramFailure("ACCOUNT_INELIGIBLE");
        if (await db.TelegramPolling.AnyAsync(x => x.AccountId == id || x.BotId == expectedBot, ct)) throw new TelegramFailure("BOT_IDENTITY_CONFLICT");
        db.TelegramPolling.Add(new() { WorkspaceId = workspace, AccountId = id, BotId = expectedBot,
            Synthetic = true, Active = true, ActivatedAt = await Now(ct), ConnectionGeneration = account.ConfigurationVersion, AdmittedAt = await Now(ct) });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException) { throw new TelegramFailure("BOT_IDENTITY_CONFLICT"); }
    }

    internal async Task<string> PollAsync(string workspace, Guid id, CancellationToken ct = default)
    {
        if (!options.Enabled) return "FEATURE_DISABLED";
        var credential = await credentials.ResolveAsync(workspace, id, ct);
        if (credential is null) return "MISSING_CREDENTIALS";
        // A verified credential must retain the provenance of the explicitly admitted transport.
        if (!admission.Allowed || credential.Synthetic != admission.Synthetic) return "LIVE_NOT_ADMITTED";
        await db.Database.OpenConnectionAsync(ct);
        var resource = $"communications:telegram:poll:{id:N}";
        var locked = false;
        var owner = Guid.NewGuid();
        long fence = 0, generation = 0;
        try
        {
            // Session lock spans HTTP, not a SQL transaction. It prevents overlapping polls even after
            // durable lease expiry; a second host cannot steal an in-flight request's provider offset.
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @r<0 THROW 51003,'TELEGRAM_LEASE_BUSY',1;", ct);
            locked = true;
            long offset;
            long bot;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                var account = await Account(workspace, id, ct);
                var state = await State(workspace, id, ct);
                var now = await Now(ct);
                if (!Eligible(account, state) || credential.ConfigurationVersion is { } resolvedVersion && resolvedVersion != account!.ConfigurationVersion
                    || state!.RetryAfter > now || state.LeaseExpiry > now) return "ACCOUNT_NOT_DUE";
                state.Fence = checked(state.Fence + 1); state.LeaseOwner = owner;
                state.LeaseExpiry = now.AddSeconds(90); state.LastAttemptAt = now;
                fence = state.Fence; generation = state.ConnectionGeneration; offset = state.NextOffset; bot = state.BotId;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            await CheckCurrentCredential(credential, workspace, id, ct);
            var me = await api.ReadAsync(credential, "getMe", 0, ct);
            if (TelegramUpdateNormalizer.Id(me, "id") != bot || !me.TryGetProperty("is_bot", out var isBot) || isBot.ValueKind != System.Text.Json.JsonValueKind.True)
                throw new TelegramFailure("BOT_BINDING_INVALID");
            await CheckCurrentCredential(credential, workspace, id, ct);
            await CheckWebhook(credential, ct);
            await CheckCurrentCredential(credential, workspace, id, ct);
            var updates = await api.ReadAsync(credential, "getUpdates", offset, ct);
            await CheckCurrentCredential(credential, workspace, id, ct);
            if (updates.ValueKind != System.Text.Json.JsonValueKind.Array || updates.GetArrayLength() > 100) throw new TelegramFailure("RESPONSE_INVALID");
            var previous = -1L;
            foreach (var update in updates.EnumerateArray())
            {
                var updateId = TelegramUpdateNormalizer.Id(update, "update_id");
                if (updateId < previous) throw new TelegramFailure("UPDATE_ORDER_INVALID");
                previous = updateId;
            }
            foreach (var update in updates.EnumerateArray())
            {
                var inbound = TelegramUpdateNormalizer.Normalize(update);
                await CommitAsync(workspace, id, generation, owner, fence, inbound, ct);
            }
            await Finish(workspace, id, generation, owner, fence, null, ct);
            return "POLL_COMMITTED";
        }
        catch (SqlException error) when (error.Number == 51003) { return "LEASE_BUSY"; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Never log exception, URI, raw response or message. SQL failures are retried from durable offset.
            var failure = error as TelegramFailure ?? new TelegramFailure("STORAGE_TRANSIENT");
            db.ChangeTracker.Clear();
            if (fence > 0) await Finish(workspace, id, generation, owner, fence, failure, ct);
            return failure.Code;
        }
        finally
        {
            if (locked) await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource},@LockOwner='Session';", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
            db.ChangeTracker.Clear();
        }
    }

    internal async Task CommitAsync(string workspace, Guid id, long generation, Guid owner, long fence, TelegramInbound inbound, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await Account(workspace, id, ct);
        var state = await State(workspace, id, ct);
        var now = await Now(ct);
        if (!Eligible(account, state) || state!.ConnectionGeneration != generation || state.LeaseOwner != owner || state.Fence != fence || state.LeaseExpiry <= now)
            throw new TelegramFailure("LEASE_LOST");
        if (await db.TelegramAdmissions.AnyAsync(x => x.WorkspaceId == workspace && x.AccountId == id && x.UpdateId == inbound.UpdateId, ct)) return;
        if (inbound.UpdateId < state.NextOffset) throw new TelegramFailure("OFFSET_REPLAY_UNKNOWN");
        if (inbound.Classification == "TEXT")
            await core.AdmitInTransactionAsync(account!, $"telegram:{inbound.ChatId}", $"telegram:{inbound.UserId}",
                $"telegram:{inbound.ChatId}:{inbound.MessageId}", inbound.Text!, inbound.At, ct);
        db.TelegramAdmissions.Add(new() { WorkspaceId = workspace, AccountId = id, UpdateId = inbound.UpdateId,
            Classification = inbound.Classification, CommittedAt = now });
        state.NextOffset = checked(inbound.UpdateId + 1);
        state.LastCommittedUpdateAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct); // business + classification + checkpoint, never split acknowledgment
    }

    private async Task Finish(string workspace, Guid id, long generation, Guid owner, long fence, TelegramFailure? failure, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Account(workspace, id, ct);
        var state = await State(workspace, id, ct);
        if (state is null || state.ConnectionGeneration != generation || state.LeaseOwner != owner || state.Fence != fence) return;
        var now = await Now(ct);
        state.LeaseOwner = null; state.LeaseExpiry = null;
        state.FailureClassification = failure?.Code;
        state.FailureCount = failure is null ? 0 : Math.Min(state.FailureCount + 1, 16);
        state.RetryAfter = failure is null ? now.AddSeconds(1) : now.AddSeconds(Math.Max(failure.RetrySeconds, Math.Min(300, 1 << Math.Min(state.FailureCount, 8))));
        if (failure?.Code is "REAUTH_REQUIRED" or "POLLING_CONFLICT" or "WEBHOOK_CONFLICT" or "BOT_BINDING_INVALID")
        { state.Active = false; state.RetryAfter = DateTimeOffset.MaxValue; }
        if (failure is null) state.LastSuccessfulPollAt = now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private static bool Eligible(ChannelAccount? account, TelegramPollingState? state) => account is { ProviderKey: "telegram", Status: "DRAFT" }
        && state is { Active: true } && account.ConfigurationVersion == state.ConnectionGeneration;
    private Task<ChannelAccount?> Account(string workspace, Guid id, CancellationToken ct) => db.Accounts.FromSqlInterpolated($"SELECT * FROM communications.ChannelAccounts WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={workspace} AND Id={id}").SingleOrDefaultAsync(ct);
    private Task<TelegramPollingState?> State(string workspace, Guid id, CancellationToken ct) => db.TelegramPolling.FromSqlInterpolated($"SELECT * FROM communications.TelegramPolling WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={workspace} AND AccountId={id}").SingleOrDefaultAsync(ct);
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQueryRaw<DateTimeOffset>("SELECT SYSDATETIMEOFFSET() AS [Value]").SingleAsync(ct);
    private async Task CheckCurrentCredential(TelegramCredential credential, string workspace, Guid account, CancellationToken ct)
    {
        if (credential.ConfigurationVersion is null) return; // legacy friend-verifier transport only
        var current = await credentials.ResolveAsync(workspace, account, ct);
        if (current is null || current.ConfigurationVersion != credential.ConfigurationVersion || current.CredentialGeneration != credential.CredentialGeneration
            || current.CredentialId != credential.CredentialId) throw new TelegramFailure("CONNECTION_GENERATION_STALE");
    }
    private async Task CheckWebhook(TelegramCredential credential, CancellationToken ct)
    {
        var webhook = await api.ReadAsync(credential, "getWebhookInfo", 0, ct);
        if (webhook.ValueKind != System.Text.Json.JsonValueKind.Object || !webhook.TryGetProperty("url", out var url) || url.ValueKind != System.Text.Json.JsonValueKind.String)
            throw new TelegramFailure("RESPONSE_INVALID");
        if (!string.IsNullOrEmpty(url.GetString())) throw new TelegramFailure("WEBHOOK_CONFLICT");
    }
}
