using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class QuoteProofCipher(IDataProtectionProvider provider) : IQuoteProofCipher
{
    public byte[] Protect(string workspace,string caseId,string proofId,byte[] bytes)=>Protector(workspace,caseId,proofId).Protect(bytes);
    internal byte[] Unprotect(string workspace,string caseId,string proofId,byte[] bytes)=>Protector(workspace,caseId,proofId).Unprotect(bytes);
    private IDataProtector Protector(string workspace,string caseId,string proofId)=>provider.CreateProtector("UnicoreCRM.Quotes.AcceptanceProof.v1",workspace,caseId,proofId);
}
