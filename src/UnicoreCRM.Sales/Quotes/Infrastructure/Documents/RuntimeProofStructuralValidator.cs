using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;
internal sealed class RuntimeProofStructuralValidator(ITrustedClamAvRuntimeReader runtime):IQuoteProofStructuralValidator
{
    public async Task<ProofParserResult> ValidateAsync(byte[] bytes,CancellationToken ct)
    {var r=await runtime.ReadAsync(ct);return r is null?new("RESULT_INDETERMINATE","PARSER_RUNTIME_AUTHORITY_UNAVAILABLE","",bytes.Length,"qpdf","","",0):await new QpdfProofValidator().ValidateAsync(bytes,r.QpdfExecutable,r.QpdfSha256,ct);}
}
