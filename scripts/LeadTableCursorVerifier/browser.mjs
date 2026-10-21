import {createRequire} from 'node:module';
import fs from 'node:fs';
import assert from 'node:assert/strict';
import path from 'node:path';
const e=process.env, require=createRequire(path.join(e.CURSOR_FE,'package.json'));
const {chromium}=require('@playwright/test');
const browser=await chromium.launch({headless:true});
const context=await browser.newContext({baseURL:e.CURSOR_WEB});
const page=await context.newPage(); page.setDefaultTimeout(25000);
const observations=[];
function wait(predicate){const pending=page.waitForResponse(r=>{const u=new URL(r.url());return u.origin===e.CURSOR_API&&u.pathname==='/leads'&&predicate(u,r);});pending.catch(()=>{});return pending;}
async function visible(body){for(const lead of body.items)await page.getByText(lead.displayName,{exact:true}).first().waitFor({state:'visible'});}
let token;
async function api(method,url,body,version){
 const response=await fetch(e.CURSOR_API+url,{method,headers:{Authorization:'Bearer '+token,'X-Workspace-Id':e.CURSOR_WORKSPACE_ID,'X-Request-Id':crypto.randomUUID(),'X-Correlation-Id':crypto.randomUUID(),...(body?{'Content-Type':'application/json','Idempotency-Key':crypto.randomUUID()}:{}),...(version?{'If-Match':'"'+version+'"'}:{})},...(body?{body:JSON.stringify(body)}:{})});
 assert.equal(response.status,200);return response.json();
}
async function phonePolicy(hidden){
 const d=await api('GET','/access/directory');const r=d.roles.find(r=>r.name==='A_ADMIN');
 await api('PUT','/access/roles/'+r.roleId,{name:r.name,isActive:true,capabilities:r.capabilities,dataScopes:['leads','contacts','tasks','organizations','customers'].map(resourceKey=>({resourceKey,scope:'WORKSPACE'})),fieldSecurity:hidden?[{resourceKey:'leads',fieldKey:'phone',access:'HIDDEN'}]:[]},r.version);
}
try{
 await page.goto('/#/login');await page.getByLabel('Email',{exact:true}).fill(e.CURSOR_EMAIL);await page.getByLabel(/Mật khẩu|Password/,{exact:true}).fill(e.CURSOR_PASSWORD);
 await page.getByRole('button',{name:/Đăng nhập|Sign in/,exact:true}).click();await page.waitForURL(new RegExp('#/w/'+e.CURSOR_WORKSPACE_KEY+'/crm/'),{timeout:45000});
 const route='/#/w/'+e.CURSOR_WORKSPACE_KEY+'/crm/leads';
 let pending=wait(u=>!u.searchParams.has('cursor'));await page.goto(route);let response=await pending;assert.equal(response.status(),200);let first=await response.json();assert.equal(first.items.length,50);assert.equal(first.pageInfo.totalCount,120);await visible(first);observations.push('first page 50/120');
 pending=wait(u=>u.searchParams.has('cursor'));await page.getByRole('button',{name:/Trang sau|Next page/,exact:true}).click();response=await pending;assert.equal(response.status(),200);const second=await response.json();assert.equal(second.items.length,50);assert.equal(new URL(response.url()).searchParams.get('cursor'),first.pageInfo.nextCursor);await visible(second);assert.equal(await page.getByText(first.items[0].displayName,{exact:true}).count(),0);observations.push('next page uses opaque cursor and clears previous rows');
 const search=page.getByPlaceholder(/Tìm kiếm nhanh tên|Quick search name/).filter({visible:true});
 pending=wait(u=>u.searchParams.get('search')==='Cursor proof 00'&&!u.searchParams.has('cursor'));await search.fill('Cursor proof 00');response=await pending;assert.equal(response.status(),200);let filtered=await response.json();assert.equal(filtered.pageInfo.totalCount,10);await visible(filtered);observations.push('query change resets cursor');
 pending=wait(u=>!u.searchParams.has('search')&&!u.searchParams.has('cursor'));await search.fill('');response=await pending;first=await response.json();await visible(first);observations.push('clear search recovers first page');
 const sign=await fetch(e.CURSOR_API+'/auth/sessions',{method:'POST',headers:{'Content-Type':'application/json','Idempotency-Key':crypto.randomUUID(),'X-Request-Id':crypto.randomUUID(),'X-Correlation-Id':crypto.randomUUID()},body:JSON.stringify({email:e.CURSOR_EMAIL,password:e.CURSOR_PASSWORD})});assert.equal(sign.status,200);token=(await sign.json()).accessToken;
 await phonePolicy(true);
 try{
   pending=wait(u=>u.searchParams.has('cursor'));await page.getByRole('button',{name:/Trang sau|Next page/,exact:true}).click();response=await pending;assert.equal(response.status(),422);assert.equal((await response.json()).code,'VALIDATION_FAILED');
   await page.locator('[data-list-state="error"]').waitFor({state:'visible'});assert.equal(await page.getByText(first.items[0].displayName,{exact:true}).count(),0);observations.push('invalid context receives 422; stale rows cleared and typed error shown');
 }finally{await phonePolicy(false);}
 pending=wait(u=>u.searchParams.get('search')==='Cursor proof'&&!u.searchParams.has('cursor'));await search.fill('Cursor proof');response=await pending;assert.equal(response.status(),200);await visible(await response.json());observations.push('explicit query reset recovers from invalid cursor');
 pending=wait(u=>!u.searchParams.has('search')&&!u.searchParams.has('cursor'));await search.fill('');response=await pending;assert.equal(response.status(),200);await visible(await response.json());
 pending=wait(u=>!u.searchParams.has('cursor'));await page.getByRole('button',{name:/Làm mới danh sách khách hàng tiềm năng|Refresh Lead list/,exact:true}).click();response=await pending;assert.equal(response.status(),200);const recovered=await response.json();assert.equal(recovered.items.length,50);await visible(recovered);observations.push('refresh obtains fresh first-page token');
 await page.screenshot({path:path.join(e.CURSOR_OUTPUT,'lead-table.png')});
 fs.writeFileSync(path.join(e.CURSOR_OUTPUT,'browser-summary.json'),JSON.stringify({status:'PASS',httpStatus:200,browserVersion:browser.version(),observations,realAuthentication:true,realBackend:true,mockedResponses:false},null,2));
}catch(error){await page.screenshot({path:path.join(e.CURSOR_OUTPUT,'browser-failure.png')});console.error(String(error.message).replaceAll(e.CURSOR_PASSWORD,'[REDACTED]'));process.exitCode=1;}
finally{await context.close();await browser.close();}
