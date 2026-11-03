import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../frontend/unicorecrm-web");
const { chromium, expect } = createRequire(path.join(frontend, "package.json"))("@playwright/test");
const env = process.env, before = env.LOADING_TEST_PHASE === "Before";
const evidence = env.LOADING_TEST_EVIDENCE, api = env.LOADING_TEST_API;
assert.equal(api, "http://127.0.0.1:5098");
assert.ok(evidence && env.LOADING_TEST_WORKSPACE);
await mkdir(evidence, { recursive: true });
const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
const page = await context.newPage(), requests = [], errors = [], results = [];
let status = "FAIL";
page.on("pageerror", error => errors.push(error.message));
page.on("response", r => { if (r.url().startsWith(api) && /\/(quotes|orders)(?:[/?]|$)/u.test(r.url())) requests.push({ method: r.request().method(), path: new URL(r.url()).pathname + new URL(r.url()).search, status: r.status() }); });
const route = suffix => `http://127.0.0.1:3048/#/w/${env.LOADING_TEST_WORKSPACE}/crm/${suffix}`;
async function login() {
  await page.goto("http://127.0.0.1:3048/#/login");
  await page.getByLabel("Email", { exact: true }).fill(env.LOADING_TEST_EMAIL);
  await page.getByLabel(/^(Mật khẩu|Password)$/u).fill(env.LOADING_TEST_PASSWORD);
  await page.getByRole("button", { name: /^(Đăng nhập|Sign in)$/u }).click();
  await page.waitForURL(/#\/(?:select-workspace|w\/)/u);
}
async function exerciseFilters(module, body) {
  const search = page.getByPlaceholder(module === "quotes" ? /Tìm mã hoặc tên báo giá|Search quote number or title/u : /Tìm theo số đơn|Search order number/u);
  const term = module === "quotes" && body.items.length ? body.items[0].quoteNumber : "loading-read-verification";
  let response = page.waitForResponse(r => { const url = new URL(r.url()); return r.url().startsWith(`${api}/${module}?`) && url.searchParams.get("search") === term && r.status() === 200; });
  await search.fill(term); await response;
  response = page.waitForResponse(r => r.url().startsWith(`${api}/${module}?`) && !new URL(r.url()).searchParams.has("search") && r.status() === 200);
  await search.fill(""); await response;
  await page.getByRole("button", { name: /Bộ lọc|Filters/u }).first().click();
  const select = page.locator("select").filter({ has: page.locator('option[value="DRAFT"]') }).first();
  response = page.waitForResponse(r => r.url().startsWith(`${api}/${module}?`) && /(?:status|state)=DRAFT/u.test(r.url()) && r.status() === 200);
  await select.selectOption("DRAFT"); await response;
  response = page.waitForResponse(r => r.url().startsWith(`${api}/${module}?`) && !/(?:status|state)=DRAFT/u.test(r.url()) && r.status() === 200);
  await select.selectOption(module === "quotes" ? "" : "all"); await response;
  await page.keyboard.press("Escape");
  if (module === "quotes" && body.items.length) {
    const size = page.getByRole("combobox", { name: /Số báo giá mỗi trang|quotes per page/u });
    response = page.waitForResponse(r => r.url().startsWith(`${api}/quotes?`) && new URL(r.url()).searchParams.get("limit") === "50" && r.status() === 200);
    await size.selectOption("50"); await response;
    await expect(page.getByRole("button", { name: /^(Trang sau|Next page)$/u })).toBeDisabled();
  }
  results.push({ module, search: "PASS", statusFilter: "PASS", pageSize: module === "quotes" ? "PASS" : "EMPTY_NOT_APPLICABLE" });
}
async function inspect(module, label) {
  const response = page.waitForResponse(r => r.url().startsWith(`${api}/${module}?`) && r.request().method() === "GET", { timeout: 45000 });
  await page.goto(route(module)); const http = await response;
  assert.equal(http.status(), before ? 500 : 200);
  const body = await http.json();
  if (before) {
    await expect(page.getByText(/Không thể tải|Unable to load|could not be loaded/u).first()).toBeVisible({ timeout: 20000 });
  } else {
    await expect(page.locator(module === "quotes" ? "#quote-list-page" : "#order-list-page")).toBeVisible({ timeout: 30000 });
    assert.ok(Array.isArray(body.items));
    await expect(page.getByText(/Không thể tải danh sách|Không thể tải dữ liệu mới nhất/u)).toHaveCount(0);
  }
  await page.screenshot({ path: path.join(evidence, `${label}-${module}.png`), fullPage: true });
  if (!before && label === "initial") await exerciseFilters(module, body);
  if (!before && body.items.length) {
    const first = body.items[0];
    const detail = page.waitForResponse(r => r.url() === `${api}/${module}/${first.id}` && r.status() === 200);
    await page.goto(route(`${module}/${first.id}`)); await detail;
    await expect(page.locator(module === "quotes" ? "#quote-detail-header" : "#order-detail-header")).toBeVisible({ timeout: 30000 });
    await page.screenshot({ path: path.join(evidence, `${label}-${module}-detail.png`), fullPage: true });
    await page.goto(route(module));
    results.push({ module, detail: first.id, realRecords: body.items.length });
  } else results.push({ module, realRecords: body.items?.length ?? null, expectedEmpty: !before });
  if (!before) {
    const reload = page.waitForResponse(r => r.url().startsWith(`${api}/${module}?`) && r.status() === 200);
    await page.reload(); await reload;
    await expect(page.getByText(/Không thể tải danh sách/u)).toHaveCount(0);
  }
}
try {
  await login(); await inspect("quotes", "initial"); await inspect("orders", "initial");
  if (!before) {
    await page.locator("#user-profile-dropdown").click();
    await page.getByRole("menuitem", { name: /^(Đăng xuất|Sign out)$/u }).click();
    await expect(page).toHaveURL(/#\/login/u); await login();
    await inspect("quotes", "relogin"); await inspect("orders", "relogin");
  }
  assert.equal(errors.length, 0, JSON.stringify(errors));
  assert.equal(requests.some(r => r.method !== "GET"), false, "List verification never mutates business records");
  status = "PASS";
} finally {
  if (status !== "PASS") await page.screenshot({ path: path.join(evidence, "failure.png"), fullPage: true }).catch(() => {});
  await writeFile(path.join(evidence, "result.json"), JSON.stringify({ status, phase: env.LOADING_TEST_PHASE, requests, errors, results }, null, 2));
  await browser.close();
}
