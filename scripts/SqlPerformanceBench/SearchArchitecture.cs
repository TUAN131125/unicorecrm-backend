using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Application.ListContacts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Operations.Tasks.Contracts;

// Only reachable through the explicit disposable SQL benchmark mode. No production DI registration.
internal static class SearchArchitecture
{
    const string Table="contacts.BenchmarkSearchProjection";
    const string Trigger="contacts.BenchmarkSearchSync";
    static readonly string[] Fields=["fullName",..Capture.SearchFields];
    static string Values(string alias)=>string.Join(",",Fields.Select(f=>f=="fullName"?$"UPPER({alias}.FullName)":$"UPPER(JSON_VALUE({alias}.Profile,'$.{f}'))"));
    static string Columns=>string.Join(",",Fields.Select(f=>$"[{f}]"));

    internal static string Rewrite(string sql)
    {
        if(!sql.Contains("CHARINDEX(",StringComparison.Ordinal))return sql;
        const string from="FROM [contacts].[Contacts] c";
        if(!sql.Contains(from,StringComparison.Ordinal))throw new InvalidOperationException("Canonical search source changed; refuse rewrite");
        sql=sql.Replace(from,from+$" INNER JOIN {Table} sp ON sp.WorkspaceId=c.WorkspaceId AND sp.ContactId=c.ContactId",StringComparison.Ordinal);
        sql=sql.Replace("UPPER(c.[FullName])","sp.[fullName]",StringComparison.Ordinal);
        foreach(var f in Capture.SearchFields)sql=sql.Replace($"UPPER(JSON_VALUE(c.[Profile], '$.{f}'))",$"sp.[{f}]",StringComparison.Ordinal);
        return sql;
    }

