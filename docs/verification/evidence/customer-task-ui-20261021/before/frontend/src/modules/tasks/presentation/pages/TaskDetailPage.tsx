import { useRef as useWorkspaceBindingRef } from "react";
import { useWorkspaceContextSnapshot as useWorkflowWorkspace } from "@/platform/workspace-context";
import { useTargetBoundWorkflow } from "@/shared/presentation/useTargetBoundWorkflow";
import { useUnsavedChangesGuard } from "@/shared/hooks/useUnsavedChangesGuard";
import { formatApplicationError } from "@/shared/operations";
import React, { useMemo, useState } from "react";
import { ArrowLeft, CalendarClock, CheckCircle2, FileClock, History, Link2, ListTodo, RotateCcw, UserRound, XCircle } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { toWorkspacePath } from "@/platform/navigation";
import { RecordDetailFrame } from "@/components/crm/detail-archetype";
import { OperationDetailTabs, OperationLifecycleRail } from "@/components/crm/operations";
import { Button, ConfirmDialog, Input, Modal, RecordTabTransition, SearchableSelect, Textarea } from "@/shared/components/ui";
import { useI18n } from "@/i18n";
import { getContactsSnapshot, subscribeToContacts } from "@/modules/contacts";
import { getOrganizationAccountsSnapshot, subscribeToOrganizationAccounts } from "@/modules/organizations";
import { getAuthSessionSnapshot } from "@/platform/identity-auth";
import { listWorkspaceMemberDirectory, resolveWorkspaceMemberName } from "@/platform/member-directory";
import { getOperationalAuditSnapshot, subscribeToOperationalAudit } from "@/platform/operational-audit";
import { useSubscribableSnapshot } from "@/platform/react";
import { useWorkspaceContextSnapshot } from "@/platform/workspace-context";
import {
  cancelTaskCommand,
  completeTaskCommand,
  getTaskActivitySnapshot,
  reassignTaskCommand,
  rescheduleTaskCommand,
  subscribeToTaskActivity,
  type TaskPriority,
  type TaskStatus,
} from "../../public/api";
import { resolveTaskContextLabel } from "../model/taskContextPresentation";
import { useEffectiveAccess } from "@/platform/access-control";

type TaskDetailTab = "overview" | "activity" | "audit";

const statusLabel = (status: TaskStatus, vi: boolean) => status === "OPEN" ? (vi ? "Đang mở" : "Open") : status === "COMPLETED" ? (vi ? "Đã hoàn thành" : "Completed") : (vi ? "Đã hủy" : "Cancelled");
const priorityLabel = (priority: TaskPriority, vi: boolean) => priority === "LOW" ? (vi ? "Thấp" : "Low") : priority === "NORMAL" ? (vi ? "Bình thường" : "Normal") : priority === "HIGH" ? (vi ? "Cao" : "High") : (vi ? "Khẩn cấp" : "Urgent");

const localizeTaskNarrative = (value: string, vi: boolean): string => vi
  ? value.replace(/Customer 360/gi, "Hồ sơ khách hàng").replace(/Task workspace/gi, "khu vực Công việc")
  : value;

const relatedRoute = (moduleKey?: string, recordId?: string): string | undefined => {
  if (!moduleKey || !recordId) return undefined;
  const segmentByModule: Record<string, string> = {
    leads: "leads",
    contacts: "contacts",
    customers: "customers",
    organizations: "organizations",
    deals: "deals",
    quotes: "quotes",
    orders: "orders",
    support: "support/cases",
  };
  const segment = segmentByModule[moduleKey];
  return segment ? `${segment}/${recordId}` : undefined;
};

