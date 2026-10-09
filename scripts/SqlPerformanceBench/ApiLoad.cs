using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;

internal static class ApiLoad
{
    internal static async Task Run(string directory)
    {
        if (!Path.IsPathRooted(directory)) throw new ArgumentException("Absolute output required");
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, ".git")) || Directory.Exists(Path.Combine(parent.FullName, ".git")))
                throw new ArgumentException("Output must be outside Git");
        if (File.Exists(Path.Combine(directory, "api-load.json"))) throw new ArgumentException("Use fresh HTTP evidence output");
        Directory.CreateDirectory(directory);
        string Env(string key) => Environment.GetEnvironmentVariable(key) ?? throw new ArgumentException("Missing fixture environment: " + key);
        var uri = new Uri(Env("PERFBENCH_API_URL"));
        if (!uri.IsLoopback || uri.Scheme != "http") throw new ArgumentException("Disposable loopback API only");
        var run = Env("PERFBENCH_RUN_ID");
        if (!Guid.TryParseExact(run, "N", out _)) throw new ArgumentException("GUID fixture run required");
        var builder = new SqlConnectionStringBuilder(Env("ConnectionStrings__UnicoreCRM"));
        if (!builder.IntegratedSecurity || builder.InitialCatalog != "UniCoreCRM_PerfBench_" + run)
            throw new ArgumentException("Manifest-owned integrated fixture database required");
        // A distinct diagnostic pool never borrows the API's pool or changes its limit.
        builder.ApplicationName = "UnicoreCRM.PerfBench.Diagnostics";
        await using var db = new SqlConnection(builder.ConnectionString);
        await db.OpenAsync();
        using (var marker = new SqlCommand("SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'UniCoreCRM_PerfBench_RunId'", db))
            if ((string?)await marker.ExecuteScalarAsync() != run) throw new InvalidOperationException("Database marker mismatch");
        using var api = Process.GetProcessById(int.Parse(Env("PERFBENCH_API_PID")));
        using var self = Process.GetCurrentProcess();
        using var pidCommand = new SqlCommand("SELECT CONVERT(int,SERVERPROPERTY('ProcessID'))", db);
        using var sql = Process.GetProcessById((int)(await pidCommand.ExecuteScalarAsync())!);
        var workload = Environment.GetEnvironmentVariable("PERFBENCH_WORKLOAD") ?? "mixed";
        var pass = Environment.GetEnvironmentVariable("PERFBENCH_PASS") ?? run[..8];
        if (pass.Length is < 1 or > 24 || !pass.All(x=>char.IsAsciiLetterOrDigit(x) || x=='-')) throw new ArgumentException("Invalid pass label");
        if (!new[] { "mixed", "light", "search-rare", "search-none", "summary", "kanban" }.Contains(workload)) throw new ArgumentException("Unknown workload");
        var stages = (Environment.GetEnvironmentVariable("PERFBENCH_CLIENT_STAGES") ?? "25,100,250,500,1000").Split(',').Select(int.Parse).ToArray();
        if (stages.Length == 0 || stages.Distinct().Count() != stages.Length || stages.Any(x => !new[] { 25,100,250,500,1000 }.Contains(x))) throw new ArgumentException("Invalid stages");
        var schedule = workload switch
        {
            "light" => new[] { 3 }, "search-rare" => new[] { 8 }, "search-none" => new[] { 9 },
            "summary" => new[] { 4 }, "kanban" => new[] { 6,7 },
            _ => new[] { 0,0,0,0,0,0,1,1,1,1,2,2,3,3,4,4,5,5,6,7 }
        };
        var kinds = new[] { "first", "next", "search-common", "owner", "summary", "followup", "kanban-first", "kanban-next", "search-rare", "search-none" };
        var results = new List<object>();
        foreach (var users in stages)
        {
            using var free = new SqlCommand("SELECT available_physical_memory_kb FROM sys.dm_os_sys_memory", db);
            var memory = (long)(await free.ExecuteScalarAsync())!;
            var reserveMiB = users switch { 25=>2048,100=>3072,250=>4096,500=>6144,_=>8192 };
            if (memory < reserveMiB*1024L) { results.Add(new { users, workload, result="NOT_RUN_RESOURCE_LIMIT", availableMemoryMiB=memory/1024, requiredMiB=reserveMiB }); break; }
            if (Environment.GetEnvironmentVariable("PERFBENCH_DIAGNOSTICS_OUTPUT") is { } observerOutput)
            {
                if (!Path.IsPathFullyQualified(observerOutput)) throw new ArgumentException("Absolute diagnostics output required");
                for(var parent=new DirectoryInfo(observerOutput);parent is not null;parent=parent.Parent)
                    if(File.Exists(Path.Combine(parent.FullName,".git")) || Directory.Exists(Path.Combine(parent.FullName,".git"))) throw new ArgumentException("Diagnostics outside Git required");
                Directory.CreateDirectory(observerOutput);
                var control=Path.Combine(observerOutput,"window.json");
                if(File.Exists(control))
                {
                    using var prior=JsonDocument.Parse(await File.ReadAllTextAsync(control));
                    if(prior.RootElement.GetProperty("runId").GetString()!=run) throw new InvalidOperationException("Diagnostics ownership mismatch");
                }
                var temporary=control+".tmp";
                await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new {runId=run,window=$"{pass}-{workload}-{users}"}));
                File.Move(temporary,control,true);
            }
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy=false, MaxConnectionsPerServer=users }) { BaseAddress=uri, Timeout=TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Env("PERFBENCH_TOKEN"));
            http.DefaultRequestHeaders.Add("X-Workspace-Id", Env("PERFBENCH_WORKSPACE"));
            var samples = new ConcurrentBag<Sample>();
            var diagnostics = new List<object>();
            var clock = new Stopwatch();
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var apiCpu = Cpu(api); var sqlCpu = Cpu(sql); var generatorCpu = Cpu(self);
            var allocated = GC.GetTotalAllocatedBytes();
            var pending = 0; var peakPending = 0; var peakGeneratorQueue = 0L;
            var resourceStop = 0; var startUtc = DateTimeOffset.UtcNow;
            var clients = Enumerable.Range(0,users).Select(async client =>
            {
                await begin.Task;
                string? cursor=null, leadCursor=null;
                var step=client;
                while (clock.Elapsed.TotalSeconds < 35 && Volatile.Read(ref resourceStop)==0)
                {
                    var requestedKind=schedule[step++ % schedule.Length];
                    // Attribute requests by the operation actually sent, not scheduled continuation.
                    var kind=requestedKind==1 && cursor is null ? 0 : requestedKind==7 && leadCursor is null ? 6 : requestedKind;
                    var path=kind switch
                    {
                        0=>"/contacts?limit=25",1=>"/contacts?limit=25&cursor="+Uri.EscapeDataString(cursor!),
                        2=>"/contacts?limit=25&search=Synthetic",3=>"/contacts?limit=25&ownerId=member_1",4=>"/contacts/summary",
                        5=>"/contacts?limit=25&followUp=overdue",6=>"/leads/kanban/NEW?limit=25",7=>"/leads/kanban/NEW?limit=25&cursor="+Uri.EscapeDataString(leadCursor!),
                        8=>"/contacts?limit=25&search=RareZebra",_=>"/contacts?limit=25&search=NoSuchSyntheticMatch"
                    };
                    using var request=new HttpRequestMessage(HttpMethod.Get,path);
                    var id=$"perf-load-{pass}-{workload}-{users}-{client}-{step}";
                    request.Headers.Add("X-Request-Id",id);request.Headers.Add("X-PerfBench-Request-Id",id);
                    request.Headers.Add("X-Correlation-Id",$"perf-load-{workload}-{client}");
                    var start=clock.Elapsed.TotalSeconds;
                    if(start>=35)break;
                    var timer=Stopwatch.StartNew(); var status=0;var timeout=false;var parseMs=0d;var bytes=0;
                    var outstanding=Interlocked.Increment(ref pending);
                    InterlockedExtensions.Max(ref peakPending,outstanding);
                    try
                    {
                        using var response=await http.SendAsync(request);
                        status=(int)response.StatusCode;
                        var body=await response.Content.ReadAsByteArrayAsync();bytes=body.Length;
                        timer.Stop(); // Full HTTP body latency excludes cursor JSON processing/think time.
                        if(status==200 && kind is 0 or 1 or 6 or 7)
                        {
                            var parse=Stopwatch.StartNew();using var json=JsonDocument.Parse(body);var root=json.RootElement;
                            var info=kind<2 ? root.GetProperty("pageInfo") : root;
                            var next=info.TryGetProperty("nextCursor",out var value) && value.ValueKind==JsonValueKind.String ? value.GetString() : null;
                            if(kind<2)cursor=next;else leadCursor=next;
                            parseMs=parse.Elapsed.TotalMilliseconds;
                        }
                    }
                    catch(TaskCanceledException){timeout=true;}
                    catch(HttpRequestException){status=0;}
                    catch(JsonException){status=0;}
                    catch(KeyNotFoundException){status=0;}
                    finally { timer.Stop();Interlocked.Decrement(ref pending); }
                    var finished=start+timer.Elapsed.TotalSeconds;
                    samples.Add(new(id,kinds[kind],start,finished,timer.Elapsed.TotalMilliseconds,status,timeout,parseMs,bytes));
                    await Task.Delay(500);
                }
            }).ToArray();
            var allClients=Task.WhenAll(clients);
            clock.Start();startUtc=DateTimeOffset.UtcNow;begin.SetResult();
            while (!allClients.IsCompleted)
            {
                api.Refresh();self.Refresh();sql.Refresh();
                var data=await SqlSnapshot(db);
                var available=data.FirstOrDefault(x=>x.ContainsKey("MemoryKB"))?.GetValueOrDefault("MemoryKB");
                if(available is null)Interlocked.Exchange(ref resourceStop,2);
                else if(Convert.ToInt64(available)<2048*1024L)Interlocked.Exchange(ref resourceStop,1);
                peakGeneratorQueue=Math.Max(peakGeneratorQueue,ThreadPool.PendingWorkItemCount);
                diagnostics.Add(new { seconds=clock.Elapsed.TotalSeconds, pending=Volatile.Read(ref pending),apiMemoryBytes=api.WorkingSet64,sqlMemoryBytes=sql.WorkingSet64,generatorMemoryBytes=self.WorkingSet64,generatorThreadPoolQueue=ThreadPool.PendingWorkItemCount,generatorThreads=ThreadPool.ThreadCount,sql=data });
                await Task.WhenAny(allClients,Task.Delay(1000));
            }
            await allClients;
            var elapsed=clock.Elapsed.TotalSeconds;
            var all=samples.ToArray();
            var (cohort,completed,drain,warmup)=Window(all);
            results.Add(new { users,workload,pass,apiReplicas=1,startUtc,endUtc=DateTimeOffset.UtcNow,warmupSeconds=5,measurementSeconds=30,actualElapsedIncludingDrainSeconds=elapsed,
                thinkTimeMs=500,requests=completed.Length,startedInMeasuredWindow=cohort.Length,drainedRequests=drain.Length,warmupRequestsExcluded=warmup.Length,
                rps=completed.Length/30d,latency=Stats(completed),startedCohortLatency=Stats(cohort),drainLatency=Stats(drain),
                errors=cohort.Count(x=>x.Status!=200),timeouts=cohort.Count(x=>x.Timeout),http5xx=cohort.Count(x=>x.Status>=500),http4xx=cohort.Count(x=>x.Status is >=400 and <500),
                apiCpuPercent=(Cpu(api)-apiCpu)/elapsed/Environment.ProcessorCount*100,sqlInstanceCpuPercent=(Cpu(sql)-sqlCpu)/elapsed/Environment.ProcessorCount*100,
                generatorCpuPercent=(Cpu(self)-generatorCpu)/elapsed/Environment.ProcessorCount*100,generatorAllocatedBytes=GC.GetTotalAllocatedBytes()-allocated,peakGeneratorThreadPoolQueue=peakGeneratorQueue,peakPendingHttpRequests=peakPending,
                cpuMeasurementScope="stage-including-warmup-and-drain; allocations use same scope",
                result=Volatile.Read(ref resourceStop) switch {1=>"PARTIAL_NOT_RUN_RESOURCE_LIMIT",2=>"PARTIAL_MEMORY_MONITOR_UNAVAILABLE",_=>"MEASURED"},
                endpoints=cohort.GroupBy(x=>x.Kind).Select(g=>new { kind=g.Key,requestsCompletedInWindow=g.Count(x=>x.Finished<=35),requestsStarted=g.Count(),rps=g.Count(x=>x.Finished<=35)/30d,latency=Stats(g.Where(x=>x.Finished<=35).ToArray()),startedCohortLatency=Stats(g.ToArray()),errors=g.Count(x=>x.Status!=200),jsonParseMs=g.Sum(x=>x.ParseMs),responseBytes=g.Sum(x=>x.Bytes) }).ToArray(),
                diagnostics,acceptance="NO_PRODUCTION_SLO_OR_CAPACITY_CLAIM",windowDefinition="RPS and latency include starts>=5s AND completions<=35s; late cohort tracked separately; no warmup counted" });
            await Save();
            // Private request ids, timing and byte counts only; no auth/header/cursor/body values.
            await File.WriteAllTextAsync(Path.Combine(directory,$"{users}-requests.json"),JsonSerializer.Serialize(all,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"API LOAD {workload} {users}: {completed.Length} window completions, {drain.Length} drained, {cohort.Count(x=>x.Status!=200)} errors");
            if(cohort.Any(x=>x.Status!=200) || Volatile.Read(ref resourceStop)!=0)break;
        }
        await Save();
        async Task Save()=>await File.WriteAllTextAsync(Path.Combine(directory,"api-load.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static async Task<List<Dictionary<string,object?>>> SqlSnapshot(SqlConnection connection)
    {
        const string sql="SELECT available_physical_memory_kb MemoryKB FROM sys.dm_os_sys_memory; SELECT r.session_id,r.status,r.cpu_time,r.total_elapsed_time,r.logical_reads,r.wait_type,r.wait_time,r.blocking_session_id,r.granted_query_memory FROM sys.dm_exec_requests r WHERE r.database_id=DB_ID() AND r.session_id<>@@SPID; SELECT COUNT(*) FixtureSessions,SUM(CASE WHEN status='running' THEN 1 ELSE 0 END) RunningSessions FROM sys.dm_exec_sessions WHERE database_id=DB_ID(); SELECT w.session_id,w.wait_type,w.waiting_tasks_count,w.wait_time_ms,w.signal_wait_time_ms FROM sys.dm_exec_session_wait_stats w JOIN sys.dm_exec_sessions s ON s.session_id=w.session_id WHERE s.database_id=DB_ID() AND s.session_id<>@@SPID AND w.wait_time_ms>0; SELECT session_id,requested_memory_kb,granted_memory_kb,used_memory_kb,wait_time_ms FROM sys.dm_exec_query_memory_grants WHERE session_id IN(SELECT session_id FROM sys.dm_exec_sessions WHERE database_id=DB_ID())";
        var rows=new List<Dictionary<string,object?>>();
        try
        {
            using var command=new SqlCommand(sql,connection){CommandTimeout=5};await using var reader=await command.ExecuteReaderAsync();
            do { while(await reader.ReadAsync()){var row=new Dictionary<string,object?>();for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=await reader.IsDBNullAsync(i)?null:reader.GetValue(i);rows.Add(row);} }while(await reader.NextResultAsync());
        }
        catch(SqlException e){rows.Add(new(){["diagnosticErrorNumber"]=e.Number});}
        return rows;
    }
    private static object Stats(Sample[] samples)
    {
        var a=samples.Select(x=>x.Ms).Order().ToArray();double? P(double q)=>a.Length==0?null:a[Math.Clamp((int)Math.Ceiling(q*a.Length)-1,0,a.Length-1)];
        return new {p50=P(.5),p95=P(.95),p99=P(.99)};
    }
    private static double? Cpu(Process process){try{return process.TotalProcessorTime.TotalSeconds;}catch(System.ComponentModel.Win32Exception){return null;}}
    internal sealed record Sample(string Id,string Kind,double Start,double Finished,double Ms,int Status,bool Timeout,double ParseMs,int Bytes);
    internal static (Sample[] Cohort,Sample[] Completed,Sample[] Drain,Sample[] Warmup) Window(Sample[] all)
    {
        var cohort=all.Where(x=>x.Start>=5 && x.Start<35).ToArray();
        return (cohort,cohort.Where(x=>x.Finished<=35).ToArray(),cohort.Where(x=>x.Finished>35).ToArray(),all.Where(x=>x.Start<5).ToArray());
    }
    private static class InterlockedExtensions
    {
        internal static void Max(ref int location,int value){int old;do{old=Volatile.Read(ref location);if(old>=value)return;}while(Interlocked.CompareExchange(ref location,value,old)!=old);}
    }
}
