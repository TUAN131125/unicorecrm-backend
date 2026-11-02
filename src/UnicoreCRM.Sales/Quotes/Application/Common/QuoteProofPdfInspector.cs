using System.Text.Json;
using System.Text.RegularExpressions;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record ProofPdfInspection(string Status,string ReasonCode,int PageCount,int ObjectCount,long DecodedBytes);

/// <summary>Consumes the complete qpdf v2 graph, not the uploaded PDF syntax. Run in a bounded worker process.</summary>
internal static partial class QuoteProofPdfInspector
{
    private static readonly HashSet<string> ForbiddenNames=new(StringComparer.OrdinalIgnoreCase)
    {"/A","/AA","/OpenAction","/JS","/JavaScript","/Launch","/URI","/GoToR","/GoToE","/SubmitForm","/ImportData",
     "/Rendition","/RichMedia","/Movie","/Sound","/EmbeddedFile","/EmbeddedFiles","/EF","/XFA","/Collection",
     "/Filespec","/F","/AcroForm","/Annots","/PS","/PostScript","/Type3","/Encrypt","/Alternates","/OPI","/Ref",
     "/Metadata","/Form","/Pattern","/Image","/Shading","/Extensions","/PieceInfo","/FunctionType"};
    internal static ProofPdfInspection Inspect(ReadOnlyMemory<byte> json,int maxPages=100,int maxObjects=10000,int maxNesting=64,long maxDecodedBytes=33554432)
    {
        try
        {
            if(json.Length>67108864)return Fail("PDF_GRAPH_SIZE_LIMIT");
            var tokens=new Utf8JsonReader(json.Span,new JsonReaderOptions{MaxDepth=maxNesting+8});var tokenCount=0;
            while(tokens.Read())if(++tokenCount>1000000)return Fail("PDF_GRAPH_TOKEN_LIMIT");
            using var document=JsonDocument.Parse(json,new(){MaxDepth=maxNesting+8});var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("version",out var version) || version.GetInt32()!=2
                || !root.TryGetProperty("encrypt",out var encryption) || !encryption.TryGetProperty("encrypted",out var encrypted))return Fail("PDF_PARSER_SCHEMA_INVALID");
            if(encrypted.ValueKind!=JsonValueKind.False)return Fail("PDF_ENCRYPTED");
            if(!root.TryGetProperty("pages",out var pages) || pages.ValueKind!=JsonValueKind.Array || pages.GetArrayLength()<1 || pages.GetArrayLength()>maxPages)return Fail("PDF_PAGE_LIMIT");
            if(!root.TryGetProperty("qpdf",out var qpdf) || qpdf.ValueKind!=JsonValueKind.Array || qpdf.GetArrayLength()!=2
                || !qpdf[0].TryGetProperty("jsonversion",out var jv) || jv.GetInt32()!=2 || !qpdf[0].TryGetProperty("calledgetallpages",out var allPages) || allPages.ValueKind!=JsonValueKind.True
                || !qpdf[0].TryGetProperty("pdfversion",out var pv) || pv.GetString() is not ("1.0" or "1.1" or "1.2" or "1.3" or "1.4" or "1.5" or "1.6" or "1.7" or "2.0"))return Fail("PDF_PARSER_SCHEMA_INVALID");
            var objects=qpdf[1];if(objects.ValueKind!=JsonValueKind.Object)return Fail("PDF_PARSER_SCHEMA_INVALID");
            var objectCount=objects.EnumerateObject().Count()-1;
            if(objectCount<1 || objectCount>maxObjects || !objects.TryGetProperty("trailer",out var trailer) || !trailer.TryGetProperty("value",out var trailerValue)
                || !trailerValue.TryGetProperty("/Root",out var catalog) || catalog.ValueKind!=JsonValueKind.String)return Fail("PDF_OBJECT_LIMIT_OR_ROOT_INVALID");
            foreach(var entry in objects.EnumerateObject())
            {
                if(entry.Name!="trailer" && (!entry.Name.StartsWith("obj:",StringComparison.Ordinal) || !Reference().IsMatch(entry.Name[4..])))return Fail("PDF_OBJECT_SCHEMA_INVALID");
                if(entry.Value.ValueKind!=JsonValueKind.Object || entry.Value.EnumerateObject().Count()!=1)return Fail("PDF_OBJECT_SCHEMA_INVALID");
                if(entry.Value.TryGetProperty("stream",out var stream))
                {
                    if(!stream.TryGetProperty("dict",out var dictionary) || dictionary.ValueKind!=JsonValueKind.Object || !stream.TryGetProperty("data",out var data) || data.ValueKind!=JsonValueKind.String
                        || stream.EnumerateObject().Any(x=>x.Name is not ("dict" or "data")))return Fail("PDF_STREAM_SCHEMA_INVALID");
                }
                else if(!entry.Value.TryGetProperty("value",out _))return Fail("PDF_OBJECT_SCHEMA_INVALID");
            }
            var names=objects.EnumerateObject().Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
            var reason=Walk(objects,0,maxNesting,names,out var decoded);
            if(reason is not null)return Fail(reason);
            if(decoded>maxDecodedBytes)return Fail("PDF_DECODED_SIZE_LIMIT");
            foreach(var page in pages.EnumerateArray())
                if(!page.TryGetProperty("object",out var reference) || reference.ValueKind!=JsonValueKind.String || !names.Contains("obj:"+reference.GetString()))return Fail("PDF_PAGE_REFERENCE_INVALID");
            foreach(var page in pages.EnumerateArray())
            {
                if(!page.TryGetProperty("contents",out var contents) || contents.ValueKind!=JsonValueKind.Array)return Fail("PDF_CONTENTS_SCHEMA_INVALID");
                foreach(var reference in contents.EnumerateArray())
                {
                    if(reference.ValueKind!=JsonValueKind.String || !objects.TryGetProperty("obj:"+reference.GetString(),out var contentObject)
                        || !contentObject.TryGetProperty("stream",out var stream) || !stream.TryGetProperty("data",out var data))return Fail("PDF_CONTENTS_SCHEMA_INVALID");
                    var operators=InspectContent(Convert.FromBase64String(data.GetString()!));if(operators is not null)return Fail(operators);
                }
            }
            return new("PASS","PDF_STRUCTURAL_POLICY_PASS",pages.GetArrayLength(),objectCount,decoded);
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or FormatException or OverflowException){return Fail("PDF_PARSER_SCHEMA_INVALID");}
    }
    private static string? Walk(JsonElement value,int depth,int maxDepth,HashSet<string> objects,out long decoded)
    {
        decoded=0;if(depth>maxDepth)return "PDF_NESTING_LIMIT";
        if(value.ValueKind==JsonValueKind.Object)
        {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())
            {
                var canonical=DecodeName(property.Name);
                if(!seen.Add(canonical))return "PDF_DUPLICATE_GRAPH_KEY";
                if(ForbiddenNames.Contains(canonical))return "PDF_ACTIVE_OR_UNSUPPORTED_CONTENT";
                if(canonical=="/Filter")return "PDF_UNSUPPORTED_STREAM_FILTER"; // generalized decoding must remove every admitted filter
                if(property.Name=="data")
                {
                    if(property.Value.ValueKind!=JsonValueKind.String)return "PDF_STREAM_DATA_INVALID";
                    var data=property.Value.GetString()!;if(data.Length>44739244)return "PDF_DECODED_SIZE_LIMIT";
                    try{decoded=checked(decoded+Convert.FromBase64String(data).LongLength);}catch(FormatException){return "PDF_STREAM_DATA_INVALID";}
                }
                else
                {
                    var reason=Walk(property.Value,depth+1,maxDepth,objects,out var nested);decoded=checked(decoded+nested);if(reason is not null)return reason;
                }
                if(decoded>33554432)return "PDF_DECODED_SIZE_LIMIT";
            }
        }
        else if(value.ValueKind==JsonValueKind.Array)
            foreach(var item in value.EnumerateArray()){var reason=Walk(item,depth+1,maxDepth,objects,out var nested);decoded=checked(decoded+nested);if(reason is not null)return reason;if(decoded>33554432)return "PDF_DECODED_SIZE_LIMIT";}
        else if(value.ValueKind==JsonValueKind.String)
        {
            var text=value.GetString()!;if(ForbiddenNames.Contains(DecodeName(text)))return "PDF_ACTIVE_OR_UNSUPPORTED_CONTENT";
            if(Reference().IsMatch(text) && !objects.Contains("obj:"+text))return "PDF_DANGLING_REFERENCE";
        }
        return null;
    }
    private static readonly HashSet<string> ContentOperators=new(StringComparer.Ordinal)
    {"q","Q","cm","w","J","j","M","d","ri","i","gs","m","l","c","v","y","h","re","S","s","f","F","f*","B","B*","b","b*","n","W","W*",
     "BT","ET","Tc","Tw","Tz","TL","Tf","Tr","Ts","Td","TD","Tm","T*","Tj","TJ","'","\"","CS","cs","SC","sc","G","g","RG","rg","K","k","MP","DP","BMC","BDC","EMC"};
    internal static string? InspectContent(ReadOnlySpan<byte> content)
    {
        static bool Space(byte b)=>b is 0 or 9 or 10 or 12 or 13 or 32;
        static bool Delimiter(byte b)=>Space(b) || b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
        var p=0;var nesting=0;var tokens=0;
        while(p<content.Length)
        {
            if(++tokens>1000000)return "PDF_CONTENT_TOKEN_LIMIT";
            var c=content[p++];if(Space(c))continue;
            if(c=='%'){while(p<content.Length && content[p] is not (10 or 13))p++;continue;}
            if(c=='(')
            {
                var depth=1;
                while(p<content.Length && depth>0){var next=content[p++];if(next=='\\'){if(p<content.Length)p++;}else if(next=='('){if(++depth>64)return "PDF_CONTENT_NESTING_LIMIT";}else if(next==')')depth--;}
                if(depth!=0)return "PDF_CONTENT_STRING_INVALID";continue;
            }
            if(c=='<' && (p>=content.Length || content[p]!='<')){while(p<content.Length && content[p]!='>')p++;if(p++>=content.Length)return "PDF_CONTENT_HEX_INVALID";continue;}
            if(c=='<' && p<content.Length && content[p]=='<'){p++;if(++nesting>64)return "PDF_CONTENT_NESTING_LIMIT";continue;}
            if(c=='>' && p<content.Length && content[p]=='>'){p++;if(--nesting<0)return "PDF_CONTENT_CONTAINER_INVALID";continue;}
            if(c=='['){if(++nesting>64)return "PDF_CONTENT_NESTING_LIMIT";continue;}
            if(c==']'){if(--nesting<0)return "PDF_CONTENT_CONTAINER_INVALID";continue;}
            if(c=='/'){while(p<content.Length && !Delimiter(content[p]))p++;continue;}
            if(Delimiter(c))return "PDF_UNSUPPORTED_CONTENT_TOKEN";
            var start=p-1;while(p<content.Length && !Delimiter(content[p]))p++;
            var token=System.Text.Encoding.ASCII.GetString(content[start..p]);
            if(token is "true" or "false" or "null" || Number().IsMatch(token))continue;
            if(!ContentOperators.Contains(token))return "PDF_UNSUPPORTED_CONTENT_OPERATOR";
        }
        return nesting==0?null:"PDF_CONTENT_CONTAINER_INVALID";
    }
    [GeneratedRegex("^[+-]?([0-9]+(\\.[0-9]*)?|\\.[0-9]+)$",RegexOptions.CultureInvariant)]private static partial Regex Number();
    private static string DecodeName(string value)=>value.StartsWith('/')?NameEscape().Replace(value,m=>((char)Convert.ToInt32(m.Groups[1].Value,16)).ToString()):value;
    [GeneratedRegex("#([0-9a-fA-F]{2})",RegexOptions.CultureInvariant)]private static partial Regex NameEscape();
    [GeneratedRegex("^[0-9]+ [0-9]+ R$",RegexOptions.CultureInvariant)]private static partial Regex Reference();
    private static ProofPdfInspection Fail(string code)=>new("INVALID_PDF",code,0,0,0);
}
