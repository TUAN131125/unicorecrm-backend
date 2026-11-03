import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../frontend/unicorecrm-web");
const { chromium, expect } = createRequire(path.join(frontend, "package.json"))("@playwright/test");
const api = process.env.UNICORECRM_ORDER_TEST_API;
const workspace = process.env.UNICORECRM_ORDER_TEST_WORKSPACE;
const evidence = process.env.UNICORECRM_ORDER_TEST_EVIDENCE;
assert.match(api ?? "", /^http:\/\/127\.0\.0\.1:\d+$/u);
assert.ok(workspace && evidence && process.env.UNICORECRM_ORDER_TEST_PASSWORD);
await mkdir(evidence, { recursive: true });
const browser = await chromium.launch(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
  ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {});
const requests = [];
const pending = [];
const diagnostics = [];
const route = suffix => `http://127.0.0.1:3049/#/w/${workspace}/crm/${suffix}`;
let status = "FAIL";
try {
  // Two independent authenticated sessions: no copied browser state or mocked responses.
  for (const phase of ["initial", "relogin"]) {
    const context = await browser.newContext();
    const page = await context.newPage();
    page.on("pageerror", error => diagnostics.push(error.message));
    page.on("console", message => { if (message.type() === "error") diagnostics.push(message.text()); });
    page.on("response", response => {
      if (response.url().startsWith(`${api}/orders`)) pending.push((async () => requests.push({ phase,
        method: response.request().method(), path: new URL(response.url()).pathname, status: response.status(),
        body: await response.json().catch(() => null) }))());
    });
    await page.goto("http://127.0.0.1:3049/#/login", { timeout: 60_000 });
    await page.getByLabel("Email", { exact: true }).fill(process.env.UNICORECRM_ORDER_TEST_EMAIL);
    await page.getByLabel(/^(Mật khẩu|Password)$/u).fill(process.env.UNICORECRM_ORDER_TEST_PASSWORD);
    await page.getByRole("button", { name: /^(Đăng nhập|Sign in)$/u }).click();
    await page.waitForURL(/#\/(?:select-workspace|w\/)/u, { timeout: 30_000 });
    await page.goto(route("orders"));
    try {
      await expect(page.getByText("ORD-2026-0001", { exact: true }).first()).toBeVisible({ timeout: 30_000 });
    } catch (error) {
      await page.screenshot({ path: path.join(evidence, `${phase}-failure.png`), fullPage: true });
      throw error;
    }
    await page.screenshot({ path: path.join(evidence, `${phase}-list.png`), fullPage: true });
    await page.goto(route("orders/order_read_core_a"));
    await expect(page.getByText("ORD-2026-0001", { exact: true }).first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("Historical Product Order Snapshot", { exact: true }).first()).toBeVisible();
    await page.reload();
    await expect(page.getByText("ORD-2026-0001", { exact: true }).first()).toBeVisible({ timeout: 30_000 });
    await page.screenshot({ path: path.join(evidence, `${phase}-detail-refresh.png`), fullPage: true });
    await context.close();
  }
  assert.ok(requests.some(row => row.path === "/orders" && row.status === 200));
  assert.ok(requests.filter(row => row.path === "/orders/order_read_core_a" && row.status === 200).length >= 4);
  assert.ok(requests.every(row => row.method === "GET"));
  status = "PASS";
} finally {
  await Promise.all(pending);
  await writeFile(path.join(evidence, "result.json"), JSON.stringify({ status, fixtureOnly: true, requests, diagnostics }, null, 2));
  await browser.close();
}
