namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteLifecyclePolicy(string Version, bool AllowRevision, bool AllowExpiration, bool AllowArchive);
internal interface IQuoteLifecyclePolicyReader { QuoteLifecyclePolicy? Read(string workspaceId); }
