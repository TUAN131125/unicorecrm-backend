using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.ProvideQuoteRecordAccessFacts;

internal sealed partial class QuoteRecordAccessFactProvider(IQuotesPersistence persistence) : IRecordAccessFactProvider
{
    private static readonly RecordAccessResourceDescriptor QuotesDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: QuoteAuthorization.ResourceKey,
        readCapability: QuoteCapabilities.Read.Capability,
        enforceableFields: QuoteFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => QuotesDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!EntityIdPattern().IsMatch(recordId))
            return RecordAccessFacts.NotFound;

        var quote = await persistence.ReadQuoteAsync(
            trustedWorkspace.WorkspaceId,
            recordId,
            cancellationToken);
        return quote is null ? RecordAccessFacts.NotFound : QuoteAuthorization.Facts(quote);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
