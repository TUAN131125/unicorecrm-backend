using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.EvaluateEffectiveRecordAccess;

internal sealed record ValidatedRecordAccessRequest(
    string ResourceKey,
    string? RecordId,
    IReadOnlyList<string> RequestedCommands,
    IReadOnlyList<string> RequestedFields,
    bool IncludeExport,
    bool IncludeApproval);

/// <summary>
/// Contract-shape validation only. It proves the request is well formed; it decides nothing about
/// access. An unknown resource key is not a validation failure - it fails closed in evaluation, so
/// a caller cannot probe which resource keys exist by reading status codes.
/// </summary>
internal static partial class Validator
{
    private const int MaxCommands = 500;
    private const int MaxFields = 1000;

    internal static bool TryValidate(
        EvaluateEffectiveRecordAccessRequest request,
        out ValidatedRecordAccessRequest? validated,
        out IReadOnlyDictionary<string, string[]>? fieldErrors)
    {
        validated = null;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var resourceKey = request.ResourceKey?.Trim() ?? string.Empty;
        if (resourceKey.Length is < 1 or > 160)
            errors["resourceKey"] = ["resourceKey must contain between 1 and 160 characters."];

        string? recordId = null;
        if (request.RecordId is not null)
        {
            recordId = request.RecordId.Trim();
            if (!EntityIdPattern().IsMatch(recordId))
                errors["recordId"] = ["recordId is not a valid entity identifier."];
        }

        var commands = Tokens(request.RequestedCommands, MaxCommands, "requestedCommands", errors);
        var fields = Tokens(request.RequestedFields, MaxFields, "requestedFields", errors);

        if (errors.Count != 0)
        {
            fieldErrors = errors;
            return false;
        }

        fieldErrors = null;
        validated = new ValidatedRecordAccessRequest(
            resourceKey,
            recordId,
            commands,
            fields,
            request.IncludeExport ?? false,
            request.IncludeApproval ?? false);
        return true;
    }

    private static IReadOnlyList<string> Tokens(
        IReadOnlyList<string>? supplied,
        int maxItems,
        string name,
        Dictionary<string, string[]> errors)
    {
        if (supplied is null || supplied.Count == 0)
            return [];
        if (supplied.Count > maxItems)
        {
            errors[name] = [$"{name} must contain at most {maxItems} items."];
            return [];
        }

        var result = new List<string>(supplied.Count);
        foreach (var item in supplied)
        {
            var token = item?.Trim() ?? string.Empty;
            if (token.Length is < 1 or > 160)
            {
                errors[name] = [$"{name} items must contain between 1 and 160 characters."];
                return [];
            }

            if (!result.Contains(token, StringComparer.Ordinal))
                result.Add(token);
        }

        return result;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
