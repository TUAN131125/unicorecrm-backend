-- Deterministic synthetic-only fixture. @from/@to permit bounded incremental batches.
-- Workspace skew: 50% large, 30% across five medium, 20% across twenty small.
-- Independent moduli vary owners/status/profile/dates; equal sort values intentionally occur.
WITH digits AS (SELECT v FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(v))
SELECT @from + a.v + 10*b.v + 100*c.v + 1000*d.v AS n
INTO #seed FROM digits a CROSS JOIN digits b CROSS JOIN digits c CROSS JOIN digits d
WHERE @from + a.v + 10*b.v + 100*c.v + 1000*d.v <= @to;
ALTER TABLE #seed ADD ws nvarchar(128), id nvarchar(128), owner nvarchar(128);
UPDATE #seed SET ws = CASE WHEN n%10<5 THEN N'bench_large'
 WHEN n%10<8 THEN CONCAT(N'bench_medium_',(n/10)%5) ELSE CONCAT(N'bench_small_',(n/10)%20) END,
 id=CONCAT(N'contact_',RIGHT(CONCAT(N'00000000',n),8)), owner=CONCAT(N'member_',n%23);
INSERT contacts.Contacts (ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,ArchivedAt,Profile,NormalizedWorkEmail)
SELECT id,ws,owner,CONCAT(CASE WHEN n%997=0 THEN N'RareZebra ' ELSE N'Synthetic Contact ' END,n%2000),
 CASE WHEN n%29=0 THEN N'archived' WHEN n%7=0 THEN N'needs_follow_up' WHEN n%7=1 THEN N'in_consulting'
 WHEN n%7=2 THEN N'has_open_opportunity' WHEN n%7=3 THEN N'inactive' WHEN n%7=4 THEN N'do_not_contact' ELSE N'active' END,
 0,'2026-01-01',DATEADD(minute,n%10000,CAST('2026-10-01' AS datetimeoffset)),
 CASE WHEN n%29=0 THEN CAST('2026-10-08' AS datetimeoffset) END,
 CONCAT(N'{"source":"',CASE WHEN n%101=0 THEN N'rare_campaign' WHEN n%5=0 THEN N'web' WHEN n%5=1 THEN N'referral' ELSE N'import' END,
 N'","relationshipLevel":"',CASE WHEN n%3=0 THEN N'close' WHEN n%3=1 THEN N'known' ELSE N'new' END,
 N'","decisionRole":"',CASE WHEN n%11=0 THEN N'decision_maker' ELSE N'end_user' END,
 N'","doNotContact":',CASE WHEN n%17=0 THEN N'true' ELSE N'false' END,
 N',"workEmail":"synthetic',n,N'@example.invalid","displayName":"Synthetic ',n,N'","mobilePhone":"000',n,N'"}'),
 CONCAT(N'SYNTHETIC',n,N'@EXAMPLE.INVALID') FROM #seed;
INSERT contacts.CustomerRelationships
 (RelationshipId,WorkspaceId,ContactId,CustomerId,Role,EffectiveFrom,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy)
SELECT CONCAT(N'rel_',n),ws,id,CONCAT(N'customer_',n%1000),N'end_user','2026-01-01','2026-01-01',N'bench','2026-01-01',N'bench'
FROM #seed WHERE n%4=0;
INSERT tasks.Tasks (TaskId,WorkspaceId,Title,Status,Priority,AssigneeId,DueAt,RecordModuleKey,RecordId,CreatedAt,UpdatedAt,ArchivedAt,Version)
SELECT CONCAT(N'task_',s.n,N'_',v.k),s.ws,N'Synthetic follow up',CASE WHEN s.n%13=0 THEN 1 WHEN s.n%19=0 THEN 2 ELSE 0 END,
 1,CONCAT(N'member_',(s.n+v.k)%23),DATEADD(day,(s.n+v.k)%31-15,CAST('2026-10-08' AS datetimeoffset)),
 CASE WHEN s.n%37=0 THEN N'leads' ELSE N'contacts' END,s.id,'2026-01-01','2026-01-01',
 CASE WHEN s.n%41=0 THEN CAST('2026-10-08' AS datetimeoffset) END,0
FROM #seed s CROSS JOIN (VALUES(0),(1),(2),(3),(4)) v(k)
WHERE s.n%3<>0 AND (v.k<2 OR s.n%17=0);
INSERT leads.Leads
 (LeadId,WorkspaceId,Profile,ScopeOwnerId,SearchText,PhoneSearchText,WorkState,QualificationOutcome,Score,CreatedAt,UpdatedAt,ArchivedAt,Version)
SELECT CONCAT(N'lead_',RIGHT(CONCAT(N'00000000',n),8)),ws,
 CONCAT(N'{"displayName":"Synthetic Lead ',n,N'","ownerId":"',owner,N'","interestedProducts":[],"tags":[],"customFields":[]}'),
 owner,CONCAT(N'SYNTHETIC LEAD ',n),N'000000',n%4,CASE WHEN n%4=3 THEN 1 END,0,
 '2026-01-01',DATEADD(minute,n%10000,CAST('2026-10-01' AS datetimeoffset)),
 CASE WHEN n%29=0 THEN CAST('2026-10-08' AS datetimeoffset) END,0 FROM #seed;
DROP TABLE #seed;
