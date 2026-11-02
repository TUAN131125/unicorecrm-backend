using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed record TrustedClamAvRuntime(string Digest,int Port,int ProcessId,string EngineVersion,string SignatureVersion,DateTimeOffset SignatureUpdatedAt,string QpdfExecutable,string QpdfSha256);
internal interface ITrustedClamAvRuntimeReader { Task<TrustedClamAvRuntime?> ReadAsync(CancellationToken ct); }

internal sealed partial class ClamAvProofScanner(ITrustedClamAvRuntimeReader runtime,TimeProvider clock) : ITrustedProofScanner
{
    public async Task<TrustedProofScanResult> ScanAsync(TrustedProofScanInput input,Stream quarantinedContent,CancellationToken ct)
    {
        var started=clock.GetUtcNow();var watch=System.Diagnostics.Stopwatch.StartNew();TrustedClamAvRuntime? trusted=null;
        var id="qscan_"+Guid.NewGuid().ToString("N");var actualHash="";long actualLength=0;
        TrustedProofScanResult Result(string verdict,string code)=>new(id,input.ProofId,actualHash,actualLength,"CLAMAV_INSTREAM",trusted?.EngineVersion??"",trusted?.SignatureVersion??"",trusted?.SignatureUpdatedAt,
            trusted?.Digest??"",verdict=="CLEAN"?ProofScanState.CLEAN:verdict=="UNSAFE"?ProofScanState.UNSAFE:ProofScanState.SCAN_FAILED,
            started,clock.GetUtcNow(),verdict,code,watch.ElapsedMilliseconds);
        if(input.ContentLength is <1 or >10485760 || input.Sha256.Length!=64)return Result("RESULT_INDETERMINATE","PROOF_INPUT_BINDING_INVALID");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            trusted=await runtime.ReadAsync(timeout.Token);if(trusted is null)return Result("SCANNER_UNAVAILABLE","SCANNER_RUNTIME_AUTHORITY_UNAVAILABLE");
            if(trusted.SignatureUpdatedAt>started || trusted.SignatureUpdatedAt<started.AddHours(-24))return Result("SIGNATURE_STALE","SCANNER_SIGNATURE_NOT_FRESH");
            var before=await CommandAsync(trusted.Port,"VERSION",timeout.Token);
            if(!before.StartsWith($"ClamAV {trusted.EngineVersion}/{trusted.SignatureVersion}/",StringComparison.Ordinal))return Result("RESULT_INDETERMINATE","SCANNER_DATABASE_BINDING_MISMATCH");
            using var client=new TcpClient(AddressFamily.InterNetwork);await client.ConnectAsync(IPAddress.Loopback,trusted.Port,timeout.Token);
            await using var connection=client.GetStream();await connection.WriteAsync("zINSTREAM\0"u8.ToArray(),timeout.Token);
            var buffer=new byte[65536];var frame=new byte[4];using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);int read;
            while((read=await quarantinedContent.ReadAsync(buffer,timeout.Token))>0)
            {
                actualLength=checked(actualLength+read);if(actualLength>10485760 || actualLength>input.ContentLength)return Result("RESULT_INDETERMINATE","PROOF_INPUT_LENGTH_MISMATCH");
                sha.AppendData(buffer,0,read);BinaryPrimitives.WriteInt32BigEndian(frame,read);await connection.WriteAsync(frame,timeout.Token);await connection.WriteAsync(buffer.AsMemory(0,read),timeout.Token);
            }
            actualHash=Convert.ToHexString(sha.GetHashAndReset());
            if(actualLength!=input.ContentLength || !string.Equals(actualHash,input.Sha256,StringComparison.OrdinalIgnoreCase))return Result("RESULT_INDETERMINATE","PROOF_INPUT_HASH_MISMATCH");
            await connection.WriteAsync(new byte[4],timeout.Token);var terminal=await ReadTerminalAsync(connection,timeout.Token);
            var after=await CommandAsync(trusted.Port,"VERSION",timeout.Token);var current=await runtime.ReadAsync(timeout.Token);
            if(before!=after || current is null || trusted!=current)return Result("RESULT_INDETERMINATE","SCANNER_RUNTIME_CHANGED_DURING_SCAN");
            var completed=clock.GetUtcNow();
            if(completed<started || trusted.SignatureUpdatedAt>completed || trusted.SignatureUpdatedAt<completed.AddHours(-24))return Result("SIGNATURE_STALE","SCANNER_SIGNATURE_NOT_FRESH_AT_COMPLETION");
            var verdict=ParseTerminal(terminal);
            return Result(verdict.Verdict,verdict.Code);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return Result("SCANNER_TIMEOUT","SCANNER_DEADLINE_EXCEEDED");}
        catch(Exception e) when(e is SocketException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){return Result("SCANNER_UNAVAILABLE","SCANNER_CONNECTION_OR_RUNTIME_FAILED");}
    }
    internal static (string Verdict,string Code) ParseTerminal(string text)
    {
        if(text=="stream: OK")return ("CLEAN","CLAMAV_SCAN_CLEAN");
        if(text.StartsWith("stream: ",StringComparison.Ordinal) && text.EndsWith(" FOUND",StringComparison.Ordinal))
        {
            var threat=text[8..^6];if(!ThreatName().IsMatch(threat))return ("RESULT_INDETERMINATE","SCANNER_RESPONSE_INVALID");
            return threat.Contains("Limits.Exceeded",StringComparison.OrdinalIgnoreCase)?("RESULT_INDETERMINATE","SCANNER_RESOURCE_LIMIT"):("UNSAFE","CLAMAV_THREAT_DETECTED");
        }
        return ("SCAN_FAILED","SCANNER_ERROR_OR_PARTIAL_RESULT");
    }
    private static async Task<string> CommandAsync(int port,string command,CancellationToken ct)
    {
        using var client=new TcpClient(AddressFamily.InterNetwork);await client.ConnectAsync(IPAddress.Loopback,port,ct);
        await using var stream=client.GetStream();await stream.WriteAsync(Encoding.ASCII.GetBytes("z"+command+"\0"),ct);return await ReadTerminalAsync(stream,ct);
    }
    internal static async Task<string> ReadTerminalAsync(Stream stream,CancellationToken ct)
    {
        var result=new byte[4096];var count=0;
        while(count<result.Length)
        {
            var read=await stream.ReadAsync(result.AsMemory(count),ct);if(read==0)throw new IOException("Incomplete scanner response.");
            var end=Array.IndexOf(result,(byte)0,count,read);if(end>=0){if(end!=count+read-1)throw new IOException("Ambiguous scanner response.");return Encoding.ASCII.GetString(result,0,end);}
            count+=read;
        }
        throw new IOException("Scanner response limit.");
    }
    [GeneratedRegex("^[A-Za-z0-9_.()!+:-]{1,180}$",RegexOptions.CultureInvariant)]private static partial Regex ThreatName();
}