    internal static async Task Run(int tier,ContactsDbContext db,EfContactsPersistence persistence,SqlConnection connection,Capture capture,
        Func<string,Func<Task>,Capture,SqlConnection,Task> measure,Func<string,object,Task> save,Func<Task<List<Dictionary<string,object?>>>> guard,bool validationOnly=false)
    {
        using(var own=new SqlCommand("SELECT DB_NAME(),CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'UniCoreCRM_PerfBench_RunId'",connection))
        await using(var r=await own.ExecuteReaderAsync())
        {
            if(!await r.ReadAsync() || !Guid.TryParseExact(r.GetString(1),"N",out _) || r.GetString(0)!="UniCoreCRM_PerfBench_"+r.GetString(1))throw new InvalidOperationException("Owned fixture required");
        }
        await guard();
        var before=await Storage(connection);
        var collations=await Rows(connection,"SELECT name,collation_name FROM sys.columns WHERE object_id=OBJECT_ID('contacts.Contacts') AND name IN ('WorkspaceId','ContactId','FullName','Profile')");
        string Collation(string column)
        {
            var c=(string)collations.Single(x=>Equals(x["name"],column))["collation_name"]!;
            if(!Regex.IsMatch(c,@"\A[A-Za-z0-9_]+\z"))throw new InvalidOperationException("Unexpected collation identifier");
            return c;
        }
        await save($"{tier}-architecture-collations.json",collations);
        var create=$"CREATE TABLE {Table}(WorkspaceId nvarchar(128) COLLATE {Collation("WorkspaceId")} NOT NULL,ContactId nvarchar(128) COLLATE {Collation("ContactId")} NOT NULL,SourceVersion bigint NOT NULL,"+
            string.Join(",",Fields.Select(f=>$"[{f}] nvarchar(4000) COLLATE {Collation(f=="fullName"?"FullName":"Profile")} NULL"))+
            $",CONSTRAINT PK_BenchmarkSearchProjection PRIMARY KEY(WorkspaceId,ContactId),CONSTRAINT FK_BenchmarkSearchProjection FOREIGN KEY(WorkspaceId,ContactId) REFERENCES contacts.Contacts(WorkspaceId,ContactId) ON DELETE CASCADE)";
        var trigger=$"CREATE TRIGGER {Trigger} ON contacts.Contacts AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; DELETE p FROM {Table} p JOIN deleted d ON d.WorkspaceId=p.WorkspaceId AND d.ContactId=p.ContactId; INSERT {Table}(WorkspaceId,ContactId,SourceVersion,{Columns}) SELECT i.WorkspaceId,i.ContactId,i.Version,{Values("i")} FROM inserted i; END";
        try
        {
            var install=Stopwatch.StartNew();
            await using(var tx=(SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
            {
                await Exec(connection,"SELECT COUNT_BIG(*) FROM contacts.Contacts WITH(TABLOCKX,HOLDLOCK)",tx);
                await Exec(connection,create,tx);
                await Exec(connection,trigger,tx);
                await Exec(connection,$"INSERT {Table}(WorkspaceId,ContactId,SourceVersion,{Columns}) SELECT c.WorkspaceId,c.ContactId,c.Version,{Values("c")} FROM contacts.Contacts c",tx);
                await tx.CommitAsync();
            }
            await save($"{tier}-architecture-backfill.json",new{result="PASS",scope="Prototype install on existing canonical schema; serialized table-lock transaction, not production EF migration",elapsedMs=install.Elapsed.TotalMilliseconds,liveWriteConsistency="Separate lock/interleaving test below",storageBefore=before,storageAfter=await Storage(connection)});
            await guard();
            await VerifyProjection(connection);
            await SearchArchitectureVerification.Run(db,persistence,connection,capture,Rewrite,save,tier);
            await Consistency(db,connection,capture,save,tier);
            if(validationOnly)
            {
                await FocusedReadBench(persistence,connection,capture,measure,tier);
                capture.Rewrite=Rewrite;
                await save($"{tier}-architecture-handler-cursor.json",new{result="PASS",checks=await CursorVerification.Run(persistence,connection),scope="Existing Handler cursor security under prototype schema; these no-search queries do not exercise projection. Search keyset parity is separately covered by persistence comparisons. Explicit fixture policy decisions, not public HTTP evaluator."});
                return;
            }
            await WriteBench(connection,save,tier,guard);
            // Both algorithms operate on the same fixture with identical schema/storage/caches.
            var cases=new(string Name,string Search,string[] Fields)[]{("common","Synthetic",Capture.SearchFields),("rare","RareZebra",Capture.SearchFields),("none","NoSuchSyntheticMatch",Capture.SearchFields),
                ("one-char","a",Capture.SearchFields),("two-char","ar",Capture.SearchFields),("three-char","are",Capture.SearchFields),("long",new string('x',200),Capture.SearchFields),
                ("name","Synthetic Contact 10",Capture.SearchFields),("email","@example.invalid",Capture.SearchFields),("phone","000",Capture.SearchFields),("unicode","đ",Capture.SearchFields),
                ("multiple-fields","10",Capture.SearchFields),("fls-email","@example.invalid",["workEmail"]),("name-only","RareZebra",[])};
            foreach(var item in cases)
            {
                var spec=new ContactListSpecification("bench_large",null,new(Search:item.Search),item.Fields,FollowUpAuthority:new ContactFollowUpReadAuthority("bench_large",null));
                foreach(var mode in new[]{"baseline","projection"})
                {
                    capture.Rewrite=mode=="projection"?Rewrite:null;
                    await measure($"{tier}-architecture-{mode}-{item.Name}",async()=>{await persistence.ReadContactPageAsync(spec,26,default);},capture,connection);
                    if(item.Name is "common" or "rare" or "none")await measure($"{tier}-architecture-summary-{mode}-{item.Name}",async()=>{await persistence.ReadContactStatusCountsAsync(spec,default);},capture,connection);
                }
            }
            // Reverse order repeat for the three acceptance-sensitive cases, preserving raw variance.
            foreach(var item in cases.Take(3))
            foreach(var mode in new[]{"projection","baseline"})
            {
                capture.Rewrite=mode=="projection"?Rewrite:null;
                var spec=new ContactListSpecification("bench_large",null,new(Search:item.Search),item.Fields,FollowUpAuthority:new ContactFollowUpReadAuthority("bench_large",null));
                await measure($"{tier}-architecture-repeat-{mode}-{item.Name}",async()=>{await persistence.ReadContactPageAsync(spec,26,default);},capture,connection);
            }
            await save($"{tier}-architecture-storage.json",new{before,after=await Storage(connection),representation="One row/contact, seven field-separated SQL-uppercase scalar values, composite PK and FK; no ngrams",productionChanged=false});
        }
        finally
        {
            capture.Rewrite=null;
            await Exec(connection,$"DROP TRIGGER IF EXISTS {Trigger}; DROP TABLE IF EXISTS {Table}");
            await save($"{tier}-architecture-teardown.json",new{result="REMOVED_PROTOTYPE",productionChanged=false});
        }
    }

    static async Task FocusedReadBench(EfContactsPersistence persistence,SqlConnection c,Capture capture,Func<string,Func<Task>,Capture,SqlConnection,Task> measure,int tier)
    {
        // Explicit matching Unicode fixture, separate from the main same-distribution comparisons.
        await Exec(c,"INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,Version,CreatedAt,UpdatedAt) VALUES(N'arch_unicode',N'bench_large',N'member_1',N'Nguyễn Đặng 😀',N'active',N'{\"displayName\":\"Trần Đặng 😀\",\"workEmail\":\"unicode@example.invalid\"}',0,'2026-01-01','2026-01-01')");
        try
        {
            foreach(var mode in new[]{"baseline","projection"})
            {
                capture.Rewrite=mode=="projection"?Rewrite:null;
                var spec=new ContactListSpecification("bench_large",null,new(Search:"Đặng"),Capture.SearchFields,FollowUpAuthority:new ContactFollowUpReadAuthority("bench_large",null));
                await measure($"{tier}-architecture-focused-{mode}-unicode-match",async()=>{var p=await persistence.ReadContactPageAsync(spec,26,default);if(p.TotalCount!=1 || p.Items.Count!=1 || p.Items[0].Contact.ContactId!="arch_unicode")throw new InvalidOperationException("Expected Unicode match missing");},capture,c);
            }
        }
        finally{await Exec(c,"DELETE contacts.Contacts WHERE ContactId=N'arch_unicode'");capture.Rewrite=null;}
        foreach(var sort in new[]{"recentlyUpdated","nameAsc","nextFollowUp"})
        {
            var spec=new ContactListSpecification("bench_large",null,new(Search:"Synthetic",Sort:sort),Capture.SearchFields,FollowUpAuthority:new ContactFollowUpReadAuthority("bench_large",null));
            capture.Rewrite=null;
            for(var page=1;page<10;page++)
            {
                var rows=await persistence.ReadContactPageAsync(spec,26,default);
                if(rows.Items.Count<=25)throw new InvalidOperationException("Deep fixture unexpectedly exhausted");
                var last=rows.Items[24];spec=spec with{CursorId=last.Contact.ContactId,CursorName=last.Contact.FullName,CursorUpdatedAt=last.Contact.UpdatedAt,CursorFollowUpAt=last.NextFollowUpAt,CursorFollowUpIsNull=last.NextFollowUpAt is null};
            }
            foreach(var mode in new[]{"baseline","projection"})
            {
                capture.Rewrite=mode=="projection"?Rewrite:null;
                await measure($"{tier}-architecture-focused-{mode}-deep-{sort}",async()=>{await persistence.ReadContactPageAsync(spec,26,default);},capture,c);
            }
        }
        capture.Rewrite=null;
    }

    static async Task VerifyProjection(SqlConnection c)
    {
        var mismatch=await Rows(c,$"SELECT COUNT_BIG(*) mismatches FROM contacts.Contacts c FULL JOIN {Table} p ON p.WorkspaceId=c.WorkspaceId AND p.ContactId=c.ContactId WHERE c.ContactId IS NULL OR p.ContactId IS NULL OR p.SourceVersion<>c.Version OR "+
            string.Join(" OR ",Fields.Select(f=>{var value=f=="fullName"?"UPPER(c.FullName)":$"UPPER(JSON_VALUE(c.Profile,'$.{f}'))";return $"(p.[{f}] COLLATE Latin1_General_100_BIN2 <> {value} COLLATE Latin1_General_100_BIN2 OR (p.[{f}] IS NULL AND {value} IS NOT NULL) OR (p.[{f}] IS NOT NULL AND {value} IS NULL) OR ISNULL(DATALENGTH(p.[{f}]),-1)<>ISNULL(DATALENGTH({value}),-1))";})));
        if(Convert.ToInt64(mismatch[0]["mismatches"])!=0)throw new InvalidOperationException("Projection mismatch");
    }

    static async Task Consistency(ContactsDbContext db,SqlConnection c,Capture capture,Func<string,object,Task> save,int tier)
    {
        var checks=new List<string>();
        const string id="arch_mutation";
        async Task Check(string name)
        {
            await VerifyProjection(c);
            foreach(var needle in new[]{"Original","New Name","old@example.invalid","new@example.invalid","oldphone","Winner","After Backfill","Rolled Back"})
            foreach(var fields in new[]{Capture.SearchFields,Array.Empty<string>()})
            {
                var spec=new ContactListSpecification("bench_arch_mutation",null,new(Search:needle,Status:"archived"),fields);
                foreach(var status in new[]{"active","archived"})
                {
                    spec=spec with{Filters=spec.Filters with{Status=status}};
                    capture.Rewrite=null;var old=await ContactListSql.Filter(db,spec).Select(x=>x.ContactId).ToArrayAsync();
                    capture.Rewrite=Rewrite;var current=await ContactListSql.Filter(db,spec).Select(x=>x.ContactId).ToArrayAsync();
                    if(!old.SequenceEqual(current))throw new InvalidOperationException("Immediate mutation search parity failed");
                }
            }
            capture.Rewrite=null;checks.Add(name);
        }
        async Task Write(string sql){await Exec(c,sql);await Check(sql.Split(' ')[0]);}
        try
        {
            await Write($"INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,Version,CreatedAt,UpdatedAt) VALUES(N'{id}',N'bench_arch_mutation',N'member_1',N'Original Needle',N'active',N'{{\"workEmail\":\"old@example.invalid\",\"mobilePhone\":\"oldphone\"}}',0,'2026-01-01','2026-01-01')");
            foreach(var set in new[]{"FullName=N'New Name Needle'","Profile=JSON_MODIFY(Profile,'$.workEmail',N'new@example.invalid')","Profile=JSON_MODIFY(Profile,'$.mobilePhone',NULL)","Profile=N'[]'","OwnerId=N'member_2'","Status=N'archived',ArchivedAt='2026-10-09'"})
                await Write($"UPDATE contacts.Contacts SET {set},Version=Version+1 WHERE ContactId=N'{id}'");
            await using(var tx=(SqlTransaction)await c.BeginTransactionAsync())
            {
                await Exec(c,$"UPDATE contacts.Contacts SET FullName=N'Rolled Back',Version=Version+1 WHERE ContactId=N'{id}'",tx);
                await tx.RollbackAsync();
            }
            await Check("transaction rollback restores exact projection");
            var version=Convert.ToInt64((await Rows(c,$"SELECT Version FROM contacts.Contacts WHERE ContactId=N'{id}'"))[0]["Version"]);
            await using(var other=new SqlConnection(c.ConnectionString))
            {
                await other.OpenAsync();
                await using var tx=(SqlTransaction)await c.BeginTransactionAsync();
                await Exec(c,$"UPDATE contacts.Contacts SET FullName=N'Winner',Version=Version+1 WHERE ContactId=N'{id}' AND Version={version}",tx);
                using var losing=new SqlCommand($"UPDATE contacts.Contacts SET FullName=N'Loser',Version=Version+1 WHERE ContactId=N'{id}' AND Version={version}",other){CommandTimeout=15};
                var pending=losing.ExecuteNonQueryAsync();
                await Task.Delay(100);
                if(pending.IsCompleted)throw new InvalidOperationException("Expected update serialization not observed");
                await tx.CommitAsync();
                if(await pending!=0)throw new InvalidOperationException("Stale version updated");
            }
            await Check("two-session optimistic update: one winner, stale update zero rows");
            // An idempotent serialized backfill blocks a concurrent authoritative update, then the trigger repairs its committed version.
            await using(var other=new SqlConnection(c.ConnectionString))
            {
                await other.OpenAsync();
                await using var tx=(SqlTransaction)await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                await Exec(c,$"SELECT ContactId FROM contacts.Contacts WITH(UPDLOCK,HOLDLOCK) WHERE ContactId=N'{id}'",tx);
                using var updating=new SqlCommand($"UPDATE contacts.Contacts SET FullName=N'After Backfill',Version=Version+1 WHERE ContactId=N'{id}'",other){CommandTimeout=15};
                var pending=updating.ExecuteNonQueryAsync();await Task.Delay(100);
                if(pending.IsCompleted)throw new InvalidOperationException("Backfill serialization not observed");
                await Exec(c,$"DELETE FROM {Table} WHERE ContactId=N'{id}'; INSERT {Table}(WorkspaceId,ContactId,SourceVersion,{Columns}) SELECT c.WorkspaceId,c.ContactId,c.Version,{Values("c")} FROM contacts.Contacts c WHERE ContactId=N'{id}'",tx);
                await tx.CommitAsync();await pending;
            }
            await Check("source-row locked backfill/live update interleaving");
            var efResults=new List<object>();
            var entity=new Contact("bench_arch_ef","member_1","EF compatible probe","active",new ContactProfile(),DateTimeOffset.UtcNow);
            try
            {
                db.Contacts.Add(entity);
                foreach(var operation in new[]{"create","update-name","archive"})
                {
                    if(operation=="update-name"){db.Entry(entity).Property(nameof(Contact.FullName)).CurrentValue="EF new name";db.Entry(entity).Property(nameof(Contact.Version)).CurrentValue=1L;}
                    if(operation=="archive"){entity.Archive(DateTimeOffset.UtcNow);}
                    try{await db.SaveChangesAsync();efResults.Add(new{operation,result="PASS",sqlError=(int?)null});}
                    catch(DbUpdateException e)
                    {
                        efResults.Add(new{operation,result="FAIL",sqlError=FindSql(e)});
                        foreach(var skipped in new[]{"create","update-name","archive"}.SkipWhile(x=>x!=operation).Skip(1))
                            efResults.Add(new{operation=skipped,result="NOT_RUN",sqlError=(int?)null});
                        break;
                    }
                }
            }
            finally{db.ChangeTracker.Clear();await Exec(c,$"DELETE contacts.Contacts WHERE WorkspaceId=N'bench_arch_ef'");}
            await Write($"INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,Version,CreatedAt,UpdatedAt) SELECT N'arch_multi_'+CONVERT(nvarchar(1),v.n),N'bench_arch_multi',N'member_1',N'Multi',N'active',N'{{}}',0,'2026-01-01','2026-01-01' FROM(VALUES(1),(2))v(n)");
            await Write("UPDATE contacts.Contacts SET FullName=N'Multi updated',Version=Version+1 WHERE WorkspaceId=N'bench_arch_multi'");
            await Write("DELETE contacts.Contacts WHERE WorkspaceId=N'bench_arch_multi'");
            var beforeFailure=Convert.ToInt64((await Rows(c,$"SELECT Version FROM contacts.Contacts WHERE ContactId=N'{id}'"))[0]["Version"]);
            var failedStatement=false;
            try{await Exec(c,$"UPDATE contacts.Contacts SET Profile=N'invalid JSON',Version=Version+1 WHERE ContactId=N'{id}'");}
            catch(SqlException){failedStatement=true;}
            if(!failedStatement || Convert.ToInt64((await Rows(c,$"SELECT Version FROM contacts.Contacts WHERE ContactId=N'{id}'"))[0]["Version"])!=beforeFailure)throw new InvalidOperationException("Failed statement did not preserve authoritative state");
            await Check("failed constraint statement rolls back source/projection");
            await save($"{tier}-architecture-consistency.json",new{sqlLifecycle="PASS",checks,efSaveChangesCompatibility=efResults,scope="Raw authoritative-row SQL transaction/trigger tests; EF persistence create/update/archive separately tested. Full public command/idempotency/outbox verification NOT_RUN for prototype.",failedStatement="Invalid JSON rejected and unchanged version verified; stale version affects zero rows",liveBackfill="Serialized row lock prototype final state PASS; competing request incomplete at100ms, no DMV wait attribution; initial install holds table lock; no nonblocking online deployment claim"});
        }
        finally{capture.Rewrite=null;await Exec(c,$"DELETE contacts.Contacts WHERE ContactId=N'{id}' OR WorkspaceId=N'bench_arch_multi'");}
    }
    static int? FindSql(Exception e){for(Exception? x=e;x!=null;x=x.InnerException)if(x is SqlException s)return s.Number;return null;}

    static async Task WriteBench(SqlConnection c,Func<string,object,Task> save,int tier,Func<Task<List<Dictionary<string,object?>>>> guard)
    {
        var result=new List<object>();
        var operations=new[]{("create",""),("update-name","FullName=N'Write new name'"),("update-email","Profile=JSON_MODIFY(Profile,'$.workEmail',N'write@example.invalid')"),("update-phone","Profile=JSON_MODIFY(Profile,'$.mobilePhone',N'123456')"),("update-multiple","FullName=N'Write multiple',Profile=JSON_MODIFY(Profile,'$.workEmail',N'multiple@example.invalid')"),("archive","Status=N'archived',ArchivedAt='2026-10-09'")};
        const string id="arch_write";
        foreach(var (name,set) in operations)
        foreach(var mode in new[]{"baseline","projection"})
        {
            await guard();
            await Exec(c,$"{(mode=="baseline"?"DISABLE":"ENABLE")} TRIGGER {Trigger} ON contacts.Contacts");
            var samples=new List<object>();
            for(var n=0;n<21;n++)
            {
                // Reset outside measured transaction; all compared writes start from identical state.
                await Exec(c,$"DELETE contacts.Contacts WHERE ContactId=N'{id}'");
                var insert=$"INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,Version,CreatedAt,UpdatedAt) VALUES(N'{id}',N'bench_arch_write',N'member_1',N'Write original',N'active',N'{{\"workEmail\":\"old@example.invalid\",\"mobilePhone\":\"oldphone\"}}',0,'2026-01-01','2026-01-01')";
                if(name!="create")await Exec(c,insert);
                var start=await Rows(c,"SELECT cpu_time,logical_reads,writes FROM sys.dm_exec_sessions WHERE session_id=@@SPID");
                var watch=Stopwatch.StartNew();long? logBytes=null;
                await using(var tx=(SqlTransaction)await c.BeginTransactionAsync())
                {
                    await Exec(c,name=="create"?insert:$"UPDATE contacts.Contacts SET {set},Version=Version+1 WHERE ContactId=N'{id}' AND Version=0",tx);
                    using var logs=new SqlCommand("SELECT dt.database_transaction_log_bytes_used FROM sys.dm_tran_session_transactions st JOIN sys.dm_tran_database_transactions dt ON st.transaction_id=dt.transaction_id WHERE st.session_id=@@SPID AND dt.database_id=DB_ID()",c,tx);
                    var v=await logs.ExecuteScalarAsync();if(v!=null&&v!=DBNull.Value)logBytes=Convert.ToInt64(v);
                    await tx.CommitAsync();
                }
                watch.Stop();
                var end=await Rows(c,"SELECT cpu_time,logical_reads,writes FROM sys.dm_exec_sessions WHERE session_id=@@SPID");
                samples.Add(new{warmup=n==0,transactionMs=watch.Elapsed.TotalMilliseconds,logBytes,cpuMs=Convert.ToInt64(end[0]["cpu_time"])-Convert.ToInt64(start[0]["cpu_time"]),logicalReads=Convert.ToInt64(end[0]["logical_reads"])-Convert.ToInt64(start[0]["logical_reads"]),sessionPageWrites=Convert.ToInt64(end[0]["writes"])-Convert.ToInt64(start[0]["writes"])});
            }
            result.Add(new{operation=name,mode,samples,scope="Single-row raw SQL mutation plus commit and transaction-log sampling; excludes domain/auth/idempotency/outbox and normalized-email identity index maintenance. These are projection maintenance comparisons, not full Contact command costs. Session CPU/reads/writes include diagnostic SQL; page writes are not log writes."});
            await Exec(c,$"DELETE contacts.Contacts WHERE ContactId=N'{id}'");
            await save($"{tier}-architecture-writes.json",result);
        }
        await Exec(c,$"ENABLE TRIGGER {Trigger} ON contacts.Contacts");
        await VerifyProjection(c);
    }

    static Task<List<Dictionary<string,object?>>> Storage(SqlConnection c)=>Rows(c,"SELECT OBJECT_SCHEMA_NAME(p.object_id) schemaName,OBJECT_NAME(p.object_id) tableName,i.name indexName,p.row_count,p.reserved_page_count*8.0 reservedKiB,p.used_page_count*8.0 usedKiB FROM sys.dm_db_partition_stats p JOIN sys.indexes i ON i.object_id=p.object_id AND i.index_id=p.index_id WHERE p.object_id IN (OBJECT_ID('contacts.Contacts'),OBJECT_ID('contacts.BenchmarkSearchProjection')); SELECT name,type_desc,size*8.0 allocatedKiB,FILEPROPERTY(name,'SpaceUsed')*8.0 usedKiB FROM sys.database_files; SELECT COUNT_BIG(*) tasks FROM tasks.Tasks; SELECT COUNT_BIG(*) contacts,COUNT(DISTINCT WorkspaceId) workspaces FROM contacts.Contacts");
    static async Task Exec(SqlConnection c,string sql,SqlTransaction? tx=null){using var cmd=new SqlCommand(sql,c,tx){CommandTimeout=60};await cmd.ExecuteNonQueryAsync();}
    static async Task<List<Dictionary<string,object?>>> Rows(SqlConnection c,string sql){using var cmd=new SqlCommand(sql,c){CommandTimeout=30};await using var r=await cmd.ExecuteReaderAsync();var list=new List<Dictionary<string,object?>>();do{while(await r.ReadAsync()){var row=new Dictionary<string,object?>();for(var i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);list.Add(row);}}while(await r.NextResultAsync());return list;}
}
