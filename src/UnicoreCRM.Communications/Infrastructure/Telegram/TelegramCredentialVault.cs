using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Infrastructure.Persistence;

namespace UnicoreCRM.Communications.Infrastructure.Telegram;

// No configuration boolean can admit production secrets/live I/O. Custody must be supplied by an admitted composition.
internal interface ITelegramCredentialCustody
{
    bool SyntheticReady { get; }
    bool Ready => SyntheticReady && Protection is not null;
    IDataProtectionProvider? Protection { get; }
}
internal sealed class UnavailableTelegramCustody : ITelegramCredentialCustody
{
    public bool SyntheticReady => false;
    public IDataProtectionProvider? Protection => null;
}

internal sealed class TelegramConnection
{
    public Guid AccountId { get; set; }
    public string WorkspaceId { get; set; } = "";
    public Guid CredentialId { get; set; } = Guid.NewGuid();
    public string? Ciphertext { get; set; }
    public string ReplayKeyCiphertext { get; set; } = "";
    public long CredentialGeneration { get; set; }
    public long? VerifiedGeneration { get; set; }
    public long? VerifiedConfigurationVersion { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public bool Disconnected { get; set; }
    public string? LastError { get; set; }
}

internal sealed class TelegramCredentialVault(CommunicationsDbContext db, ITelegramCredentialCustody custody, TelegramProviderAdmission admission) : ITelegramCredentials
{
    internal bool Ready => custody.Ready && custody.Protection is not null;
    internal bool CanRead(TelegramConnection? row)
    {
        if (!Ready) return false;
        if (row?.Ciphertext is null) return true;
        try { _ = Unprotect(row); return true; }
        catch (TelegramFailure) { return false; }
    }
    internal IDataProtector Protector(string workspace, Guid account, Guid credential, long generation, string purpose = "token") =>
        (custody.Protection ?? throw new TelegramFailure("SECURITY_NOT_READY")).CreateProtector(
            "Communications.Telegram.Credential.v1", purpose, workspace, account.ToString("N"), credential.ToString("N"), generation.ToString(System.Globalization.CultureInfo.InvariantCulture));

    internal string Protect(TelegramConnection row, string value) => Protector(row.WorkspaceId, row.AccountId, row.CredentialId, row.CredentialGeneration).Protect(value);
    internal string Unprotect(TelegramConnection row)
    {
        try { return Protector(row.WorkspaceId, row.AccountId, row.CredentialId, row.CredentialGeneration).Unprotect(row.Ciphertext ?? throw new CryptographicException()); }
        catch (CryptographicException) { throw new TelegramFailure("CREDENTIAL_UNAVAILABLE"); }
    }
    internal byte[] ReplayKey(TelegramConnection row)
    {
        var protector = Protector(row.WorkspaceId, row.AccountId, row.CredentialId, 0, "replay-key");
        try { return protector.Unprotect(Convert.FromBase64String(row.ReplayKeyCiphertext)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { throw new TelegramFailure("CREDENTIAL_UNAVAILABLE"); }
    }
    internal void CreateReplayKey(TelegramConnection row) => row.ReplayKeyCiphertext = Convert.ToBase64String(
        Protector(row.WorkspaceId, row.AccountId, row.CredentialId, 0, "replay-key").Protect(RandomNumberGenerator.GetBytes(32)));

    internal async Task<TelegramCredential?> ResolveForVerificationAsync(string workspace, Guid account, CancellationToken ct, bool reconnect = false)
    {
        if (!Ready || !admission.Allowed) return null;
        var row = await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == account, ct);
        var metadata = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.Id == account && x.ProviderKey == "telegram", ct);
        if (row?.Ciphertext is null || row.Disconnected && !reconnect || metadata is not { Status: "DRAFT" }) return null;
        var prior = await db.TelegramPolling.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == account, ct);
        if (prior is not null && prior.Synthetic != admission.Synthetic) return null;
        var value = Unprotect(row);
        if (!admission.Synthetic && value.StartsWith("fixture-", StringComparison.Ordinal)) return null;
        return new TelegramCredential(value, admission.Synthetic, metadata.ConfigurationVersion, row.CredentialGeneration, row.CredentialId);
    }

    public async ValueTask<TelegramCredential?> ResolveAsync(string workspace, Guid account, CancellationToken ct)
    {
        if (!Ready || !admission.Allowed) return null;
        var row = await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == account, ct);
        var metadata = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.Id == account && x.ProviderKey == "telegram", ct);
        var polling = await db.TelegramPolling.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.AccountId == account, ct);
        if (row?.Ciphertext is null || row.Disconnected || row.VerifiedGeneration != row.CredentialGeneration
            || metadata is not { Status: "DRAFT" } || row.VerifiedConfigurationVersion != metadata.ConfigurationVersion
            || polling is not { Active: true } || polling.Synthetic != admission.Synthetic || polling.ConnectionGeneration != metadata.ConfigurationVersion) return null;
        try { return new TelegramCredential(Unprotect(row), admission.Synthetic, metadata.ConfigurationVersion, row.CredentialGeneration, row.CredentialId); }
        catch (TelegramFailure) { return null; } // no ciphertext/key exceptions or token escape to worker/logging
    }
}
