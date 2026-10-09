using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Application.ListContacts;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Operations.Tasks.Contracts;

internal static class SearchArchitectureVerification
{
    internal static async Task Run(ContactsDbContext db,EfContactsPersistence persistence,SqlConnection connection,Capture capture,
        Func<string,string> rewrite,Func<string,object,Task> save,int tier,bool boundedEdgePages=false)
    {
        var results=new List<object>();
        var result="FAIL";
        const string probeWorkspace="bench_arch_probe";
        var profiles=new[]{"{}","[]","[{\"displayName\":\"Needle\"}]","[{},{}]","{\"displayName\":null}",
            "{\"displayName\":{\"value\":\"Needle\"}}","{\"displayName\":[\"Needle\"]}",
            "{\"displayName\":\"Needle\",\"displayName\":\"Second\"}","{\"displayName\":null,\"displayName\":\"Needle\"}",
            "{\"DisplayName\":\"WrongCase\",\"displayName\":\"Needle\"}",
            JsonSerializer.Serialize(new{displayName=new string('a',3999)+"Z"}),JsonSerializer.Serialize(new{displayName=new string('a',4000)+"Z"}),
            JsonSerializer.Serialize(new{displayName="Nguyễn NGUYỄN Trần Đặng é É e E İstanbul ı i I 😀 𐐀 𐐨",workEmail="Mixed.Needle@example.test",personalEmail="private-secret@example.test",mobilePhone="+84 090 Needle",workPhone="123-456",otherPhone="999"}),
            "{\"displayName\":123,\"workEmail\":true}","{\"displayName\":\"abc\",\"workEmail\":\"def\"}",
            JsonSerializer.Serialize(new{displayName=new string('a',3998)+"😀"}),JsonSerializer.Serialize(new{displayName=new string('a',3999)+"😀"})};
        try
        {
            for(var i=0;i<profiles.Length;i++)
            {
                using var cmd=new SqlCommand("INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,NormalizedWorkEmail,NormalizedPersonalEmail,CreatedAt,UpdatedAt,ArchivedAt,Version) SELECT TOP(1) @id,@workspace,OwnerId,@name,Status,@profile,NULL,NULL,CreatedAt,UpdatedAt,NULL,Version FROM contacts.Contacts WHERE WorkspaceId='bench_large' AND ArchivedAt IS NULL",connection);
                cmd.Parameters.AddWithValue("@id",$"arch_probe_{i}");cmd.Parameters.AddWithValue("@workspace",probeWorkspace);cmd.Parameters.AddWithValue("@name",i==0?"Name Needle Nguyễn 😀":"Probe person");cmd.Parameters.AddWithValue("@profile",profiles[i]);await cmd.ExecuteNonQueryAsync();
            }
            foreach(var fields in new[]{Capture.SearchFields,new[]{"workEmail"},new[]{"displayName"},Array.Empty<string>()})
            foreach(var needle in new[]{"Needle","private-secret","abcde","Z","Nguyễn","NGUYỄN","nguyen","Đặng","é","e","İ","ı","😀","𐐨","123","true","090","Second","WrongCase"})
            {
                var spec=new ContactListSpecification(probeWorkspace,null,new(Search:needle),fields);
                capture.Rewrite=null;
                var baselineQuery=ContactListSql.Filter(db,spec).OrderBy(c=>c.ContactId).Select(c=>new{c.ContactId,c.Status});
                if(boundedEdgePages)baselineQuery=baselineQuery.Take(100);
                var baseline=await baselineQuery.ToArrayAsync();
                var summary=await ContactListSql.Filter(db,spec).GroupBy(c=>c.Status).Select(g=>new{Status=g.Key,Count=g.LongCount()}).OrderBy(c=>c.Status).ToArrayAsync();
                capture.Rewrite=rewrite;
                var candidateQuery=ContactListSql.Filter(db,spec).OrderBy(c=>c.ContactId).Select(c=>new{c.ContactId,c.Status});
                if(boundedEdgePages)candidateQuery=candidateQuery.Take(100);
                var candidate=await candidateQuery.ToArrayAsync();
                var candidateSummary=await ContactListSql.Filter(db,spec).GroupBy(c=>c.Status).Select(g=>new{Status=g.Key,Count=g.LongCount()}).OrderBy(c=>c.Status).ToArrayAsync();
                Assert(JsonSerializer.Serialize(baseline)==JsonSerializer.Serialize(candidate)&&JsonSerializer.Serialize(summary)==JsonSerializer.Serialize(candidateSummary),"JSON/Unicode/field differential");
                results.Add(new{kind="json-unicode-fls",needle,readableFields=fields,result="PASS",matchingCount=baseline.Length});
            }
            // Special JSON is queried only as identities/status, never deserialized into ContactProfile.
            await CleanProbes(connection,probeWorkspace);capture.Rewrite=null;
            var all=Capture.SearchFields;
            var scenarios=new(string Name,ContactListSpecification Spec,int Pages)[]{
                ("workspace-large",Spec("bench_large",new(Search:"Synthetic"),all),10),
                ("workspace-medium",Spec("bench_medium_0",new(Search:"Synthetic"),all),2),
                ("workspace-small",Spec("bench_small_8",new(Search:"Synthetic"),all),2),
                ("workspace-absent",Spec("bench_absent",new(Search:"Synthetic"),all),1),
                ("own",Spec("bench_large",new(Search:"Synthetic"),all,ownerScope:"member_1"),2),
                ("owner-filter",Spec("bench_large",new(Search:"Synthetic",OwnerId:"member_2"),all),2),
                ("fls-email",Spec("bench_large",new(Search:"@example.invalid"),["workEmail"]),2),
                ("fls-hidden-email",Spec("bench_large",new(Search:"@example.invalid"),[]),1),
                ("fls-none-name",Spec("bench_large",new(Search:"Synthetic"),[]),2),
                ("status",Spec("bench_large",new(Search:"Synthetic",Status:"needs_follow_up"),all),2),
                ("archived",Spec("bench_large",new(Search:"Synthetic",Status:"archived"),all),2),
                ("profile-source",Spec("bench_large",new(Search:"Synthetic",Source:"web"),all),2),
                ("profile-pair",Spec("bench_large",new(Search:"Synthetic",RelationshipLevel:"close",DoNotContact:true),all),2),
                ("linked",Spec("bench_large",new(Search:"Synthetic",Link:"linked"),all),2),
                ("unlinked",Spec("bench_large",new(Search:"Synthetic",Link:"unlinked"),all),2),
                ("followup-overdue",Spec("bench_large",new(Search:"Synthetic",FollowUp:"overdue"),all),2),
                ("followup-today-own",Spec("bench_large",new(Search:"Synthetic",FollowUp:"today"),all,taskOwner:"member_1"),2),
                ("sort-name",Spec("bench_large",new(Search:"Synthetic",Sort:"nameAsc"),all),10),
                ("sort-followup",Spec("bench_large",new(Search:"Synthetic",Sort:"nextFollowUp"),all),10),
                ("rare",Spec("bench_large",new(Search:"RareZebra"),all),2),
                ("none",Spec("bench_large",new(Search:"NoSuchSyntheticMatch"),all),1),
                ("no-task-authority",Spec("bench_large",new(Search:"Synthetic"),all,follow:false),2)};
            foreach(var scenario in scenarios)
            {
                var spec=scenario.Spec;var seen=new HashSet<string>();var pages=0;
                for(var page=1;page<=scenario.Pages;page++)
                {
                    capture.Rewrite=null;var baseline=await persistence.ReadContactPageAsync(spec,26,default);
                    capture.Rewrite=rewrite;var candidate=await persistence.ReadContactPageAsync(spec,26,default);
                    Assert(baseline.TotalCount==candidate.TotalCount,"exact total count");
                    Assert(baseline.Items.Select(x=>x.Contact.ContactId).SequenceEqual(candidate.Items.Select(x=>x.Contact.ContactId)),"ordered IDs");
                    Assert(baseline.Items.Select(x=>x.NextFollowUpAt).SequenceEqual(candidate.Items.Select(x=>x.NextFollowUpAt)),"Tasks followup values");
                    Assert((baseline.Items.Count>25)==(candidate.Items.Count>25),"hasNextPage");
                    foreach(var row in baseline.Items.Take(25)){Assert(row.Contact.WorkspaceId==spec.WorkspaceId,"workspace");if(spec.ScopeOwnerId is{} owner)Assert(row.Contact.OwnerId==owner,"record owner scope");Assert(seen.Add(row.Contact.ContactId),"cursor duplicate");}
                    if(page==1)
                    {
                        capture.Rewrite=null;var counts=await persistence.ReadContactStatusCountsAsync(spec,default);
                        capture.Rewrite=rewrite;var candidateCounts=await persistence.ReadContactStatusCountsAsync(spec,default);
                        Assert(JsonSerializer.Serialize(counts.OrderBy(x=>x.Key))==JsonSerializer.Serialize(candidateCounts.OrderBy(x=>x.Key)),"summary");
                        Assert(counts.Values.Sum()==baseline.TotalCount,"summary/count identity");
                    }
                    pages=page;
                    if(baseline.Items.Count<=25)break;
                    var last=baseline.Items[24];
                    spec=spec with{CursorId=last.Contact.ContactId,CursorName=last.Contact.FullName,CursorUpdatedAt=last.Contact.UpdatedAt,CursorFollowUpAt=last.NextFollowUpAt,CursorFollowUpIsNull=last.NextFollowUpAt is null};
                }
                results.Add(new{kind="persistence-pairwise",scenario=scenario.Name,pages,uniqueTraversedRows=seen.Count,result="PASS",identities=true,order=true,exactCount=true,summary=true,hasNextPage=true});
            }
            result="PASS";
        }
        finally
        {
            capture.Rewrite=null;await CleanProbes(connection,probeWorkspace);
            await save($"{tier}-search-architecture-verification.json",new{result,cases=results,completedCases=results.Count,probeRowsRemoved=true,
                fieldAccessEvidence="Canonical specification readable-field subsets, not authenticated HTTP principals",cursorEvidence="Persistence keyset continuation; protected Handler cursor security is unchanged and not separately exercised here",liveSnapshot="NOT_PROVIDED"});
        }
    }
    private static ContactListSpecification Spec(string workspace,ContactListFilters filters,string[] fields,string? ownerScope=null,string? taskOwner=null,bool follow=true)
        =>new(workspace,ownerScope,filters,fields,FollowUpAuthority:follow?new ContactFollowUpReadAuthority(workspace,taskOwner):null,
            DayStart:filters.FollowUp is null?null:DateTimeOffset.Parse("2026-10-08T00:00:00Z"),DayEnd:filters.FollowUp is null?null:DateTimeOffset.Parse("2026-10-09T00:00:00Z"));
    private static void Assert(bool condition,string name){if(!condition)throw new InvalidOperationException("Search architecture parity failed: "+name);}
    private static async Task CleanProbes(SqlConnection connection,string workspace)
    {
        using var cmd=new SqlCommand("DELETE contacts.Contacts WHERE WorkspaceId=@workspace AND ContactId LIKE 'arch_probe_%'",connection);cmd.Parameters.AddWithValue("@workspace",workspace);await cmd.ExecuteNonQueryAsync();
    }
}
