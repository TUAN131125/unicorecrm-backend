using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Infrastructure.Documents;
if(args.Length!=3)return 2;
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(18));using var data=new MemoryStream();var buffer=new byte[65536];int count;
while((count=await Console.OpenStandardInput().ReadAsync(buffer,timeout.Token))>0){if(data.Length+count>10485760)return 3;await data.WriteAsync(buffer.AsMemory(0,count),timeout.Token);}
var result=await new QpdfProofValidatorCore().ValidateAsync(data.ToArray(),args[0],args[1],timeout.Token,args[2]);
Console.WriteLine(JsonSerializer.Serialize(result));return 0;
