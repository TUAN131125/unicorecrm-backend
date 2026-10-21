using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Application;

internal sealed class ChannelManagement(CommunicationsDbContext db, ICurrentWorkspace workspace,
    IRecordAccessEvaluator evaluator, IDataProtectionProvider protection, TimeProvider clock)
{
    internal sealed record Authority(string WorkspaceId, string AccountId, string MemberId, string MembershipId, string PolicyFingerprint);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector cursors = protection.CreateProtector("Communications.ChannelAccounts.Cursor.v1");
    private sealed record Cursor(string Scope, DateTimeOffset CreatedAt, Guid Id);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal async Task<Authority> Authorize(string correlation, string? operation, CancellationToken ct)
    {
        if (!workspace.IsResolved) throw new CommunicationReadException("ACCESS_DENIED", 403);
        var result = await evaluator.AuthorizeResourceAsync("communications.channels", "communications.channels.manage",
            ChannelAccessFacts.Fields.Keys.ToArray(), RecordAccessRepresentation.Full, new("", correlation), ct);
        var a = result.TrustedWorkspace;
        var w = workspace.Require();
        string[] writtenFields = operation switch
        {
            "create" => ["providerKey", "displayName", "status"], "rename" => ["displayName"],
            "disable" or "enable" or "credential" or "disconnect" => ["status"], _ => []
        };
        if (!result.IsAllowed || a is null || a.WorkspaceId != w.WorkspaceId || a.MemberId != w.MemberId
            || a.AccountId != w.AccountId || a.MembershipId != w.MembershipId
            || result.ScopeFilter != RecordAccessScopeFilter.Workspace || result.UnenforceableFieldKeys.Count != 0
            || writtenFields.Any(field => !result.CanWrite(field)))
            throw new CommunicationReadException("ACCESS_DENIED", 403);
        return new(a.WorkspaceId, a.AccountId, a.MemberId, a.MembershipId, result.PolicyFingerprint);
    }

    internal async Task<IReadOnlyList<ChannelProviderDocument>> Providers(string correlation, CancellationToken ct)
    {
        await Authorize(correlation, null, ct);
        return ChannelProviderCatalog.All;
    }

    internal async Task<ChannelAccountDocument> Get(Guid id, string correlation, CancellationToken ct)
    {
        var a = await Authorize(correlation, null, ct);
        var row = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == a.WorkspaceId && x.Id == id, ct)
            ?? throw new CommunicationReadException("CHANNEL_NOT_FOUND", 404);
        return Project(row);
    }

    internal async Task<ChannelAccountPage> List(int limit, string? token, string? provider, string? status, string correlation, CancellationToken ct)
    {
        var a = await Authorize(correlation, null, ct);
        if (limit is < 1 or > 100 || provider is not null && !ChannelProviderCatalog.All.Any(x => x.Key == provider)
            || status is not null && status is not ("DRAFT" or "DISABLED" or "UNVERIFIED"))
            throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        var scope = Hash(JsonSerializer.Serialize(new { a.WorkspaceId, a.AccountId, a.MemberId, a.MembershipId,
            a.PolicyFingerprint, provider, status, limit }));
        Cursor? after = null;
        if (token is not null)
        {
            if (token.Length is < 1 or > 4096) throw new CommunicationReadException("COMMUNICATION_CURSOR_INVALID", 400);
            try
            {
                after = JsonSerializer.Deserialize<Cursor>(cursors.Unprotect(token));
                if (after is null || after.Scope != scope || after.Id == Guid.Empty)
                    throw new CommunicationReadException("COMMUNICATION_CURSOR_INVALID", 400);
            }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
            { throw new CommunicationReadException("COMMUNICATION_CURSOR_INVALID", 400); }
        }
        var all = db.Accounts.AsNoTracking().Where(x => x.WorkspaceId == a.WorkspaceId);
        var query = all;
        if (provider is not null) query = query.Where(x => x.ProviderKey == provider);
        if (status is not null) query = status == "UNVERIFIED" ? query.Where(x => x.Status == null) : query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        if (after is not null)
        {
            var at = after.CreatedAt;
            var id = after.Id;
            query = at == DateTimeOffset.MinValue
                ? query.Where(x => x.CreatedAt == null && x.Id.CompareTo(id) < 0)
                : query.Where(x => x.CreatedAt == null || x.CreatedAt < at || x.CreatedAt == at && x.Id.CompareTo(id) < 0);
        }
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        var page = rows.Take(limit).ToArray();
        var summary = new ChannelSummary(await all.CountAsync(ct), await all.CountAsync(x => x.Status == "DRAFT", ct),
            0, await all.CountAsync(x => x.Status == "DISABLED", ct), await all.CountAsync(x => x.Status == null, ct),
            await all.Where(x => x.ProviderKey != null).Select(x => x.ProviderKey).Distinct().CountAsync(ct));
        var last = page.LastOrDefault();
        var next = more && last is not null ? cursors.Protect(JsonSerializer.Serialize(new Cursor(scope, last.CreatedAt ?? DateTimeOffset.MinValue, last.Id))) : null;
        return new(page.Select(Project).ToArray(), next, total, summary);
    }

    internal async Task<ChannelMutationDocument> Mutate(string operation, Guid? id, string? provider, string? name,
        long expectedVersion, string key, string correlation, CancellationToken ct)
    {
        var a = await Authorize(correlation, operation, ct);
        if (key.Length is < 1 or > 128 || key.Any(char.IsControl) || correlation.Length > 128 || expectedVersion < 0
            || name is not null && (string.IsNullOrWhiteSpace(name) || name.Length > 160 || name != name.Trim() || name.Any(char.IsControl)))
            throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        var descriptor = ChannelProviderCatalog.All.SingleOrDefault(x => x.Key == provider);
        if (operation == "create" && (descriptor is null || name is null) || operation == "rename" && name is null)
            throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
        var fingerprint = Hash(JsonSerializer.Serialize(new { operation, id, provider, name, expectedVersion }));
        var requestKey = Hash(key);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize same-key requests across processes without a singleton account or process mutex.
        var resource = "communications:channel:" + Hash(a.WorkspaceId + "|" + a.MemberId + "|" + requestKey);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51002, 'CHANNEL_REQUEST_BUSY', 1;", ct);
        }
        catch (SqlException e) when (e.Number == 51002) { throw new CommunicationReadException("CHANNEL_REQUEST_BUSY", 503); }
        var replay = await db.ChannelMutations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == a.WorkspaceId
            && x.ActorMemberId == a.MemberId && x.RequestKey == requestKey, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) throw new CommunicationReadException("CHANNEL_IDEMPOTENCY_CONFLICT", 409);
            return (JsonSerializer.Deserialize<ChannelMutationDocument>(replay.ResponseJson, Json)
                ?? throw new InvalidOperationException("CHANNEL_REPLAY_INVALID")) with { Replayed = true };
        }
        var now = clock.GetUtcNow();
        ChannelAccount account;
        if (operation == "create")
        {
            account = ChannelAccount.Draft(a.WorkspaceId, descriptor!.Key, descriptor.ChannelType, name!, now);
            db.Accounts.Add(account);
        }
        else
        {
            account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM communications.ChannelAccounts WITH (UPDLOCK,HOLDLOCK) WHERE WorkspaceId={a.WorkspaceId} AND Id={id}")
                .SingleOrDefaultAsync(ct) ?? throw new CommunicationReadException("CHANNEL_NOT_FOUND", 404);
            if (account.ConfigurationVersion != expectedVersion) throw new CommunicationReadException("CHANNEL_VERSION_CONFLICT", 409);
            if (operation == "disconnect" && account.ProviderKey != "telegram") throw new CommunicationReadException("CHANNEL_CAPABILITY_NOT_SUPPORTED", 501);
            if (account.Status is not ("DRAFT" or "DISABLED")) throw new CommunicationReadException("CHANNEL_LEGACY_ACCOUNT", 409);
            var changed = operation == "disconnect";
            if (changed) account.InvalidateConnection(now);
            else changed = account.Configure(name, operation == "disable" ? true : operation == "enable" ? false : null, now);
            if (changed && account.ProviderKey == "telegram")
                await TelegramConnectionLifecycle.InvalidateAsync(db, account, false, operation == "disconnect", ct);
        }
        var audit = new ChannelMutation { WorkspaceId = a.WorkspaceId, AccountId = account.Id,
            ActorMemberId = a.MemberId, RequestKey = requestKey, Fingerprint = fingerprint, Operation = operation,
            Version = account.ConfigurationVersion, RecordedAt = now, CorrelationId = correlation };
        var response = new ChannelMutationDocument(Project(account), audit.Id.ToString(), now, correlation);
        audit.ResponseJson = JsonSerializer.Serialize(response, Json);
        db.ChannelMutations.Add(audit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new CommunicationReadException("CHANNEL_VERSION_CONFLICT", 409); }
        await transaction.CommitAsync(ct);
        return response;
    }

    private static ChannelAccountDocument Project(ChannelAccount row) => new(row.Id.ToString(), row.ProviderKey,
        row.ChannelType, row.DisplayName, row.Status ?? "UNVERIFIED", row.ConfigurationVersion, row.CreatedAt, row.UpdatedAt);
}
