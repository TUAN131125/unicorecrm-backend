using System.Security.Cryptography;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

if(args.Length is not (3 or 4) || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Development")throw new InvalidOperationException("Development runtime verifier invocation required.");
var manifest=Path.GetFullPath(args[0]);var fixture=Path.GetFullPath(args[2]);
if(!fixture.Contains(Path.Combine("quote-proof-scanner","fixtures"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Only task synthetic fixtures admitted.");
var reader=new TrustedClamAvRuntimeReader(manifest,args[1]);var runtime=await reader.ReadAsync(CancellationToken.None);
if(runtime is null){Console.WriteLine("{\"runtimeAuthority\":\"UNAVAILABLE\"}");return 2;}
var bytes=await File.ReadAllBytesAsync(fixture);
var detection=args.Length==4 && args[3] is "safe-av-detection" or "pdf-av-detection";
if(detection)
{
    using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};
    bytes=await http.GetByteArrayAsync("https://secure.eicar.org/eicar.com.txt");
    if(bytes.Length!=68 || Convert.ToHexString(SHA256.HashData(bytes))!="275A021BBFB6489E54D471899F7DB9D1663FC695EC2FE2A2C4538AABF651FD0F")throw new InvalidOperationException("Official safe detection fixture identity mismatch.");
    if(args[3]=="pdf-av-detection")
    {
        var consent="BT /F1 12 Tf (SYNTHETIC SAFE SCANNER DETECTION TEST ONLY) Tj ET";
        var objects=new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",$"<< /Length {consent.Length} >>\nstream\n{consent}\nendstream",
            $"<< /Length 68 >>\nstream\n{System.Text.Encoding.ASCII.GetString(bytes)}\nendstream"};
        using var output=new MemoryStream();void Write(string s)=>output.Write(System.Text.Encoding.ASCII.GetBytes(s));
        Write("%PDF-1.7\n");var offsets=new List<long>();for(var i=0;i<objects.Length;i++){offsets.Add(output.Length);Write($"{i+1} 0 obj\n{objects[i]}\nendobj\n");}
        var xref=output.Length;Write($"xref\n0 {objects.Length+1}\n0000000000 65535 f \n");foreach(var offset in offsets)Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");bytes=output.ToArray();
    }
}
var hash=Convert.ToHexString(SHA256.HashData(bytes));
var parser=await new QpdfProofValidator().ValidateAsync(bytes,runtime.QpdfExecutable,runtime.QpdfSha256,CancellationToken.None);
await using var stream=new MemoryStream(bytes,false);
var malware=await new ClamAvProofScanner(reader,TimeProvider.System).ScanAsync(new("synthetic_workspace","synthetic_case","synthetic_proof",hash,bytes.Length),stream,CancellationToken.None);
Console.WriteLine(JsonSerializer.Serialize(new{runtimeAuthority="VERIFIED_LOCAL_RUNTIME",parser,malware,commercialDecision="NONE",contentReleased=false}));
return detection ? (malware.Verdict=="UNSAFE"?0:3) : parser.Status=="PASS" && malware.Verdict=="CLEAN" ? 0 : 3;
