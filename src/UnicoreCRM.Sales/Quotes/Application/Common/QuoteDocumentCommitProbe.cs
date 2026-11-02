namespace UnicoreCRM.Sales.Quotes.Application.Common;

/// <summary>Optional Development verification probe. No HTTP input selects or configures this hook.</summary>
internal interface IQuoteDocumentCommitProbe { void AfterStorageFlush(string workspaceId); }
internal sealed class QuoteDocumentVerificationStorageException() : Exception("Development document storage interruption.");
