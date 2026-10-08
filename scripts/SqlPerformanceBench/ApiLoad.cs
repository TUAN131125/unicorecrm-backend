using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;

internal static class ApiLoad
{
    // Fixture-only entry point. Secrets remain in process environment and never in output.
    internal static async Task Run(string directory)
    {
        if(!Path.IsPathRooted(directory))throw new ArgumentException("Absolute output required");
        for(var parent=new DirectoryInfo(directory);parent is not null;parent=parent.Parent)
            if(File.Exists(Path.Combine(parent.FullName,".git")) || Directory.Exists(Path.Combine(parent.FullName,".git")))throw new ArgumentException("Output must be outside Git");
        if(File.Exists(Path.Combine(directory,"api-load.json")))throw new ArgumentException("Use fresh HTTP evidence output");
        Directory.CreateDirectory(directory);
        string Env(string key)=>Environment.GetEnvironmentVariable(key)??throw new ArgumentException("Missing fixture environment: "+key);
        var baseUrl=Env("PERFBENCH_API_URL");var uri=new Uri(baseUrl);
        if(!uri.IsLoopback || uri.Scheme!="http")throw new ArgumentException("Disposable loopback API only");
        var run=Env("PERFBENCH_RUN_ID");var workspace=Env("PERFBENCH_WORKSPACE");
        if(!Guid.TryParseExact(run,"N",out _))throw new ArgumentException("GUID fixture run required");
        var cs=Env("ConnectionStrings__UnicoreCRM");var builder=new SqlConnectionStringBuilder(cs);
        if(!builder.IntegratedSecurity || builder.InitialCatalog!="UniCoreCRM_PerfBench_"+run)throw new ArgumentException("Manifest-owned integrated fixture database required");
        await using var db=new SqlConnection(cs);await db.OpenAsync();
        using(var marker=new SqlCommand("SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'UniCoreCRM_PerfBench_RunId'",db))
            if((string?)await marker.ExecuteScalarAsync()!=run)throw new InvalidOperationException("Database ownership marker mismatch");
        using var api=Process.GetProcessById(int.Parse(Env("PERFBENCH_API_PID")));
        using var pidCommand=new SqlCommand("SELECT CONVERT(int,SERVERPROPERTY('ProcessID'))",db);
        using var sql=Process.GetProcessById((int)(await pidCommand.ExecuteScalarAsync())!);
        var result=new List<object>();
        var kinds=new[]{"first","next","search","owner","summary","followup","kanban-first","kanban-next"};
        // 30/20/10/10/10/10/5/5 percent; fixed client/step schedule, 500ms think time.
        var schedule=new[]{0,0,0,0,0,0,1,1,1,1,2,2,3,3,4,4,5,5,6,7};
        foreach(var users in new[]{25,100,250,500,1000})
        {
            using var free=new SqlCommand("SELECT available_physical_memory_kb FROM sys.dm_os_sys_memory",db);
            var memory=(long)(await free.ExecuteScalarAsync())!;
            // Reserve progressively more headroom for API/SQL grants and client connections.
            var requiredMiB=users==25 ? 2048 : users==100 ? 3072 : users==250 ? 4096 : users==500 ? 6144 : 8192;
            if(memory<requiredMiB*1024L){result.Add(new{users,result="NOT_RUN_RESOURCE_LIMIT",availableMemoryMiB=memory/1024,requiredMiB});break;}
            using var http=new HttpClient(new SocketsHttpHandler{UseProxy=false,MaxConnectionsPerServer=users}){BaseAddress=uri,Timeout=TimeSpan.FromSeconds(30)};
            http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",Env("PERFBENCH_TOKEN"));
            http.DefaultRequestHeaders.Add("X-Workspace-Id",workspace);
            var samples=new ConcurrentBag<Sample>();var connections=0;var peakApi=api.WorkingSet64;var peakSql=sql.WorkingSet64;
            var apiStart=Cpu(api);var sqlStart=Cpu(sql);var stage=Stopwatch.StartNew();
            var clients=Enumerable.Range(0,users).Select(async client=>
            {
                string? cursor=null,leadCursor=null;
                var step=client;
                while(stage.Elapsed.TotalSeconds<35)
                {
                    var kind=schedule[step++%schedule.Length];
                    var path=kind switch {0=>"/contacts?limit=25",1=>"/contacts?limit=25"+(cursor is null?"":"&cursor="+Uri.EscapeDataString(cursor)),
                        2=>"/contacts?limit=25&search=Synthetic",3=>"/contacts?limit=25&ownerId=member_1",4=>"/contacts/summary",
                        5=>"/contacts?limit=25&followUp=overdue",6=>"/leads/kanban/NEW?limit=25",
                        _=>"/leads/kanban/NEW?limit=25"+(leadCursor is null?"":"&cursor="+Uri.EscapeDataString(leadCursor))};
                    using var request=new HttpRequestMessage(HttpMethod.Get,path);
                    request.Headers.Add("X-Request-Id",$"perf-load-{client}-{step}");request.Headers.Add("X-Correlation-Id",$"perf-load-{client}");
                    var measured=stage.Elapsed.TotalSeconds>=5;var timer=Stopwatch.StartNew();var status=0;var timeout=false;
                    try
                    {
                        using var response=await http.SendAsync(request);status=(int)response.StatusCode;
                        var body=await response.Content.ReadAsByteArrayAsync();
                        if(status==200 && kind is 0 or 1 or 6 or 7)
                        {
                            using var json=JsonDocument.Parse(body);var root=json.RootElement;
                            if(kind<2){var info=root.GetProperty("pageInfo");cursor=info.TryGetProperty("nextCursor",out var next)&&next.ValueKind==JsonValueKind.String?next.GetString():null;}
                            else leadCursor=root.TryGetProperty("nextCursor",out var next)&&next.ValueKind==JsonValueKind.String?next.GetString():null;
                        }
                    }
                    catch(TaskCanceledException){timeout=true;}
                    catch(HttpRequestException){status=0;}
                    if(measured)samples.Add(new(kinds[kind],timer.Elapsed.TotalMilliseconds,status,timeout));
                    await Task.Delay(500);
                }
            }).ToArray();
            while(!Task.WhenAll(clients).IsCompleted)
            {
                using var active=new SqlCommand("SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID()",db);
                connections=Math.Max(connections,(int)(await active.ExecuteScalarAsync())!);
                api.Refresh();sql.Refresh();peakApi=Math.Max(peakApi,api.WorkingSet64);peakSql=Math.Max(peakSql,sql.WorkingSet64);
                await Task.Delay(1000);
            }
            await Task.WhenAll(clients);
            var elapsed=stage.Elapsed.TotalSeconds;var all=samples.ToArray();
            var cpuApi=(Cpu(api)-apiStart)/elapsed/Environment.ProcessorCount*100;
            var cpuSql=(Cpu(sql)-sqlStart)/elapsed/Environment.ProcessorCount*100;
            result.Add(new{users,apiReplicas=1,warmupSeconds=5,targetMeasuredSeconds=30,actualElapsedSeconds=elapsed,thinkTimeMs=500,requests=all.Length,
                rps=all.Length/(elapsed-5),latency=Stats(all),errorRate=all.Count(x=>x.Status!=200)/(double)Math.Max(1,all.Length),
                timeouts=all.Count(x=>x.Timeout),http5xx=all.Count(x=>x.Status>=500),http4xx=all.Count(x=>x.Status is >=400 and <500),
                apiCpuPercent=cpuApi,sqlInstanceCpuPercent=cpuSql,peakApiMemoryBytes=peakApi,peakSqlMemoryBytes=peakSql,peakFixtureSqlSessions=connections,
                endpoints=all.GroupBy(x=>x.Kind).Select(g=>new{kind=g.Key,requests=g.Count(),latency=Stats(g.ToArray()),errors=g.Count(x=>x.Status!=200)}).ToArray(),
                sqlWaits="NOT_SAMPLED",threadPoolStarvation="NOT_ESTABLISHED",continuationNote="Clients request first page when no continuation exists; classifications describe scheduled mix",acceptance="NO_PRODUCTION_SLO_OR_CAPACITY_CLAIM"});
            await Save();Console.WriteLine($"API LOAD measured {users} clients: {all.Length} requests, {all.Count(x=>x.Status!=200)} errors");
            if(all.Any(x=>x.Status!=200))break;
        }
        await Save();
        async Task Save()=>await File.WriteAllTextAsync(Path.Combine(directory,"api-load.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static object Stats(Sample[] samples)
    {
        var a=samples.Select(x=>x.Ms).Order().ToArray();double? P(double q)=>a.Length==0?null:a[Math.Clamp((int)Math.Ceiling(q*a.Length)-1,0,a.Length-1)];
        return new{p50=P(.5),p95=P(.95),p99=P(.99)};
    }
    private static double? Cpu(Process process){try{return process.TotalProcessorTime.TotalSeconds;}catch(System.ComponentModel.Win32Exception){return null;}}
    private sealed record Sample(string Kind,double Ms,int Status,bool Timeout);
}
