import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import type { Task, Activity } from "@/modules/tasks";
import type { AuthoritativePage } from "@/shared/application";
const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: "http://localhost", pretendToBeVisual: true });
for (const key of ["window", "document", "navigator", "localStorage", "HTMLElement", "Element", "Node", "Event", "CustomEvent", "EventTarget", "MutationObserver"] as const) Object.defineProperty(globalThis, key, { value: key === "window" ? dom.window : dom.window[key], configurable: true });
Object.defineProperty(globalThis, "IS_REACT_ACT_ENVIRONMENT", { value: true, configurable: true });
Object.defineProperty(dom.window, "matchMedia", { value: () => ({ matches: true, addEventListener() {}, removeEventListener() {} }) });
const React = await import("react"), { act } = React;
const { createRoot } = await import("react-dom/client");
const auth = await import("@/platform/identity-auth");
assert.ok(auth.signIn({ email: "admin@unicorecrm.local", password: "admin123" }).ok);
const { initializeApplicationComposition } = await import("@/app/composition");
await initializeApplicationComposition({ mode: "demo" });
const ac = await import("@/platform/access-control"), ws = await import("@/platform/workspace-context");
await ac.loadAccessGovernance(ws.getWorkspaceContextSnapshot().workspaceId);
const binding = ac.getAccessGovernanceRuntimeBinding(), baseline = ac.resolveEffectiveAccess();
let readable = true, hidden = "", restrictedHistory = false, maskedReference = false;
ac.configureAccessGovernanceRuntime({ ...binding,
  getState: () => binding.getState(), getRuntime: () => binding.getRuntime(), subscribe: fn => binding.subscribe(fn),
  load: (...args) => binding.load(...args), refresh: (...args) => binding.refresh(...args), applyMutation: (...args) => binding.applyMutation(...args), clear: () => binding.clear(),
  getEffectiveAccess: () => ({ ...baseline,
  can: c => c === "tasks.read" ? readable : baseline.can(c), getDataScope: () => "WORKSPACE",
  getFieldAccess: (resource, field) => field === hidden ? "HIDDEN" : maskedReference && resource === "tasks" && field === "recordref" ? "MASKED" : restrictedHistory && resource === "tasks" && field === "description" ? "READ_ONLY" : "READ_WRITE",
}) });
const services = await import("@/modules/tasks/application/composition/taskApplicationServices"), original = services.getTaskApplicationServices();
const { ApplicationError } = await import("@/shared/domain");
const at = "2026-10-21T10:00:00Z";
const task = (id: string, recordId = "customer-A", moduleKey = "customers"): Task => ({ id, title: id, status: "OPEN", priority: "NORMAL", assigneeId: baseline.memberId, dueAt: at, createdAt: at, updatedAt: at, recordRef: { moduleKey, recordId } });
const event = (id: string): Activity => ({ id, type: "NOTE", subject: id, body: "Authorized Activity body", actorId: baseline.memberId, occurredAt: at, recordRef: { moduleKey: "customers", recordId: "customer-A" } });
let mode: "data" | "empty" | "error" | "delay" = "data";
let release: ((value: AuthoritativePage<Task>) => void) | undefined;
const requests: unknown[] = [];
services.configureTaskApplication({ repository: original.repository, api: { ...original.api, mode: "connected", queries: { ...original.api.queries,
  async list(query) {
    requests.push(query);
    if (mode === "error") throw new ApplicationError({ code: "NETWORK_ERROR", message: "PRIVATE_DIAGNOSTIC", category: "NETWORK" });
    if (mode === "delay") return new Promise(resolve => { release = resolve; });
    return { items: mode === "empty" ? [] : query?.cursor ? [task("T49"), task("T50")] : [...Array.from({ length: 50 }, (_, i) => task("T" + i)), task("WRONG-CUSTOMER", "customer-B"), task("CONTACT-TASK", "customer-A", "contacts")], pageInfo: { hasNextPage: mode === "data" && !query?.cursor, ...(mode === "data" && !query?.cursor ? { nextCursor: "tasks-next" } : {}) }, authority: "backend", loadedAt: at };
  },
  async listActivities(query) {
    requests.push(query);
    return { items: query?.cursor ? [event("E1"), event("E2")] : [event("E1")], pageInfo: { hasNextPage: !query?.cursor, ...(!query?.cursor ? { nextCursor: "history-next" } : {}) }, authority: "backend", loadedAt: at };
  },
} } });
const { CustomerRelationshipPanel } = await import("@/modules/customers/presentation/detail/CustomerRelationshipPanel");
const { CustomerConnectedWorkspace } = await import("@/modules/customers/presentation/detail/CustomerConnectedWorkspace");
const { I18nProvider } = await import("@/i18n"), { MemoryRouter, useLocation } = await import("react-router-dom");
function LocationProbe() { return React.createElement("output", { "data-test-location": useLocation().pathname }); }
const host = document.getElementById("root"); if (!host) throw new Error("fixture root");
const root = createRoot(host); let generation = 0;
async function render(id = "customer-A", visible = true) { await act(async () => root.render(React.createElement(I18nProvider, null, React.createElement(MemoryRouter, null, React.createElement(React.Fragment, null, React.createElement(CustomerRelationshipPanel, { key: ++generation, customerId: id, visible }), React.createElement(LocationProbe)))))); }
async function click(text: string) { const b = [...document.querySelectorAll("button")].find(b => b.textContent === text); assert.ok(b, text); await act(async () => b.click()); }
await render(); assert.equal(document.querySelectorAll("[data-customer-task-id]").length, 50); assert.ok(!document.body.textContent?.includes("WRONG-CUSTOMER")); assert.ok(!document.body.textContent?.includes("CONTACT-TASK"));
await click("Tải thêm công việc"); assert.equal(document.querySelectorAll("[data-customer-task-id]").length, 51);
assert.deepEqual([...document.querySelectorAll("[data-customer-task-id]")].map(x => x.getAttribute("data-customer-task-id")), Array.from({ length: 51 }, (_, i) => "T" + i));
const taskLink = document.querySelector<HTMLButtonElement>('[data-customer-task-id="T0"] button'); assert.ok(taskLink);
await act(async () => taskLink.click()); assert.equal(document.querySelector("[data-test-location]")?.getAttribute("data-test-location"), "/tasks/T0");
await click("Lịch sử"); await click("Tải thêm hoạt động"); assert.equal(document.querySelectorAll("[data-customer-history-id]").length, 2);
const activity = document.querySelector<HTMLButtonElement>("[data-customer-history-id] button"); assert.ok(activity); activity.focus(); await act(async () => activity.click()); assert.match(document.body.textContent ?? "", /Authorized Activity body/);
await act(async () => dom.window.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }))); assert.equal(document.querySelectorAll('[role="dialog"]').length, 0);
assert.ok(requests.every(value => JSON.stringify(value).includes('"recordModuleKey":"customers"') && JSON.stringify(value).includes('"recordId":"customer-A"')));
mode = "empty"; await render(); assert.match(document.body.textContent ?? "", /Không có công việc gắn trực tiếp/);
mode = "error"; await render(); assert.match(document.body.textContent ?? "", /Không thể tải dữ liệu/); assert.ok(!document.body.textContent?.includes("PRIVATE_DIAGNOSTIC"));
mode = "data"; await click("Thử lại"); assert.equal(document.querySelectorAll("[data-customer-task-id]").length, 50);
readable = false; await render(); assert.equal(document.querySelectorAll("[data-customer-task-id]").length, 0); assert.match(document.body.textContent ?? "", /không có quyền/); readable = true;
hidden = "recordref"; await render(); assert.equal(document.querySelectorAll("[data-customer-task-id]").length, 0); assert.match(document.body.textContent ?? "", /Công việc chưa khả dụng/); hidden = "";
maskedReference = true; await render(); assert.equal(document.querySelectorAll("[data-customer-task-id]").length, 0); assert.match(document.body.textContent ?? "", /Công việc chưa khả dụng/); maskedReference = false;
restrictedHistory = true; await render(); await click("Lịch sử"); assert.match(document.body.textContent ?? "", /Lịch sử chưa khả dụng/); assert.equal(document.querySelectorAll("[data-customer-history-id]").length, 0); restrictedHistory = false;
mode = "delay"; await render(); assert.match(document.body.textContent ?? "", /Đang tải/); mode = "empty"; await render("customer-B"); await act(async () => { release?.({ items: [task("RETIRED")], pageInfo: { hasNextPage: false }, authority: "backend", loadedAt: at }); }); assert.ok(!document.body.textContent?.includes("RETIRED"));
hidden = "segment";
await act(async () => root.render(React.createElement(I18nProvider, null, React.createElement(MemoryRouter, null, React.createElement(CustomerConnectedWorkspace, { projection: {
  customer: { id: "customer-C", workspaceId: baseline.workspaceId, customerCode: "SYNTHETIC-C", type: "B2C", relationshipRef: { type: "CONTACT", id: "contact-C" }, status: "NEW", health: null, firstPurchaseAt: null, lastPurchaseAt: null, tags: [], segment: "WITHHELD-SEGMENT", createdAt: at, updatedAt: at },
  identity: { displayName: "Only authoritative identity", contactId: "contact-C" }, metrics: {}, linkedRecords: [], stakeholderContacts: [], allowedActions: [], projectionVersion: 0, generatedAt: at,
} })))));
assert.match(document.body.textContent ?? "", /Only authoritative identity/); assert.ok(!document.body.textContent?.includes("WITHHELD-SEGMENT")); assert.match(document.body.textContent ?? "", /Chưa có tổng hợp doanh thu/);
const meetingButton = [...document.querySelectorAll("button")].find(x => x.textContent === "Tạo cuộc hẹn");
assert.ok(meetingButton); assert.equal(meetingButton.disabled, true);
assert.equal(document.querySelector("#customer-meeting-form-title"), null);
assert.equal([...document.querySelectorAll("button")].some(x => /Tạo công việc/.test(x.textContent ?? "")), false);
assert.ok(document.querySelector('button[aria-controls]')); await act(async () => root.unmount());
console.log("PASS isolated Customer panel/Overview: exact direct refs, stable dedup paging, Activity detail/Escape, loading/empty/error/retry, scope/FLS refusal, retired response, projected-only identity, Meeting disabled without record authority and generic creation absent. Not connected evidence.");
