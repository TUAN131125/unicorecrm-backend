using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Application.ListContacts;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Operations.Tasks.Contracts;

internal static class SearchInvestigation
{
    internal static string OneParse(string sql)
    {
        var needle=Regex.Match(sql,@"CHARINDEX\((@\w+), UPPER\(c\.\[FullName\]\)\)");
        if(!needle.Success) return sql;
        var fields=Capture.SearchFields.Where(f=>sql.Contains($"UPPER(JSON_VALUE(c.[Profile], '$.{f}'))",StringComparison.Ordinal)).ToArray();
        if(fields.Length==0) return sql;
        var columns=string.Join(",",fields.Select(f=>$"[{f}] nvarchar(max) '$.{f}'"));
        var apply=$" OUTER APPLY OPENJSON(CASE WHEN CHARINDEX({needle.Groups[1].Value}, UPPER(c.[FullName])) > 0 THEN N'{{}}' ELSE c.[Profile] END) WITH ({columns}) AS j";
        sql=sql.Replace("FROM [contacts].[Contacts] c", "FROM [contacts].[Contacts] c"+apply,StringComparison.Ordinal);
        foreach(var f in fields) sql=sql.Replace($"UPPER(JSON_VALUE(c.[Profile], '$.{f}'))",$"UPPER(CASE WHEN DATALENGTH(j.[{f}]) <= 8000 THEN j.[{f}] END)",StringComparison.Ordinal);
        return sql;
    }
    internal static string NoRowGoal(string sql)=>sql.Contains("SELECT TOP(",StringComparison.Ordinal)&&sql.Contains("CHARINDEX(",StringComparison.Ordinal)
        ?sql+"\nOPTION (USE HINT('DISABLE_OPTIMIZER_ROWGOAL'))":sql;

    internal static async Task Run(int tier,ContactsDbContext db,EfContactsPersistence persistence,SqlConnection connection,Capture capture,
        Func<string,Func<Task>,Capture,SqlConnection,Task> measure,Func<string,object,Task> save)
    {
        var collations=new List<object>();
        using(var cmd=new SqlCommand("SELECT CONVERT(nvarchar(128),DATABASEPROPERTYEX(DB_NAME(),'Collation')) DbCollation, c.name,c.collation_name FROM sys.columns c WHERE c.object_id=OBJECT_ID('contacts.Contacts') AND c.name IN ('FullName','Profile')",connection))
        await using(var reader=await cmd.ExecuteReaderAsync())while(await reader.ReadAsync())collations.Add(new{database=reader.GetString(0),column=reader.GetString(1),collation=reader.GetString(2)});
        await save($"{tier}-search-collations.json",collations);
        var parity=await Differential(tier,db,connection,capture,save);
        var cases=new(string Name,string Search,string[] Fields)[]{
            ("common","Synthetic",Capture.SearchFields),("rare","RareZebra",Capture.SearchFields),("none","NoSuchSyntheticMatch",Capture.SearchFields),
            ("short","a",Capture.SearchFields),("long",new string('x',200),Capture.SearchFields),
            ("name","Synthetic Contact 10",Capture.SearchFields),("email","@example.invalid",Capture.SearchFields),
            ("phone","000",Capture.SearchFields),("multiple-fields","10",Capture.SearchFields),
            ("fls-email","@example.invalid",["workEmail"]),("name-only","RareZebra",[])};
        try
        {
            foreach(var candidate in new[]{"baseline","one-parse","no-rowgoal"})
            {
                if(candidate=="one-parse" && !parity)continue;
                capture.Rewrite=candidate switch{"one-parse"=>OneParse,"no-rowgoal"=>NoRowGoal,_=>null};
                foreach(var item in cases)
                {
                    if(candidate=="no-rowgoal" && item.Name is not ("common" or "rare" or "none"))continue;
                    var spec=new ContactListSpecification("bench_large",null,new(Search:item.Search),item.Fields,FollowUpAuthority:new ContactFollowUpReadAuthority("bench_large",null));
                    await measure($"{tier}-search-{candidate}-{item.Name}",async()=>{await persistence.ReadContactPageAsync(spec,26,default);},capture,connection);
                    if(item.Name is "common" or "rare" or "none")
                        await measure($"{tier}-search-summary-{candidate}-{item.Name}",async()=>{await persistence.ReadContactStatusCountsAsync(spec,default);},capture,connection);
                }
            }
        }
        finally {capture.Rewrite=null;}
        await save($"{tier}-search-investigation-scope.json",new{result="MEASURED_PROTOTYPES_ONLY",zeroCountSkip="REJECTED: changes existing separate-command live count/page observation window",productionChanged=false,
            mapping="Combined latency includes EF materialization; individual command replay includes SQL/client transport. These timings do not cleanly isolate JSON/UPPER CPU.",collationScope="Only recorded actual column/database collations validated; no claim for all collations."});
    }

