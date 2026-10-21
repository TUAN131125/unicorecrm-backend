"""Task-owned adapter around official ApiHost maintenance and public HTTP; no auth overrides."""
import datetime, hashlib, json, os, pathlib, secrets, socket, subprocess, time, urllib.request, urllib.error, uuid, sys
import argparse
parser=argparse.ArgumentParser()
parser.add_argument('--output',required=True)
parser.add_argument('--frontend',required=True)
options=parser.parse_args()
BE=pathlib.Path(__file__).resolve().parents[2]
ROOT=BE.parent
OUT=pathlib.Path(options.output).resolve()
OUT.mkdir(parents=True,exist_ok=False)
FE=pathlib.Path(options.frontend).resolve()
RUN=uuid.uuid4().hex; save_run={'runId':RUN}
(OUT/'run.json').write_text(json.dumps(save_run))
DB='UniCoreCRM_LeadTableCursor_'+RUN
assert len(RUN)==32 and uuid.UUID(hex=RUN).hex==RUN
assert not (OUT/'manifest.json').exists(), 'Use a fresh run manifest; never recreate a prior fixture'
MARKER='UniCoreCRM_ConnectedAcceptance_RunId'; SERVER='localhost'
DLL=BE/'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll'
def save(name,data): (OUT/name).write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding='utf-8')
def quote(value): return "N'"+str(value).replace("'","''")+"'"
def sql(query,database='master',json_result=True):
    # Credentials are Windows integrated. Only fixed master metadata or this run's DB is permitted.
    assert database in ('master',DB)
    p=subprocess.run(['sqlcmd','-S',SERVER,'-E','-C','-d',database,'-l','8','-t','60','-b','-y','0','-w','65535','-Q','SET NOCOUNT ON; '+query],capture_output=True,text=True)
    if p.returncode: raise RuntimeError('SQL operation failed: '+p.stdout.strip()+p.stderr.strip())
    value=p.stdout.strip()
    return (json.loads(''.join(value[value.index('['):].splitlines())) if value else []) if json_result else value
def rows(query,database=DB): return sql(query+' FOR JSON PATH, INCLUDE_NULL_VALUES',database)
def metadata():
    return rows("SELECT CONVERT(nvarchar(128),@@SERVERNAME) serverName,CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) edition,CONVERT(nvarchar(40),SERVERPROPERTY('ProductVersion')) version,CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultDataPath')) dataPath,CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultLogPath')) logPath,HAS_PERMS_BY_NAME(NULL,NULL,'CREATE ANY DATABASE') canCreate",'master')[0]
def guard():
    r=rows("SELECT available_physical_memory_kb ramKB FROM sys.dm_os_sys_memory",'master')[0]
    r.update(rows("SELECT MIN(v.available_bytes) diskBytes FROM sys.master_files f CROSS APPLY sys.dm_os_volume_stats(f.database_id,f.file_id) v",'master')[0])
    r.update(rows("SELECT COUNT(*) activeRequests FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id WHERE s.is_user_process=1 AND r.session_id<>@@SPID AND r.status IN ('running','runnable','suspended')",'master')[0])
    # Existing smallest SQL fixture guards, retained without lowering.
    if r['ramKB']<1024*1024 or r['diskBytes']<8*1024**3+10000*12000 or r['activeRequests']>8:
        raise RuntimeError('RESOURCE_LIMIT: '+json.dumps(r))
    return r
def files(): return rows('SELECT name,type_desc,physical_name FROM sys.master_files WHERE database_id=DB_ID('+quote(DB)+') ORDER BY file_id','master')
def verify_owned():
    assert metadata()['serverName']==manifest['serverName']
    assert rows('SELECT CONVERT(nvarchar(128),value) runId FROM sys.extended_properties WHERE class=0 AND name='+quote(MARKER))==[{'runId':RUN}]
    assert files()==manifest['files']
    for item in manifest['files']:
        parent=pathlib.PureWindowsPath(item['physical_name']).parent
        expected=pathlib.PureWindowsPath(manifest['logPath'] if item['type_desc']=='LOG' else manifest['dataPath'])
        assert parent==expected
    return True

