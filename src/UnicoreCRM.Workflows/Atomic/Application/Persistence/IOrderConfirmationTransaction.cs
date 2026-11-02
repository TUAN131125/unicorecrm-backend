using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Workflows.Atomic.Domain;
namespace UnicoreCRM.Workflows.Atomic.Application.Persistence;
internal interface IOrderConfirmationTransaction
{
    Task<IOrderConfirmationSession> BeginAsync(string scope,string workspace,string order,CancellationToken ct);
}
internal interface IOrderConfirmationSession:IAsyncDisposable
{
    OwnerTransaction OwnerTransaction {get;}
    Task<OrderConfirmationReceipt?> FindAsync(string scope,CancellationToken ct);
    Task CommitAsync(OrderConfirmationReceipt receipt,CancellationToken ct);
}
