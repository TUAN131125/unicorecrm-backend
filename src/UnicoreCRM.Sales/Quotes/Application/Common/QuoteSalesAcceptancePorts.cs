using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal interface IQuoteSalesAcceptanceStore
{
    Task<QuoteSalesAcceptance?> ReadAsync(string workspace,string quote,CancellationToken ct);
    Task<QuoteAcceptanceInvitation?> InvitationAsync(string tokenHash,CancellationToken ct);
    Task<QuoteAcceptanceInvitation?> InvitationByIdAsync(string workspace,string quote,string invitationId,CancellationToken ct);
    Task<IReadOnlyList<QuoteAcceptanceInvitation>> InvitationsAsync(string workspace,string quote,CancellationToken ct);
    void Add(QuoteSalesAcceptance acceptance);
    void Add(QuoteAcceptanceInvitation invitation);
    Task ReloadAsync(QuoteAcceptanceInvitation invitation,CancellationToken ct);
    Task ReloadQuoteAsync(Quote quote,CancellationToken ct);
    Task<bool> HasUnresolvedDeliveryAsync(string workspace,string quote,CancellationToken ct);
}
internal interface IQuoteGmailSender
{
    string? ConfigurationError(string workspace);
    Task<QuoteGmailResult> SendAsync(string workspace,string recipient,string subject,string body,byte[] pdf,string fileName,string messageId,CancellationToken ct);
}
internal sealed record QuoteGmailResult(string Status, string? ErrorCode);
