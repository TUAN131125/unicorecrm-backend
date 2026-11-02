namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuotePdfRenderResult(byte[] Bytes, int PageCount, long DurationMs, long PeakMemoryBytes);
internal interface IQuotePdfRenderer
{
    Task<QuotePdfRenderResult> RenderAsync(string snapshotJson, CancellationToken cancellationToken);
}
internal sealed class QuotePdfException(string code) : Exception(code) { internal string Code { get; } = code; }
