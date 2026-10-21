from pathlib import Path
import json,hashlib,subprocess,shutil,difflib,re
r=Path('D:/Project_All/UnicoreCRM');back=r/'backend';front=r/'frontend/unicorecrm-web';repos={'backend':back,'frontend':front}
e=back/'docs/verification/evidence/p0-checkpoint-finalization-20261021'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
phases=['connected-crm-20261021','connected-crm-review-20261021','activity-performance-20261021','customer-task-ui-20261021','activity-write-contract-20261021']
files={}
for phase in phases:
    p=back/'docs/verification/evidence'/phase/'changes.json'
    if p.exists():
        for x in json.loads(p.read_text(encoding='utf-8-sig')):
            key=(x['repository'],x['file']);files.setdefault(key,[]).append(phase)
current={'backend':['scripts/verify-activity-security.ps1'],'frontend':['src/modules/deals/presentation/hooks/useDealDetailController.tsx','src/guidance/content/crm/extendedScreens.ts','tests/contract/deal-quote-transition-outcome.test.mts']}
for repo,paths in current.items():
    for p in paths:files.setdefault((repo,p),[]).append('p0-checkpoint-finalization-20261021')
generated=json.loads((back/'docs/verification/evidence/activity-write-contract-20261021/generated-output-allowlist.json').read_text())
for p in generated:files.setdefault(('frontend',p),[])
manifest=[];tracked={repo:set(subprocess.check_output(['git','ls-files'],cwd=d,text=True).splitlines()) for repo,d in repos.items()}
for (repo,p),sourcePhases in sorted(files.items()):
    f=repos[repo]/p;assert f.is_file(),f
    if p not in tracked[repo]:state='added'
    else:state='modified' if subprocess.run(['git','diff','--quiet','HEAD','--',p],cwd=repos[repo]).returncode else 'unchanged'
    manifest.append({'repository':repo,'file':p,'sha256':sha(f),'kind':'generated' if repo=='frontend' and p in generated else 'source_or_test','gitState':state,'phases':sourcePhases})
