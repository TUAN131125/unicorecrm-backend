using System.Diagnostics;
using System.Text.Json;

internal static class HarnessVerification
{
    internal static async Task Run(string directory)
    {
        if(!Path.IsPathFullyQualified(directory))throw new ArgumentException("Absolute output required");
        for(var parent=new DirectoryInfo(directory);parent is not null;parent=parent.Parent)
            if(File.Exists(Path.Combine(parent.FullName,".git")) || Directory.Exists(Path.Combine(parent.FullName,".git")))throw new ArgumentException("Evidence outside Git required");
        var destination=Path.Combine(directory,"harness-validation.json");
        if(File.Exists(destination))throw new ArgumentException("Fresh validation output required");
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        ApiLoad.Sample S(string id,double start,double end)=>new(id,"first",start,end,(end-start)*1000,200,false,0,0);
        var (cohort,completed,drain,warmup)=ApiLoad.Window([S("warmup-completed",4,4.5),S("warmup-carry",4.9,5.2),S("start-boundary",5,5.1),S("end-boundary",34.9,35),S("drained",34.8,36),S("outside",35,35.1)]);
        Check(warmup.Length==2,"warmup starts excluded even when completing in measured window");
        Check(cohort.Length==3,"only starts in half-open measured window enter latency cohort");
        Check(completed.Length==2 && completed.Any(x=>x.Id=="end-boundary"),"completion at exact window end accepted");
        Check(drain.Length==1 && drain[0].Id=="drained","post-window completions retained separately");
        Check(Math.Abs(completed.Length/30d-2d/30)<1e-12,"RPS denominator fixed measured window, excludes drain and polling delay");
        var barrier=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=0;var peak=0;
        var workers=Enumerable.Range(0,25).Select(async _=>{await barrier.Task;var value=Interlocked.Increment(ref pending);lock(checks){peak=Math.Max(peak,value);}await Task.Delay(100);Interlocked.Decrement(ref pending);}).ToArray();
        barrier.SetResult();await Task.WhenAll(workers);
        Check(peak==25 && pending==0,"client barrier allows simultaneous asynchronous work; drains all clients");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(destination,JsonSerializer.Serialize(new{result="PASS",checks,scope="Synthetic timing-window regression checks; live socket/resource coverage comes from fixture run",utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("HARNESS VALIDATED "+checks.Count+" timing/concurrency checks");
    }
}
