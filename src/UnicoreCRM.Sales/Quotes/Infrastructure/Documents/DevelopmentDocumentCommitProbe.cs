using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed class DevelopmentDocumentCommitProbe(string? workspaceId = null, bool failAfterFlush = false) : IQuoteDocumentCommitProbe
{
    public void AfterStorageFlush(string currentWorkspace)
    { if (failAfterFlush && workspaceId is not null && workspaceId == currentWorkspace) throw new QuoteDocumentVerificationStorageException(); }
}