for repo,directory in repos.items():
    selected=[x['file'] for x in manifest if x['repository']==repo];existing=[p for p in selected if p in tracked[repo]]
    patch=subprocess.check_output(['git','diff','HEAD','--',*existing],cwd=directory)
    for p in selected:
        if p not in tracked[repo]:
            result=subprocess.run(['git','diff','--no-index','--','/dev/null',p],cwd=directory,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            assert result.returncode in (0,1),result.stderr;patch+=result.stdout
    (e/(repo+'-cumulative.patch')).write_bytes(patch)
    local=[]
    for p in current[repo]:
        before=e/'before'/repo/p
        a=before.read_text(encoding='utf-8-sig') if before.exists() else ''
        b=(directory/p).read_text(encoding='utf-8-sig')
        local.extend(difflib.unified_diff(a.splitlines(keepends=True),b.splitlines(keepends=True),fromfile='a/'+p if before.exists() else '/dev/null',tofile='b/'+p))
    (e/(repo+'-this-task.patch')).write_text(''.join(local),encoding='utf-8')
    (e/(repo+'-status-after.txt')).write_bytes(subprocess.check_output(['git','status','--short'],cwd=directory))
for run,label in [('6de52df30c624eb2a5fda1c1faa2efe2','security-performance'),('b06d50ebf40e46779ba8402eccbc0d14','connected-regression'),('b025ecf180ba470188a776aab066790e','ui-only')]:
    dest=e/label;dest.mkdir(exist_ok=True)
    for f in (back/'artifacts/activity-security'/run).iterdir():
        if f.is_file() and f.suffix in ('.json','.sql','.sqlplan'):shutil.copy2(f,dest/f.name)
    checks=json.loads((dest/'results.json').read_text(encoding='utf-8-sig'));assert all(x['status']=='PASS' for x in checks)
for folder,label in [('crm-ui-real','browser-ui'),('crm-real','browser-api')]:
    dest=e/label;dest.mkdir(exist_ok=True)
    for f in (front/'test-results'/folder).rglob('*'):
        if f.is_file() and f.suffix in ('.json','.png') and f.name!='.last-run.json':shutil.copy2(f,dest/f.name)
tools=e/'evidence-tools';tools.mkdir(exist_ok=True)
for name in ['build-compatibility-ledger.py','package-p0-cumulative-candidate.py']:
    shutil.copy2(r/'review-artifacts/connected-crm-20261021'/name,tools/name)
for name in ['checkpoint-security-quote.log','checkpoint-ui.log','checkpoint-connected.log']:shutil.copy2(back/'artifacts'/name,e/name)
immutable=[]
for p in ['docs/api/openapi-breaking-baseline.json','scripts/api/generate-openapi-client.mjs','scripts/api/openapi/breaking-changes.mjs','scripts/api/openapi/write-or-check.mjs']:
    immutable.append({'repository':'frontend','file':p,'sha256':sha(front/p)})
assert immutable[0]['sha256']=='c8e77d670453860796e3c32bcf17bc9ffbefe06bc0a8915d9509338bcabf66ef'
evidence=[];oldMismatch=[]
for phase in phases:
    folder=back/'docs/verification/evidence'/phase;index=folder/'artifact-hashes.json'
    if not index.exists():continue
    for row in json.loads(index.read_text(encoding='utf-8-sig')):
        file=folder/row['file']
        if sha(file)!=row['sha256']:oldMismatch.append(str(file))
    evidence.append({'repository':'backend','file':str(index.relative_to(back)).replace('\\','/'),'sha256':sha(index),'role':'historical evidence index; child hashes verified'})
assert not oldMismatch,oldMismatch
for f in sorted(e.rglob('*')):
    if f.is_file() and f.name not in ('cumulative-candidate-manifest.json','artifact-hashes.json'):
        evidence.append({'repository':'backend','file':str(f.relative_to(back)).replace('\\','/'),'sha256':sha(f),'role':'current evidence'})
for p in ['docs/verification/p0-conditional-checkpoint.md','docs/verification/f01-security-approval-proposal.md','docs/verification/activity-write-contract.md','docs/verification/customer-task-ui.md','docs/verification/activity-performance-and-lead-ui.md','docs/verification/connected-crm-p0-review.md']:
    f=back/p;evidence.append({'repository':'backend','file':p,'sha256':sha(f),'role':'report; historical reports retain their original verdict'})
migrations=[x for x in manifest if '/Migrations/' in x['file']];assert len(migrations)>=3
candidate={'status':'READY_FOR_CONDITIONAL_FUNCTIONAL_CHECKPOINT','proposalOnly':True,'userApprovalGranted':False,'productionReady':False,
'scope':'Paired current repos; Login/Workspace/Lead/Contact/Organization/Customer/Task/Activity/History. Quote mutations, TEAM positive support and production compatibility are not accepted.',
'baseCommits':{repo:subprocess.check_output(['git','rev-parse','HEAD'],cwd=d,text=True).strip() for repo,d in repos.items()},
'functionalVerification':{'securityPerformanceAssertions':580,'connectedAssertions':128,'uiOnlySteps':12,'dealQuoteController':'PASS in-process controlled owner port','quoteMutationSqlAcceptance':'BLOCKED: runtime routes missing'},
'compatibility':{'gate':'FAIL','intentionalF01':'APPROVAL_REQUESTED','preExistingFindings':42,'preExistingAccepted':0,'baselineChanged':False},
'waiversApproved':False,'commitsOrTagsCreated':False,'sourceFiles':manifest,'immutableReferences':immutable,'migrationFiles':migrations,'evidenceReferences':evidence,
'proposedCheckpointNames':{'backend':'connected-crm-p0-conditional-be','frontend':'connected-crm-p0-conditional-fe'},
'rollback':'Review SHA first, reverse only this-task patches to retain earlier secure P0/F01; never reset user work or roll back to vulnerable baseline HEAD. Keep additive SQL index and data.'}
write(e/'cumulative-candidate-manifest.json',candidate)
write(e/'changed-file-allowlist.json',[x for x in manifest if x['gitState']!='unchanged'])
write(e/'artifact-hashes.json',[{'file':str(f.relative_to(e)).replace('\\','/'),'sha256':sha(f)} for f in sorted(e.rglob('*')) if f.is_file() and f.name!='artifact-hashes.json'])
print('Cumulative files',len(manifest),'generated',sum(x['kind']=='generated' for x in manifest),'migration files',len(migrations),'historical child hash mismatches',len(oldMismatch),'evidence refs',len(evidence))
