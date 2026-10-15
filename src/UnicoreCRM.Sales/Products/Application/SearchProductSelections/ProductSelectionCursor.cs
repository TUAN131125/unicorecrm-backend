using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Sales.Products.Application.Common;

namespace UnicoreCRM.Sales.Products.Application.SearchProductSelections;

internal sealed record ProductSelectionContinuation(DateTimeOffset CreatedAt, string Id, string Binding, DateTimeOffset ExpiresAt);

internal sealed class ProductSelectionCursor(IDataProtectionProvider provider, TimeProvider clock)
{
    private readonly IDataProtector protector = provider.CreateProtector("UnicoreCRM.Products.SelectionCursor.v1");

    internal static string Bind(ProductAccess access, string search, int limit) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            access.Trusted.WorkspaceId, access.Trusted.AccountId, access.Trusted.MemberId,
            access.Trusted.MembershipId, access.Authorization.PolicyFingerprint,
            access.Authorization.ScopeFilter, access.Authorization.ScopeOwnerMemberId,
            Fields = access.Authorization.FieldEnforcement.OrderBy(field => field.Key, StringComparer.Ordinal),
            Search = search, Limit = limit
        }))));

    internal string Encode(ProductSelectionRow last, string binding) => protector.Protect(
        JsonSerializer.Serialize(new ProductSelectionContinuation(last.CreatedAt, last.Id, binding, clock.GetUtcNow().AddMinutes(15))));

    internal bool TryDecode(string token, string binding, out ProductSelectionContinuation? continuation)
    {
        continuation = null;
        if (token.Length > 8192) return false;
        try
        {
            var value = JsonSerializer.Deserialize<ProductSelectionContinuation>(protector.Unprotect(token));
            if (value is null || value.Binding != binding || value.ExpiresAt <= clock.GetUtcNow()
                || string.IsNullOrWhiteSpace(value.Id) || value.Id.Length > 160) return false;
            continuation = value;
            return true;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
