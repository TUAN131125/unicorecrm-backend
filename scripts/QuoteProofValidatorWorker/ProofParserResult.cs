using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Application.Common;
internal sealed record ProofParserResult(string Status,string ReasonCode,string InputSha256,int InputLength,string ParserIdentity,string ParserVersion,string ParserSha256,long DurationMs,ProofPdfInspection? Inspection=null);
