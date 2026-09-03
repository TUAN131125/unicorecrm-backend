namespace UnicoreCRM.Crm.Contacts.Domain;

/// <summary>
/// The Contact canonical name bound, frozen by <c>DEC-LEAD-CONTACT-NAME-BOUND</c>.
///
/// It is one number in four places that all already agreed: <c>ContactDocument.fullName</c> and its
/// read-only <c>displayName</c> projection, <c>CreateContactRequest.fullName</c> and
/// <c>UpdateContactRequest.fullName</c>, and the <c>contacts.Contacts.FullName</c> column. Any name
/// this owner accepts must be representable in every one of them, so the bound is the Contact
/// aggregate's, and every writer - including the Lead qualification transfer - adopts it rather than
/// carrying its own.
/// </summary>
internal static class ContactNameBound
{
    internal const int MaxLength = 200;
}
