using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed record ProofToolResult(int ExitCode,byte[] Output,bool HadDiagnostic);
internal sealed class ProofToolException(string code) : Exception(code) { internal string Code { get; }=code; }

/// <summary>Owner-specific bounded process launcher. Native work cannot consume an unbounded API background task.</summary>
internal static class ProofToolProcess
{
    internal static async Task<ProofToolResult> RunAsync(string executable,IReadOnlyList<string> arguments,ReadOnlyMemory<byte> input,
        int outputLimit,TimeSpan deadline,CancellationToken ct,bool argumentsOnStdin=false,long memoryLimit=536870912,int maximumProcesses=1,string? managedHeapLimit=null)
    {
        if(!OperatingSystem.IsWindows())throw new ProofToolException("PDF_EXECUTION_ISOLATION_UNAVAILABLE");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(deadline);
        using var process=new Process();using var job=new ProcessJob(memoryLimit,maximumProcesses);
        process.StartInfo=new(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            WorkingDirectory=Path.GetDirectoryName(executable)!};
        process.StartInfo.Environment.Clear();
        foreach(var key in new[]{"SystemRoot","WINDIR","TEMP","TMP"})if(Environment.GetEnvironmentVariable(key) is {} value)process.StartInfo.Environment[key]=value;
        process.StartInfo.Environment["PATH"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32");
        if(managedHeapLimit is not null){process.StartInfo.Environment["DOTNET_GCHeapHardLimit"]=managedHeapLimit;process.StartInfo.Environment["DOTNET_GCServer"]="0";}
        if(argumentsOnStdin)process.StartInfo.ArgumentList.Add("@-");else foreach(var arg in arguments)process.StartInfo.ArgumentList.Add(arg);
        var diagnostics=false;
        try
        {
            if(!process.Start())throw new ProofToolException("PDF_PROCESS_START_FAILED");
            // qpdf @- blocks before reading arguments/input until the job is attached.
            job.Attach(process);
            using var cancelled=timeout.Token.Register(()=>Kill(process));
            var output=ReadBoundedAsync(process.StandardOutput.BaseStream,outputLimit,timeout.Token,()=>Kill(process));
            var errors=ReadBoundedAsync(process.StandardError.BaseStream,65536,timeout.Token,()=>Kill(process));
            var payload=argumentsOnStdin?Encoding.UTF8.GetBytes(string.Join("\n",arguments)+"\n"):input.ToArray();
            await process.StandardInput.BaseStream.WriteAsync(payload,timeout.Token);process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var bytes=await output;diagnostics=(await errors).Length!=0;
            return new(process.ExitCode,bytes,diagnostics);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new ProofToolException("PDF_PROCESS_TIMEOUT");}
        finally{Kill(process);try{await process.WaitForExitAsync(CancellationToken.None);}catch(InvalidOperationException){}}
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream,int maximum,CancellationToken ct,Action abort)
    {
        using var data=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await stream.ReadAsync(buffer,ct))>0){if(data.Length+read>maximum){abort();throw new ProofToolException("PDF_PROCESS_OUTPUT_LIMIT");}await data.WriteAsync(buffer.AsMemory(0,read),ct);}
        return data.ToArray();
    }
    private static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    private sealed class ProcessJob : IDisposable
    {
        private readonly IntPtr handle;
        internal ProcessJob(long memory,int maximumProcesses)
        {
            handle=CreateJobObjectW(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new ProofToolException("PDF_JOB_LIMIT_UNAVAILABLE");
            var info=new ExtendedLimit{Basic=new(){LimitFlags=0x100|0x200|0x2000|0x8,ActiveProcessLimit=(uint)maximumProcesses},ProcessMemoryLimit=(UIntPtr)memory,JobMemoryLimit=(UIntPtr)(memory+268435456)};
            var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<ExtendedLimit>());
            try{Marshal.StructureToPtr(info,pointer,false);if(!SetInformationJobObject(handle,9,pointer,(uint)Marshal.SizeOf<ExtendedLimit>()))throw new ProofToolException("PDF_JOB_LIMIT_UNAVAILABLE");}
            catch{CloseHandle(handle);throw;}finally{Marshal.FreeHGlobal(pointer);}
        }
        internal void Attach(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new ProofToolException("PDF_JOB_ATTACH_FAILED");}
        public void Dispose()=>CloseHandle(handle);
    }
    [StructLayout(LayoutKind.Sequential)]private struct BasicLimit { public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)]private struct IoCounters { public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)]private struct ExtendedLimit { public BasicLimit Basic;public IoCounters Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateJobObjectW(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetInformationJobObject(IntPtr job,int infoClass,IntPtr info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(IntPtr handle);
}
