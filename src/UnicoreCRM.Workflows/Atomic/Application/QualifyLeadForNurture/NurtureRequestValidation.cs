using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using UnicoreCRM.Workflows.Atomic.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForNurture;

/// <summary>
/// The complete adopted <c>QualifyLeadNurtureRequest</c> contract, enforced in one place before the
/// coordinator touches any owner.
///
/// It exists because a partial check is worse than none here: the workflow commits Contact, then
/// Task, then the Lead close in three owner-local transactions, and recovery is forward-only, so a
/// field this stage lets through can only be refused after a Contact already exists. Every bound
/// below is read from the pinned schema rather than chosen, and the name bound is the Contact
/// canonical 200 frozen by <c>DEC-LEAD-CONTACT-NAME-BOUND</c> - the qualification display name is
/// transferred verbatim into <c>Contact.fullName</c>, so it cannot be allowed to exceed it.
///
/// Nothing here is state-dependent: it reads the request and nothing else, so running it before the
/// Task 8A authorization gate discloses no Lead, Contact or workflow-anchor fact.
/// </summary>
internal static partial class NurtureRequestValidation
{
    /// <summary>Frozen by <c>DEC-LEAD-CONTACT-NAME-BOUND</c>; equals <c>ContactDocument.fullName</c>.</summary>
    internal const int DisplayNameMaxLength = 200;

    private const int EmailMaxLength = 320;
    private const int PhoneMaxLength = 64;
    private const int TitleMaxLength = 160;
    private const int ReasonMaxLength = 1000;
    private const int NoteMaxLength = 4000;
    private const int EntityIdMaxLength = 128;

    /// <summary>
    /// Returns every field error the request carries, or an empty dictionary when it fully satisfies
    /// the adopted contract. Errors are accumulated rather than short-circuited so one round trip
    /// reports the whole request.
    /// </summary>
    internal static IReadOnlyDictionary<string, string[]> Validate(LeadNurtureQualificationCommand command)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            fields["Idempotency-Key"] = ["Idempotency-Key is required."];
        if (command.ExpectedVersion < 0)
            fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];

        Relationship(command.Contact, fields);

        Utc(command.RevisitAt, "revisitAt", fields);
        Text(command.Reason, "reason", 1, ReasonMaxLength, true, fields);
        Text(command.Note, "note", 0, NoteMaxLength, false, fields);
        Entity(command.TaskOwnerId, "ownerId", false, fields);

        return fields;
    }

    /// <summary>
    /// The admitted relationship shape. <c>contact</c> is required by the pinned schema for both
    /// modes, so an EXISTING request that omits it is refused here rather than silently linking:
    /// accepting a contract-invalid body because this workflow happens to ignore that object would
    /// make the wire contract advisory.
    /// </summary>
    private static void Relationship(LeadNurtureContactIntent contact, IDictionary<string, string[]> fields)
    {
        if (!contact.ContactSupplied)
            fields["relationship.contact"] = ["contact is required."];

        // Declared by the schema only for the ORGANIZATION_ACCOUNT kind, whose owner has no admitted
        // mutation contract. Carrying it on a CONTACT relationship asserts an intent this workflow
        // never honours, so it is refused instead of discarded.
        if (contact.OrganizationSupplied)
            fields["relationship.organization"] = ["organization is not admitted for a CONTACT relationship."];

        switch (contact.Mode)
        {
            case LeadNurtureRelationshipMode.Existing:
                if (string.IsNullOrWhiteSpace(contact.SelectedContactId))
                    fields["relationship.selectedId"] = ["selectedId is required when mode is EXISTING."];
                else
                    Entity(contact.SelectedContactId, "relationship.selectedId", true, fields);
                break;

            case LeadNurtureRelationshipMode.New:
                // NEW asserts that this person does not exist. Naming an existing Contact in the same
                // breath is a contradiction, and the frozen identity model forbids the backend from
                // choosing a limb: the decision is caller-declared, never backend-discovered.
                if (!string.IsNullOrWhiteSpace(contact.SelectedContactId))
                    fields["relationship.selectedId"] = ["selectedId must be absent when mode is NEW."];
                break;
        }

        if (contact.ContactSupplied)
        {
            Text(contact.DisplayName, "relationship.contact.displayName", 1, DisplayNameMaxLength, true, fields);
            Email(contact.Email, "relationship.contact.email", fields);
            Text(contact.Phone, "relationship.contact.phone", 1, PhoneMaxLength, false, fields);
            Text(contact.Title, "relationship.contact.title", 0, TitleMaxLength, false, fields);
        }
    }

    private static string? Text(
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

        // Bounds are applied to the trimmed value because the trimmed value is what is stored: the
        // frozen transfer writes the display name into the Contact verbatim after trimming.
        var value = input.Trim();
        if (value.Length == 0 && !required)
            return null;
        if (value.Length < minimum || value.Length > maximum)
            fields[field] = [$"{field} must contain between {minimum} and {maximum} characters."];
        return value;
    }

    /// <summary>
    /// The same address rule Leads already applies to its own <c>format: email</c> fields. A second,
    /// different notion of a valid address inside one system would be the invention.
    /// </summary>
    private static void Email(string? input, string field, IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, 0, EmailMaxLength, false, fields);
        if (value is null || fields.ContainsKey(field))
            return;
        try
        {
            var parsed = new MailAddress(value);
            if (!string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            fields[field] = [$"{field} must be a valid email address."];
        }
    }

    private static void Entity(string? input, string field, bool required, IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, required ? 1 : 0, EntityIdMaxLength, required, fields);
        if (value is null || fields.ContainsKey(field))
            return;
        if (!EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
    }

    /// <summary>
    /// The pinned <c>UtcDateTime</c> is <c>format: date-time</c> with pattern <c>Z$</c>. This is the
    /// identical rule Tasks applies to <c>dueAt</c>, which is where this value lands, so a value that
    /// passes here cannot fail there after a Contact has committed.
    /// </summary>
    private static void Utc(string? input, string field, IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input))
        {
            fields[field] = [$"{field} is required."];
            return;
        }
        if (!input.EndsWith('Z')
            || !DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            fields[field] = [$"{field} must be a UTC date-time ending in Z."];
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
