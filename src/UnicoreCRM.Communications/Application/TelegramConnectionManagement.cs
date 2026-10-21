using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Communications.Infrastructure.Telegram;

namespace UnicoreCRM.Communications.Application;

internal sealed partial class TelegramConnectionManagement(CommunicationsDbContext db, ChannelManagement channels,
    TelegramCredentialVault vault, TelegramApiClient api, TimeProvider clock, TelegramProviderAdmission admission, TelegramPollingOptions pollingOptions)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal async Task<TelegramConnectionDocument> GetAsync(Guid id, string correlation, CancellationToken ct)
    {
        var authority = await channels.Authorize(correlation, null, ct);
        return await Project(authority.WorkspaceId, id, ct);
    }
    private async Task<TelegramConnectionDocument> Project(string workspace, Guid id, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.Id == id, ct)
            ?? throw new CommunicationReadException("CHANNEL_NOT_FOUND", 404);
        if (account.ProviderKey != "telegram") throw new CommunicationReadException("CHANNEL_PROVIDER_UNSUPPORTED", 400);
        var row = await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == id, ct);
        var poll = await db.TelegramPolling.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == id, ct);
        var present = row?.Ciphertext is not null;
        var securityReady = vault.CanRead(row);
        var verified = securityReady && present && poll is not null && row!.VerifiedGeneration == row.CredentialGeneration
            && row.VerifiedConfigurationVersion == account.ConfigurationVersion && poll.Synthetic == admission.Synthetic;
        var active = verified && poll!.Active && admission.Allowed && pollingOptions.Enabled && account.Status == "DRAFT" && row?.Disconnected != true
            && poll.ConnectionGeneration == account.ConfigurationVersion;
        var failure = row?.LastError ?? poll?.FailureClassification;
        var health = !securityReady ? "SECURITY_NOT_READY" : account.Status == "DISABLED" ? "DISABLED" : !present ? "NOT_CONFIGURED"
            : row?.Disconnected == true ? "RECONNECT_REQUIRED" : failure == "REAUTH_REQUIRED" ? "AUTH_FAILED"
            : failure == "WEBHOOK_CONFLICT" ? "WEBHOOK_CONFLICT"
            : failure is "POLLING_CONFLICT" or "BOT_BINDING_INVALID" || poll is { Active: true } && (!admission.Allowed || !pollingOptions.Enabled) ? "RECONNECT_REQUIRED"
            : !verified ? "UNVERIFIED" : !active ? "READY_TO_ACTIVATE"
            : failure is not null || poll?.LastSuccessfulPollAt < clock.GetUtcNow().AddMinutes(-2) ? "DEGRADED"
            : poll?.LastSuccessfulPollAt >= poll?.ActivatedAt ? "HEALTHY" : "STARTING";
        return new(id.ToString(), "telegram", account.ConfigurationVersion,
            row?.Disconnected == true ? "DISCONNECTED" : present ? "CONFIGURED" : "NOT_CONFIGURED", present, row?.CredentialGeneration ?? 0,
            verified ? poll!.Synthetic ? "SYNTHETIC_VERIFIED" : "VERIFIED" : "UNVERIFIED", verified ? poll?.BotId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            verified ? row!.LastVerifiedAt : null,
            health, !securityReady ? "SECURITY_NOT_READY" : row?.LastError ?? poll?.FailureClassification, account.Status == "DRAFT", securityReady,
            admission.Allowed && !admission.Synthetic, admission.Allowed, active,
            poll?.LastSuccessfulPollAt, poll?.LastCommittedUpdateAt, poll?.RetryAfter, poll?.ConnectionGeneration ?? 0);
    }

    internal async Task<TelegramConnectionDocument> MutateAsync(Guid id, string? token, long expectedVersion,
        string key, string correlation, CancellationToken ct)
    {
        var authority = await channels.Authorize(correlation, "credential", ct);
        if (key.Length is < 1 or > 128 || key.Any(char.IsControl) || expectedVersion < 1 || correlation.Length > 128)
            throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        if (token is not null && !vault.Ready) throw new CommunicationReadException("SECURITY_NOT_READY", 503);
        // Real token-shaped input requires separate environment admission; fake transport only accepts fixtures.
        if (token is not null)
        {
            var fixture = token.StartsWith("fixture-", StringComparison.Ordinal) && token.Length is >= 8 and <= 256
                && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
            var split = token.Split(':');
            var liveShape = split.Length == 2 && split[0].Length is >= 1 and <= 20 && split[0].All(char.IsAsciiDigit)
                && split[1].Length is >= 20 and <= 200 && split[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
            if (!fixture && (!admission.Allowed || admission.Synthetic)) throw new CommunicationReadException("LIVE_NOT_ADMITTED", 503);
            if (!(admission.Synthetic || !admission.Allowed ? fixture : liveShape)) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        }
        var workspace = authority.WorkspaceId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var requestKey = Hash(key);
        var resource = "communications:channel:" + Hash(workspace + "|" + authority.MemberId + "|" + requestKey);
        try { await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51002,'CHANNEL_REQUEST_BUSY',1;", ct); }
        catch (SqlException e) when (e.Number == 51002) { throw new CommunicationReadException("CHANNEL_REQUEST_BUSY", 503); }
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM communications.ChannelAccounts WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={workspace} AND Id={id}").SingleOrDefaultAsync(ct)
            ?? throw new CommunicationReadException("CHANNEL_NOT_FOUND", 404);
        if (account.ProviderKey != "telegram") throw new CommunicationReadException("CHANNEL_PROVIDER_UNSUPPORTED", 400);
        var row = await db.TelegramConnections.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == id, ct);
        var operation = token is null ? "TELEGRAM_CREDENTIAL_REMOVED" : "TELEGRAM_CREDENTIAL_REPLACED";
        var replay = await db.ChannelMutations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.ActorMemberId == authority.MemberId && x.RequestKey == requestKey, ct);
        var canonical = JsonSerializer.Serialize(new { operation, id, expectedVersion, token });
        string Fingerprint() => token is null ? Hash(canonical) : Convert.ToHexString(HMACSHA256.HashData(vault.ReplayKey(row!), Encoding.UTF8.GetBytes(canonical)));
        if (replay is not null)
        {
            if (replay.Operation != operation || row is null || replay.Fingerprint != Fingerprint()) throw new CommunicationReadException("CHANNEL_IDEMPOTENCY_CONFLICT", 409);
            return JsonSerializer.Deserialize<TelegramConnectionDocument>(replay.ResponseJson, Json) ?? throw new CommunicationReadException("CHANNEL_REQUEST_BUSY", 503);
        }
        if (account.ConfigurationVersion != expectedVersion) throw new CommunicationReadException("CHANNEL_VERSION_CONFLICT", 409);
        if (row is null)
        {
            if (!vault.Ready) throw new CommunicationReadException("SECURITY_NOT_READY", 503);
            row = new() { AccountId = id, WorkspaceId = workspace }; vault.CreateReplayKey(row); db.TelegramConnections.Add(row);
        }
        var fingerprint = Fingerprint();
        await TelegramConnectionLifecycle.InvalidateAsync(db, account, false, false, ct);
        row.CredentialGeneration = checked(row.CredentialGeneration + 1);
        row.Ciphertext = token is null ? null : vault.Protect(row, token);
        row.Disconnected = false;
        account.InvalidateConnection(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var result = await Project(workspace, id, ct);
        db.ChannelMutations.Add(new() { AccountId = id, WorkspaceId = workspace, ActorMemberId = authority.MemberId,
            RequestKey = requestKey, Fingerprint = fingerprint, Operation = operation, Version = account.ConfigurationVersion,
            RecordedAt = clock.GetUtcNow(), CorrelationId = correlation, ResponseJson = JsonSerializer.Serialize(result, Json) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result;
    }

    // Friend-only fixture entry; public operations use CommandAsync and the same guarded commit path.
    internal async Task VerifySyntheticAsync(string workspace, Guid id, CancellationToken ct = default)
    {
        var credential = await vault.ResolveForVerificationAsync(workspace, id, ct) ?? throw new TelegramFailure("CREDENTIAL_UNAVAILABLE");
        if (!credential.Synthetic) throw new TelegramFailure("SYNTHETIC_ADMISSION_DENIED");
        try { await VerifySnapshotAsync(workspace, id, credential, ct); }
        catch (TelegramFailure error)
        {
            await RecordFailureAsync(workspace, id, credential, error.Code, ct);
            throw;
        }
    }

    private async Task VerifySnapshotAsync(string workspace, Guid id, TelegramCredential credential, CancellationToken ct)
    {
        var result = await api.ReadAsync(credential, "getMe", 0, ct);
        var bot = TelegramUpdateNormalizer.Id(result, "id");
        if (!result.TryGetProperty("is_bot", out var flag) || flag.ValueKind != JsonValueKind.True) throw new TelegramFailure("BOT_BINDING_INVALID");
        var current = await vault.ResolveForVerificationAsync(workspace, id, ct);
        if (current is null || current.ConfigurationVersion != credential.ConfigurationVersion || current.CredentialGeneration != credential.CredentialGeneration
            || current.CredentialId != credential.CredentialId) throw new TelegramFailure("CONNECTION_GENERATION_STALE");
        var webhook = await api.ReadAsync(credential, "getWebhookInfo", 0, ct);
        if (webhook.ValueKind != JsonValueKind.Object || !webhook.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) throw new TelegramFailure("RESPONSE_INVALID");
        if (!string.IsNullOrEmpty(url.GetString())) throw new TelegramFailure("WEBHOOK_CONFLICT");
        await CompleteSyntheticVerificationAsync(workspace, id, credential, bot, ct);
    }

    private async Task RecordFailureAsync(string workspace, Guid id, TelegramCredential credential, string code, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM communications.ChannelAccounts WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={workspace} AND Id={id}").SingleOrDefaultAsync(ct);
        var row = await db.TelegramConnections.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == id, ct);
        if (account is null || row is null || account.ConfigurationVersion != credential.ConfigurationVersion
            || row.CredentialGeneration != credential.CredentialGeneration || row.CredentialId != credential.CredentialId) return;
        await TelegramConnectionLifecycle.InvalidateAsync(db, account, false, false, ct);
        row.LastError = code;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    internal async Task CompleteSyntheticVerificationAsync(string workspace, Guid id, TelegramCredential credential, long bot, CancellationToken ct)
    {
        if (!credential.Synthetic) throw new TelegramFailure("SYNTHETIC_ADMISSION_DENIED");
        await CommitVerificationAsync(workspace, id, credential, bot, ct);
    }

    private async Task CommitVerificationAsync(string workspace, Guid id, TelegramCredential credential, long bot, CancellationToken ct,
        Func<UnicoreCRM.Communications.Domain.ChannelAccount, TelegramConnection, TelegramPollingState, Task>? command = null, bool reconnect = false)
    {
        if (!vault.Ready || !admission.Allowed || credential.Synthetic != admission.Synthetic) throw new TelegramFailure("SECURITY_NOT_READY");
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var botResource = "communications:telegram:bot:" + bot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={botResource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51002,'CHANNEL_REQUEST_BUSY',1;", ct);
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM communications.ChannelAccounts WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={workspace} AND Id={id}").SingleOrDefaultAsync(ct);
        var row = await db.TelegramConnections.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == id, ct);
        if (account is not { Status: "DRAFT", ProviderKey: "telegram" } || row?.Ciphertext is null || row.Disconnected && !reconnect
            || account.ConfigurationVersion != credential.ConfigurationVersion || row.CredentialGeneration != credential.CredentialGeneration
            || row.CredentialId != credential.CredentialId) throw new TelegramFailure("CONNECTION_GENERATION_STALE");
        var state = await db.TelegramPolling.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == id, ct);
        if (state is not null && state.BotId != bot) throw new TelegramFailure("BOT_REBIND_REQUIRED");
        if (await db.TelegramPolling.AnyAsync(x => x.AccountId != id && x.BotId == bot, ct)) throw new TelegramFailure("BOT_IDENTITY_CONFLICT");
        if (state is null) { state = new() { AccountId = id, WorkspaceId = workspace, BotId = bot, Synthetic = admission.Synthetic, AdmittedAt = clock.GetUtcNow() }; db.TelegramPolling.Add(state); }
        if (state.Synthetic != admission.Synthetic) throw new TelegramFailure("TRANSPORT_NOT_ADMITTED");
        state.Active = false;
        state.ConnectionGeneration = account.ConfigurationVersion; state.Fence = checked(state.Fence + 1);
        state.LeaseOwner = null; state.LeaseExpiry = null; state.RetryAfter = null; state.FailureClassification = null;
        row.VerifiedGeneration = row.CredentialGeneration; row.VerifiedConfigurationVersion = account.ConfigurationVersion;
        row.LastVerifiedAt = clock.GetUtcNow(); row.LastError = null;
        row.Disconnected = false;
        if (command is not null) await command(account, row, state);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 }) { throw new TelegramFailure("BOT_IDENTITY_CONFLICT"); }
    }
}
