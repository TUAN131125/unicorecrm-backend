import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir, writeFile, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../frontend/unicorecrm-web");
const { chromium, expect } = createRequire(path.join(frontend, "package.json"))("@playwright/test");
const api = process.env.ACCEPTANCE_TEST_API;
const workspace = process.env.ACCEPTANCE_TEST_WORKSPACE;
const token = process.env.ACCEPTANCE_TEST_TOKEN;
const evidence = process.env.ACCEPTANCE_TEST_EVIDENCE;
const restart = process.env.ACCEPTANCE_TEST_PHASE === "restart";
assert.match(api ?? "", /^http:\/\/127\.0\.0\.1:\d+$/u);
assert.ok(workspace && evidence);
await mkdir(evidence, { recursive: true });
const browser = await chromium.launch(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {});
const requests = []; const errors = []; let status = "FAIL"; let staffOrderId = ""; let customerOrderId = "";
const route = suffix => `http://127.0.0.1:3057/#/w/${workspace}/crm/${suffix}`;
function monitor(page) {
  page.on("pageerror", error => errors.push(error.message.replaceAll(token || "[no-token]", "[REDACTED]")));
  page.on("response", response => {
    if (response.url().startsWith(api) && /\/(quotes|orders|public\/quote-acceptance)/u.test(new URL(response.url()).pathname)) requests.push({ method: response.request().method(), path: new URL(response.url()).pathname, status: response.status() });
  });
}
async function login(page) {
  await page.goto("http://127.0.0.1:3057/#/login");
  await page.getByLabel("Email", { exact: true }).fill(process.env.ACCEPTANCE_TEST_EMAIL);
  await page.getByLabel(/^(Mật khẩu|Password)$/u).fill(process.env.ACCEPTANCE_TEST_PASSWORD);
  await page.getByRole("button", { name: /^(Đăng nhập|Sign in)$/u }).click();
  await page.waitForURL(/#\/(?:select-workspace|w\/)/u);
}
async function inspectOrder(page, id, source, phase) {
  const response = page.waitForResponse(r => r.url() === `${api}/orders/${id}` && r.status() === 200);
  await page.goto(route(`orders/${id}`)); const order = await (await response).json();
  await expect(page.getByText(order.orderNumber, { exact: true }).first()).toBeVisible({ timeout: 30000 });
  await expect(page.locator('[data-order-acceptance="summary"]')).toContainText(source);
  await expect(page.getByText("Acceptance test buyer", { exact: true }).first()).toBeVisible();
  assert.equal(order.state, "DRAFT"); assert.equal(order.grandTotal.amount, "247500");
  assert.ok(order.paymentAgreementSnapshot.lines.length); assert.equal(order.acceptance.assuranceLevel, "ORDINARY_SALES_UNVERIFIED");
  await page.reload(); await expect(page.getByText(order.orderNumber, { exact: true }).first()).toBeVisible({ timeout: 30000 });
  await page.screenshot({ path: path.join(evidence, `${phase}-${source}-order.png`), fullPage: true });
  await page.goto(route("orders")); await expect(page.getByText(order.orderNumber, { exact: true }).first()).toBeVisible({ timeout: 30000 });
  return order;
}
try {
  if (!restart) {
    assert.match(token ?? "", /^[A-F0-9]{64}$/u);
    const customer = await browser.newContext(); const page = await customer.newPage(); monitor(page);
    await page.goto(`http://127.0.0.1:3057/#/quote-acceptance/${token}`);
    await expect(page.getByRole("heading", { name: process.env.ACCEPTANCE_TEST_QUOTE_NUMBER, exact: true })).toBeVisible({ timeout: 30000 });
    assert.equal(requests.filter(r => r.path.endsWith("/consent")).length, 0, "Opening the link must not consent.");
    await page.screenshot({ path: path.join(evidence, "customer-preview.png"), fullPage: true });
    await page.getByRole("checkbox").check();
    const submitted = page.waitForResponse(r => r.url() === `${api}/public/quote-acceptance/consent`);
    await page.getByRole("button", { name: /^(Chấp nhận báo giá|Accept quotation)$/u }).click();
    const response = await submitted; assert.equal(response.status(), 200);
    const receipt = await response.json(); assert.equal(receipt.source, "CUSTOMER_LINK");
    const validation = await page.evaluate(async body => {
      const { validateOpenApiResponse } = await import("/src/platform/api/contracts/openApiRuntimeValidation.ts");
      return validateOpenApiResponse("consentCustomerQuoteAcceptance", body, 200);
    }, receipt);
    assert.equal(validation.valid, true, JSON.stringify(validation));
    await expect(page.getByRole("heading", { name: /Đã ghi nhận chấp nhận báo giá|Quotation acceptance recorded/u })).toBeVisible();
    await page.screenshot({ path: path.join(evidence, "customer-consented.png"), fullPage: true });
    // Exercise actual API retry after an ambiguous client outcome using the original request key.
    const original = response.request(); const headers = { "X-Request-Id": "browser-consent-retry", "Idempotency-Key": original.headers()["idempotency-key"] };
    const replay = await page.request.post(`${api}/public/quote-acceptance/consent`, { headers, data: original.postDataJSON() });
    assert.equal(replay.status(), 200); assert.equal((await replay.json()).acceptanceId, receipt.acceptanceId);
    const used = await page.request.post(`${api}/public/quote-acceptance/consent`, { headers: { ...headers, "Idempotency-Key": "browser-different-consent-intent" }, data: original.postDataJSON() });
    assert.equal(used.status(), 409);
    await customer.close();
    const employee = await browser.newContext(); const staff = await employee.newPage(); monitor(staff); await login(staff);
    await staff.goto(route(`quotes/${process.env.ACCEPTANCE_TEST_STAFF_QUOTE}`));
    const panel = staff.locator('[data-quote-sales-acceptance="panel"]'); await expect(panel).toBeVisible();
    await panel.getByRole("button", { name: /^(Ghi nhận khách hàng đã chấp nhận|Record customer acceptance)$/u }).click();
    await panel.getByLabel(/Thời gian khách hàng đồng ý|Customer agreement time/u).fill(new Date(Date.now() - 120000).toISOString().slice(0, 16));
    await panel.getByLabel(/Ghi chú \/ bằng chứng|Notes \/ evidence/u).fill("Customer agreed by phone; real Chromium staff attestation.");
    await panel.getByRole("checkbox").check();
    const saved = staff.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/sales-acceptances"));
    await panel.getByRole("button", { name: /Lưu chấp nhận & tạo Order nháp|Save acceptance & create draft Order/u }).click();
    assert.equal((await saved).status(), 200);
    const link = panel.getByRole("link", { name: /Mở Order đã tạo|Open created Order/u }); await expect(link).toBeVisible({ timeout: 45000 });
    staffOrderId = (await link.getAttribute("href")).split("/").at(-1);
    await inspectOrder(staff, staffOrderId, "STAFF_RECORDED", "initial");
    await staff.goto(route(`quotes/${process.env.ACCEPTANCE_TEST_CUSTOMER_QUOTE}`));
    const customerLink = staff.locator('[data-quote-sales-acceptance="panel"]').getByRole("link", { name: /Mở Order đã tạo|Open created Order/u });
    await expect(customerLink).toBeVisible({ timeout: 45000 }); customerOrderId = (await customerLink.getAttribute("href")).split("/").at(-1);
    await inspectOrder(staff, customerOrderId, "CUSTOMER_LINK", "initial");
    await staff.locator("#user-profile-dropdown").click(); await staff.getByRole("menuitem", { name: /^(Đăng xuất|Sign out)$/u }).click();
    await expect(staff).toHaveURL(/#\/login/u); await login(staff);
    await inspectOrder(staff, staffOrderId, "STAFF_RECORDED", "relogin"); await employee.close();
  } else {
    const previous = JSON.parse(await readFile(path.join(evidence, "result.json"), "utf8")); staffOrderId = previous.staffOrderId; customerOrderId = previous.customerOrderId;
    const context = await browser.newContext(); const page = await context.newPage(); monitor(page); await login(page);
    await inspectOrder(page, staffOrderId, "STAFF_RECORDED", "restart"); await inspectOrder(page, customerOrderId, "CUSTOMER_LINK", "restart"); await context.close();
  }
  assert.equal(errors.length, 0, "No browser page errors.");
  assert.ok(!requests.some(r => r.method === "POST" && r.path === "/orders"), "Acceptance must not use direct Order authoring.");
  status = "PASS";
} finally {
  if (status !== "PASS") {
    for (const [index, context] of browser.contexts().entries()) for (const [pageIndex, page] of context.pages().entries()) {
      await page.screenshot({ path: path.join(evidence, `failure-${index}-${pageIndex}.png`), fullPage: true }).catch(() => {});
    }
  }
  await writeFile(path.join(evidence, restart ? "restart-result.json" : "result.json"), JSON.stringify({ status, staffOrderId, customerOrderId, requests, errors, realProviderEmail: "NOT_RUN" }, null, 2));
  await browser.close();
}