export const TaskDetailPage: React.FC = () => {
  const { taskId: routeTaskId = "" } = useParams();
  const targetLifecycle = useTargetBoundWorkflow(routeTaskId, "task-detail-action");
  const workflowWorkspace = useWorkflowWorkspace().workspaceId;
  const currentWorkflowWorkspace = useWorkspaceBindingRef(workflowWorkspace);
  currentWorkflowWorkspace.current = workflowWorkspace;
  const openingWorkspace = useWorkspaceBindingRef({ cycle: targetLifecycle.cycle, id: workflowWorkspace });
  if (openingWorkspace.current.cycle !== targetLifecycle.cycle) openingWorkspace.current = { cycle: targetLifecycle.cycle, id: workflowWorkspace };
  const ownsWorkspace = () => openingWorkspace.current.id === currentWorkflowWorkspace.current;
  const workflow = { ...targetLifecycle,
    begin: () => ownsWorkspace() && targetLifecycle.begin(),
    isCurrent: () => ownsWorkspace() && targetLifecycle.isCurrent(),
    register: (dirty: boolean, reset: () => void, save: () => Promise<boolean>) => {
      targetLifecycle.register(dirty, reset, () => ownsWorkspace() ? save() : Promise.resolve(false));
    },
  };
  const taskId = workflow.targetId ?? "";
  const navigate = useNavigate();
  const workspace = useWorkspaceContextSnapshot();
  const { locale } = useI18n();
  const vi = locale === "vi";
  const session = getAuthSessionSnapshot();
  const access = useEffectiveAccess();
  const snapshot = useSubscribableSnapshot(getTaskActivitySnapshot, subscribeToTaskActivity);
  const contacts = useSubscribableSnapshot(getContactsSnapshot, subscribeToContacts);
  const organizations = useSubscribableSnapshot(getOrganizationAccountsSnapshot, subscribeToOrganizationAccounts);
  const getAudit = React.useCallback(() => getOperationalAuditSnapshot("tasks", taskId), [taskId]);
  const audit = useSubscribableSnapshot(getAudit, subscribeToOperationalAudit);
  const liveTask = snapshot.tasks.find((item) => item.id === taskId);
  const [activeTab, setActiveTab] = useState<TaskDetailTab>("overview");
  const [showComplete, setShowComplete] = useState(false);
  const [showReschedule, setShowReschedule] = useState(false);
  const [outcome, setOutcome] = useState("");
  const [newDueAt, setNewDueAt] = useState(liveTask?.dueAt.slice(0, 16) ?? "");
  const [message, setMessage] = useState<string | null>(null);

  const openingTask = React.useRef(liveTask);
  if (openingTask.current?.id !== taskId || (!showComplete && !showReschedule && !workflow.pending)) openingTask.current = liveTask ? structuredClone(liveTask) : undefined;
  const task = openingTask.current;
  const dirty = (showComplete && Boolean(outcome.trim())) || (showReschedule && newDueAt !== (task?.dueAt.slice(0, 16) ?? ""));
  const closeActions = () => { setShowComplete(false); setShowReschedule(false); setOutcome(""); setNewDueAt(task?.dueAt.slice(0, 16) ?? ""); };
  const guard = useUnsavedChangesGuard(closeActions);
  React.useEffect(() => guard.setIsDirty(dirty), [dirty, guard.setIsDirty]);
  const close = () => { if (!workflow.pending) guard.requestClose(); };
  workflow.register(dirty, closeActions, async () => false);
  React.useEffect(() => { closeActions(); setMessage(null); }, [taskId]);
  if (!task) return <div className="rounded-2xl border border-slate-200 bg-white p-8 text-sm text-slate-600">{vi ? "Không tìm thấy công việc." : "Task not found."}</div>;

  const actorId = session?.principal.memberId || access.memberId || undefined;
  const actorName = session?.principal.displayName || actorId;
  const canComplete = Boolean(actorId) && access.canPerform("tasks", "complete");
  const canUpdate = Boolean(actorId) && access.canPerform("tasks", "update");
  const members = listWorkspaceMemberDirectory();
  const contextLabel = resolveTaskContextLabel(task, contacts, organizations) || task.recordRef?.label;
  const route = relatedRoute(task.recordRef?.moduleKey, task.recordRef?.recordId);
  const overdue = task.status === "OPEN" && new Date(task.dueAt).getTime() < Date.now();
  const assigneeLabel = resolveWorkspaceMemberName(task.assigneeId);
  const activities = snapshot.activities.filter((activity) => activity.recordRef?.moduleKey === task.recordRef?.moduleKey && activity.recordRef?.recordId === task.recordRef?.recordId);

  const complete = async () => {
    if (!actorId || !actorName || !workflow.begin()) return;
    try {
      await completeTaskCommand(task.id, { actorId, actorName, outcome: outcome.trim() || (vi ? "Đã hoàn thành công việc." : "Task completed.") }, { expectedVersion: task.resourceVersion });
      if (!workflow.isCurrent()) return;
      setOutcome("");
      setShowComplete(false);
      setMessage(vi ? "Đã hoàn thành công việc." : "Task completed.");
    } catch (error) { if (workflow.isCurrent()) setMessage(formatApplicationError(error, { locale })); }
    finally { workflow.finish(); }
  };

  const cancel = async () => {
    if (!actorId || !actorName || !workflow.begin()) return;
    try {
      await cancelTaskCommand(task.id, { actorId, actorName, reason: vi ? "Đã hủy từ trang chi tiết Công việc" : "Cancelled from Task detail" });
      setMessage(vi ? "Đã hủy công việc." : "Task cancelled.");
    } catch (error) { if (workflow.isCurrent()) setMessage(formatApplicationError(error, { locale })); }
    finally { workflow.finish(); }
  };

  const reschedule = async () => {
    if (!actorId || !actorName || !workflow.begin()) return;
    try {
      await rescheduleTaskCommand(task.id, { actorId, actorName, dueAt: new Date(newDueAt).toISOString() }, { expectedVersion: task.resourceVersion });
      if (!workflow.isCurrent()) return;
      setShowReschedule(false);
      setMessage(vi ? "Đã đổi lịch công việc." : "Task rescheduled.");
    } catch (error) { if (workflow.isCurrent()) setMessage(formatApplicationError(error, { locale })); }
    finally { workflow.finish(); }
  };

  const reassign = async (nextAssigneeId: string) => {
    if (!actorId || !actorName || !nextAssigneeId || nextAssigneeId === task.assigneeId || showComplete || showReschedule || !workflow.begin()) return;
    try {
      await reassignTaskCommand(task.id, { assigneeId: nextAssigneeId, actorId, actorName });
      setMessage(vi ? "Đã chuyển người phụ trách." : "Task reassigned.");
    } catch (error) { if (workflow.isCurrent()) setMessage(formatApplicationError(error, { locale })); }
    finally { workflow.finish(); }
  };

  const tabs = [
    { key: "overview", label: vi ? "Tổng quan" : "Overview", icon: <ListTodo size={14} /> },
    { key: "activity", label: vi ? "Hoạt động liên quan" : "Related activity", icon: <History size={14} />, count: activities.length },
    { key: "audit", label: vi ? "Nhật ký thay đổi" : "Change log", icon: <FileClock size={14} />, count: audit.length },
  ];

  const lifecycleItems = [
    { key: "created", label: vi ? "Đã tạo" : "Created", state: "done" as const, description: new Date(task.createdAt).toLocaleString(vi ? "vi-VN" : "en-US") },
    { key: "due", label: vi ? "Đến hạn" : "Due", state: task.status === "OPEN" ? (overdue ? "blocked" as const : "current" as const) : "done" as const, description: new Date(task.dueAt).toLocaleString(vi ? "vi-VN" : "en-US") },
    { key: "done", label: task.status === "CANCELLED" ? (vi ? "Đã hủy" : "Cancelled") : (vi ? "Hoàn thành" : "Completed"), state: task.status === "OPEN" ? "next" as const : "done" as const, description: task.completedAt ? new Date(task.completedAt).toLocaleString(vi ? "vi-VN" : "en-US") : task.cancelledAt ? new Date(task.cancelledAt).toLocaleString(vi ? "vi-VN" : "en-US") : undefined },
  ];

  return (
    <RecordDetailFrame id="task-detail-page">
      <button type="button" onClick={() => navigate("..")} className="inline-flex items-center gap-2 text-xs font-medium text-slate-500 hover:text-indigo-700"><ArrowLeft size={14} /> {vi ? "Quay lại Công việc" : "Back to Tasks"}</button>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_340px]">
        <main className="space-y-5">
          <section className="rounded-3xl border border-slate-200 bg-white p-6 shadow-sm">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0 flex-1 overflow-hidden">
                <div className="text-[10px] font-semibold uppercase tracking-[0.16em] text-indigo-500">{vi ? "Công việc" : "Task"}</div>
                <h1 className="mt-2 max-w-full break-words text-2xl font-semibold leading-tight text-slate-950 [overflow-wrap:anywhere]">{task.title}</h1>
                <p className="mt-2 max-w-3xl break-words text-sm leading-6 text-slate-500 [overflow-wrap:anywhere]">{task.description || (vi ? "Chưa có mô tả." : "No description provided.")}</p>
              </div>
              <div className="flex shrink-0 flex-wrap gap-2 lg:max-w-[220px] lg:justify-end">
                <span className="rounded-full border border-indigo-100 bg-indigo-50 px-3 py-1.5 text-xs font-semibold text-indigo-700">{statusLabel(task.status, vi)}</span>
                <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-medium text-slate-700">{priorityLabel(task.priority, vi)}</span>
              </div>
            </div>

            <div className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              <Info label={vi ? "Người phụ trách" : "Assignee"} value={assigneeLabel || "—"} icon={<UserRound size={14} />} />
              <Info label={vi ? "Hạn xử lý" : "Due"} value={new Date(task.dueAt).toLocaleString(vi ? "vi-VN" : "en-US")} icon={<CalendarClock size={14} />} tone={overdue ? "rose" : "slate"} />
              <Info label={vi ? "Liên quan đến" : "Related to"} value={contextLabel || (vi ? "Không gắn bản ghi" : "No related record")} icon={<Link2 size={14} />} />
            </div>
            {message && <div className="mt-4 rounded-xl border border-indigo-100 bg-indigo-50 px-4 py-3 text-xs font-semibold text-indigo-800">{message}</div>}
          </section>

          <OperationDetailTabs items={tabs} activeKey={activeTab} onChange={(key) => setActiveTab(key as TaskDetailTab)} />

          <RecordTabTransition transitionKey={activeTab} axis="x" minHeightClassName="min-h-[320px]" className="space-y-5">
          {activeTab === "overview" && (
            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h2 className="text-sm font-semibold text-slate-900">{vi ? "Thông tin công việc" : "Task information"}</h2>
              <div className="mt-4 grid gap-3 md:grid-cols-2">
                <Info label={vi ? "Trạng thái" : "Status"} value={statusLabel(task.status, vi)} />
                <Info label={vi ? "Ưu tiên" : "Priority"} value={priorityLabel(task.priority, vi)} />
                <Info label={vi ? "Ngày tạo" : "Created"} value={new Date(task.createdAt).toLocaleString(vi ? "vi-VN" : "en-US")} />
                <Info label={vi ? "Cập nhật gần nhất" : "Last updated"} value={new Date(task.updatedAt).toLocaleString(vi ? "vi-VN" : "en-US")} />
                {task.outcome && <Info label={vi ? "Kết quả" : "Outcome"} value={localizeTaskNarrative(task.outcome, vi)} wide />}
                {task.cancellationReason && <Info label={vi ? "Lý do hủy" : "Cancellation reason"} value={localizeTaskNarrative(task.cancellationReason, vi)} wide />}
              </div>
              {route && (
                <Button type="button" actionIntent="navigate" size="sm" icon={<Link2 size={14} />} className="mt-4" onClick={() => navigate(toWorkspacePath(workspace.workspaceKey, "crm", route))}>{vi ? "Mở bản ghi liên quan" : "Open related record"}</Button>
              )}
            </section>
          )}

          {activeTab === "activity" && (
            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h2 className="text-sm font-semibold text-slate-900">{vi ? "Hoạt động của bản ghi liên quan" : "Related record activity"}</h2>
              <div className="mt-4 space-y-3">
                {activities.length === 0 ? <Empty text={vi ? "Chưa có hoạt động liên quan." : "No related activity yet."} /> : activities.map((activity) => (
                  <div key={activity.id} className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3">
                    <div className="text-xs font-medium text-slate-900">{activity.subject}</div>
                    {activity.body && <p className="mt-1 text-xs leading-5 text-slate-500">{activity.body}</p>}
                    <div className="mt-2 text-[10px] font-semibold text-slate-400">{new Date(activity.occurredAt).toLocaleString(vi ? "vi-VN" : "en-US")}</div>
                  </div>
                ))}
              </div>
            </section>
          )}

          {activeTab === "audit" && (
            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h2 className="text-sm font-semibold text-slate-900">{vi ? "Nhật ký thay đổi" : "Change log"}</h2>
              <div className="mt-4 space-y-3">
                {audit.length === 0 ? <Empty text={vi ? "Chưa có thay đổi được ghi nhận." : "No changes recorded."} /> : audit.map((item) => (
                  <div key={item.id} className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3">
                    <div className="text-xs font-medium text-slate-900">{item.action}</div>
                    <div className="mt-1 text-[10px] font-semibold text-slate-400">{item.actorName || item.actorId} · {new Date(item.occurredAt).toLocaleString(vi ? "vi-VN" : "en-US")}</div>
                  </div>
                ))}
              </div>
            </section>
          )}
          </RecordTabTransition>
        </main>

        <aside className="self-start xl:sticky xl:top-4">
          <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div className="border-b border-slate-100 px-5 py-4">
              <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-indigo-500">{vi ? "XỬ LÝ CÔNG VIỆC" : "TASK ACTIONS"}</div>
              <h2 className="mt-1 text-sm font-semibold text-slate-950">{statusLabel(task.status, vi)}</h2>
            </div>

            <div className="space-y-3 p-4">
              <Row label={vi ? "Người phụ trách" : "Assignee"} value={assigneeLabel || "—"} />
              <Row label={vi ? "Hạn" : "Due"} value={new Date(task.dueAt).toLocaleString(vi ? "vi-VN" : "en-US")} />
                {task.status === "OPEN" && canUpdate && (
                  <SearchableSelect disabled={workflow.pending} label={vi ? "Chuyển người phụ trách" : "Reassign"} value={task.assigneeId} onChange={(value) => void reassign(value)} clearable={false} placeholder={vi ? "Chọn nhân viên" : "Select member"} searchPlaceholder={vi ? "Tìm tên hoặc email..." : "Search name or email..."} options={members.map((member) => ({ value: member.memberId, label: member.displayName, description: member.email, keywords: member.email }))} />
                )}
              {overdue && <div className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 text-xs font-semibold text-rose-700">{vi ? "Công việc đã quá hạn." : "Task is overdue."}</div>}

              {task.status === "OPEN" && (
                <div className="grid gap-2 pt-1">
                  {canComplete && <Button type="button" actionIntent="complete" size="sm" icon={<CheckCircle2 size={15} />} className="text-white [&_svg]:stroke-white" onClick={() => { if (!workflow.pending && !showReschedule) { setOutcome(""); setShowComplete(true); } }}>{vi ? "Hoàn thành" : "Complete"}</Button>}
                  {canUpdate && <Button type="button" actionIntent="retry" size="sm" icon={<RotateCcw size={15} />} onClick={() => { if (!workflow.pending && !showComplete) { setNewDueAt(task.dueAt.slice(0, 16)); setShowReschedule(true); } }}>{vi ? "Đổi lịch" : "Reschedule"}</Button>}
                  {canUpdate && <Button type="button" actionIntent="destructive" size="sm" icon={<XCircle size={15} />} className="text-white [&_svg]:stroke-white" onClick={() => void cancel()}>{vi ? "Hủy công việc" : "Cancel task"}</Button>}
                </div>
              )}
            </div>

            <div className="border-t border-slate-100 p-4">
              <div className="mb-3 text-[10px] font-semibold uppercase tracking-wider text-slate-400">{vi ? "Vòng đời" : "Lifecycle"}</div>
              <OperationLifecycleRail steps={lifecycleItems} />
            </div>
          </div>
        </aside>
      </div>

      <Modal isOpen={showComplete} onClose={close} title={vi ? "Hoàn thành công việc" : "Complete task"} size="sm" variant="form">
        <div className="space-y-4 text-left">
          <label className="block"><span className="text-[10px] font-semibold uppercase text-slate-400">{vi ? "Kết quả" : "Outcome"}</span><Textarea disabled={workflow.pending} rows={4} value={outcome} onChange={(event) => setOutcome(event.target.value)} /></label>
          <div className="flex justify-end gap-2"><Button type="button" actionIntent="neutral" size="sm" onClick={close}>{vi ? "Hủy" : "Cancel"}</Button><Button type="button" actionIntent="confirm" size="sm" disabled={workflow.pending} onClick={() => void complete()}>{vi ? "Xác nhận" : "Confirm"}</Button></div>
        </div>
      </Modal>

      <Modal isOpen={showReschedule} onClose={close} title={vi ? "Đổi lịch công việc" : "Reschedule task"} size="sm" variant="form">
        <div className="space-y-4 text-left">
          <label className="block"><span className="text-[10px] font-semibold uppercase text-slate-400">{vi ? "Hạn mới" : "New due date"}</span><Input disabled={workflow.pending} type="datetime-local" value={newDueAt} onChange={(event) => setNewDueAt(event.target.value)} /></label>
          <div className="flex justify-end gap-2"><Button type="button" actionIntent="neutral" size="sm" onClick={close}>{vi ? "Hủy" : "Cancel"}</Button><Button type="button" actionIntent="save" size="sm" disabled={workflow.pending} onClick={() => void reschedule()}>{vi ? "Lưu lịch" : "Save"}</Button></div>
        </div>
      </Modal>
      <ConfirmDialog isOpen={guard.isConfirmOpen} onClose={() => guard.setIsConfirmOpen(false)} onConfirm={() => { if (!workflow.pending) guard.confirmDiscard(); }}
        title={vi ? "Bỏ thay đổi chưa lưu?" : "Discard unsaved changes?"} message={vi ? "Các thay đổi chưa được lưu. Bạn có muốn đóng biểu mẫu?" : "Your changes have not been saved. Close the form?"}
        confirmText={vi ? "Bỏ thay đổi" : "Discard changes"} cancelText={vi ? "Tiếp tục chỉnh sửa" : "Keep editing"} type="warning" />
    </RecordDetailFrame>
  );
};

const Info: React.FC<{ label: string; value: string; icon?: React.ReactNode; tone?: "slate" | "rose"; wide?: boolean }> = ({ label, value, icon, tone = "slate", wide }) => (
  <div className={`rounded-xl border border-slate-100 bg-slate-50 px-4 py-3 ${wide ? "md:col-span-2" : ""}`}>
    <div className="flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-400">{icon}{label}</div>
    <div className={`mt-1.5 text-sm font-medium ${tone === "rose" ? "text-rose-700" : "text-slate-900"}`}>{value}</div>
  </div>
);

const Row: React.FC<{ label: string; value: string }> = ({ label, value }) => <div className="flex items-start justify-between gap-3 rounded-xl bg-slate-50 px-3 py-2.5"><span className="text-xs font-semibold text-slate-500">{label}</span><span className="text-right text-xs font-medium text-slate-800">{value}</span></div>;
const Empty: React.FC<{ text: string }> = ({ text }) => <div className="rounded-xl border border-dashed border-slate-200 bg-slate-50 px-4 py-8 text-center text-xs font-semibold text-slate-400">{text}</div>;