    internal static async Task<bool> Differential(int tier,ContactsDbContext db,SqlConnection connection,Capture capture,Func<string,object,Task> save)
    {
        const string workspace="bench_search_probe";
        var profiles=new[]{"{}","{\"displayName\":null}","{\"displayName\":{\"value\":\"Needle\"}}","{\"displayName\":[\"Needle\"]}",
            "{\"displayName\":\"Needle\",\"displayName\":\"second\"}","{\"DisplayName\":\"wrongCase\",\"displayName\":\"Needle\"}",
            JsonSerializer.Serialize(new{displayName=new string('a',3999)+"Z"}),JsonSerializer.Serialize(new{displayName=new string('a',4000)+"Z"}),
            JsonSerializer.Serialize(new{displayName="é É e E İstanbul ı i I 😀 𐐀 𐐨",workEmail="Mixed.Needle@example.test",personalEmail="private-secret@example.test",mobilePhone="+84 090 Needle",workPhone="123-456",otherPhone="999"}),
            "{\"displayName\":123,\"workEmail\":true}","{\"displayName\":\"abc\",\"workEmail\":\"def\"}",
            "[]","[{\"displayName\":\"Needle\"}]","[{\"displayName\":\"Needle\"},{\"displayName\":\"Needle\"}]"};
        var validations=new List<object>();
        var parity=true;
        var completed=false;
        try
        {
            for(var i=0;i<profiles.Length;i++)
            {
                using var cmd=new SqlCommand("INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,NormalizedWorkEmail,NormalizedPersonalEmail,CreatedAt,UpdatedAt,ArchivedAt,Version) SELECT TOP(1) @id,@workspace,OwnerId,@name,Status,@profile,NULL,NULL,CreatedAt,UpdatedAt,NULL,Version FROM contacts.Contacts WHERE WorkspaceId='bench_large' AND ArchivedAt IS NULL",connection);
                cmd.Parameters.AddWithValue("@id",$"probe_search_{i}");cmd.Parameters.AddWithValue("@workspace",workspace);cmd.Parameters.AddWithValue("@name",i==0?"Name Needle é 😀":"Probe person");cmd.Parameters.AddWithValue("@profile",profiles[i]);await cmd.ExecuteNonQueryAsync();
            }
            foreach(var fields in new[]{Capture.SearchFields,new[]{"workEmail"},new[]{"displayName"},Array.Empty<string>()})
            foreach(var needle in new[]{"Needle","private-secret","abcde","Z","é","e","İ","ı","😀","𐐨","123","true","090","second","wrongCase"})
            {
                var spec=new ContactListSpecification(workspace,null,new(Search:needle),fields);
                capture.Rewrite=null;
                var baseline=await ContactListSql.Filter(db,spec).OrderBy(c=>c.ContactId).Select(c=>new{c.ContactId,c.Status}).ToArrayAsync();
                var counts=await ContactListSql.Filter(db,spec).GroupBy(c=>c.Status).Select(g=>new{Status=g.Key,Count=g.LongCount()}).OrderBy(c=>c.Status).ToArrayAsync();
                capture.Rewrite=OneParse;
                var candidate=await ContactListSql.Filter(db,spec).OrderBy(c=>c.ContactId).Select(c=>new{c.ContactId,c.Status}).ToArrayAsync();
                var candidateCounts=await ContactListSql.Filter(db,spec).GroupBy(c=>c.Status).Select(g=>new{Status=g.Key,Count=g.LongCount()}).OrderBy(c=>c.Status).ToArrayAsync();
                var equal=JsonSerializer.Serialize(baseline)==JsonSerializer.Serialize(candidate)&&JsonSerializer.Serialize(counts)==JsonSerializer.Serialize(candidateCounts);
                validations.Add(new{needle,fields,result=equal?"PASS":"FAIL",count=baseline.Length,exactIds=true,summary=true});
                if(!equal)parity=false;
            }
            foreach(var scope in new[]{"bench_large","bench_medium_0","bench_small_8","bench_absent"})
            foreach(var needle in new[]{"Synthetic","RareZebra","NoSuchSyntheticMatch"})
            {
                var spec=new ContactListSpecification(scope,null,new(Search:needle),Capture.SearchFields);
                capture.Rewrite=null;var baseline=await ContactListSql.Filter(db,spec).OrderBy(c=>c.ContactId).Select(c=>c.ContactId).ToArrayAsync();
                capture.Rewrite=OneParse;var candidate=await ContactListSql.Filter(db,spec).OrderBy(c=>c.ContactId).Select(c=>c.ContactId).ToArrayAsync();
                if(!baseline.SequenceEqual(candidate))
                {
                    parity=false;
                    throw new InvalidOperationException("Workspace search differential mismatch");
                }
                validations.Add(new{scope,needle,result="PASS",count=baseline.Length,exactIds=true});
            }
            completed=true;
        }
        finally
        {
            capture.Rewrite=null;
            using var clean=new SqlCommand("DELETE contacts.Contacts WHERE WorkspaceId=@workspace AND ContactId LIKE 'probe_search_%'",connection);clean.Parameters.AddWithValue("@workspace",workspace);await clean.ExecuteNonQueryAsync();
            await save($"{tier}-search-differential.json",new{result=!completed?"INCOMPLETE":parity?"PASS":"FAIL",completed,cases=validations,probeRowsRemoved=true,claim="Prototype identity/summary parity only; includes root-array rows admitted by SQL constraint; no production promotion on FAIL or INCOMPLETE"});
        }
        return parity;
    }
}
