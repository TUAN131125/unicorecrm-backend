using System.Data;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed partial class EfQuoteSellerReader(WorkspaceDbContext db, ICurrentWorkspace workspace, IAccessAuthorizer authorizer)
    : IQuoteSellerReader
{
    public async Task<IQuoteSellerLease> AcquireAsync(string requestId, string correlationId, CancellationToken ct)
    {
        var access = await authorizer.AuthorizeAsync(AccessRequirement.ForCanonicalCapability("quotes.read"), correlationId, ct);
        if (!access.IsAllowed || !workspace.IsResolved) return new Lease(null, "ACCESS_DENIED", []);
        var trusted = workspace.Require();
        if (access.Context?.WorkspaceId != trusted.WorkspaceId) return new Lease(null, "WORKSPACE_MISMATCH", []);
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var row = await db.StudioConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == trusted.WorkspaceId, ct);
            if (row is null) { await transaction.DisposeAsync(); return new Lease(null, null, ["SELLER_CONFIGURATION_MISSING"]); }
            var business = StudioJson.Deserialize<WorkspaceBusinessInformationDocument>(row.BusinessInformationJson);
            var addresses = StudioJson.Deserialize<WorkspaceBusinessAddressDocument[]>(row.AddressesJson)
                .Where(x => x.Active && x.Purposes.Contains("REGISTERED", StringComparer.Ordinal)).ToArray();
            var blockers = new List<string>();
            if (string.IsNullOrWhiteSpace(business.LegalName)) blockers.Add("SELLER_LEGAL_NAME_REQUIRED");
            if (string.IsNullOrWhiteSpace(business.DisplayName)) blockers.Add("SELLER_DISPLAY_NAME_REQUIRED");
            if (addresses.Length != 1 || string.IsNullOrWhiteSpace(addresses[0].AddressLine1) || addresses[0].CountryCode.Length != 2)
                blockers.Add("SELLER_REGISTERED_ADDRESS_REQUIRED_OR_AMBIGUOUS");
            var email = Clean(business.Email); var phone = Clean(business.Phone);
            var emailValid = email is not null && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && !email.Any(char.IsControl);
            var phoneValid = phone is not null && PhonePattern().IsMatch(phone) && phone.Count(char.IsDigit) is >= 7 and <= 15;
            if (email is not null && !emailValid) blockers.Add("SELLER_EMAIL_INVALID");
            if (phone is not null && !phoneValid) blockers.Add("SELLER_PHONE_INVALID");
            if (!emailValid && !phoneValid) blockers.Add("SELLER_CONTACT_REQUIRED");
            if (!string.IsNullOrWhiteSpace(business.LogoReference)) blockers.Add("BLOCKED_CONTRACT_LOGO_ASSET");
            if (blockers.Count != 0) { await transaction.DisposeAsync(); return new Lease(null, null, blockers); }
            var timeZone = StudioJson.Deserialize<WorkspaceLocaleRegionDocument>(row.LocaleRegionJson).Timezone;
            var a = addresses[0];
            var address = string.Join(", ", new[] { a.AddressLine1, a.AddressLine2, a.WardCode, a.DistrictCode, a.ProvinceCode, a.CountryCode, a.PostalCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            return new Lease(transaction, null, [], new(trusted.WorkspaceId, row.Revision, business.DisplayName.Trim(),
                business.LegalName.Trim(), address, Clean(business.TaxId), email, phone, Clean(business.Website), timeZone));
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    [GeneratedRegex("^[+0-9 ()\\-.]+$", RegexOptions.CultureInvariant)] private static partial Regex PhonePattern();
    private sealed class Lease(IDbContextTransaction? transaction, string? errorCode, IReadOnlyList<string> blockers,
        QuoteSellerSnapshot? snapshot = null) : IQuoteSellerLease
    {
        public QuoteSellerSnapshot? Snapshot { get; } = snapshot;
        public string? ErrorCode { get; } = errorCode;
        public IReadOnlyList<string> Blockers { get; } = blockers;
        public async ValueTask DisposeAsync() { var held = Interlocked.Exchange(ref transaction, null); if (held is not null) await held.DisposeAsync(); }
    }
}
