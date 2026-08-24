using Microsoft.AspNetCore.WebUtilities;

namespace UnicoreCRM.Crm.Deals.Application.ListDeals;

/// <summary>
/// Opaque offset cursor codec for the Deals list page. The encoded value is transport
/// detail and is never interpreted by callers.
/// </summary>
internal static class DealListCursor
{
    internal static bool TryParse(string? cursor, IDictionary<string, string[]> fields, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor))
            return true;
        if (cursor.Length > 512)
        {
            fields["cursor"] = ["cursor must contain at most 512 characters."];
            return false;
        }
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            if (bytes.Length != sizeof(int))
                throw new FormatException();
            offset = BitConverter.ToInt32(bytes);
            if (offset < 0)
                throw new FormatException();
            return true;
        }
        catch (FormatException)
        {
            fields["cursor"] = ["cursor is invalid."];
            return false;
        }
    }

    internal static string Encode(int offset) => WebEncoders.Base64UrlEncode(BitConverter.GetBytes(offset));
}
