using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Infrastructure.Documents;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteProofScannerTests
{
    private static JsonObject Graph()=>JsonNode.Parse("""
    {"version":2,"encrypt":{"encrypted":false},"pages":[{"object":"3 0 R","contents":["4 0 R"]}],"qpdf":[{"jsonversion":2,"pdfversion":"1.7","calledgetallpages":true},{
      "obj:1 0 R":{"value":{"/Type":"/Catalog","/Pages":"2 0 R"}},
      "obj:2 0 R":{"value":{"/Type":"/Pages","/Kids":["3 0 R"],"/Count":1}},
      "obj:3 0 R":{"value":{"/Type":"/Page","/Parent":"2 0 R","/Contents":"4 0 R"}},
      "obj:4 0 R":{"stream":{"dict":{},"data":"QlQgL0YxIDEyIFRmIChTQUZFKSBUaiBFVA=="}},
      "trailer":{"value":{"/Root":"1 0 R","/Size":5}}
    }]}
    """)!.AsObject();
    private static ProofPdfInspection Inspect(JsonObject node)=>QuoteProofPdfInspector.Inspect(Encoding.UTF8.GetBytes(node.ToJsonString()));
    [Fact]public void Complete_bounded_graph_passes_without_implying_malware_or_consent()=>Assert.Equal("PASS",Inspect(Graph()).Status);
    [Theory]
    [InlineData("/OpenAction")][InlineData("/AA")][InlineData("/Java#53cript")][InlineData("/EmbeddedFiles")][InlineData("/XFA")][InlineData("/URI")][InlineData("/AcroForm")]
    public void Forbidden_or_encoded_features_are_rejected(string key)
    {var g=Graph();g["qpdf"]![1]!["obj:1 0 R"]!["value"]![key]="4 0 R";Assert.Equal("PDF_ACTIVE_OR_UNSUPPORTED_CONTENT",Inspect(g).ReasonCode);}
    [Fact]public void Encrypted_graph_missing_stream_unresolved_references_and_unknown_schema_fail_closed()
    {
        var g=Graph();g["encrypt"]!["encrypted"]=true;Assert.Equal("PDF_ENCRYPTED",Inspect(g).ReasonCode);
        g=Graph();g["qpdf"]![1]!["obj:4 0 R"]!["stream"]!.AsObject().Remove("data");Assert.Equal("PDF_STREAM_SCHEMA_INVALID",Inspect(g).ReasonCode);
        g=Graph();g["qpdf"]![1]!["obj:3 0 R"]!["value"]!["/Contents"]="999 0 R";Assert.Equal("PDF_DANGLING_REFERENCE",Inspect(g).ReasonCode);
        g=Graph();g["version"]=1;Assert.Equal("INVALID_PDF",Inspect(g).Status);
    }
    [Fact]public void Unsupported_filters_page_count_objects_and_decoded_size_are_bounded()
    {
        var g=Graph();g["qpdf"]![1]!["obj:4 0 R"]!["stream"]!["dict"]!["/Filter"]="/DCTDecode";Assert.Equal("PDF_UNSUPPORTED_STREAM_FILTER",Inspect(g).ReasonCode);
        var bytes=Encoding.UTF8.GetBytes(Graph().ToJsonString());Assert.Equal("INVALID_PDF",QuoteProofPdfInspector.Inspect(bytes,maxPages:0).Status);
        Assert.Equal("INVALID_PDF",QuoteProofPdfInspector.Inspect(bytes,maxObjects:1).Status);
        Assert.Equal("PDF_DECODED_SIZE_LIMIT",QuoteProofPdfInspector.Inspect(bytes,maxDecodedBytes:1).ReasonCode);
    }
    [Theory]
    [InlineData("BI /W 1 /H 1 /F /DCT ID x EI")][InlineData("/Fm1 Do")][InlineData("/Sh1 sh")][InlineData("BX FutureOperator EX")]
    public void Unsupported_rendering_operators_are_not_hidden_inside_streams(string content)=>Assert.Equal("PDF_UNSUPPORTED_CONTENT_OPERATOR",QuoteProofPdfInspector.InspectContent(Encoding.ASCII.GetBytes(content)));
    [Fact]public void Text_comments_names_and_hex_strings_do_not_become_executable_operators()=>Assert.Null(QuoteProofPdfInspector.InspectContent("% BI\nBT /F1 12 Tf (BI ID EI Do) Tj <4249> Tj ET"u8));
    [Fact]public void Lease_identity_expiration_and_epoch_protect_against_stale_completion()
    {
        var now=DateTimeOffset.Parse("2026-10-26T08:00:00Z");var job=new UnicoreCRM.Sales.Quotes.Domain.QuoteProofScanJob("w","c","p",new string('A',64),610,now);
        Assert.True(job.TryClaim("policy+signatures","attempt1","lease1",now,TimeSpan.FromSeconds(90),3,12));
        Assert.False(job.TryClaim("policy+signatures","attempt2","lease2",now,TimeSpan.FromSeconds(90),3,12));
        Assert.False(job.TryComplete("attempt1","wrong","policy+signatures","CLEAN",now,TimeSpan.Zero));
        Assert.False(job.TryComplete("attempt1","lease1","wrong-policy","CLEAN",now,TimeSpan.Zero));
        Assert.False(job.TryComplete("attempt1","lease1","policy+signatures","CLEAN",now.AddSeconds(90),TimeSpan.Zero));
        Assert.True(job.TryExpireLease("attempt1","lease1",now.AddSeconds(90),TimeSpan.FromSeconds(30)));
        Assert.False(job.TryClaim("policy+signatures","attempt2","lease2",now.AddSeconds(100),TimeSpan.FromSeconds(90),3,12));
        Assert.True(job.TryClaim("policy+signatures","attempt2","lease2",now.AddSeconds(120),TimeSpan.FromSeconds(90),3,12));
        Assert.False(job.TryComplete("attempt1","lease1","policy+signatures","CLEAN",now.AddSeconds(121),TimeSpan.Zero));
        Assert.True(job.TryComplete("attempt2","lease2","policy+signatures","CLEAN",now.AddSeconds(121),TimeSpan.Zero));
        Assert.Equal(2,job.LifetimeAttempts);
    }
    [Fact]public void Automatic_retry_budget_is_finite_even_across_epoch_changes()
    {
        var now=DateTimeOffset.Parse("2026-10-26T08:00:00Z");var job=new UnicoreCRM.Sales.Quotes.Domain.QuoteProofScanJob("w","c","p",new string('A',64),610,now);
        for(var n=0;n<3;n++)
        {Assert.True(job.TryClaim("epoch1","a"+n,"l"+n,now,TimeSpan.FromSeconds(90),3,4));Assert.True(job.TryComplete("a"+n,"l"+n,"epoch1","SCAN_FAILED",now,TimeSpan.Zero));}
        Assert.False(job.TryClaim("epoch1","a3","l3",now,TimeSpan.FromSeconds(90),3,4));
        Assert.True(job.TryClaim("epoch2","a4","l4",now,TimeSpan.FromSeconds(90),3,4));Assert.True(job.TryComplete("a4","l4","epoch2","SCAN_FAILED",now,TimeSpan.Zero));
        Assert.False(job.TryClaim("epoch3","a5","l5",now,TimeSpan.FromSeconds(90),3,4));
    }
    [Fact]public void Unapproved_terminal_rescan_cannot_turn_unsafe_into_clean_by_metadata()
    {
        var now=DateTimeOffset.Parse("2026-10-26T08:00:00Z");var job=new UnicoreCRM.Sales.Quotes.Domain.QuoteProofScanJob("w","c","p",new string('A',64),610,now);
        Assert.True(job.TryClaim("epoch1","a","l",now,TimeSpan.FromSeconds(90),3,12));Assert.True(job.TryComplete("a","l","epoch1","UNSAFE",now,TimeSpan.Zero));
        Assert.False(job.TryClaim("epoch2","b","m",now,TimeSpan.FromSeconds(90),3,12));Assert.Equal("UNSAFE",job.Status);
    }
    [Fact]public void Pinned_runtime_configuration_still_requires_enabled_scanning_and_rejects_overrides()
    {
        var config="""
        DatabaseDirectory "C:/scanner/database"
        CVDCertsDirectory "C:/scanner/certs"
        TCPSocket 3317
        TCPAddr 127.0.0.1
        Foreground yes
        LogFile "C:/scanner/clamd.log"
        LogFileMaxSize 2M
        LogTime yes
        LogClean no
        LogVerbose no
        LogRotate yes
        StreamMaxLength 10M
        MaxThreads 2
        MaxQueue 4
        ReadTimeout 30
        CommandReadTimeout 5
        SendBufTimeout 1000
        SelfCheck 0
        ScanPDF yes
        ScanArchive yes
        MaxScanTime 30000
        MaxScanSize 64M
        MaxFileSize 10M
        MaxRecursion 8
        MaxFiles 1000
        AlertExceedsMax yes
        Bytecode yes
        BytecodeSecurity TrustSigned
        BytecodeUnsigned no
        DisableCache yes
        EnableReloadCommand no
        EnableShutdownCommand no
        EnableVersionCommand yes
        EnableStatsCommand yes
        """.Split('\n').Select(x=>x.Trim()).ToArray();
        bool Check(string[] lines)=>TrustedClamAvRuntimeReader.ConfigurationValid(lines,3317,"C:/scanner/database","C:/scanner/certs","C:/scanner/clamd.log");
        Assert.True(Check(config));
        Assert.False(Check(config.Select(x=>x=="ScanPDF yes"?"ScanPDF no":x).ToArray()));
        Assert.False(Check([..config,"ScanPDF no"]));
        Assert.False(Check([..config,"Include C:/arbitrary.conf"]));
        Assert.False(Check(config.Select(x=>x.StartsWith("DatabaseDirectory")?"DatabaseDirectory C:/different":x).ToArray()));
    }
    [Theory]
    [InlineData("stream: OK","CLEAN")][InlineData("stream: Eicar-Test-Signature FOUND","UNSAFE")]
    [InlineData("stream: Heuristics.Limits.Exceeded FOUND","RESULT_INDETERMINATE")]
    [InlineData("stream: ERROR","SCAN_FAILED")][InlineData("OK","SCAN_FAILED")][InlineData("stream: fake FOUND trailing","SCAN_FAILED")]
    public void Only_exact_complete_scanner_success_is_clean(string response,string verdict)=>Assert.Equal(verdict,ClamAvProofScanner.ParseTerminal(response).Verdict);
    [Fact]public async Task Truncated_or_ambiguous_scanner_terminal_is_never_clean()
    {
        await using var truncated=new MemoryStream("stream: OK"u8.ToArray());await Assert.ThrowsAsync<IOException>(()=>ClamAvProofScanner.ReadTerminalAsync(truncated,CancellationToken.None));
        await using var ambiguous=new MemoryStream("stream: OK\0extra"u8.ToArray());await Assert.ThrowsAsync<IOException>(()=>ClamAvProofScanner.ReadTerminalAsync(ambiguous,CancellationToken.None));
    }
}
