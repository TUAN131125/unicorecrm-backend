"""Public cursor regression: real sessions, public commands, SQL read-back. No auth substitutes."""
import urllib.parse, uuid, base64

def run(h):
    def request(name,method,path,body=None,version=None):
        return h.request(name,method,path,body,'cursor-'+uuid.uuid4().hex if body is not None else None,version)
    def role(name,scope='WORKSPACE',fields=None):
        status,d=request(name,'GET','/access/directory'); assert status==200
        r=next(r for r in d['roles'] if r['name']==name)
        body={'name':name,'isActive':True,'capabilities':r['capabilities'],'dataScopes':[{'resourceKey':k,'scope':scope} for k in ['leads','contacts','tasks','organizations','customers']],'fieldSecurity':fields or []}
        status,_=request(name,'PUT','/access/roles/'+r['roleId'],body,r['version']); assert status==200
    for name in ['A_ADMIN','A_OWNER','B_ADMIN']: role(name)
    for name,count in [('A_ADMIN',120),('B_ADMIN',2)]:
        for i in range(count):
            status,response=request(name,'POST','/leads',{'displayName':f'Cursor proof {i:03}','email':f'cursor-{i}@example.test','phone':'0901234567','ownerId':h.identities[name]['memberId'],'interestedProducts':[],'tags':[]})
            assert status==201 and response['outcome']=='COMMITTED'
    def get(name='A_ADMIN',**query): return request(name,'GET','/leads?'+urllib.parse.urlencode(query))
    def check(id,fn,name='A_ADMIN'): h.check(id,name,'Cursor contract enforced',fn,'SYNTHETIC','PASS_READ_ONLY_SQL_ASSERTIONS' if id in ['T01-NORMAL-PAGINATION','KEYSET-EQUAL-TIMESTAMP-TIEBREAKER'] else 'NOT_RUN')
    def reject(cursor,name='A_ADMIN',**query):
        status,d=get(name,cursor=cursor,limit=50,**query)
        assert status==422 and d.get('code')=='VALIDATION_FAILED', f'Expected 422 VALIDATION_FAILED, got {status} {d.get("code")}'
        return {'httpStatus':status,'code':d['code']}
    status,first=get(limit=50); assert status==200
    cursor=first['pageInfo']['nextCursor']; assert cursor
    def pages():
        collected=[]; lengths=[]; token=None
        for _ in range(3):
            status,d=get(limit=50,**({'cursor':token} if token else {})); assert status==200
            assert d['pageInfo']['totalCount']==120
            collected.extend(i['id'] for i in d['items']); lengths.append(len(d['items'])); token=d['pageInfo'].get('nextCursor')
        assert lengths==[50,50,20] and token is None and len(set(collected))==120
        stored=h.rows('SELECT LeadId FROM leads.Leads WHERE WorkspaceId='+h.quote(h.identities['A_ADMIN']['workspaceId'])+' ORDER BY UpdatedAt DESC,LeadId DESC')
        assert collected==[r['LeadId'] for r in stored]
        return {'httpStatus':200,'lengths':lengths,'uniqueRows':len(set(collected)),'exactCount':120,'orderMatchesSQL':True}
    check('T01-NORMAL-PAGINATION',pages)
    check('T02-FOREIGN-WORKSPACE',lambda:reject(cursor,'B_ADMIN'),'B_ADMIN')
    check('T03-TAMPER',lambda:reject(cursor[:12]+('A' if cursor[12]!='A' else 'B')+cursor[13:]))
    for label,value in [('EMPTY',''),('MALFORMED','%%%'),('TRUNCATED',cursor[:20]),('OVERSIZED','a'*4097)]:
        check('T04-'+label,lambda value=value:reject(value))
    for label,query in [('SEARCH',{'search':'proof'}),('WORKSTATE',{'workState':'NEW'}),('OWNER',{'ownerId':h.identities['A_ADMIN']['memberId']}),('ASSIGNMENT',{'assignmentState':'ASSIGNED'})]:
        check('CONTEXT-'+label,lambda query=query:reject(cursor,**query))
    def canonical():
        status,d=get(limit=50,search='  CURSOR proof  '); assert status==200
        status,e=get(limit=50,search='cursor PROOF',cursor=d['pageInfo']['nextCursor']); assert status==200 and len(e['items'])==50
        return {'httpStatus':200,'canonicalSearchContinuation':True}
    check('SEARCH-CANONICALIZATION',canonical)
    def admitted_filters():
        for query,count in [({'workState':'NEW'},120),({'ownerId':h.identities['A_ADMIN']['memberId']},120),({'assignmentState':'ASSIGNED'},120),({'assignmentState':'UNASSIGNED'},0),({'workState':'CONTACTING'},0),({'search':'NONEXISTENTZZZ'},0),({'search':'0901234567'},120)]:
            status,d=get(limit=50,**query); assert status==200 and d['pageInfo']['totalCount']==count
            if d['pageInfo']['hasNextPage']:
                status,e=get(limit=50,cursor=d['pageInfo']['nextCursor'],**query); assert status==200 and len(e['items'])==50 and e['pageInfo']['totalCount']==count
        return {'httpStatus':200,'admittedFilterCases':7,'filteredContinuation':True}
    check('ADMITTED-SEARCH-FILTER-REGRESSION',admitted_filters)
    def equivalent_principal():
        status,d=get('A_OWNER',limit=50,cursor=cursor); assert status==200 and len(d['items'])==50
        return {'httpStatus':200,'equivalentEffectiveAuthority':True}
    check('T11-EQUIVALENT-PRINCIPAL',equivalent_principal,'A_OWNER')
    def changed_limit():
        status,d=get(limit=25,cursor=cursor); assert status==200 and len(d['items'])==25 and d['pageInfo']['totalCount']==120
        status,e=get(limit=45,cursor=d['pageInfo']['nextCursor']); assert status==200 and len(e['items'])==45 and not e['pageInfo']['hasNextPage']
        ids=[i['id'] for i in first['items']+d['items']+e['items']]; assert len(set(ids))==120
        return {'httpStatus':200,'pageLengths':[50,25,45],'uniqueRows':120}
    check('T12-PAGE-SIZE-CHANGE',changed_limit)
    # A syntactically valid legacy keyset, not merely malformed input.
    row=h.rows('SELECT TOP(1) LeadId,UpdatedAt FROM leads.Leads WHERE WorkspaceId='+h.quote(h.identities['A_ADMIN']['workspaceId'])+' ORDER BY UpdatedAt DESC,LeadId DESC')[0]
    import datetime
    date=datetime.datetime.fromisoformat(row['UpdatedAt']); ticks=int((date.replace(tzinfo=None)-datetime.datetime(1,1,1)).total_seconds()*10000000)
    legacy=base64.urlsafe_b64encode(f'{ticks}\n{row["LeadId"]}'.encode()).decode().rstrip('=')
    check('T13-LEGACY',lambda:reject(legacy))
    def scope_change():
        role('A_ADMIN','OWN')
        try: return reject(cursor)
        finally: role('A_ADMIN')
    check('T09-EFFECTIVE-SCOPE-CHANGE',scope_change)
    def phone_change():
        role('A_ADMIN',fields=[{'resourceKey':'leads','fieldKey':'phone','access':'HIDDEN'}])
        try:
            rejection=reject(cursor)
            status,d=get(search='0901234567',limit=50); assert status==200 and d['pageInfo']['totalCount']==0
            status,d=get(limit=50); assert status==200 and all(i.get('phone') is None for i in d['items'])
            rejection['hiddenPhoneSearchCount']=0; return rejection
        finally: role('A_ADMIN')
    check('T10-PHONE-READABILITY',phone_change)
    def denied_capability():
        status,d=request('A_ADMIN','GET','/access/directory'); assert status==200
        r=next(r for r in d['roles'] if r['name']=='A_LIMITED')
        status,_=request('A_ADMIN','PUT','/access/roles/'+r['roleId'],{'name':r['name'],'isActive':True,'capabilities':[c for c in r['capabilities'] if c!='leads.read'],'dataScopes':[],'fieldSecurity':[]},r['version']); assert status==200
        status,d=get('A_LIMITED',limit=50,cursor=cursor); assert status==403
        return {'httpStatus':status,'code':d.get('code')}
    check('T15-CAPABILITY-DENIED',denied_capability,'A_LIMITED')
    def unauthorized():
        status,d=request(None,'GET','/leads?'+urllib.parse.urlencode({'cursor':cursor})); assert status==401
        return {'httpStatus':status,'code':d.get('code')}
    check('T15-UNAUTHENTICATED',unauthorized)
    def adjacent_kanban():
        status,d=request('A_ADMIN','GET','/leads/kanban/NEW?limit=50'); assert status==200 and d['pageInfo']['totalCount']==120
        status,e=request('A_ADMIN','GET','/leads/kanban/NEW?'+urllib.parse.urlencode({'limit':50,'cursor':d['pageInfo']['nextCursor']})); assert status==200 and len(e['items'])==50 and e['pageInfo']['totalCount']==120
        status,empty=request('A_ADMIN','GET','/leads/kanban/CONTACTING?limit=50'); assert status==200 and empty['pageInfo']['totalCount']==0
        reject(d['pageInfo']['nextCursor'])
        status,e=request('B_ADMIN','GET','/leads/kanban/NEW?'+urllib.parse.urlencode({'limit':50,'cursor':d['pageInfo']['nextCursor']})); assert status==422
        return {'httpStatus':200,'independentColumnCount':120,'emptyOtherColumn':True,'foreignWorkspaceRejected':True,'wrongTablePurposeRejected':True}
    check('ADJACENT-KANBAN',adjacent_kanban)
    def adjacent_contact():
        for i in range(2):
            status,d=request('A_ADMIN','POST','/contacts',{'fullName':f'Cursor Contact {i}','workEmail':f'contact-{i}@example.test','ownerId':h.identities['A_ADMIN']['memberId']}); assert status==201, f'Contact fixture {status}: {d.get("code")} {d.get("fieldErrors")}'
        status,d=request('A_ADMIN','GET','/contacts?limit=1'); assert status==200 and d['pageInfo']['totalCount']==2
        status,e=request('A_ADMIN','GET','/contacts?'+urllib.parse.urlencode({'limit':1,'cursor':d['pageInfo']['nextCursor']})); assert status==200 and len(e['items'])==1 and e['pageInfo']['totalCount']==2
        assert d['items'][0]['id']!=e['items'][0]['id']
        status,e=request('B_ADMIN','GET','/contacts?'+urllib.parse.urlencode({'limit':1,'cursor':d['pageInfo']['nextCursor']})); assert status==422
        reject(d['pageInfo']['nextCursor'])
        return {'httpStatus':200,'exactCount':2,'foreignWorkspaceRejected':True,'wrongTablePurposeRejected':True}
    check('ADJACENT-CONTACT',adjacent_contact)
    def tied_timestamps():
        h.verify_owned()
        h.sql('UPDATE leads.Leads SET UpdatedAt=CONVERT(datetimeoffset,\'2026-10-11T00:00:00+00:00\') WHERE WorkspaceId='+h.quote(h.identities['A_ADMIN']['workspaceId']),h.DB,False)
        result=pages(); result['equalTimestampFixture']=True; return result
    check('KEYSET-EQUAL-TIMESTAMP-TIEBREAKER',tied_timestamps)
    import browser_acceptance
    browser_acceptance.run(h)
    h.verify_owned(); h.guard()
