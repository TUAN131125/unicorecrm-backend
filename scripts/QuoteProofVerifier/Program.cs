using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text.Json;
// Development-only read-only SQL integrity harness. It emits hashes/counts, never proof plaintext or keys.
if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Development" || args.Length!=3)throw new InvalidOperationException("Development integrity invocation required.");
var connection=Environment.GetEnvironmentVariable("UNICORE_PROOF_VERIFY_CONNECTION")??throw new InvalidOperationException("Connection required.");
var builder=new SqlConnectionStringBuilder(connection);
if(builder.DataSource!="DESKTOP-ICH225R" || builder.InitialCatalog!="UnicoreCRM_Development")throw new InvalidOperationException("Exact Development database required.");
await using var db=new SqlConnection(connection);await db.OpenAsync();
await using(var machine=new SqlCommand("SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128))",db))
 if(!string.Equals((string?)await machine.ExecuteScalarAsync(),Environment.MachineName,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Local SQL required.");
await using var command=new SqlCommand("SELECT c.ProtectedBytes,c.Sha256,c.ContentLength,p.Sha256,p.FileSize,p.StorageStatus,p.ScanStatus FROM quotes.AcceptanceProofContents c JOIN quotes.AcceptanceProofs p ON p.WorkspaceId=c.WorkspaceId AND p.ProofId=c.ProofId AND p.CaseId=c.CaseId WHERE c.WorkspaceId=@w AND c.CaseId=@c AND c.ProofId=@p",db);
command.Parameters.AddWithValue("@w",args[0]);command.Parameters.AddWithValue("@c",args[1]);command.Parameters.AddWithValue("@p",args[2]);
await using var row=await command.ExecuteReaderAsync();if(!await row.ReadAsync())throw new InvalidOperationException("Proof missing.");
var provider=DataProtectionProvider.Create("UnicoreCRM.AI");var encrypted=(byte[])row[0];
var bytes=provider.CreateProtector("UnicoreCRM.Quotes.AcceptanceProof.v1",args[0],args[1],args[2]).Unprotect(encrypted);
var hash=Convert.ToHexString(SHA256.HashData(bytes));
var valid=hash==row.GetString(1) && hash==row.GetString(3) && bytes.Length==row.GetInt32(2) && bytes.Length==row.GetInt32(4);
Console.WriteLine(JsonSerializer.Serialize(new{proofId=args[2],sha256=hash,plainBytes=bytes.Length,storedBytes=encrypted.Length,hashMatches=valid,storageStatus=row.GetString(5),scanStatus=row.GetString(6),recoveryScope="SAME_WINDOWS_PROFILE_KEYRING_ONLY"}));
CryptographicOperations.ZeroMemory(bytes);if(!valid)Environment.ExitCode=1;
