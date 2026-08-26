using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// Field-level validation shared by more than one Support slice. Every bound and vocabulary
/// here is transcribed from the verified OpenAPI Support schemas; nothing is widened.
/// </summary>
internal static partial class SupportValidation
{
    internal static bool IsEntityId(string? value) => value is not null && EntityIdPattern().IsMatch(value);

    internal static string? Text(
        string? input,
        string field,
        int minimum,
        int maximum,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        var value = input.Trim();
        if (value.Length < minimum || value.Length > maximum)
            fields[field] = [$"{field} must contain between {minimum} and {maximum} characters."];
        return value.Length == 0 && !required ? null : value;
    }

    internal static string? Entity(string? input, string field, bool required, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        var value = input.Trim();
        if (value.Length == 0 && !required)
            return null;
        if (!EntityIdPattern().IsMatch(value))
        {
            fields[field] = [$"{field} is not a valid entity identifier."];
            return null;
        }
        return value;
    }

    internal static DateTimeOffset? Utc(string? input, string field, bool required, IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input))
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!input.EndsWith('Z')
            || !DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            fields[field] = [$"{field} must be a UTC date-time ending in Z."];
            return null;
        }
        return value.ToUniversalTime();
    }

    /// <summary>The admitted tag bound: at most 100 items, each 1-100 characters.</summary>
    internal static IReadOnlyList<string> Tags(IReadOnlyList<string>? input, IDictionary<string, string[]> fields)
    {
        if (input is null || input.Count == 0)
            return [];
        if (input.Count > 100)
        {
            fields["tags"] = ["tags must contain at most 100 items."];
            return [];
        }
        var values = new List<string>(input.Count);
        foreach (var entry in input)
        {
            var value = entry?.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > 100)
            {
                fields["tags"] = ["each tag must contain between 1 and 100 characters."];
                return [];
            }
            values.Add(value);
        }
        return values;
    }

    internal static SupportCaseRelationship? Relationship(
        string? type,
        string? id,
        IDictionary<string, string[]> fields)
    {
        var relationshipType = type?.Trim();
        if (relationshipType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
        {
            fields["relationshipRef.type"] = ["relationshipRef.type must be CONTACT or ORGANIZATION_ACCOUNT."];
            relationshipType = null;
        }
        var relationshipId = Entity(id, "relationshipRef.id", true, fields);
        return relationshipType is null || relationshipId is null
            ? null
            : new SupportCaseRelationship(relationshipType, relationshipId);
    }

    internal static SupportCaseStatus? Status(string? value, string field, IDictionary<string, string[]> fields) => value switch
    {
        "new" => SupportCaseStatus.New,
        "in_progress" => SupportCaseStatus.InProgress,
        "waiting_customer" => SupportCaseStatus.WaitingCustomer,
        "waiting_internal" => SupportCaseStatus.WaitingInternal,
        "resolved" => SupportCaseStatus.Resolved,
        "closed" => SupportCaseStatus.Closed,
        "reopened" => SupportCaseStatus.Reopened,
        "cancelled" => SupportCaseStatus.Cancelled,
        _ => Invalid<SupportCaseStatus>(
            field,
            "status must be one of new, in_progress, waiting_customer, waiting_internal, resolved, closed, reopened, cancelled.",
            fields)
    };

    internal static SupportCasePriority? Priority(string? value, string field, IDictionary<string, string[]> fields) => value switch
    {
        "low" => SupportCasePriority.Low,
        "medium" => SupportCasePriority.Medium,
        "high" => SupportCasePriority.High,
        "critical" => SupportCasePriority.Critical,
        _ => Invalid<SupportCasePriority>(field, "priority must be one of low, medium, high, critical.", fields)
    };

    /// <summary>The full read/replace category vocabulary.</summary>
    internal static SupportCaseCategory? Category(string? value, string field, IDictionary<string, string[]> fields) => value switch
    {
        "request" => SupportCaseCategory.Request,
        "consultation" => SupportCaseCategory.Consultation,
        "complaint" => SupportCaseCategory.Complaint,
        "follow_up" => SupportCaseCategory.FollowUp,
        "onboarding" => SupportCaseCategory.Onboarding,
        "usage_issue" => SupportCaseCategory.UsageIssue,
        "post_purchase" => SupportCaseCategory.PostPurchase,
        "technical_support" => SupportCaseCategory.TechnicalSupport,
        "warranty" => SupportCaseCategory.Warranty,
        "customer_care" => SupportCaseCategory.CustomerCare,
        "billing" => SupportCaseCategory.Billing,
        "feature_request" => SupportCaseCategory.FeatureRequest,
        _ => Invalid<SupportCaseCategory>(field, "category is not an admitted Support Case category.", fields)
    };

    /// <summary>
    /// The restricted creation vocabulary. The OpenAPI <c>SupportCaseCreateCategory</c> schema
    /// admits only these seven values on <c>createSupportCase</c>; the five legacy values stay
    /// readable and replaceable but cannot be created.
    /// </summary>
    internal static SupportCaseCategory? CreateCategory(string? value, IDictionary<string, string[]> fields) => value switch
    {
        "request" => SupportCaseCategory.Request,
        "consultation" => SupportCaseCategory.Consultation,
        "complaint" => SupportCaseCategory.Complaint,
        "follow_up" => SupportCaseCategory.FollowUp,
        "onboarding" => SupportCaseCategory.Onboarding,
        "usage_issue" => SupportCaseCategory.UsageIssue,
        "post_purchase" => SupportCaseCategory.PostPurchase,
        _ => Invalid<SupportCaseCategory>(
            "category",
            "category must be one of request, consultation, complaint, follow_up, onboarding, usage_issue, post_purchase.",
            fields)
    };

    internal static SupportCaseSource? Source(string? value, IDictionary<string, string[]> fields) => value switch
    {
        "manual" => SupportCaseSource.Manual,
        "customer_360" => SupportCaseSource.Customer360,
        "email" => SupportCaseSource.Email,
        "phone" => SupportCaseSource.Phone,
        "chat" => SupportCaseSource.Chat,
        "web_form" => SupportCaseSource.WebForm,
        "order" => SupportCaseSource.Order,
        "product" => SupportCaseSource.Product,
        _ => Invalid<SupportCaseSource>("source", "source is not an admitted Support Case source.", fields)
    };

    /// <summary>Channel is optional; a supplied value must still be admitted.</summary>
    internal static SupportCaseChannel? Channel(string? value, IDictionary<string, string[]> fields)
    {
        if (value is null)
            return null;
        return value switch
        {
            "email" => SupportCaseChannel.Email,
            "phone" => SupportCaseChannel.Phone,
            "chat" => SupportCaseChannel.Chat,
            "meeting" => SupportCaseChannel.Meeting,
            "internal" => SupportCaseChannel.Internal,
            _ => Invalid<SupportCaseChannel>("channel", "channel must be one of email, phone, chat, meeting, internal.", fields)
        };
    }

    /// <summary>The declared SLA vocabulary, accepted as a list filter only.</summary>
    internal static bool IsSlaStatus(string value) =>
        value is "on_track" or "at_risk" or "breached" or "paused" or "not_applicable";

    internal static bool TryCursor(string? cursor, IDictionary<string, string[]> fields, out int offset)
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

    internal static string Cursor(int offset) => WebEncoders.Base64UrlEncode(BitConverter.GetBytes(offset));

    private static T? Invalid<T>(string field, string message, IDictionary<string, string[]> fields)
        where T : struct
    {
        fields[field] = [message];
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
