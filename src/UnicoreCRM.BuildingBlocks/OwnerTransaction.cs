using System.Data.Common;
namespace UnicoreCRM.BuildingBlocks;

// A caller-owned local SQL transaction. Each participant enlists its own persistence;
// no owner exposes a DbContext or writes another owner's tables.
public sealed record OwnerTransaction(DbConnection Connection, DbTransaction Transaction);
public sealed class BusinessCommandException(string code,int status=409):Exception(code)
{
    public string Code { get; }=code;
    public int Status { get; }=status;
}
