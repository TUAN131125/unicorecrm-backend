import { createRequire } from "node:module";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const { chromium, expect } = createRequire(path.join(root, "frontend/unicorecrm-web/package.json"))("@playwright/test");
const env = process.env, output = env.LOADING_TEST_EVIDENCE;
await mkdir(output, { recursive: true });
const browser = await chromium.launch();
const page = await browser.newPage();
const route = suffix => `http://127.0.0.1:3048/#/w/${env.LOADING_TEST_WORKSPACE}/crm/${suffix}`;
try {
  await page.goto("http://127.0.0.1:3048/#/login");
  await page.getByLabel("Email", { exact: true }).fill(env.LOADING_TEST_EMAIL);
  await page.getByLabel(/^(Mật khẩu|Password)$/u).fill(env.LOADING_TEST_PASSWORD);
  await page.getByRole("button", { name: /^(Đăng nhập|Sign in)$/u }).click();
  await page.waitForURL(/#\/(?:select-workspace|w\/)/u);
  const response = page.waitForResponse(r => r.url().startsWith(`${env.LOADING_TEST_API}/quotes?`) && r.status() === 200);
  await page.goto(route("quotes")); const data = await (await response).json();
  if (!data.items.length) throw new Error("Existing Quote required for before capture; no fixture may be created here.");
  await page.goto(route(`quotes/${data.items[0].id}`));
  await expect(page.locator("#quote-detail-header")).toBeVisible({ timeout: 30000 });
  await expect(page.locator('[data-quote-documents="panel"]')).toBeVisible();
  for (const width of [1440, 1280, 768, 390]) {
    await page.setViewportSize({ width, height: 1050 });
    await page.screenshot({ path: path.join(output, `quote-detail-${width}.png`), fullPage: true });
  }
  await writeFile(path.join(output, "result.json"), JSON.stringify({ status: "PASS", quoteId: data.items[0].id, readOnly: true, widths: [1440, 1280, 768, 390] }, null, 2));
} finally { await browser.close(); }
