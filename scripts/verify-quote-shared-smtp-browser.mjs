import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../frontend/unicorecrm-web");
const { chromium, expect } = createRequire(path.join(frontend, "package.json"))("@playwright/test");
const env = process.env;
const evidence = env.ACCEPTANCE_TEST_EVIDENCE;
const api = env.ACCEPTANCE_TEST_API;
assert.match(api ?? "", /^http:\/\/127\.0\.0\.1:\d+$/u);
assert.ok(evidence);
await mkdir(evidence, { recursive: true });
const browser = await chromium.launch();
const errors = [], requests = [], checks = [];
let status = "FAIL", staffOrderId;
const route = suffix => `http://127.0.0.1:3057/#/w/${env.ACCEPTANCE_TEST_WORKSPACE}/crm/${suffix}`;
async function shot(page, name) { await page.screenshot({ path: path.join(evidence, `${name}.png`), fullPage: true }); }
function monitor(page) {
  page.on("pageerror", e => errors.push({ name: e.name || "Error" }));
  page.on("response", r => { if (r.url().startsWith(api) && /\/(quotes|orders|payment-plans)/u.test(new URL(r.url()).pathname)) requests.push({ method: r.request().method(), path: new URL(r.url()).pathname, status: r.status() }); });
}
async function login(page) {
  await page.goto("http://127.0.0.1:3057/#/login");
  await page.getByLabel("Email", { exact: true }).fill(env.ACCEPTANCE_TEST_EMAIL);
  await page.getByLabel(/^(Mật khẩu|Password)$/u).fill(env.ACCEPTANCE_TEST_PASSWORD);
  const response = page.waitForResponse(r => r.url().endsWith("/auth/sessions") && r.request().method() === "POST");
  await page.getByRole("button", { name: /^(Đăng nhập|Sign in)$/u }).click();
  const body = await (await response).json();
  await page.waitForURL(/#\/(?:select-workspace|w\/)/u);
  return body.accessToken;
}
async function inspectOrder(page, id, name) {
  const response = page.waitForResponse(r => r.url() === `${api}/orders/${id}` && r.status() === 200);
  await page.goto(route(`orders/${id}`));
  const order = await (await response).json();
  assert.equal(order.state, "CONFIRMED"); assert.equal(order.confirmationOrigin, "WORKSPACE_POLICY");
  assert.equal(order.grandTotal.amount, "247500");
  await expect(page.getByText(order.orderNumber, { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/Đã xác nhận tự động theo chính sách Workspace|Automatically confirmed by Workspace policy/u)).toBeVisible();
  await expect(page.locator('[data-order-confirmation="panel"]')).toContainText(/Đang hiệu lực|Active/u);
  await expect(page.locator('[data-order-confirmation="panel"]').getByRole("button", { name: /Xác nhận|Confirm/u })).toHaveCount(0);
  await shot(page, name);
  await page.reload(); await expect(page.getByText(order.orderNumber, { exact: true }).first()).toBeVisible();
  checks.push(`${name}: confirmed origin and commercial data survive refresh`);
}
try {
  const beforeServer=spawn(process.execPath,[path.resolve(frontend,"../../backend/scripts/run-quote-review-vite.mjs")],{cwd:frontend,env:process.env,stdio:"ignore",windowsHide:true});
  try {
    for(let n=0;n<80;n++){try{if((await fetch("http://127.0.0.1:3058")).ok)break;}catch{}await new Promise(resolve=>setTimeout(resolve,500));}
    const before=await browser.newPage();
    await before.goto("http://127.0.0.1:3058/#/login");
    await before.getByLabel("Email",{exact:true}).fill(env.ACCEPTANCE_TEST_EMAIL);
    await before.getByLabel(/^(Mật khẩu|Password)$/u).fill(env.ACCEPTANCE_TEST_PASSWORD);
    await before.getByRole("button",{name:/^(Đăng nhập|Sign in)$/u}).click();
    await before.waitForURL(/#\/(?:select-workspace|w\/)/u);
    await before.goto(`http://127.0.0.1:3058/#/w/${env.ACCEPTANCE_TEST_WORKSPACE}/crm/quotes/${env.ACCEPTANCE_TEST_CUSTOMER_QUOTE}`);
    await expect(before.locator('[data-quote-workflow="unified"]')).toBeVisible({timeout:30000});
    for(const width of [1440,1280,768,390]){
      await before.setViewportSize({width,height:1050});await shot(before,`before-${width}`);
      await before.locator('[data-quote-workflow="unified"]').scrollIntoViewIfNeeded();await shot(before,`before-workflow-${width}`);
    }
    await before.close();
  } finally {beforeServer.kill();}
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
  const page = await context.newPage(); monitor(page); const accessToken = await login(page);
  await page.goto(route(`quotes/${env.ACCEPTANCE_TEST_MISSING_DOCUMENT_QUOTE}`));
  await expect(page.locator("#quote-detail-header").getByRole("button",{name:/^(Phát hành báo giá|Issue quote)$/u})).toBeVisible({timeout:30000});
  await expect(page.locator('[data-workflow-step="0"]')).toHaveAttribute("aria-current","step");
  await shot(page,"manual-sent-missing-document-recovery");
  await page.goto(route(`quotes/${env.ACCEPTANCE_TEST_STAFF_QUOTE}`));
  const header = page.locator("#quote-detail-header");
  try {await expect(header.getByRole("button", { name: /^(Phát hành báo giá|Issue quote)$/u })).toBeVisible({ timeout: 30000 });}
  catch(error){
    const diagnostic=await page.evaluate(async id=>{
      const module=await import("/src/modules/quotes/application/vertical-slice/quoteAuthoritativeQueries.ts");
      const state=module.getQuoteDetailResource(id).getSnapshot();
      return {state:state.state,code:state.error?.code,message:state.error?.message,details:state.error?.details};
    },env.ACCEPTANCE_TEST_STAFF_QUOTE);
    await writeFile(path.join(evidence,"quote-resource-error.json"),JSON.stringify(diagnostic,null,2));throw error;
  }
  assert.equal(await page.getByRole("button", { name: /^(Gửi duyệt|Request approval)$/u }).count(), 0);
  assert.equal(await header.locator("button.bg-violet-600").count(), 1, "One primary header button");
  const typography = await header.getByRole("heading").first().evaluate(el => ({ size: getComputedStyle(el).fontSize, weight: getComputedStyle(el).fontWeight }));
  assert.deepEqual(typography, { size: "24px", weight: "600" });
  await shot(page, "quote-draft-desktop"); checks.push("ordinary draft has direct issuance and 24px semibold title");
  await header.getByRole("button", { name: /^(Phát hành báo giá|Issue quote)$/u }).click();
  const issue = page.getByRole("dialog").filter({ hasText: /Phát hành PDF chính thức|Issue official PDF/u });
  await expect(issue).toBeVisible();
  const issued = page.waitForResponse(r => r.url().endsWith("/documents/issue") && r.request().method() === "POST");
  await issue.getByRole("button", { name: /^(Xác nhận phát hành|Confirm issue)$/u }).click();
  const issuance = await issued; assert.equal(issuance.status(), 201); const result = await issuance.json();
  await expect(issue).not.toBeVisible();
  await expect(header.getByRole("button", { name: /^(Gửi qua Gmail|Send via Gmail)$/u })).toBeVisible({ timeout: 30000 });
  await shot(page, "quote-issued-desktop");
  const downloaded=page.waitForEvent("download");
  await page.locator('[data-quote-documents="panel"]').getByRole("button",{name:/^(Tải PDF đã phát hành|Download issued PDF)$/u}).click();
  const pdf=await downloaded;assert.equal(await pdf.failure(),null);
  await pdf.saveAs(path.join(evidence,"issued-quote.pdf"));
  assert.equal(await page.locator('[data-quote-workflow="unified"] details').count(),0);
  assert.equal(await page.locator('[data-workflow-step]').count(),4);
  for(const width of [1440,1280,768,390]){
    await page.setViewportSize({width,height:1050});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`No overflow at ${width}`);
    await shot(page,`after-${width}`);
    const workflow=page.locator('[data-quote-workflow="unified"]');
    await workflow.scrollIntoViewIfNeeded();await shot(page,`after-workflow-${width}`);
    const ai=await page.getByRole("button",{name:/Mở trợ lý AI|Open AI assistant/u}).boundingBox();
    const send=await workflow.getByRole("button",{name:/^(Gửi qua Gmail|Send via Gmail)$/u}).boundingBox();
    if(ai&&send)assert.ok(ai.x+ai.width<=send.x||send.x+send.width<=ai.x||ai.y+ai.height<=send.y||send.y+send.height<=ai.y,"AI button does not cover Gmail action");
  }
  await page.setViewportSize({width:1440,height:1050});
  await header.getByRole("button",{name:/^(Gửi qua Gmail|Send via Gmail)$/u}).click();
  const sendDialog=page.getByRole("dialog").filter({hasText:/Gửi báo giá|Send quote/u});
  await expect(sendDialog).toBeVisible();
  const gmailResult=page.waitForResponse(r=>r.url().endsWith("/acceptance-invitations")&&r.request().method()==="POST");
  await sendDialog.getByRole("button",{name:/^(Gửi báo giá qua Gmail|Send quote via Gmail)$/u}).click();
  assert.equal((await gmailResult).status(),200);
  await expect(page.getByText(/Gửi thất bại|Sending failed/u).first()).toBeVisible();
  await expect(header.getByRole("button",{name:/^(Gửi qua Gmail|Send via Gmail)$/u})).toBeVisible();
  checks.push("Gmail action before SENT uses real operation; isolated loopback refusal never marks SENT");
  const quote = result.result.quote;
  const headers = { Authorization: `Bearer ${accessToken}`, "X-Workspace-Id": env.ACCEPTANCE_TEST_WORKSPACE_ID, "Idempotency-Key": "browser-actual-delivery-0001", "If-Match": `"${quote.resourceVersion}"`, "X-Request-Id": "browser-actual-delivery-0001" };
  const delivered = await page.request.post(`${api}/quotes/${quote.id}/send`, { headers, data: { deliveryId: "browser-delivery-0001", channel: "EMAIL", evidenceType: "USER_CONFIRMED_SENT", recipientEmail: "customer@example.test" } });
  assert.equal(delivered.status(), 200); await page.reload();
  await expect(page.locator('[data-workflow-step="1"]').getByRole("button",{name:/^(Gửi qua Gmail|Send via Gmail)$/u})).toHaveCount(1);
  await expect(header.getByRole("button", { name: /^(Ghi nhận khách đã chấp nhận|Record customer acceptance)$/u })).toBeVisible({ timeout: 30000 });
  await shot(page, "quote-sent-desktop");
  await header.getByRole("button", { name: /^(Ghi nhận khách đã chấp nhận|Record customer acceptance)$/u }).click();
  const dialog = page.getByRole("dialog").filter({ hasText: /Ghi nhận khách đã chấp nhận|Record customer acceptance/u });
  await expect(dialog).toBeVisible();
  const rect=await dialog.boundingBox();assert.ok(rect);
  assert.ok(Math.abs(rect.x+rect.width/2-720)<8,"Modal centered horizontally");
  assert.ok(Math.abs(rect.y+rect.height/2-525)<40,"Modal centered vertically");
  await dialog.getByRole("button",{name:/^(Lưu chấp nhận|Save acceptance)$/u}).click();
  await expect(dialog.locator('[aria-invalid="true"]')).toHaveCount(3);
  assert.equal(await dialog.locator('[role="alert"]').count(),3);
  await shot(page,"staff-validation-fields");
  await dialog.getByLabel(/Thời gian khách hàng đồng ý|Customer agreement time/u).fill(new Date(Date.now() - 120000).toISOString().slice(0, 16));
  await dialog.getByLabel(/Ghi chú \/ bằng chứng|Notes \/ evidence/u).fill("Customer agreed by phone. Connected Chromium staff attestation.");
  await dialog.getByRole("checkbox").check(); await shot(page, "staff-acceptance-dialog");
  await dialog.getByLabel(/Thời gian khách hàng đồng ý|Customer agreement time/u).fill(new Date(Date.now()+172800000).toISOString().slice(0,16));
  const rejected=page.waitForResponse(r=>r.url().endsWith("/sales-acceptances")&&r.request().method()==="POST");
  await dialog.getByRole("button",{name:/^(Lưu chấp nhận|Save acceptance)$/u}).click();
  assert.equal((await rejected).status(),422);
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel(/Ghi chú \/ bằng chứng|Notes \/ evidence/u)).toHaveValue("Customer agreed by phone. Connected Chromium staff attestation.");
  await expect(dialog.getByLabel(/Thời gian khách hàng đồng ý|Customer agreement time/u)).toHaveAttribute("aria-invalid","true");
  await shot(page,"staff-server-validation-preserves-data");
  await dialog.getByLabel(/Thời gian khách hàng đồng ý|Customer agreement time/u).fill(new Date(Date.now()-120000).toISOString().slice(0,16));
  const saved = page.waitForResponse(r => r.url().endsWith("/sales-acceptances") && r.request().method() === "POST");
  await dialog.getByRole("button", { name: /^(Lưu chấp nhận|Save acceptance)$/u }).click();
  assert.equal((await saved).status(), 200); await expect(dialog).not.toBeVisible();
  await shot(page, "quote-accepted-processing");
  const orderLink = header.getByRole("link", { name: /^(Xem đơn hàng|View Order)$/u });
  await expect(orderLink).toBeVisible({ timeout: 60000 });
  await expect(header.getByText(/^(Đã chấp nhận|Accepted)$/u)).toBeVisible();
  await expect(page.locator('[data-quote-documents="panel"]').getByRole("button", { name: /^(Phát hành PDF|Issue PDF)$/u })).toHaveCount(0);
  staffOrderId = (await orderLink.getAttribute("href")).split("/").at(-1);
  await shot(page, "quote-confirmed-order-desktop");
  for (const [name, width] of [["tablet", 820], ["mobile", 390]]) {
    await page.setViewportSize({ width, height: 900 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} page has no horizontal overflow`);
    await shot(page, `quote-confirmed-order-${name}`);
  }
  await page.setViewportSize({ width: 1440, height: 1050 });
  await inspectOrder(page, staffOrderId, "staff-confirmed-order");
  const publicContext = await browser.newContext(); const customer = await publicContext.newPage(); monitor(customer);
  await customer.goto(`http://127.0.0.1:3057/#/quote-acceptance/${env.ACCEPTANCE_TEST_TOKEN}`);
  await expect(customer.getByRole("checkbox")).toBeVisible({ timeout: 30000 });
  await shot(customer, "customer-public-preview");
  await customer.getByRole("checkbox").check();
  const consent = customer.waitForResponse(r => r.url().endsWith("/public/quote-acceptance/consent"));
  await customer.getByRole("button", { name: /^(Chấp nhận báo giá|Accept quotation)$/u }).click();
  assert.equal((await consent).status(), 200); await shot(customer, "customer-public-consented");
  await publicContext.close();
  await page.goto(route(`quotes/${env.ACCEPTANCE_TEST_CUSTOMER_QUOTE}`));
  const customerOrderLink = page.locator("#quote-detail-header").getByRole("link", { name: /^(Xem đơn hàng|View Order)$/u });
  await expect(customerOrderLink).toBeVisible({ timeout: 60000 });
  const customerOrderId = (await customerOrderLink.getAttribute("href")).split("/").at(-1);
  await inspectOrder(page, customerOrderId, "customer-confirmed-order");
  await page.locator("#user-profile-dropdown").click(); await page.getByRole("menuitem", { name: /^(Đăng xuất|Sign out)$/u }).click();
  await expect(page).toHaveURL(/#\/login/u); await login(page);
  await inspectOrder(page, staffOrderId, "relogin-confirmed-order");
  assert.equal(errors.length, 0, JSON.stringify(errors));
  assert.equal(requests.some(r => r.method === "POST" && /confirm-with-payment-plan|^\/orders$/u.test(r.path)), false);
  checks.push("staff and customer connected flow, no manual confirmation request, responsive, refresh and relogin");
  status = "PASS";
} catch {
  // Playwright call logs can echo filled credentials or a token-bearing URL.
  throw new Error("CONNECTED_QUOTE_BROWSER_FAILED; inspect bounded result and screenshots.");
} finally {
  if (status !== "PASS") for (const [i, context] of browser.contexts().entries()) for (const [j, page] of context.pages().entries()) await shot(page, `failure-${i}-${j}`).catch(() => {});
  await writeFile(path.join(evidence, "result.json"), JSON.stringify({ status, staffOrderId, checks, requests, errors, liveGmail: "NOT_RUN" }, null, 2));
  await browser.close();
}
