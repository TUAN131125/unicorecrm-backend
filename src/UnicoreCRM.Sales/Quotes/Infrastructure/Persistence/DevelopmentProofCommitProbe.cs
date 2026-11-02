using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;
internal sealed class DevelopmentProofCommitProbe(string? workspaceId=null,bool failAfterFlush=false) : IQuoteProofCommitProbe
{
    public void AfterStorageFlush(string workspace) { if(failAfterFlush && workspaceId==workspace) throw new InvalidOperationException("Development proof storage commit probe."); }
}
