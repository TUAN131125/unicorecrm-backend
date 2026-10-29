import { expect, test } from "@playwright/test";
import { writeFile } from "node:fs/promises";

test("CRM journey through UI controls only", async ({ page }, info) => {
  test.setTimeout(180_000);
  const steps = ["Login", "Workspace", "Create Lead", "Link/Create Contact", "Organization relationship",
    "Conversion", "Customer", "Task", "Activity/History", "Refresh"]
    .map(name => ({ name, status: "BLOCKED", reason: "Earlier UI prerequisite has not completed" }));
  const http: { method: string; path: string; status: number; correlationId: string | undefined }[] = [];
  const marker = `ui-crm-${Date.now()}`;
  const records: Record<string, string> = { marker };
  let zeroAreaGuardDismissals = 0;
  const retiredGuard = page.getByRole("dialog", { name: "Bạn có thay đổi chưa lưu", exact: true });
  await page.addLocatorHandler(retiredGuard, async () => {
    // Navigation can display a guard whose saved form has already unregistered.
    // Use its visible UI only when it confirms there are zero unsaved areas.
    await expect(retiredGuard).toContainText("Có 0 khu vực cấu hình chưa được lưu");
    zeroAreaGuardDismissals++;
    await retiredGuard.getByRole("button", { name: "Bỏ thay đổi", exact: true }).click();
  });
  async function mutation(path: string, click: () => Promise<void>, expected: number) {
    const pending = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === path);
    await click();
    const response = await pending;
    http.push({ method: "POST", path, status: response.status(), correlationId: response.request().headers()["x-correlation-id"] });
    expect(response.status()).toBe(expected);
    return response.json() as Promise<{ aggregateId: string; result: { customerId?: string } }>;
  }
  try {
    await page.goto("/#/login");
    await page.getByLabel("Email", { exact: true }).fill(process.env.UNICORECRM_TEST_EMAIL ?? "");
    await page.getByLabel(/Mật khẩu|Password/u, { exact: true }).fill(process.env.UNICORECRM_TEST_PASSWORD ?? "");
    await page.getByRole("button", { name: /^(Đăng nhập|Sign in)$/u }).click();
    await page.waitForURL(/#\/(?:select-workspace|w\/[^/]+\/crm\/)/u);
    Object.assign(steps[0], { status: "PASS", reason: "Real sign-in through UI" });
    try { await page.waitForURL(/#\/w\/[^/]+\/crm\//u, { timeout: 5_000 }); }
    catch { await page.getByRole("button").filter({ hasText: "unicore-demo" }).click(); }
    await expect(page).toHaveURL(/#\/w\/[^/]+\/crm\//u);
    Object.assign(steps[1], { status: "PASS", reason: "Authenticated Workspace selected" });
    const key = page.url().match(/#\/w\/([^/]+)/u)?.[1];
    await page.goto(`/#/w/${key}/crm/leads`);
    await page.getByRole("button", { name: /Thêm Tiềm năng|Thêm Lead|Add Lead/iu }).first().click();
    const dialog = page.getByRole("dialog", { name: /Thêm Tiềm năng mới|Thêm Lead mới|Add new Lead/iu });
    await expect(dialog).toBeVisible();
    await dialog.locator("#lead-name").fill(marker);
    await dialog.locator("#lead-email").fill(`${marker}@example.test`);
    await dialog.locator("#lead-phone").fill("0900000123");
    await dialog.locator('[data-guidance-id="leads.form.advanced-toggle"]').click();
    await expect(dialog.locator("#lead-source")).toHaveValue("");
    const sourceControlValue = await dialog.locator("#lead-source").inputValue();
    const result = await mutation("/leads", () => dialog.locator('button[type="submit"]').click(), 201);
    records.leadId = result.aggregateId;
    const leadRequest = http.find(entry => entry.path === "/leads");
    await writeFile(info.outputPath("lead-create-evidence.json"), JSON.stringify({
      sourceControlValue,
      request: leadRequest, response: { aggregateId: result.aggregateId, status: 201 },
    }, null, 2));
    Object.assign(steps[2], { status: "PASS", reason: "Lead created through UI with Source empty" });
    await writeFile(info.outputPath("ui-records.json"), JSON.stringify({ marker, leadId: result.aggregateId, sourceSubmitted: "" }, null, 2));
    await expect(dialog).toBeHidden();
    await page.goto(`/#/w/${key}/crm/leads/${result.aggregateId}`);
    await expect(page.getByText(marker, { exact: true }).first()).toBeVisible();
    await page.reload();
    await expect(page.getByText(marker, { exact: true }).first()).toBeVisible();
    await page.screenshot({ path: info.outputPath("lead-empty-source-created.png"), fullPage: true });
    await page.goto(`/#/w/${key}/crm/contacts`);
    await page.getByRole("button", { name: /^Thêm liên hệ(?: mới)?$/iu }).first().click();
    const contactDialog = page.getByRole("dialog").filter({ has: page.locator("#contact-create-name") });
    await contactDialog.locator("#contact-create-name").fill(`${marker} Contact`);
    await contactDialog.locator("#contact-create-email").fill(`${marker}-contact@example.test`);
    const contact = await mutation("/contacts", () => contactDialog.locator('button[type="submit"]').click(), 201);
    records.contactId = contact.aggregateId;
    await expect(contactDialog).toBeHidden();
    Object.assign(steps[3], { status: "PASS", reason: "Contact created through UI; used as conversion subject below" });
    await page.goto(`/#/w/${key}/crm/organizations`);
    await page.getByRole("button", { name: /^Thêm tổ chức(?: B2B)?$/iu }).first().click();
    const organizationDialog = page.getByRole("dialog").filter({ has: page.locator("#organization-create-displayName") });
    await organizationDialog.locator("#organization-create-displayName").fill(`${marker} Organization`);
    const organization = await mutation("/organizations", () => organizationDialog.locator('button[type="submit"]').click(), 200);
    records.organizationId = organization.aggregateId;
    await expect(organizationDialog).toBeHidden();
    await page.goto(`/#/w/${key}/crm/contacts/${contact.aggregateId}`);
    await page.getByRole("button", { name: "Quan hệ", exact: true }).click();
    await page.getByRole("button", { name: "Liên kết", exact: true }).click();
    const affiliation = page.getByRole("heading", { name: "Quan hệ với tổ chức", exact: true }).locator("xpath=ancestor::section[1]");
    await affiliation.getByRole("button", { name: "Liên kết", exact: true }).click();
    const affiliationDialog = page.getByRole("dialog", { name: "Liên kết tổ chức", exact: true });
    await affiliationDialog.locator("#organization-relationship-subject").selectOption(organization.aggregateId);
    await affiliationDialog.locator("#organization-relationship-primary").check();
    await mutation(`/contacts/${contact.aggregateId}/organization-relationships`, () => affiliationDialog.locator('button[type="submit"]').click(), 200);
    await expect(affiliationDialog).toBeHidden();
    await expect(affiliation.getByText(`${marker} Organization`, { exact: true })).toBeVisible();
    Object.assign(steps[4], { status: "PASS", reason: "Organization created and affiliated through UI" });
    await page.goto(`/#/w/${key}/crm/leads/${result.aggregateId}/convert`);
    await page.getByLabel("Mã đối tượng hiện có", { exact: true }).fill(contact.aggregateId);
    const converted = await mutation(`/workflows/lead-customer-conversion/${result.aggregateId}`,
      () => page.getByRole("button", { name: "Chuyển thành khách hàng", exact: true }).click(), 200);
    const customerId = converted.result.customerId;
    expect(customerId).toBeTruthy();
    records.customerId = customerId ?? "";
    Object.assign(steps[5], { status: "PASS", reason: "Existing Contact submitted using the conversion UI" });
    await page.getByRole("link", { name: customerId, exact: true }).click();
    await expect(page.locator("[data-customer-connected-workspace]")).toBeVisible();
    Object.assign(steps[6], { status: "PASS", reason: "Authoritative connected Customer workspace opened" });
    const taskButton = page.getByRole("button", { name: /^(Thêm công việc|Tạo công việc|Add task|Create task)$/u });
    if (await taskButton.count() === 0) {
      Object.assign(steps[7], { reason: "Connected Customer workspace has no Create Task control; only the Task-based Meeting control is exposed" });
      await page.screenshot({ path: info.outputPath("customer-task-control-missing.png"), fullPage: true });
    } else {
      await taskButton.first().click();
      const taskDialog = page.getByRole("dialog").filter({ has: page.locator("#task-create-title") });
      await taskDialog.locator("#task-create-title").fill(`${marker} Task`);
      await taskDialog.locator("#task-create-dueAt").fill("2026-11-01T09:00");
      const task = await mutation("/tasks", () => taskDialog.locator('button[type="submit"]').click(), 201);
      records.taskId = task.aggregateId;
      Object.assign(steps[7], { status: "PASS", reason: "Customer Task created through UI" });
    }
    await page.goto(`/#/w/${key}/crm/contacts/${contact.aggregateId}`);
    await page.getByRole("button", { name: "Ghi chú nhanh", exact: true }).click();
    const noteDialog = page.getByRole("dialog", { name: "Ghi chú nhanh", exact: true });
    await noteDialog.locator("#contact-quick-note-form-title").fill(`${marker} Activity`);
    await noteDialog.locator("#contact-quick-note-form-body").fill("UI-only Activity persistence verification");
    const activity = await mutation("/activities", () => noteDialog.locator('button[type="submit"]').click(), 201);
    records.activityId = activity.aggregateId;
    await expect(noteDialog).toBeHidden();
    await page.getByRole("tab", { name: "Lịch sử", exact: true }).click();
    await expect(page.getByText(`${marker} Activity`, { exact: true }).first()).toBeVisible();
    Object.assign(steps[8], { status: "PASS", reason: "Manual Contact Activity saved and read through UI History" });
    await page.reload();
    await page.getByRole("tab", { name: "Lịch sử", exact: true }).click();
    await expect(page.getByText(`${marker} Activity`, { exact: true }).first()).toBeVisible();
    Object.assign(steps[9], { status: "PASS", reason: "Persisted Contact Activity survives browser refresh" });
    await page.screenshot({ path: info.outputPath("contact-ui-history-refresh.png"), fullPage: true });
    const blocked = steps.filter(step => step.status === "BLOCKED");
    if (blocked.length) throw new Error(`BLOCKED: ${blocked.map(step => `${step.name}: ${step.reason}`).join("; ")}`);
  } finally {
    await writeFile(info.outputPath("ui-steps.json"), JSON.stringify(steps, null, 2));
    await writeFile(info.outputPath("ui-http.json"), JSON.stringify(http, null, 2));
    await writeFile(info.outputPath("ui-journey-records.json"), JSON.stringify(records, null, 2));
    await writeFile(info.outputPath("ui-observations.json"), JSON.stringify({ zeroAreaGuardDismissals }, null, 2));
  }
});