passwords={name:secrets.token_urlsafe(28)+'Aa1!' for name in ['A_ADMIN','A_OWNER','A_LIMITED','A_FLS','B_ADMIN']}
tokens={}; identities={}; scenarios=[]; process=None; created=False
manifest={'runId':RUN,'purpose':'LEAD_TABLE_CONTEXT_BOUND_CURSOR_REMEDIATION','databaseName':DB,'sqlTarget':SERVER,'createdAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),'createdByTask':False,'cleanup':'NOT_CREATED','syntheticOnly':True,'processes':[]}
secret_values=list(passwords.values())
def redact(text):
    for value in secret_values: text=text.replace(value,'[REDACTED]')
    return text
env={k:v for k,v in os.environ.items() if not k.startswith(('ConnectionStrings__','IdentityAuth__','Workspace__DevelopmentBootstrap','AccessControl__DevelopmentBootstrap','DevelopmentDemoBootstrap','AI__DataProtection','UNICORE_DEV_SEED'))}
env.update({'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development','ConnectionStrings__UnicoreCRM':f'Server={SERVER};Database={DB};Integrated Security=True;TrustServerCertificate=True;Application Name=UniCoreCRM.ConnectedAcceptance.{RUN};Pooling=False',
 'AI__DataProtection__KeyRingPath':str(OUT/'private-keyring'),'Logging__LogLevel__Default':'Warning','Logging__LogLevel__Microsoft.EntityFrameworkCore':'Warning',
 'IdentityAuth__EmailVerification__Sender__Kind':'Unavailable','IdentityAuth__DevelopmentBootstrap__Enabled':'false','Workspace__DevelopmentBootstrap__Enabled':'false','AccessControl__DevelopmentBootstrap__Enabled':'false'})
for key in ['IdentityAuth__Jwt__SigningKey','IdentityAuth__RefreshTokenPepper','IdentityAuth__EmailVerification__CodePepper','IdentityAuth__EmailVerification__PayloadEncryptionKey']:
    value=secrets.token_hex(32); env[key]=value; secret_values.append(value)
hostroot=OUT/'host-content'; hostroot.mkdir(exist_ok=True)
# Only tracked credential-free base defaults, never staged/local Development settings.
base=subprocess.check_output(['git','show','HEAD:src/UnicoreCRM.ApiHost/appsettings.json'],cwd=BE)
(hostroot/'appsettings.json').write_bytes(base)
args=['dotnet',str(DLL),'--contentRoot',str(hostroot)]
caps=['workspace.context.resolve','access.read','access.configure','leads.read','leads.create','leads.update','leads.assign','leads.claim','leads.queue.read','leads.qualify','leads.delete','contacts.read','contacts.create','contacts.update','contacts.delete','organizations.read','organizations.create','organizations.update','customers.view','tasks.read','tasks.create','tasks.update','tasks.complete']
def maintenance(command,extra,log):
    p=subprocess.run(args+[command],env={**env,**extra},cwd=hostroot,capture_output=True,text=True,timeout=180)
    (OUT/log).write_text(redact(p.stdout+p.stderr),encoding='utf-8')
    assert p.returncode==0, 'Maintenance failed: '+log
def bootstrap(name):
    workspace='b' if name=='B_ADMIN' else 'a'
    email=name.lower()+'-'+RUN[:8]+'@example.test'
    extra={'IdentityAuth__DevelopmentBootstrap__Enabled':'true','IdentityAuth__DevelopmentBootstrap__Email':email,'IdentityAuth__DevelopmentBootstrap__Password':passwords[name], 'IdentityAuth__DevelopmentBootstrap__DisplayName':name,
        'Workspace__DevelopmentBootstrap__Enabled':'true','Workspace__DevelopmentBootstrap__IdentityEmail':email,
        'AccessControl__DevelopmentBootstrap__Enabled':'true','AccessControl__DevelopmentBootstrap__IdentityEmail':email,'AccessControl__DevelopmentBootstrap__WorkspaceKey':'acceptance-'+workspace+'-'+RUN[:8],'AccessControl__DevelopmentBootstrap__RoleName':name}
    for side,letter in [('MemberWorkspace',workspace),('NonMemberWorkspace','a' if workspace=='b' else 'b')]:
        prefix='Workspace__DevelopmentBootstrap__'+side+'__'
        extra.update({prefix+'Key':'acceptance-'+letter+'-'+RUN[:8],prefix+'Name':'Acceptance '+letter.upper(),prefix+'LogoText':letter.upper(),prefix+'BaseCurrency':'VND'})
        for i,module in enumerate(['leads','contacts','customers','organizations','tasks','deals','products']): extra[prefix+'EnabledModuleKeys__'+str(i)]=module
        for i,cap in enumerate(caps): extra[prefix+'Capabilities__'+str(i)]=cap
    for i,cap in enumerate(caps if name!='A_LIMITED' else ['workspace.context.resolve','access.read','leads.read','contacts.read']): extra['AccessControl__DevelopmentBootstrap__Capabilities__'+str(i)]=cap
    maintenance('--seed-demo',extra,'bootstrap-'+name+'.log')
    identities[name]={'email':email,'workspaceLetter':workspace}
    guard()
def request(name,method,path,body=None,key=None,version=None,workspace=None):
    global last_http
    headers={'X-Request-Id':'acceptance-'+uuid.uuid4().hex,'X-Correlation-Id':'acceptance-'+uuid.uuid4().hex}
    if name: headers.update({'Authorization':'Bearer '+tokens[name],'X-Workspace-Id':workspace or identities[name]['workspaceId']})
    if key: headers['Idempotency-Key']=key
    if version is not None: headers['If-Match']='"'+str(version)+'"'
    data=json.dumps(body).encode() if body is not None else None
    if data is not None: headers['Content-Type']='application/json'
    try:
        with urllib.request.urlopen(urllib.request.Request(baseurl+path,data,headers,method=method),timeout=30) as response:
            result=json.loads(response.read() or b'{}'); last_http={'method':method,'path':path.split('?')[0],'status':response.status}; return response.status,result
    except urllib.error.HTTPError as e:
        result=json.loads(e.read() or b'{}'); last_http={'method':method,'path':path.split('?')[0],'status':e.code,'code':result.get('code')}; return e.code,result
def check(id,name,expected,fn,input_category,db_verification='NOT_RUN'):
    global last_http
    last_http={}
    try:
        actual=fn(); status='PASS'; detail=actual
    except Exception as e:
        status='FAIL'; detail={'exception':type(e).__name__,'message':redact(str(e)),'lastObservedHTTP':last_http}; db_verification='NOT_COMPLETED'
    row={'scenarioId':id,'executionLevel':'LEVEL_3_REAL_ASPNET_SQL_HTTP','backendSha':manifest['backendSha'],'frontendSha':manifest['frontendSha'],'fixtureRunId':RUN,'testPrincipal':name,'workspaceIdentity':identities.get(name,{}).get('workspaceId'),'inputCategory':input_category,'expectedResult':expected,'actualResult':detail,'databaseVerification':db_verification,'browserVerification':'NOT_RUN','status':status,'evidencePath':str(OUT/'scenarios.json')}
    row['httpStatus']=detail.get('httpStatus',detail.get('lastObservedHTTP',{}).get('status','NOT_ESTABLISHED'))
    scenarios.append(row); save('scenarios.json',scenarios); print(id+': '+status,flush=True)
    return status
try:
    meta=metadata(); resources=guard()
    for value in [meta['dataPath'],meta['logPath']]:
        path=pathlib.PureWindowsPath(value)
        assert path.is_absolute() and 'Microsoft SQL Server' in value and path.name.upper()=='DATA' and path!=path.parent
    assert meta['canCreate']==1 and 'Developer' in meta['edition']
    assert not rows('SELECT name FROM sys.databases WHERE name='+quote(DB),'master')
    manifest.update(meta); manifest['serverName']=meta['serverName']; manifest['resources']=resources
    manifest['backendSha']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=BE,text=True).strip(); manifest['frontendSha']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=FE,text=True).strip()
    manifest['initialDatabases']=rows('SELECT name,database_id,create_date FROM sys.databases ORDER BY database_id','master')
    save('manifest.json',manifest)
    save('environment-preflight.json',{'SQL_INSTANCE_IDENTIFIED':'YES','SQL_MANAGED_DATA_PATH_VERIFIED':'YES','SQL_MANAGED_LOG_PATH_VERIFIED':'YES','ISOLATED_DATABASE_CAPABILITY':'YES','AUTHENTICATED_TEST_IDENTITIES_AVAILABLE':'YES_OFFICIAL_BOOTSTRAP_CAPABILITY','WORKSPACE_FIXTURE_CAPABILITY':'YES','RESOURCE_HEADROOM':'SUFFICIENT','SAFE_EXECUTION':'ADMITTED','resourceGuards':{'availableRamMiB':1024,'diskReserveGiB':8,'rowAllowanceBytes':120000000,'otherActiveRequestsMax':8},'environment':meta,'resources':resources})
    sql('CREATE DATABASE ['+DB+']',json_result=False); created=True; manifest['createdByTask']=True
    sql('EXEC sys.sp_addextendedproperty @name='+quote(MARKER)+',@value='+quote(RUN),DB,False)
    manifest['files']=files(); manifest['cleanup']='PENDING'; save('manifest.json',manifest); verify_owned()
    maintenance('--migrate',{},'migrations.log')
    for name in passwords: bootstrap(name)
    with socket.socket() as sock: sock.bind(('127.0.0.1',0)); port=sock.getsockname()[1]
    baseurl='http://127.0.0.1:'+str(port)
    with socket.socket() as sock: sock.bind(('127.0.0.1',0)); frontend_port=sock.getsockname()[1]
    frontend_url='http://127.0.0.1:'+str(frontend_port)
    env['Frontend__AllowedOrigins__0']=frontend_url
    log=(OUT/'api-host.log').open('w',encoding='utf-8')
    process=subprocess.Popen(args+['--urls',baseurl],env=env,cwd=hostroot,stdout=log,stderr=subprocess.STDOUT)
    manifest['processes'].append({'pid':process.pid,'kind':'REAL_APIHOST','url':baseurl,'owned':True}); save('manifest.json',manifest)
    for _ in range(100):
        if process.poll() is not None: raise RuntimeError('ApiHost stopped before ready')
        try: request(None,'GET','/auth/session'); break
        except urllib.error.URLError: time.sleep(.2)
    else: raise RuntimeError('ApiHost readiness timed out')
    for name,identity in identities.items():
        status,result=request(None,'POST','/auth/sessions',{'email':identity['email'],'password':passwords[name]},'signin-'+uuid.uuid4().hex)
        assert status==200, 'Real sign-in refused: '+name+' '+str(status)+' '+str(result.get('code'))
        tokens[name]=result['accessToken']; secret_values.append(tokens[name])
        ref=rows('SELECT w.WorkspaceId,m.MembershipId,m.MemberId,m.AccountId FROM workspace.Workspaces w JOIN workspace.Memberships m ON w.WorkspaceId=m.WorkspaceId JOIN iam.Accounts a ON a.AccountId=m.AccountId WHERE a.NormalizedEmail='+quote(identity['email'].upper()))
        assert len(ref)==1; identity.update({k[0].lower()+k[1:]:v for k,v in ref[0].items()})
    save('identities.json',{k:{a:b for a,b in v.items() if a!='email'} for k,v in identities.items()})
    import public_scenarios
    public_scenarios.run(sys.modules[__name__])
    save('execution-summary.json',{'status':'PASS' if scenarios and all(s['status']=='PASS' for s in scenarios) else 'FAIL','total':len(scenarios),'passed':sum(s['status']=='PASS' for s in scenarios),'failed':sum(s['status']=='FAIL' for s in scenarios),'realAspnet':'RUN','realSql':'USED'})
