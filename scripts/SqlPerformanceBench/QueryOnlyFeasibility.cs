using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using UnicoreCRM.Crm.Contacts.Application.ListContacts;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Operations.Tasks.Contracts;

// Two explicit experimental SQL shapes; never selected by application runtime or needle length.
internal static class QueryOnlyFeasibility
{
    internal static string Late(string sql)=>Rewrite(sql,false);
    internal static string ClusteredLate(string sql)=>Rewrite(sql,true);
    static string Rewrite(string sql,bool clustered)
    {
        if(!sql.StartsWith("SELECT TOP(",StringComparison.Ordinal)||!sql.Contains("CHARINDEX(",StringComparison.Ordinal))return sql;
        var match=Regex.Match(sql,@"\ASELECT TOP\((?<limit>[^)]+)\) (?<columns>.*?)\r?\nFROM (?<from>.*)\r?\nORDER BY (?<order>.*)\z",RegexOptions.Singleline);
        if(!match.Success || !sql.Contains("FROM [contacts].[Contacts] c",StringComparison.Ordinal) || !sql.Contains(") AS [u]",StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported canonical page shape; refuse experimental rewrite");
        var fields=new List<string>{"[u].[WorkspaceId]","[u].[ContactId]"};
        var order=match.Groups["order"].Value;
        foreach(var field in new[]{"FullName","UpdatedAt"})if(order.Contains($"[u].[{field}]",StringComparison.Ordinal))fields.Add($"[u].[{field}]");
        var follow=Regex.Match(sql,@"(?<alias>\[\w+\])\.\[NextFollowUpAt\]");
        if(follow.Success)fields.Add(follow.Value+" AS [NextFollowUpAt]");
        var from=match.Groups["from"].Value;
        if(clustered)from=from.Replace("FROM [contacts].[Contacts] c","FROM [contacts].[Contacts] c WITH (INDEX([PK_Contacts]))",StringComparison.Ordinal);
        var candidate="SELECT TOP("+match.Groups["limit"].Value+") "+string.Join(",",fields)+"\nFROM "+from+"\nORDER BY "+order;
        var columns=match.Groups["columns"].Value.Replace("[u].","[materialized].",StringComparison.Ordinal);
        var finalOrder=order.Replace("[u].","[candidate].",StringComparison.Ordinal);
        if(follow.Success)
        {
            columns=columns.Replace(follow.Groups["alias"].Value+".[NextFollowUpAt]","[candidate].[NextFollowUpAt]",StringComparison.Ordinal);
            finalOrder=finalOrder.Replace(follow.Groups["alias"].Value+".[NextFollowUpAt]","[candidate].[NextFollowUpAt]",StringComparison.Ordinal);
        }
        return "SELECT "+columns+"\nFROM (\n"+candidate+"\n) AS [candidate]\nINNER JOIN [contacts].[Contacts] AS [materialized] ON [materialized].[WorkspaceId]=[candidate].[WorkspaceId] AND [materialized].[ContactId]=[candidate].[ContactId]\nORDER BY "+finalOrder;
    }

    internal static async Task Run(int tier,ContactsDbContext db,EfContactsPersistence persistence,SqlConnection connection,Capture capture,
        Func<string,Func<Task>,Capture,SqlConnection,Task> measure,Func<string,object,Task> save,Func<Task> guard)
    {
        await guard();
        using(var isolation=new SqlCommand("SELECT is_read_committed_snapshot_on,snapshot_isolation_state_desc FROM sys.databases WHERE database_id=DB_ID()",connection))
        await using(var r=await isolation.ExecuteReaderAsync()){await r.ReadAsync();await save($"{tier}-query-only-isolation.json",new{readCommittedSnapshot=r.GetBoolean(0),snapshotIsolation=r.GetString(1)});}
        await save($"{tier}-query-only-scope.json",new{productionChanged=false,commands="Exact count unchanged; page candidate qualification and full-row join in one SQL command; no extra client materialization/round trip",summary="Unchanged",querySelection="Explicit benchmark modes, not application heuristics",materialization="Derived TOP preserves order/cursor on controlled state; actual plans required, no assumption CTE materializes",consistency="Same command is not a snapshot under locking READ COMMITTED. A second Contact read may observe changed owner/profile/order keys; qualification-to-materialization interleaving parity NOT_ESTABLISHED. Existing count/page separate live commands retained.",writeCost="No new persistent search schema in WorkstreamA"});
        try
        {
            var admittedModes=new List<string>{"baseline"};
            foreach(var mode in new[]{"late","clustered-late"})
            {
                Func<string,string> rewrite=mode=="late"?Late:ClusteredLate;
                try
                {
                    await SearchArchitectureVerification.Run(db,persistence,connection,capture,rewrite,async(name,value)=>await save(name.Replace("search-architecture",$"query-only-{mode}"),value),tier,true);
                    await LivePages(persistence,connection,capture,rewrite,save,tier,mode);
                    admittedModes.Add(mode);
                }
                catch(Exception e) when(e is InvalidOperationException or SqlException)
                {
                    capture.Rewrite=null;
                    await save($"{tier}-query-only-{mode}-rejected.json",new{result="REJECTED",reason=e.Message,sqlError=e is SqlException s?(int?)s.Number:null,productionChanged=false});
                }
            }
            var cases=new(string Name,string Search,string[] Fields,string Sort)[]{
                ("common","Synthetic",Capture.SearchFields,"recentlyUpdated"),("rare","RareZebra",Capture.SearchFields,"recentlyUpdated"),("none","NoSuchSyntheticMatch",Capture.SearchFields,"recentlyUpdated"),
                ("name-only","RareZebra",[],"recentlyUpdated"),("one-char","a",Capture.SearchFields,"recentlyUpdated"),("two-char","ar",Capture.SearchFields,"recentlyUpdated"),("long",new string('x',200),Capture.SearchFields,"recentlyUpdated"),
                ("name","Synthetic Contact 10",Capture.SearchFields,"recentlyUpdated"),("email","@example.invalid",Capture.SearchFields,"recentlyUpdated"),("phone","000",Capture.SearchFields,"recentlyUpdated"),("multiple","10",Capture.SearchFields,"recentlyUpdated"),
                ("nameAsc","Synthetic",Capture.SearchFields,"nameAsc"),("nextFollowUp","Synthetic",Capture.SearchFields,"nextFollowUp")};
            foreach(var item in cases)
            {
                var spec=Spec(new(Search:item.Search,Sort:item.Sort),item.Fields);
                foreach(var mode in admittedModes)
                {
                    capture.Rewrite=mode switch{"late"=>Late,"clustered-late"=>ClusteredLate,_=>null};
                    await measure($"{tier}-query-only-{mode}-{item.Name}",async()=>{await persistence.ReadContactPageAsync(spec,26,default);},capture,connection);
                }
                if(item.Name is "common" or "rare" or "none")
                {
                    capture.Rewrite=null;
                    await measure($"{tier}-query-only-summary-baseline-{item.Name}",async()=>{await persistence.ReadContactStatusCountsAsync(spec,default);},capture,connection);
                }
            }
            // Matching Unicode and deep pages use the same state for all three algorithms.
            using(var add=new SqlCommand("INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,Version,CreatedAt,UpdatedAt) VALUES(N'query_unicode',N'bench_large',N'member_1',N'Nguyễn Đặng 😀',N'active',N'{\"displayName\":\"Trần Đặng 😀\"}',0,'2026-01-01','2026-01-01')",connection))await add.ExecuteNonQueryAsync();
            try
            {
                var unicode=Spec(new(Search:"Đặng"),Capture.SearchFields);
                foreach(var mode in admittedModes)
                {
                    capture.Rewrite=mode switch{"late"=>Late,"clustered-late"=>ClusteredLate,_=>null};
                    await measure($"{tier}-query-only-{mode}-unicode-match",async()=>{var p=await persistence.ReadContactPageAsync(unicode,26,default);if(p.TotalCount!=1||p.Items.Count!=1||p.Items[0].Contact.ContactId!="query_unicode")throw new InvalidOperationException("Unicode identity mismatch");},capture,connection);
                }
            }
            finally{using var clean=new SqlCommand("DELETE contacts.Contacts WHERE ContactId=N'query_unicode'",connection);await clean.ExecuteNonQueryAsync();}
            foreach(var sort in new[]{"recentlyUpdated","nameAsc","nextFollowUp"})
            {
                capture.Rewrite=null;var spec=Spec(new(Search:"Synthetic",Sort:sort),Capture.SearchFields);
                for(var page=1;page<10;page++){var p=await persistence.ReadContactPageAsync(spec,26,default);var last=p.Items[24];spec=spec with{CursorId=last.Contact.ContactId,CursorUpdatedAt=last.Contact.UpdatedAt,CursorName=last.Contact.FullName,CursorFollowUpAt=last.NextFollowUpAt,CursorFollowUpIsNull=last.NextFollowUpAt is null};}
                foreach(var mode in admittedModes)
                {
                    capture.Rewrite=mode switch{"late"=>Late,"clustered-late"=>ClusteredLate,_=>null};
                    await measure($"{tier}-query-only-{mode}-deep-{sort}",async()=>{await persistence.ReadContactPageAsync(spec,26,default);},capture,connection);
                }
            }
            foreach(var item in cases.Take(3))foreach(var mode in admittedModes.AsEnumerable().Reverse())
            {
                capture.Rewrite=mode switch{"late"=>Late,"clustered-late"=>ClusteredLate,_=>null};var spec=Spec(new(Search:item.Search),item.Fields);
                await measure($"{tier}-query-only-repeat-{mode}-{item.Name}",async()=>{await persistence.ReadContactPageAsync(spec,26,default);},capture,connection);
            }
        }
        finally{capture.Rewrite=null;}
    }
    static async Task LivePages(EfContactsPersistence persistence,SqlConnection c,Capture capture,Func<string,string> rewrite,Func<string,object,Task> save,int tier,string mode)
    {
        const string workspace="bench_query_live";
        var checks=new List<string>();var completed=false;
        async Task Exec(string sql){using var command=new SqlCommand(sql,c){CommandTimeout=30};await command.ExecuteNonQueryAsync();}
        try
        {
            foreach(var sort in new[]{"recentlyUpdated","nameAsc","nextFollowUp"})
            {
                await Exec($"DELETE contacts.Contacts WHERE WorkspaceId=N'{workspace}'; WITH n AS(SELECT TOP(60) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) n FROM sys.all_objects) INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,Version,CreatedAt,UpdatedAt) SELECT CONCAT(N'query_live_',RIGHT(CONCAT('000',n),3)),N'{workspace}',N'member_1',N'Needle repeated',N'active',N'{{}}',0,'2026-01-01','2026-01-01' FROM n");
                var spec=new ContactListSpecification(workspace,"member_1",new(Search:"Needle",Sort:sort),Capture.SearchFields,FollowUpAuthority:new ContactFollowUpReadAuthority(workspace,null));
                capture.Rewrite=null;var baseline=await persistence.ReadContactPageAsync(spec,26,default);
                capture.Rewrite=rewrite;var candidate=await persistence.ReadContactPageAsync(spec,26,default);
                if(!baseline.Items.Select(x=>x.Contact.ContactId).SequenceEqual(candidate.Items.Select(x=>x.Contact.ContactId)))throw new InvalidOperationException("Live first-page mismatch");
                var last=baseline.Items[24];spec=spec with{CursorId=last.Contact.ContactId,CursorUpdatedAt=last.Contact.UpdatedAt,CursorName=last.Contact.FullName,CursorFollowUpAt=last.NextFollowUpAt,CursorFollowUpIsNull=last.NextFollowUpAt is null};
                capture.Rewrite=null;var next=await persistence.ReadContactPageAsync(spec,26,default);
                using(var mutation=new SqlCommand($"INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,Version,CreatedAt,UpdatedAt) VALUES(N'query_live_000',N'{workspace}',N'member_1',N'A Needle inserted',N'active',N'{{}}',0,'2026-01-01','2030-01-01'); DELETE contacts.Contacts WHERE WorkspaceId=@workspace AND ContactId=@deleted; UPDATE contacts.Contacts SET FullName=N'A Needle moved',UpdatedAt='2031-01-01',OwnerId=N'member_2',Version=Version+1 WHERE WorkspaceId=@workspace AND ContactId=@updated;",c))
                {mutation.Parameters.AddWithValue("@workspace",workspace);mutation.Parameters.AddWithValue("@deleted",next.Items[0].Contact.ContactId);mutation.Parameters.AddWithValue("@updated",baseline.Items[0].Contact.ContactId);await mutation.ExecuteNonQueryAsync();}
                capture.Rewrite=null;var after=await persistence.ReadContactPageAsync(spec,26,default);
                capture.Rewrite=rewrite;var rewritten=await persistence.ReadContactPageAsync(spec,26,default);
                if(after.TotalCount!=rewritten.TotalCount || !after.Items.Select(x=>x.Contact.ContactId).SequenceEqual(rewritten.Items.Select(x=>x.Contact.ContactId)) || rewritten.Items.Any(x=>x.Contact.OwnerId!="member_1") || rewritten.Items.Any(x=>x.Contact.ContactId=="query_live_000") || rewritten.Items.Select(x=>x.Contact.ContactId).Distinct().Count()!=rewritten.Items.Count)
                    throw new InvalidOperationException("Inter-page write parity/scope failed");
                checks.Add(sort+": first/next and committed inter-page insert/update/delete/owner change parity");
            }
            completed=true;
        }
        finally
        {
            capture.Rewrite=null;await Exec($"DELETE contacts.Contacts WHERE WorkspaceId=N'{workspace}'");
            await save($"{tier}-query-only-{mode}-live-pages.json",new{result=completed?"PASS":"INCOMPLETE",checks,scope="Sequential committed writes between pages, same controlled state for old/new read. No coordinated mutation between candidate qualification and outer row fetch; that parity remains NOT_ESTABLISHED."});
        }
    }
    static ContactListSpecification Spec(ContactListFilters filters,string[] fields)=>new("bench_large",null,filters,fields,FollowUpAuthority:new ContactFollowUpReadAuthority("bench_large",null));
}
