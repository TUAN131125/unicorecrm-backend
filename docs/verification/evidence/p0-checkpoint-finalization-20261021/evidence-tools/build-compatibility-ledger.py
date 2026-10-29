from pathlib import Path
import json,re,hashlib
r=Path('D:/Project_All/UnicoreCRM');e=r/'backend/docs/verification/evidence/p0-checkpoint-finalization-20261021'
front=r/'frontend/unicorecrm-web'
impact=json.loads((r/'backend/docs/verification/evidence/activity-write-contract-20261021/compatibility-impact.json').read_text())
baseline=json.loads((front/'docs/api/openapi-breaking-baseline.json').read_text(encoding='utf-8-sig'))
spec=json.loads((front/'docs/api/openapi.json').read_text(encoding='utf-8-sig'));schemas=spec['components']['schemas']
old=baseline['schemas']
added=sorted(set(schemas['ErrorCode']['enum'])-set(old['ErrorCode']['enum']));removed=sorted(set(old['ErrorCode']['enum'])-set(schemas['ErrorCode']['enum']))
assert len(impact['before'])==42 and len(added)==13 and not removed
def resolved(s,all):
    while '$ref' in s:s=all[s['$ref'].split('/')[-1]]
    return s
def leaf(name,path,all):
    s=all[name]
    for token in re.findall(r'\.([A-Za-z0-9_]+)|(\[\])',path):
        s=resolved(s,all);s=s['items'] if token[1] else s['properties'][token[0]]
    return resolved(s,all)
def refs(s,seen=None):
    seen=set() if seen is None else seen
    if not isinstance(s,dict):return seen
    if '$ref' in s:
        name=s['$ref'].split('/')[-1]
        if name not in seen:
            seen.add(name)
            if name in schemas:refs(schemas[name],seen)
    for k,v in s.items():
        if k=='$ref':continue
        if isinstance(v,dict):refs(v,seen)
        elif isinstance(v,list):
            for x in v:
                if isinstance(x,dict):refs(x,seen)
    return seen
operations=[]
for path,methods in spec['paths'].items():
    for method,op in methods.items():
        if not isinstance(op,dict) or 'operationId' not in op:continue
        statuses={}
        for status,response in op.get('responses',{}).items():
            names=set()
            for media in response.get('content',{}).values():names|=refs(media.get('schema',{}))
            statuses[status]=names
        operations.append((op['operationId'],method.upper(),path,statuses))
rows=[]
for i,finding in enumerate(impact['before'],1):
    match=re.fullmatch(r'Schema (\w+) \[RESPONSE\](.*): enum domain is incompatible\.',finding);assert match,finding
    name,path=match.groups();before=leaf(name,path,old);after=leaf(name,path,schemas)
    assert before['enum']==old['ErrorCode']['enum'] and after['enum']==schemas['ErrorCode']['enum'],finding
    related=[{'operationId':id,'method':method,'path':route,'responseStatuses':[status for status,names in statuses.items() if name in names]} for id,method,route,statuses in operations if any(name in names for names in statuses.values())]
    direct=name=='ErrorCode'
    rows.append({'id':f'C{i:02}','finding':finding,'schema':name,'schemaPath':path or '(root)','direction':'RESPONSE','rootChange':'ErrorCode response enum widened by 13 values; no values removed','addedValues':added,'removedValues':removed,'operations':related,
    'realClientRisk':'An older generated/strict client may reject a new enum value instead of rendering the response or business error. Current regenerated client includes all 13 values. No real occurrence of AI_* in Quote/Order blockerCodes was demonstrated.',
    'connectedCrmP0Impact':('POTENTIAL: LEAD_CONVERSION_IN_PROGRESS may be returned by a concurrent Lead conversion; current client has an explicit recovery predicate. Existing successful journey and negative tests passed.' if direct else 'No direct successful Lead/Contact/Organization/Customer/Task response in the scoped P0 journey. This is a propagated Quote/Order/commercial action/credit blocker contract path, not a distinct observed P0 defect.'),
    'beforeCheckpoint':{'codeFixRequired':False,'conditionalReviewRequired':True,'condition':'Checkpoint is scoped to the current paired frontend/backend, not compatibility with historical clients. Explicit conditional approval is required; this row remains UNACCEPTED.'},
    'beforeProduction':{'decisionRequired':True,'action':'Inventory consumers, test unknown-enum handling, then explicitly approve response-enum policy/versioning or a reviewed baseline. Do not blanket-accept unrelated findings.'},
    'evidence':['docs/api/openapi-breaking-baseline.json#/schemas/ErrorCode','docs/api/openapi.json#/components/schemas/ErrorCode',f'docs/api/openapi.json#/components/schemas/{name}', 'src/platform/api/contracts/openApiRuntimeValidation.ts (enum validation)', 'src/workflows/lead-customer-conversion/application/composition/leadCustomerConversionApplicationServices.ts (specific in-progress recovery)' if direct else 'compatibility-impact.json: unchanged before/after F01 response finding'],
    'proposal':'Review once at shared ErrorCode root with all affected operation paths attached; do not edit these wrappers merely to reduce the count. Keep compatibility FAIL until approved policy is implemented.', 'approvalStatus':'UNACCEPTED'})
assert len({x['finding'] for x in rows})==42
ledger={'acceptedBaselineSha256':hashlib.sha256((front/'docs/api/openapi-breaking-baseline.json').read_bytes()).hexdigest(),'findings':rows,'summary':{'count':42,'direction':'all RESPONSE','distinctRootChanges':1,'rootSchema':'ErrorCode','addedEnumValues':added,'removedEnumValues':removed,'approvedFindings':0}}
(e/'compatibility-42-ledger.json').write_text(json.dumps(ledger,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
lines=['# Ledger 42 compatibility findings có trước F01','', '42 findings cùng xuất phát từ một widening của ErrorCode response enum: thêm 12 AI_* và LEAD_CONVERSION_IN_PROGRESS; không xóa giá trị. Mỗi dòng vẫn giữ nguyên finding và operation mapping riêng. Không có dòng nào được chấp nhận.','', '| ID | Schema / path | Operation mapping | P0 impact | Before checkpoint | Before production |','|---|---|---|---|---|---|']
for row in rows:
    ops=row['operations'];display=', '.join(x['operationId'] for x in ops) if len(ops)<=5 else ', '.join(x['operationId'] for x in ops[:5])+f'; +{len(ops)-5} (full JSON)'
    lines.append(f"| {row['id']} | {row['schema']}{row['schemaPath']} | {display or 'No reachable declared response (review required)'} | {'Potential conversion concurrency error' if row['schema']=='ErrorCode' else 'Commercial wrapper; outside scoped successful P0 path'} | Conditional review; no automatic code fix/acceptance | Consumer inventory + enum/version decision required |")
lines+=['','Full operation/method/path/status mappings, exact added values, client risk, evidence and proposed handling are in compatibility-42-ledger.json. Shared response enum risk applies to strict/exhaustive historical clients; no actual AI blocker response in Quote/Order is claimed.','', 'For the conditional functional checkpoint, paired-current-client behavior is distinguished from historical-client compatibility. All 42 remain unaccepted and must be decided before production release.']
(e/'compatibility-42-ledger.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('PASS ledger coverage:',len(rows),'findings; root changes:1; additions:',len(added));print('operation counts',sorted(set(len(x['operations']) for x in rows)))