except Exception as failure:
    save('execution-error.json',{'status':'BLOCKED','exception':type(failure).__name__,'reason':redact(str(failure))})
    print('EXECUTION_BLOCKED: '+redact(str(failure)),flush=True)
finally:
    if process:
        process.terminate()
        try: process.wait(timeout=25)
        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
        manifest['processes'][0]['exited']=True
        log.close()
        (OUT/'api-host.log').write_text(redact((OUT/'api-host.log').read_text(encoding='utf-8')),encoding='utf-8')
    if created:
        try:
            verify_owned()
            active=rows('SELECT COUNT(*) sessions FROM sys.dm_exec_sessions WHERE database_id=DB_ID('+quote(DB)+')','master')[0]['sessions']
            assert active==0, 'Refusing DROP with active sessions: '+str(active)
            sql('DROP DATABASE ['+DB+']',json_result=False)
            assert not rows('SELECT name FROM sys.databases WHERE name='+quote(DB),'master')
            manifest['cleanup']='CLEANED'
            remaining=rows('SELECT name,database_id,create_date FROM sys.databases ORDER BY database_id','master')
            assert remaining==manifest['initialDatabases'], 'Unrelated database metadata changed'
            manifest['initialDatabasesPreserved']=True
        except Exception as cleanup_error:
            manifest['cleanup']='BLOCKED'; manifest['cleanupReason']=redact(str(cleanup_error))
    save('manifest.json',manifest)
    print('DATABASE_CLEANUP: '+manifest['cleanup'],flush=True)

if manifest['cleanup']!='CLEANED' or (OUT/'execution-error.json').exists() or not scenarios or any(s['status']!='PASS' for s in scenarios): sys.exit(1)
