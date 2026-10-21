import { MeetingActivityCreateModal } from "@/modules/tasks";
import { useCustomerMeeting } from "../hooks/useCustomerMeeting";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useI18n } from "@/i18n";
import { Button, Modal } from "@/shared/components/ui";
import { resolveWorkspaceMemberLabel } from "@/platform/member-directory";
import { RelationshipWorkPanelShell, type RelationshipPanelMode } from "@/components/crm/relationship-panel/RelationshipWorkPanelShell";
import { WorkSection, WorkTaskItem, workHeaderClass, workTitleClass } from "@/components/crm/relationship-panel/RelationshipWorkPresentation";
import { useCustomerPanelData } from "../hooks/useCustomerPanelData";

export function CustomerRelationshipPanel({ customerId, status, visible }: { customerId: string; status?: string; visible: boolean }) {
  const { locale } = useI18n();
  const vi = locale === "vi";
  const text = (a: string, b: string) => vi ? a : b;
  const navigate = useNavigate();
  const [mode, setMode] = useState<RelationshipPanelMode>("WORK");
  const [selectedId, setSelectedId] = useState<string>();
  const data = useCustomerPanelData(customerId, mode === "HISTORY", visible);
  const meeting = useCustomerMeeting(customerId, status, data.taskEntry.refresh);
  useEffect(() => { if (!visible) setSelectedId(undefined); }, [visible]);
  const selected = visible ? data.historyItems.find(item => item.id === selectedId) : undefined;
  const date = (value: string) => Number.isFinite(Date.parse(value)) ? new Date(value).toLocaleString(locale) : text("Thời điểm chưa khả dụng", "Time unavailable");
  function message(kind: "tasks" | "history") {
    const query = kind === "tasks" ? data.tasks : data.history;
    if (!data.readable) return text("Bạn không có quyền đọc công việc và hoạt động.", "You cannot read tasks and activities.");
    if (kind === "history" && !data.historySupported) return text("Lịch sử chưa khả dụng với phạm vi hoặc quyền trường hiện tại.", "History is unavailable for the current scope or field policy.");
    if (kind === "tasks" && !data.tasksEnabled) return text("Công việc chưa khả dụng với quyền trường hiện tại.", "Tasks are unavailable for the current field policy.");
    if (query.loading) return text("Đang tải…", "Loading…");
    if (query.error) return text("Không thể tải dữ liệu trong phạm vi hiện tại.", "Data could not be loaded in the current scope.");
    return "";
  }
  const taskMessage = message("tasks"), historyMessage = message("history");
  const history = <section aria-label={text("Hoạt động khách hàng", "Customer activities")} className="space-y-3">
    {historyMessage && <p role="status">{historyMessage}</p>}
    {data.history.error && data.historySupported && <Button onClick={() => void data.historyEntry.refresh()}>{text("Thử lại", "Retry")}</Button>}
    {!historyMessage && !data.historyItems.length && <p role="status">{text("Không có hoạt động gắn trực tiếp khách hàng trong phạm vi này.", "No directly linked activities in this scope.")}</p>}
    <ol className="space-y-2">{data.historyItems.map(item => <li key={item.id} data-customer-history-id={item.id}><button type="button" aria-haspopup="dialog" onClick={() => setSelectedId(item.id)} className="w-full space-y-1 rounded-lg border border-slate-200 p-3 text-left text-xs focus-visible:outline focus-visible:outline-indigo-600 [overflow-wrap:anywhere]">
      <span className="block text-indigo-600">{item.type}</span><span className="block font-semibold">{item.subject}</span><time dateTime={item.occurredAt}>{date(item.occurredAt)}</time>
    </button></li>)}</ol>
    {!historyMessage && data.history.data?.pageInfo.hasNextPage && <Button disabled={data.historyItems.length >= 500} onClick={() => void data.historyEntry.more()}>{text("Tải thêm hoạt động", "Load more activities")}</Button>}
    {data.historyItems.length >= 500 && <p role="status">{text("Đã tải tối đa 500 hoạt động.", "500-activity display limit reached.")}</p>}
    <p className="text-[10px] text-slate-500">{text("Chỉ gồm hoạt động gắn trực tiếp Customer; không suy diễn từ Task, Contact hoặc Organization. Ghi nhận Email/SMS không xác nhận gửi.", "Direct Customer activities only; no inferred Task, Contact or Organization history. Email/SMS logs do not confirm delivery.")}</p>
    <Modal isOpen={Boolean(selected)} onClose={() => setSelectedId(undefined)} title={text("Chi tiết hoạt động", "Activity detail")} size="sm">{selected && <article className="space-y-3 text-sm [overflow-wrap:anywhere]"><h3>{selected.subject}</h3><time dateTime={selected.occurredAt}>{date(selected.occurredAt)}</time><p>{resolveWorkspaceMemberLabel(selected.actorId, vi ? "vi" : "en")}</p><p className="whitespace-pre-wrap">{selected.body}</p></article>}</Modal>
  </section>;
  return <><MeetingActivityCreateModal targetId={customerId} recordingOnly guardChanges isOpen={meeting.isOpen} onClose={meeting.close} formId="customer-meeting-form" defaults={{ reminder: false }} onSubmit={async () => {}} onSave={meeting.save}/><RelationshipWorkPanelShell isVisible={visible} motionKey={customerId} panelId="customer-right-work-panel" ariaLabel={text("Bảng quan hệ khách hàng", "Customer relationship panel")}
    bodyClassName="max-h-[60vh] space-y-3 overflow-y-auto pr-1 crm-scroll-y xl:max-h-none"
    header={<div className={workHeaderClass}><h2 className={workTitleClass}>{mode === "WORK" ? text("Công việc khách hàng", "Customer work") : text("Lịch sử khách hàng", "Customer history")}</h2></div>}
    mode={{ value: mode, onChange: value => { setMode(value); setSelectedId(undefined); }, workLabel: text("Công việc", "Work"), historyLabel: text("Lịch sử", "History"), label: text("Chế độ bảng quan hệ", "Relationship panel mode") }} history={history}
    footer={<div className="space-y-2"><Button data-guidance-id="customers.detail.meeting" disabled={!meeting.canCreate} onClick={meeting.open}>{text("Tạo cuộc hẹn", "Create meeting")}</Button><p className="text-xs text-slate-500">{text("Cuộc hẹn được lưu thành công việc; chọn chính bạn trong Workspace. Khách hàng lưu trữ không nhận cuộc hẹn mới. Lịch sử không tự sinh hoạt động.", "Meetings are saved as tasks; select yourself in the Workspace. Archived customers cannot receive new meetings. History does not fabricate activities.")}</p></div>}>
    <WorkSection first title={text("Công việc liên quan trực tiếp", "Directly linked work")}>
      {taskMessage && <p role="status">{taskMessage}</p>}
      {data.tasks.error && <Button onClick={() => void data.taskEntry.refresh()}>{text("Thử lại", "Retry")}</Button>}
      {!taskMessage && !data.taskItems.length && <p role="status">{text("Không có công việc gắn trực tiếp khách hàng trong phạm vi này.", "No directly linked tasks in this scope.")}</p>}
      <ul className="space-y-2">{data.taskItems.map(task => <li key={task.id} data-customer-task-id={task.id}><WorkTaskItem title={task.title} onOpen={() => navigate(`/tasks/${task.id}`)} metadata={<>{task.status} · {task.priority} · <time dateTime={task.dueAt}>{date(task.dueAt)}</time> · {resolveWorkspaceMemberLabel(task.assigneeId, vi ? "vi" : "en")}</>}/></li>)}</ul>
      {!taskMessage && data.tasks.data?.pageInfo.hasNextPage && <Button disabled={data.taskItems.length >= 500} onClick={() => void data.taskEntry.more()}>{text("Tải thêm công việc", "Load more tasks")}</Button>}
      {data.taskItems.length >= 500 && <p role="status">{text("Đã tải tối đa 500 công việc.", "500-task display limit reached.")}</p>}
    </WorkSection>
  </RelationshipWorkPanelShell></>;
}
