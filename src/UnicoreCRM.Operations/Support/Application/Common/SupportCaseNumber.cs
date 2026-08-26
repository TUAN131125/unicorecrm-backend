using System.Globalization;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// Support-owned allocation of the human-readable case number.
///
/// <para>The canonical Support design baseline names <c>generateSupportCaseNumber</c> as a
/// Support-owned semantic, and the read contract requires a non-empty <c>caseNumber</c>, so
/// the backend owner must allocate it. The shape follows the only current consumer evidence,
/// <c>CASE-{year}-{sequence}</c> with the sequence zero-padded to four digits. The sequence
/// itself is backend-owned and allocated per trusted Workspace and per calendar year from a
/// durable Support column inside the SERIALIZABLE create transaction, so no caller or foreign
/// module can influence or fabricate it.</para>
/// </summary>
internal static class SupportCaseNumber
{
    internal const string Prefix = "CASE";

    internal static string Format(int caseYear, int sequence) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}-{caseYear.ToString("D4", CultureInfo.InvariantCulture)}-{sequence.ToString("D4", CultureInfo.InvariantCulture)}");
}
