using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed class QpdfProofValidatorCore
{
    internal async Task<ProofParserResult> ValidateAsync(byte[] bytes,string executable,string expectedExecutableSha256,CancellationToken ct,string ownedDirectory)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();var hash=Convert.ToHexString(SHA256.HashData(bytes));
        ProofParserResult Result(string status,string reason,string version="",ProofPdfInspection? inspection=null)=>new(status,reason,hash,bytes.Length,"qpdf",version,expectedExecutableSha256,watch.ElapsedMilliseconds,inspection);
        if(bytes.Length is <1 or >10485760 || !bytes.AsSpan().StartsWith("%PDF-"u8))return Result("INVALID_PDF","PDF_INPUT_INVALID");
        if(!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || expectedExecutableSha256.Length!=64
            || Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable,ct)))!=expectedExecutableSha256.ToUpperInvariant())return Result("RESULT_INDETERMINATE","PDF_RUNTIME_PIN_MISMATCH");
        var directory=Path.GetFullPath(ownedDirectory);
        if(!directory.StartsWith(Path.Combine(Path.GetTempPath(),"UnicoreCRM-QuarantineValidation")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return Result("RESULT_INDETERMINATE","PDF_TEMP_SCOPE_INVALID");
        var input=Path.Combine(directory,"proof.pdf");string version="";
        try
        {
            await File.WriteAllBytesAsync(input,bytes,ct);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var versionResult=await ProofToolProcess.RunAsync(executable,["--version"],ReadOnlyMemory<byte>.Empty,4096,TimeSpan.FromSeconds(3),timeout.Token);
            version=System.Text.Encoding.UTF8.GetString(versionResult.Output).Split('\n')[0].Trim();
            if(versionResult.ExitCode!=0 || versionResult.HadDiagnostic || version!="qpdf version 12.4.2")return Result("RESULT_INDETERMINATE","PDF_RUNTIME_VERSION_UNSUPPORTED",version);
            string[] limits=["--global","--parser-max-nesting=64","--parser-max-container-size=10000","--parser-max-container-size-damaged=10000","--parser-max-errors=1","--max-stream-filters=4","--"];
            var check=await ProofToolProcess.RunAsync(executable,[..limits,"--check",input],ReadOnlyMemory<byte>.Empty,65536,TimeSpan.FromSeconds(12),timeout.Token,true);
            if(check.ExitCode!=0 || check.HadDiagnostic)return Result("INVALID_PDF","PDF_SYNTAX_WARNING_OR_ERROR",version);
            var graph=await ProofToolProcess.RunAsync(executable,[..limits,"--json","--json-stream-data=inline","--decode-level=generalized",input],ReadOnlyMemory<byte>.Empty,67108864,TimeSpan.FromSeconds(12),timeout.Token,true);
            if(graph.ExitCode!=0 || graph.HadDiagnostic)return Result("INVALID_PDF","PDF_GRAPH_WARNING_OR_ERROR",version);
            if(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(input,ct)))!=hash)return Result("RESULT_INDETERMINATE","PDF_INPUT_CHANGED",version);
            var inspection=QuoteProofPdfInspector.Inspect(graph.Output);
            return Result(inspection.Status,inspection.ReasonCode,version,inspection);
        }
        catch(ProofToolException e){return Result("RESULT_INDETERMINATE",e.Code,version);}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return Result("RESULT_INDETERMINATE","PDF_PROCESS_TIMEOUT",version);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){return Result("RESULT_INDETERMINATE","PDF_RUNTIME_UNAVAILABLE",version);}
        finally
        {
            // Only the generated owner directory is removed, never a caller-provided location.
            if(directory.StartsWith(Path.Combine(Path.GetTempPath(),"UnicoreCRM-QuarantineValidation")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                try{Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
