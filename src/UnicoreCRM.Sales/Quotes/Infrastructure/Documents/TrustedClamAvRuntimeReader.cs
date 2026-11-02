using UnicoreCRM.Sales.Quotes.Application.Common;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Sockets;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ProofRuntimeManifest(string Root,Dictionary<string,string> Files,string ClamdRelativePath,string QpdfRelativePath,string SigtoolRelativePath,
    string DailyRelativePath,string ConfigRelativePath,int ProcessId,DateTimeOffset ProcessStartedAt,int Port,string EngineVersion);

/// <summary>Server-configured pins and live local process ownership. No client target, version or trust flag is consulted.</summary>
internal sealed class TrustedClamAvRuntimeReader(string manifestPath,string expectedManifestSha256) : ITrustedClamAvRuntimeReader, IQuoteProofRuntimeAuthority
{
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    public async Task<QuoteProofRuntimeSnapshot?> ReadSnapshotAsync(CancellationToken ct)
    {var r=await ReadAsync(ct);return r is null?null:new(r.Digest,r.EngineVersion,r.SignatureVersion,r.SignatureUpdatedAt,r.QpdfSha256);}
    public async Task<TrustedClamAvRuntime?> ReadAsync(CancellationToken ct)
    {
        if(!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(manifestPath) || expectedManifestSha256.Length!=64)return null;
        try
        {
            if(new FileInfo(manifestPath).Length>1048576)return null;
            var manifestBytes=await File.ReadAllBytesAsync(manifestPath,ct);
            var digest=Convert.ToHexString(SHA256.HashData(manifestBytes));if(!string.Equals(digest,expectedManifestSha256,StringComparison.OrdinalIgnoreCase))return null;
            var m=JsonSerializer.Deserialize<ProofRuntimeManifest>(manifestBytes,Json);
            if(m is null || !Path.IsPathFullyQualified(m.Root) || m.Files.Count is <1 or >500 || m.Port is <1024 or >65535 || m.ProcessId<1 || m.EngineVersion!="1.5.4")return null;
            string? Resolve(string relative)
            {
                if(Path.IsPathRooted(relative) || relative.Contains('\n') || relative.Contains('\r'))return null;
                var full=Path.GetFullPath(relative,m.Root);return full.StartsWith(Path.GetFullPath(m.Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?full:null;
            }
            foreach(var item in m.Files)
            {
                var file=Resolve(item.Key);if(file is null || item.Value.Length!=64 || !File.Exists(file))return null;
                await using var stream=File.OpenRead(file);var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
                if(!string.Equals(hash,item.Value,StringComparison.OrdinalIgnoreCase))return null;
            }
            foreach(var required in new[]{m.ClamdRelativePath,m.QpdfRelativePath,m.SigtoolRelativePath,m.DailyRelativePath,m.ConfigRelativePath})if(!m.Files.ContainsKey(required))return null;
            var clamd=Resolve(m.ClamdRelativePath)!;var qpdf=Resolve(m.QpdfRelativePath)!;var sigtool=Resolve(m.SigtoolRelativePath)!;var daily=Resolve(m.DailyRelativePath)!;
            using var process=Process.GetProcessById(m.ProcessId);
            if(process.HasExited || process.StartTime.ToUniversalTime()!=m.ProcessStartedAt.UtcDateTime || !string.Equals(process.MainModule?.FileName,clamd,StringComparison.OrdinalIgnoreCase)
                || ListenerProcessId(m.Port)!=m.ProcessId || process.WorkingSet64>2147483648)return null;
            var config=await File.ReadAllLinesAsync(Resolve(m.ConfigRelativePath)!,ct);
            if(!ConfigurationValid(config,m.Port,Path.GetDirectoryName(daily)!,Path.Combine(Path.GetDirectoryName(clamd)!,"certs"),Path.Combine(m.Root,"clamd.log")))return null;
            foreach(var folder in new[]{Path.GetDirectoryName(clamd)!,Path.GetDirectoryName(qpdf)!}.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach(var file in Directory.EnumerateFiles(folder).Where(f=>Path.GetExtension(f).Equals(".dll",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(f).Equals(".exe",StringComparison.OrdinalIgnoreCase)))
                    if(!m.Files.ContainsKey(Path.GetRelativePath(m.Root,file).Replace('\\','/')))return null;
            foreach(var file in Directory.EnumerateFiles(Path.GetDirectoryName(daily)!))
                if(Path.GetFileName(file)!="freshclam.dat" && !m.Files.ContainsKey(Path.GetRelativePath(m.Root,file).Replace('\\','/')))return null;
            foreach(var file in Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(clamd)!,"certs")))
                if(!m.Files.ContainsKey(Path.GetRelativePath(m.Root,file).Replace('\\','/')))return null;
            var verified=await ProofToolProcess.RunAsync(sigtool,["--info",daily],ReadOnlyMemory<byte>.Empty,65536,TimeSpan.FromSeconds(5),ct);
            if(verified.ExitCode!=0 || verified.HadDiagnostic || !Encoding.UTF8.GetString(verified.Output).Split('\n').Any(x=>x.Trim()=="Verification OK."))return null;
            var header=new byte[512];await using(var data=File.OpenRead(daily))await data.ReadExactlyAsync(header,ct);
            var fields=Encoding.ASCII.GetString(header).Trim().Split(':');
            if(fields.Length<9 || fields[0]!="ClamAV-VDB" || !long.TryParse(fields[8],NumberStyles.None,CultureInfo.InvariantCulture,out var epoch)
                || !long.TryParse(fields[2],NumberStyles.None,CultureInfo.InvariantCulture,out var version) || version<1)return null;
            // A live PID/listener alone is not scanner availability: a hung daemon must also block release of prior CLEAN bytes.
            if(!await RespondsAsync(m.Port,m.EngineVersion,fields[2],ct))return null;
            return new(digest,m.Port,m.ProcessId,m.EngineVersion,fields[2],DateTimeOffset.FromUnixTimeSeconds(epoch),qpdf,m.Files[m.QpdfRelativePath]);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception
            or ArgumentException or ProofToolException){return null;}
    }
    private static async Task<bool> RespondsAsync(int port,string engine,string signatures,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var client=new TcpClient(AddressFamily.InterNetwork);await client.ConnectAsync(IPAddress.Loopback,port,deadline.Token);
            await using var stream=client.GetStream();await stream.WriteAsync("zVERSION\0"u8.ToArray(),deadline.Token);
            var result=await ClamAvProofScanner.ReadTerminalAsync(stream,deadline.Token);
            return result.StartsWith($"ClamAV {engine}/{signatures}/",StringComparison.Ordinal);
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return false;}
        catch(Exception e)when(e is SocketException or IOException){return false;}
    }
    internal static bool ConfigurationValid(IReadOnlyList<string> lines,int port,string databaseDirectory,string certificatesDirectory,string logFile)
    {
        var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TCPSocket"]=port.ToString(CultureInfo.InvariantCulture),["TCPAddr"]="127.0.0.1",["Foreground"]="yes",["LogFileMaxSize"]="2M",["LogTime"]="yes",["LogClean"]="no",["LogVerbose"]="no",["LogRotate"]="yes",
            ["StreamMaxLength"]="10M",["MaxThreads"]="2",["MaxQueue"]="4",["ReadTimeout"]="30",["CommandReadTimeout"]="5",["SendBufTimeout"]="1000",["SelfCheck"]="0",
            ["ScanPDF"]="yes",["ScanArchive"]="yes",["MaxScanTime"]="30000",["MaxScanSize"]="64M",["MaxFileSize"]="10M",["MaxRecursion"]="8",["MaxFiles"]="1000",["AlertExceedsMax"]="yes",
            ["Bytecode"]="yes",["BytecodeSecurity"]="TrustSigned",["BytecodeUnsigned"]="no",["DisableCache"]="yes",["EnableReloadCommand"]="no",["EnableShutdownCommand"]="no",["EnableVersionCommand"]="yes",["EnableStatsCommand"]="yes"
        };
        var actual=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in lines.Select(x=>x.Trim()).Where(x=>x.Length>0 && !x.StartsWith('#')))
        {var split=line.IndexOfAny([' ','\t']);if(split<1 || !actual.TryAdd(line[..split],line[(split+1)..].Trim().Trim('"')))return false;}
        foreach(var path in new[]{("DatabaseDirectory",databaseDirectory),("CVDCertsDirectory",certificatesDirectory),("LogFile",logFile)})
        {
            if(!actual.Remove(path.Item1,out var value) || !Path.IsPathFullyQualified(value) || !string.Equals(Path.GetFullPath(value),Path.GetFullPath(path.Item2),StringComparison.OrdinalIgnoreCase))return false;
        }
        return actual.Count==expected.Count && expected.All(x=>actual.TryGetValue(x.Key,out var value) && value.Equals(x.Value,StringComparison.OrdinalIgnoreCase));
    }
    private static int ListenerProcessId(int port)
    {
        var size=0;var status=GetExtendedTcpTable(IntPtr.Zero,ref size,true,2,3,0);if(status!=122 || size is <4 or >1048576)return -1;
        var pointer=Marshal.AllocHGlobal(size);
        try
        {
            if(GetExtendedTcpTable(pointer,ref size,true,2,3,0)!=0)return -1;
            var count=Marshal.ReadInt32(pointer);var rowSize=Marshal.SizeOf<TcpRow>();if(count<0 || count>(size-4)/rowSize)return -1;
            for(var i=0;i<count;i++)
            {
                var row=Marshal.PtrToStructure<TcpRow>(IntPtr.Add(pointer,4+i*rowSize));
                var rowPort=((row.LocalPort&255)<<8)|((row.LocalPort>>8)&255);
                if(rowPort==port && row.LocalAddress==0x0100007F && row.State==2)return checked((int)row.ProcessId);
            }
            return -1;
        }
        finally{Marshal.FreeHGlobal(pointer);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct TcpRow { public uint State,LocalAddress,LocalPort,RemoteAddress,RemotePort,ProcessId; }
    [DllImport("iphlpapi.dll",SetLastError=true)]private static extern uint GetExtendedTcpTable(IntPtr table,ref int size,[MarshalAs(UnmanagedType.Bool)]bool sorted,int family,int tableClass,uint reserved);
}
