using UnicoreCRM.Sales.Quotes.Application.Common;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed class QpdfProofValidator
{
    private static readonly SemaphoreSlim Slots=new(2,2);
    internal async Task<ProofParserResult> ValidateAsync(byte[] bytes,string executable,string expectedExecutableSha256,CancellationToken ct)
    {
        var hash=Convert.ToHexString(SHA256.HashData(bytes));var watch=System.Diagnostics.Stopwatch.StartNew();
        ProofParserResult Fail(string code)=>new("RESULT_INDETERMINATE",code,hash,bytes.Length,"qpdf","",expectedExecutableSha256,watch.ElapsedMilliseconds);
        if(bytes.Length is <1 or >10485760)return Fail("PDF_INPUT_LIMIT");
        if(!await Slots.WaitAsync(0,ct))return Fail("PDF_VALIDATION_CAPACITY");
        var directory=Path.Combine(Path.GetTempPath(),"UnicoreCRM-QuarantineValidation",Guid.NewGuid().ToString("N"));
        try
        {
            if(!OperatingSystem.IsWindows())return Fail("PDF_EXECUTION_ISOLATION_UNAVAILABLE");
            Directory.CreateDirectory(directory);
            var identity=WindowsIdentity.GetCurrent().User??throw new InvalidOperationException("Proof worker identity unavailable.");
            var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);security.SetOwner(identity);
            foreach(var sid in new[]{identity,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
                security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
            var worker=Path.Combine(AppContext.BaseDirectory,"QuoteProofValidatorWorker","UnicoreCRM.QuoteProofValidatorWorker.dll");
            var dotnet=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));
            if(!File.Exists(worker) || !File.Exists(dotnet))return Fail("PDF_VALIDATOR_WORKER_UNAVAILABLE");
            var result=await ProofToolProcess.RunAsync(dotnet,[worker,executable,expectedExecutableSha256,directory],bytes,16384,TimeSpan.FromSeconds(20),ct,
                maximumProcesses:2,managedHeapLimit:"0x10000000");
            if(result.ExitCode!=0 || result.HadDiagnostic)return Fail("PDF_VALIDATOR_WORKER_FAILED");
            var assessment=JsonSerializer.Deserialize<ProofParserResult>(result.Output);
            return assessment is not null && assessment.InputSha256==hash && assessment.InputLength==bytes.Length && assessment.ParserSha256==expectedExecutableSha256
                ?assessment:Fail("PDF_VALIDATOR_RESULT_BINDING_MISMATCH");
        }
        catch(ProofToolException e){return Fail(e.Code);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception){return Fail("PDF_VALIDATOR_RUNTIME_FAILED");}
        finally
        {
            if(directory.StartsWith(Path.Combine(Path.GetTempPath(),"UnicoreCRM-QuarantineValidation")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                try{Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            Slots.Release();
        }
    }
}
