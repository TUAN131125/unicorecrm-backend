import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../frontend/unicorecrm-web");
const { chromium, expect } = createRequire(path.join(frontend, "package.json"))("@playwright/test");
const api = process.env.O2_API; const workspace = process.env.O2_WORKSPACE; const orderId = process.env.O2_ORDER;
const evidence = process.env.O2_EVIDENCE; const restart = process.env.O2_PHASE === "restart";
assert.match(api ?? "", /^http:\/\/127\.0\.0\.1:\d+$/u); assert.ok(workspace && orderId && evidence);
await mkdir(evidence, { recursive: true });
const browser = await chromium.launch(); const context = await browser.newContext(); const page = await context.newPage();
const requests = []; const errors = []; let status = "FAIL"; let lostAcknowledgment;
page.on("pageerror", error => errors.push(error.message));
page.on("response", response => { if (response.url().startsWith(api)) requests.push({ method: response.request().method(), path: new URL(response.url()).pathname, status: response.status() }); });
const route = suffix => `http://127.0.0.1:3057/#/w/${workspace}/crm/${suffix}`;
async function login() {
  await page.goto("http://127.0.0.1:3057/#/login");
  await page.waitForLoadState("networkidle");
  await page.getByLabel("Email", { exact: true }).fill(process.env.O2_EMAIL);
  await page.getByLabel(/^(Mật khẩu|Password)$/u).fill(process.env.O2_PASSWORD);
  await page.getByRole("button", { name: /^(Đăng nhập|Sign in)$/u }).click();
  await page.waitForURL(/#\/(?:select-workspace|w\/)/u);
}
async function openOrder() {
  await page.goto(route(`orders/${orderId}`));
  const loaded = page.waitForResponse(r => r.url() === `${api}/orders/${orderId}` && r.status() === 200);
  await page.reload();
  const order = await (await loaded).json();
  await expect(page.getByText(order.orderNumber, { exact: true }).first()).toBeVisible({ timeout: 30000 });
  return order;
}
try {
  await login(); const before = await openOrder(); const panel = page.locator('[data-order-confirmation="panel"]');
  await expect(panel).toBeVisible();
  if (!restart) {
    assert.equal(before.state, "DRAFT"); await expect(panel).toContainText("DRAFT");
    await page.screenshot({ path: path.join(evidence, "chromium-draft.png"), fullPage: true });
    await page.getByRole("button", { name: /^(Xác nhận đơn hàng|Xác nhận vận hành|Confirm Order|Confirm operationally)$/u }).first().click();
    const dialog = page.locator('[data-order-confirmation="dialog"]'); await expect(dialog).toBeVisible();
    const button = dialog.getByRole("button", { name: /Xác nhận Order & kích hoạt Payment Plan|Confirm Order & activate Payment Plan/u });
    await expect(button).toBeDisabled(); await dialog.getByRole("checkbox").check();
    await dialog.getByLabel(/Ghi chú kiểm tra vận hành|Operational review notes/u).fill("Real Chromium review: source, buyer, commercial snapshot, payment agreement and service fulfillment checked.");
    let first;
    await page.route(`${api}/workflows/order-confirmation/${orderId}/confirm-with-payment-plan`, async intercepted => {
      if(first){await intercepted.continue();return;}
      const upstream=await intercepted.fetch(); assert.equal(upstream.status(),200);first=await upstream.json();
      assert.equal(first.outcome,"COMMITTED");
      // The actual server committed, but Chromium receives a reset instead of the acknowledgment.
      await intercepted.abort("connectionreset");
    });
    const committed = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`/order-confirmation/${orderId}/confirm-with-payment-plan`));
    await button.click();const response=await committed;assert.equal(response.status(),200);const receipt=await response.json();
    assert.ok(first);assert.equal(first.result.orderState,"CONFIRMED");assert.equal(first.result.paymentPlanState,"ACTIVE");
    assert.equal(receipt.outcome,"REPLAYED");assert.equal(receipt.commandId,first.commandId);
    lostAcknowledgment={serverCommitted:true,chromiumAcknowledgmentDropped:true,recoveryMode:"AUTOMATIC_HTTP_RETRY",retryOutcome:receipt.outcome,commandId:receipt.commandId};
    const validation = await page.evaluate(async body => {
      const { validateOpenApiResponse } = await import("/src/platform/api/contracts/openApiRuntimeValidation.ts");
      return validateOpenApiResponse("confirmOrderWithPaymentPlan", body, 200);
    }, receipt);
    assert.equal(validation.valid, true, JSON.stringify(validation));
    await expect(dialog).not.toBeVisible(); await expect(panel).toContainText("ACTIVE");
    // A further exact HTTP replay also returns the same committed SQL receipt.
    const original = response.request(); const headers = original.headers();
    const replay = await page.request.post(original.url(), { headers: { authorization: headers.authorization, "X-Workspace-Id": headers["x-workspace-id"], "X-Request-Id": "o2-browser-lost-ack-retry", "X-Correlation-Id": "o2-browser-recovery", "Idempotency-Key": headers["idempotency-key"], "If-Match": headers["if-match"] }, data: original.postDataJSON() });
    assert.equal(replay.status(), 200); assert.equal((await replay.json()).outcome, "REPLAYED");
  } else { assert.equal(before.state, "CONFIRMED"); await expect(panel).toContainText("ACTIVE"); }
  await page.reload(); await expect(panel).toContainText("ACTIVE", { timeout: 30000 });
  await page.screenshot({ path: path.join(evidence, restart ? "chromium-restart.png" : "chromium-confirmed.png"), fullPage: true });
  const current = await openOrder(); assert.equal(current.state, "CONFIRMED"); assert.equal(current.acceptance.assuranceLevel, "ORDINARY_SALES_UNVERIFIED"); assert.equal(current.grandTotal.amount, "247500");
  await page.goto(route("orders")); await expect(page.getByText(current.orderNumber, { exact: true }).first()).toBeVisible();
  await page.locator("#user-profile-dropdown").click(); await page.getByRole("menuitem", { name: /^(Đăng xuất|Sign out)$/u }).click(); await expect(page).toHaveURL(/#\/login/u);
  await login(); assert.equal((await openOrder()).state, "CONFIRMED"); await expect(panel).toContainText("ACTIVE");
  assert.equal(errors.length, 0, "No browser runtime errors."); status = "PASS";
} finally {
  if (status !== "PASS") await page.screenshot({ path: path.join(evidence, "chromium-failure.png"), fullPage: true }).catch(() => {});
  await writeFile(path.join(evidence, restart ? "browser-restart.json" : "browser.json"), JSON.stringify({ status, orderId, lostAcknowledgment, requests, errors }, null, 2));
  await browser.close();
}
