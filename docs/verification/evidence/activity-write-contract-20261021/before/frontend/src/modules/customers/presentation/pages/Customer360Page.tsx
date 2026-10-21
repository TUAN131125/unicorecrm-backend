import { backendUnavailableMessage, formatApplicationError, formatOperationUnavailableError } from "@/shared/operations";
import { getWorkspaceContextSnapshot } from "@/platform/workspace-context";
import { getDirtyUnsavedWork } from "@/platform/unsaved-work";
import React, { useMemo, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { useNavigate } from "react-router-dom";
import { Sparkles } from "lucide-react";
import { RecordDetailFrame, type RecordAttachmentItem, type RecordAttachmentUploadData } from "@/components/crm/detail-archetype";
import { Button, ConfirmDialog, RecordTabTransition } from "@/shared/components/ui";
import { useI18n } from "@/i18n";
import { getAuthSessionSnapshot } from "@/platform/identity-auth";
import { listWorkspaceMemberDirectory, resolveWorkspaceMemberName } from "@/platform/member-directory";
import {
  completeTaskCommand,
  TaskCreateModal,
  logActivityCommand,
  type Task,
} from "@/modules/tasks";
import { createDealForCustomer, isCustomerCommercialActionsUnavailable } from "@/workflows/customer-commercial-actions";
import { DealFormModal, getDealStagesSnapshot, mapSelectedPickerItemsToDealLineItems, type DealFormDraft } from "@/modules/deals";
import {
  archiveCustomerProductionCommand,
  updateCustomerCommand,
  type Customer,
} from "../../public/api";
import { buildCustomer360ReadModel } from "../model/customer360ReadModel";
import type { Customer360ReadModel as Customer360PageModel } from "../model/customer360ReadModel.types";
import { customerPresentationPreferences } from "../customerPresentationPreferences";
import {
  CustomerDetailTabs,
  type CustomerDetailTab,
} from "../detail/CustomerDetailTabs";
import { CustomerDetailTabContent } from "../detail/CustomerDetailTabContent";
import {
  CustomerEditModal,
  type CustomerEditDraft,
} from "../detail/CustomerEditModal";
import { CustomerInsightPanel } from "../detail/CustomerInsightPanel";
import {
  CustomerQuickActivityModal,
  type CustomerQuickAction,
  type CustomerQuickActivityDraft,
} from "../detail/CustomerQuickActivityModal";
import { CustomerRecordHeader } from "../detail/CustomerRecordHeader";
import type { Customer360Projection } from "../../application/ports/CustomerApiRuntime";
import { isCustomerConnectedApiRuntime } from "../../application/composition/customerApplicationServices";

interface Customer360PageProps {
  customer: Customer;
  authoritativeProjection?: Customer360Projection;
  refreshToken?: unknown;
}

const RIGHT_PANEL_PREFERENCE_KEY = "centrix_customer_right_panel_visible";

export const Customer360Page: React.FC<Customer360PageProps> = ({
  customer,
  authoritativeProjection,
  refreshToken,
}) => {
  const navigate = useNavigate();
  const { locale } = useI18n();
  const isVi = locale === "vi";
  const model = useMemo(
    () => authoritativeProjection ? buildConnectedCustomer360Model(authoritativeProjection) : buildCustomer360ReadModel(customer),
    [authoritativeProjection, customer, refreshToken],
  );
  const session = getAuthSessionSnapshot();
  const currentMemberId = session?.principal.memberId;
  const members = listWorkspaceMemberDirectory();
  const currentActorName =
    session?.principal.displayName || session?.principal.email || "CRM User";
  const defaultOwnerId =
    customer.careOwnerId ||
    model.identity.ownerId ||
    currentMemberId ||
    members[0]?.memberId ||
    "";
  const ownerId = customer.careOwnerId || model.identity.ownerId;
  const ownerName =
    (ownerId ? resolveWorkspaceMemberName(ownerId) : undefined) ||
    session?.principal.displayName ||
    "—";

  const [activeTab, setActiveTab] = useState<CustomerDetailTab>("overview");
  const [requestedSubTab, setRequestedSubTab] = useState<string>();
  const reduceMotion = useReducedMotion();
  const [isRightPanelVisible, setIsRightPanelVisible] = useState(() =>
    customerPresentationPreferences.get(RIGHT_PANEL_PREFERENCE_KEY, true),
  );
  const [editOpen, setEditOpen] = useState(false);
  const [dealOpen, setDealOpen] = useState(false);
  const [taskOpen, setTaskOpen] = useState(false);
  const [quickAction, setQuickAction] = useState<CustomerQuickAction | null>(
    null,
  );
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [savingActivity, setSavingActivity] = useState(false);
  const [completingTaskId, setCompletingTaskId] = useState<string | null>(null);
  const [creatingDeal, setCreatingDeal] = useState(false);
  const [savingIdentity, setSavingIdentity] = useState(false);
  const [toastMessage, setToastMessage] = useState("");
  const [attachmentsByCustomer, setAttachmentsByCustomer] = useState<Record<string, RecordAttachmentItem[]>>({});
  const customerAttachments = attachmentsByCustomer[customer.id] ?? [];
  const attachmentTargetId = customer.id;
  const setCustomerAttachments = (update: (current: RecordAttachmentItem[]) => RecordAttachmentItem[]) => setAttachmentsByCustomer(current => ({ ...current, [attachmentTargetId]: update(current[attachmentTargetId] ?? []) }));

  const noteCount = model.timeline.filter(
    (item) => item.kind === "NOTE",
  ).length;
  // Keep the interaction workspace mounted while an overlay form is open. The modal
  // should layer above the detail page without changing its layout or panel preference.
  const isPanelOpen = isRightPanelVisible;


  const selectCustomerView = (tab: CustomerDetailTab, subTab?: string) => {
    setRequestedSubTab(subTab);
    setActiveTab(tab);
  };

  const showToast = (message: string) => {
    setToastMessage(message);
    window.setTimeout(() => setToastMessage(""), 2800);
  };

  const openDealModal = () => setDealOpen(true);

  const openCareModal = () =>
    navigate(
      `/support/cases/new?customerId=${customer.id}&source=customer_360`,
    );

  const openTaskModal = () => setTaskOpen(true);

  const saveCustomerProfile = async (draft: CustomerEditDraft, opening: Customer360PageModel, isCurrent: () => boolean, intentId: string) => {
    const customer = opening.customer;
    setSavingIdentity(true);
    try {
      await updateCustomerCommand(customer.id, {
        status: draft.status === "ACTIVE" || draft.status === "INACTIVE" ? draft.status : undefined,
        segment: draft.segment.trim(),
        tags: splitTags(draft.tags),
        tier: draft.tier,
        serviceLevel: draft.serviceLevel,
      }, { expectedVersion: customer.resourceVersion, idempotencyKey: intentId });
      if (!isCurrent()) return;
      showToast(
        isVi
          ? "Đã cập nhật hồ sơ Customer."
          : "Customer profile updated.",
      );
    } catch (error) {
      throw error;
    } finally {
      setSavingIdentity(false);
    }
  };

  const dealOpening = React.useRef<Customer | undefined>(undefined);
  const dealWasOpen = React.useRef(false);
  const dealWorkspace = React.useRef(getWorkspaceContextSnapshot().workspaceId);
  React.useEffect(() => {
    if (dealOpen && !dealWasOpen.current) { dealOpening.current = structuredClone(customer); dealWorkspace.current = getWorkspaceContextSnapshot().workspaceId; }
    if (!dealOpen) dealOpening.current = undefined;
    dealWasOpen.current = dealOpen;
  }, [dealOpen, customer]);

  React.useEffect(() => {
    if (dealOpen && dealOpening.current && dealOpening.current.id !== customer.id && !getDirtyUnsavedWork().some(entry => entry.id.startsWith("deal-form:"))) setDealOpen(false);
  }, [dealOpen, customer.id]);

  const createDeal = async (draft: DealFormDraft) => {
    const opening = dealOpening.current;
    if (!opening || dealWorkspace.current !== getWorkspaceContextSnapshot().workspaceId) return false;
    const commandWorkspace = dealWorkspace.current;
    const customer = opening;
    // WF-04 customer-commercial-actions is BLOCKED with
    // `connectedFrontendCoordinatorAllowed: false`. `deal.create` and `task.create` are both
    // ready, so nothing else would stop this from committing a Deal and then a Task for a
    // workflow the backend owns. Refuse on WF-04 itself, before the first command.
    if (isCustomerCommercialActionsUnavailable()) {
      showToast(backendUnavailableMessage({
        locale,
        action: isVi ? "Tạo cơ hội thương mại cho khách hàng" : "Creating a commercial opportunity for this Customer",
      }));
      return false;
    }
    const lineItems = mapSelectedPickerItemsToDealLineItems(draft.lineItems);
    setCreatingDeal(true);
    try {
      const deal = await createDealForCustomer({
      customerId: customer.id,
      id: crypto.randomUUID(),
      name: draft.name,
      amount: draft.amount,
      ownerId: draft.ownerId,
      expectedCloseDate: draft.expectedCloseDate || undefined,
      actorName: currentActorName,
      stage: draft.stage,
      probability: draft.probability,
      forecastCategory: draft.forecastCategory,
      followUpTask: draft.createFollowUpTask && draft.nextActionAt ? {
        title: draft.nextActionSummary,
        dueAt: draft.nextActionAt,
        description: draft.demandSummary || undefined,
      } : undefined,
        notes: [draft.demandSummary, draft.painPoints, draft.notes].filter(Boolean).join(" · "),
        interestedProducts: draft.lineItems.map((item) => item.product.id),
        lineItems,
      });
      if (dealOpening.current !== opening || commandWorkspace !== getWorkspaceContextSnapshot().workspaceId) return true;
      setDealOpen(false);
      navigate(`/deals/${deal.id}`);
      return true;
    } catch (error) {
      throw error;
    } finally {
      setCreatingDeal(false);
    }
  };


  const quickOpening = React.useRef<{ customer: Customer; label: string; workspaceId: string; intentId: string } | undefined>(undefined);
  const quickPending = React.useRef(false);
  const quickMounted = React.useRef(true);
  React.useEffect(() => { quickMounted.current = true; return () => { quickMounted.current = false; }; }, []);
  const quickWasOpen = React.useRef(false);
  const currentQuickTarget = React.useRef(customer.id); currentQuickTarget.current = customer.id;
  React.useEffect(() => {
    if (quickAction && !quickWasOpen.current) quickOpening.current = { customer: structuredClone(customer), label: model.identity.displayName, workspaceId: getWorkspaceContextSnapshot().workspaceId, intentId: crypto.randomUUID() };
    if (!quickAction) quickOpening.current = undefined;
    quickWasOpen.current = Boolean(quickAction);
  }, [quickAction, customer, model.identity.displayName]);
  React.useEffect(() => {
    if (quickAction && quickOpening.current && quickOpening.current.customer.id !== customer.id && !getDirtyUnsavedWork().some(entry => entry.id.startsWith("customers-activity:"))) setQuickAction(null);
  }, [quickAction, customer.id]);

  const saveQuickActivity = async (draft: CustomerQuickActivityDraft): Promise<boolean> => {
    const opening = quickOpening.current;
    if (!quickMounted.current || quickPending.current || currentQuickTarget.current !== opening?.customer.id) return false;
    if (!opening) throw new Error("CUSTOMER_ACTIVITY_TARGET_REQUIRED");
    if (opening.workspaceId !== getWorkspaceContextSnapshot().workspaceId) throw new Error("WORKSPACE_CHANGED");
    const customer = opening.customer;
    if (!currentMemberId) {
      showToast(
        isVi
          ? "Phiên đăng nhập chưa có member hợp lệ."
          : "The active session has no valid member.",
      );
      throw new Error("MEMBER_REQUIRED");
    }
    quickPending.current = true;
    setSavingActivity(true);
    try {
      const outcome = await logActivityCommand({
        id: opening.intentId,
        type: draft.type,
        subject: draft.subject,
        body: draft.body,
        actorId: currentMemberId,
        actorName: currentActorName,
        occurredAt: draft.occurredAt,
        customerId: customer.id,
        relationshipRef: customer.relationshipRef,
        recordRef: {
          moduleKey: "customers",
          recordId: customer.id,
          label: opening.label,
        },
        sourceRef: { type: "CUSTOMER_360", id: customer.id },
      }, { idempotencyKey: `activity:customer:${customer.id}:${opening.intentId}` });
      if (opening.workspaceId !== getWorkspaceContextSnapshot().workspaceId || quickOpening.current !== opening || currentQuickTarget.current !== customer.id || !quickMounted.current
        || outcome.data.recordRef?.moduleKey !== "customers" || outcome.data.recordRef.recordId !== customer.id || outcome.data.type !== draft.type
        || (draft.occurredAt !== undefined && outcome.data.occurredAt !== draft.occurredAt)
        || !["COMMITTED", "REPLAYED", "DEMO_COMMITTED"].includes(outcome.outcome ?? "")) return false;

      showToast(
        isVi
          ? "Đã ghi hoạt động vào timeline Customer 360."
          : "Activity logged to the Customer 360 timeline.",
      );
      return true;
    } catch (error) {
      throw error;
    } finally {
      quickPending.current = false;
      if (quickMounted.current) setSavingActivity(false);
    }
  };

  const completeTask = async (task: Task) => {
    if (completingTaskId) return;
    if (!currentMemberId) {
      showToast(
        isVi
          ? "Phiên đăng nhập chưa có member hợp lệ."
          : "The active session has no valid member.",
      );
      return;
    }
    setCompletingTaskId(task.id);
    try {
      await completeTaskCommand(task.id, {
        actorId: currentMemberId,
        actorName: currentActorName,
        outcome: isVi
          ? "Hoàn thành từ Customer 360"
          : "Completed from Customer 360",
      });
      showToast(isVi ? "Đã hoàn thành công việc." : "Task completed.");
    } catch (error) {
      showToast(formatApplicationError(error, { locale }));
    } finally {
      setCompletingTaskId(null);
    }
  };

  const openSource = () =>
    navigate(
      customer.relationshipRef.type === "CONTACT"
        ? `/contacts/${customer.relationshipRef.id}`
        : `/organizations/${customer.relationshipRef.id}`,
    );

  const toggleRightPanel = () => {
    setIsRightPanelVisible((current) => {
      const next = !current;
      customerPresentationPreferences.set(RIGHT_PANEL_PREFERENCE_KEY, next);
      return next;
    });
  };



  return (
    <RecordDetailFrame id="customer-detail-page-workspace" className="min-w-0 space-y-4 rounded-2xl bg-slate-50 p-1 text-[11px] text-slate-700">
      <CustomerRecordHeader
        model={model}
        ownerName={ownerName}
        onEditClick={() => setEditOpen(true)}
        onCreateOpportunityClick={openDealModal}
        onCreateQuoteClick={() =>
          navigate(`/quotes/new?customerId=${customer.id}`)
        }
        onCreateOrderClick={() =>
          navigate(`/orders/new?customerId=${customer.id}`)
        }
        onCreateCareClick={openCareModal}
        onAddTaskClick={openTaskModal}
        onOpenSourceClick={openSource}
        onArchiveClick={() => setArchiveOpen(true)}
        canEdit={!authoritativeProjection || authoritativeProjection.allowedActions.includes("updateCustomer")}
        canArchive={!authoritativeProjection || authoritativeProjection.allowedActions.includes("archiveCustomer")}
      />

      <div className="relative min-w-0 xl:min-h-[calc(100vh-170px)]">
        <div className="flex min-w-0 flex-col gap-3 xl:min-h-[calc(100vh-170px)] xl:flex-row xl:items-start">
          <main className="w-full min-w-0 xl:flex-1">
            <div className="relative z-10 min-h-[580px] overflow-visible rounded-xl border border-slate-200 bg-white shadow-sm xl:min-h-[calc(100vh-170px)]">
              <CustomerDetailTabs
                activeTab={activeTab}
                setActiveTab={(tab) => selectCustomerView(tab)}
                counts={{
                  overview: 0,
                  relationship:
                    model.identity.contacts.length + noteCount,
                  sales: model.deals.length + model.quotes.length,
                  transactions:
                    model.orders.length +
                    model.invoices.length +
                    model.receivables.length +
                    model.paymentTransactions.length +
                    model.shippingBookings.length +
                    model.returns.length +
                    model.productsPurchased.length,
                  service: model.supportCases.length,
                  work: model.tasks.length + model.timeline.length,
                  attachments: customerAttachments.length,
                }}
                isRightPanelVisible={isPanelOpen}
                onToggleRightPanel={toggleRightPanel}
              />

              <div className="p-4 text-left text-slate-800 sm:p-5">
                <RecordTabTransition transitionKey={activeTab} axis="y" minHeightClassName="min-h-[460px]">
                <CustomerDetailTabContent
                  activeTab={activeTab}
                  requestedSubTab={requestedSubTab}
                  model={model}
                  connected={Boolean(authoritativeProjection)}
                  ownerName={ownerName}
                  onEditIdentity={() => setEditOpen(true)}
                  onCreateOpportunity={openDealModal}
                  onCreateCare={openCareModal}
                  onCreateTask={openTaskModal}
                  onCreateQuickNote={() => setQuickAction("note")}
                  onCompleteTask={completeTask}
                  onOpenSource={openSource}
                  onSelectTab={selectCustomerView}
                  attachments={customerAttachments}
                  onUploadAttachment={(data: RecordAttachmentUploadData) => {
                    setCustomerAttachments((current) => [{
                      id: `customer_attachment_${Date.now()}`,
                      name: data.name,
                      size: data.size || "—",
                      date: new Date().toLocaleDateString(locale === "vi" ? "vi-VN" : "en-US"),
                      category: data.category,
                      description: data.description,
                      file: data.file,
                    }, ...current]);
                    setToastMessage(isVi ? "Đã thêm tài liệu vào phiên làm việc này." : "Attachment added to this session.");
                  }}
                  onDeleteAttachment={(id) => {
                    setCustomerAttachments((current) => current.filter((item) => item.id !== id));
                    setToastMessage(isVi ? "Tài liệu đã được xóa." : "Attachment removed.");
                  }}
                  onDownloadAttachment={(id) => {
                    const attachment = customerAttachments.find((item) => item.id === id);
                    if (!attachment?.file) {
                      setToastMessage(isVi ? "Tệp này chưa có dữ liệu tải xuống trên thiết bị hiện tại." : "This file is not available on the current device.");
                      return;
                    }
                    const url = URL.createObjectURL(attachment.file);
                    const anchor = document.createElement("a");
                    anchor.href = url;
                    anchor.download = attachment.name;
                    document.body.appendChild(anchor);
                    anchor.click();
                    anchor.remove();
                    window.setTimeout(() => URL.revokeObjectURL(url), 0);
                  }}
                />
                </RecordTabTransition>
              </div>
            </div>
          </main>

          <AnimatePresence initial={false} mode="popLayout">
            {isPanelOpen ? (
              <motion.aside
                layout="position"
                key="customer-flow-panel"
                initial={
                  reduceMotion
                    ? false
                    : { opacity: 0, x: 30, scale: 0.985, filter: "blur(3px)" }
                }
                animate={{ opacity: 1, x: 0, scale: 1, filter: "blur(0px)" }}
                exit={
                  reduceMotion
                    ? { opacity: 0 }
                    : { opacity: 0, x: 22, scale: 0.99, filter: "blur(2px)" }
                }
                transition={
                  reduceMotion
                    ? { duration: 0 }
                    : { type: "spring", stiffness: 330, damping: 34, mass: 0.78 }
                }
                className="relative z-10 w-full min-w-0 xl:sticky xl:top-4 xl:h-[calc(100vh-140px)] xl:w-[350px] xl:shrink-0"
              >
                <CustomerInsightPanel
                  model={model}
                  onLogCall={() => setQuickAction("call")}
                  onCreateTask={openTaskModal}
                  onAddMeeting={() => setQuickAction("meeting")}
                  onSendEmail={() => setQuickAction("email")}
                  onSendSms={() => setQuickAction("sms")}
                  onCreateOpportunity={openDealModal}
                  onAddNote={() => setQuickAction("note")}
                  onCreateQuote={() =>
                    navigate(`/quotes/new?customerId=${customer.id}`)
                  }
                  onCreateOrder={() =>
                    navigate(`/orders/new?customerId=${customer.id}`)
                  }
                  onCreateCare={openCareModal}
                  showToast={showToast}
                />
              </motion.aside>
            ) : null}
          </AnimatePresence>
        </div>

      </div>

      {toastMessage && (
        <div className="fixed bottom-5 right-5 z-50 flex max-w-sm items-center gap-2.5 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-xs font-bold text-slate-700 shadow-2xl animate-fade-in text-left">
          <Sparkles size={14} className="text-indigo-600 shrink-0" />
          <span>{toastMessage}</span>
        </div>
      )}

      <CustomerEditModal
        isOpen={editOpen}
        model={model}
        isVi={isVi}
        customerFieldsOnly
        onClose={() => setEditOpen(false)}
        onSave={saveCustomerProfile}
      />

      <CustomerQuickActivityModal
        targetId={quickAction && !quickWasOpen.current ? customer.id : quickOpening.current?.customer.id ?? customer.id}
        action={quickAction}
        isVi={isVi}
        email={model.identity.email}
        phone={model.identity.phone}
        onClose={() => setQuickAction(null)}
        onSave={saveQuickActivity}
      />

      <DealFormModal
        isOpen={dealOpen}
        onClose={() => setDealOpen(false)}
        mode="create"
        initialValues={{
          name: isVi ? `Cơ hội - ${model.identity.displayName}` : `Opportunity - ${model.identity.displayName}`,
          customerName: model.identity.displayName,
          ownerId: defaultOwnerId,
          source: "Customer 360",
        }}
        owners={members.map((member) => ({ memberId: member.memberId, displayName: member.displayName }))}
        stages={getDealStagesSnapshot()}
        customerNameLocked
        sourceKey={dealOpen && !dealWasOpen.current ? customer.id : dealOpening.current?.id ?? customer.id}
        onSubmit={createDeal}
      />

      <TaskCreateModal
        isOpen={taskOpen}
        onClose={() => setTaskOpen(false)}
        title={isVi ? "Thêm công việc cho Customer" : "Add Customer task"}
        context={{
          customerId: customer.id,
          relationshipRef: customer.relationshipRef,
          recordRef: { moduleKey: "customers", recordId: customer.id, label: model.identity.displayName },
          sourceRef: { type: "CUSTOMER_360", id: customer.id },
          label: `${customer.customerCode} · ${model.identity.displayName}`,
        }}
        defaults={{ assigneeId: defaultOwnerId }}
        actorId={currentMemberId}
        actorName={currentActorName}
        onCreated={() => {
          selectCustomerView("work");
          showToast(isVi ? "Đã tạo công việc và gán người xử lý." : "Task created and assigned.");
        }}
        onError={() => showToast(isVi ? "Chưa thể tạo công việc." : "Task could not be created.")}
      />

      <ConfirmDialog
        isOpen={archiveOpen}
        onClose={() => setArchiveOpen(false)}
        onConfirm={async (isCurrent) => {
          await archiveCustomerProductionCommand(customer.id);
          if (!isCurrent()) return;
          setArchiveOpen(false);
          showToast(isVi ? "Đã lưu trữ khách hàng." : "Customer archived.");
        }}
        title={isVi ? "Lưu trữ khách hàng?" : "Archive customer?"}
        description={`${customer.customerCode} · ${model.identity.displayName}`}
        confirmText={isVi ? "Lưu trữ" : "Archive"}
        cancelText={isVi ? "Hủy" : "Cancel"}
        type="danger"
      />
    </RecordDetailFrame>
  );
};

function buildConnectedCustomer360Model(projection: Customer360Projection): Customer360PageModel {
  const metrics = projection.metrics;
  return {
    customer: projection.customer,
    healthAssessment: projection.healthAssessment,
    identity: {
      ...projection.identity,
      ownerId: projection.customer.ownerId ?? undefined,
      contacts: [],
    },
    leads: [], deals: [], quotes: [], orders: [], paymentObligations: [], paymentTransactions: [],
    invoices: [], receivables: [], shippingBookings: [], returns: [], returnIntents: [], supportCases: [],
    tasks: [], activities: [], careCards: [], purchaseEvidence: [], productsPurchased: [],
    timeline: projection.linkedRecords.map((record) => ({
      id: `${record.moduleKey}:${record.recordId}`,
      kind: "CUSTOMER",
      title: record.label ?? `${record.moduleKey} · ${record.recordId}`,
      occurredAt: projection.generatedAt,
      recordRef: { moduleKey: record.moduleKey, recordId: record.recordId },
    })),
    metrics: {
      revenue: metrics.lifetimeRevenue ? Number(metrics.lifetimeRevenue.amount) : undefined,
      leadCount: metrics.leadCount,
      orderCount: metrics.orderCount,
      openTaskCount: metrics.openTaskCount,
      openSupportCount: metrics.openSupportCount,
      openDealCount: metrics.openDealCount,
      quoteCount: metrics.quoteCount,
      openInvoiceCount: metrics.openInvoiceCount,
      overdueReceivableCount: metrics.overdueReceivableCount,
      activeReturnCount: metrics.activeReturnCount,
    },
    integrity: { status: "UNKNOWN" },
  };
}



function parseOptionalNumber(value: string): number | undefined {
  if (!value.trim()) return undefined;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function splitTags(value: string): string[] {
  return [
    ...new Set(
      value
        .split(",")
        .map((item) => item.trim())
        .filter(Boolean),
    ),
  ];
}
