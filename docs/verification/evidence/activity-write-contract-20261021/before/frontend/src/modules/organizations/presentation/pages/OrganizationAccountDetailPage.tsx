import { backendUnavailableMessage, formatApplicationError } from "@/shared/operations";
import { useBoundFormDraft } from "@/shared/hooks/useBoundFormDraft";
import { getWorkspaceContextSnapshot } from "@/platform/workspace-context";
import { getDirtyUnsavedWork } from "@/platform/unsaved-work";
import React, { useMemo, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { AlertCircle, BriefcaseBusiness, Building2, CalendarClock, CheckCircle2, FileCheck2, FileText, Headphones, Package, Plus, Quote, RotateCcw, Truck, WalletCards } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import {
  RecordAttachmentsTab,
  RecordDetailFrame,
  type RecordAttachmentItem,
  type RecordAttachmentUploadData,
} from "@/components/crm/detail-archetype";
import { Button, ConfirmDialog, RecordTabTransition, Textarea } from "@/shared/components/ui";
import {
  RelationshipModuleActions,
  RelationshipWorkspace,
  relationshipRecordPath,
  relationshipWorkspaceLabel,
} from "@/components/crm/relationship-detail";
import { useI18n } from "@/i18n";
import { getContactsSnapshot, subscribeToContacts, type Contact } from "@/modules/contacts";
import { endContactOrganizationRelationshipCommand, isContactOrganizationRelationshipUnavailable, setPrimaryOrganizationRepresentativeCommand } from "@/workflows/contact-organization-relationship";
import { getDealsSnapshot, subscribeToDeals } from "@/modules/deals";
import { findCustomerByRelationshipRefSnapshot, getCustomersSnapshot, subscribeToCustomers } from "@/modules/customers";
import { getInvoicesSnapshot, getReceivablesSnapshot, subscribeToInvoices } from "@/modules/invoices";
import { getPaymentsSnapshot, subscribeToPayments } from "@/modules/payments";
import { getShippingSnapshot, subscribeToShipping } from "@/modules/shipping";
import { getReturnsSnapshot, subscribeToReturns } from "@/modules/returns";
import { getOrderListSnapshot, subscribeToOrderList } from "@/modules/orders";
import { getQuotesSnapshot, subscribeToQuotes } from "@/modules/quotes";
import { getSupportCasesSnapshot, subscribeToSupportCases } from "@/modules/support";
import {
  TaskCreateModal,
  getTaskActivitySnapshot,
  logActivityCommand,
  subscribeToTaskActivity,
} from "@/modules/tasks";
import { formatMoneyDto } from "@/shared/money";
import { relationshipRefKey, type RelationshipRef } from "@/platform/identity";
import { getAuthSessionSnapshot } from "@/platform/identity-auth";
import { listWorkspaceMemberDirectory, resolveWorkspaceMemberName } from "@/platform/member-directory";
import { useEffectiveAccess, useEffectiveRecordAccessDecision } from "@/platform/access-control";
import { recordOperationalAudit } from "@/platform/operational-audit";
import { useSubscribableSnapshot } from "@/platform/react";
import { OrganizationDetailTabs, type OrganizationDetailTab } from "../detail/OrganizationDetailTabs";
import { OrganizationEditModal } from "../detail/OrganizationEditModal";
import { OrganizationCreateOpportunityModal } from "../detail/OrganizationCreateOpportunityModal";
import { OrganizationInsightPanel } from "../detail/OrganizationInsightPanel";
import { OrganizationOverviewTab } from "../detail/OrganizationOverviewTab";
import { OrganizationConnectedWorkspace } from "../detail/OrganizationConnectedWorkspace";
import {
  OrganizationQuickActivityModal,
  type OrganizationQuickAction,
  type OrganizationQuickActivityDraft,
} from "../detail/OrganizationQuickActivityModal";
import { OrganizationRecordHeader } from "../detail/OrganizationRecordHeader";
import { OrganizationRepresentativeModal } from "../detail/OrganizationRepresentativeModal";
import { OrganizationRepresentativesTab } from "../detail/OrganizationRepresentativesTab";
import { organizationPresentationPreferences } from "../organizationPresentationPreferences";
import {
  formatOrganizationCurrency,
  getOrganizationContacts,
  getPrimaryOrganizationContact,
} from "../model/organizationAccountView";
import {
  archiveOrganizationViaApi,
  getOrganizationAccountsSnapshot,
  isOrganizationConnectedMode,
  subscribeToOrganizationAccounts,
  type OrganizationAccount,
} from "../../public/api";

const RIGHT_PANEL_PREFERENCE_KEY = "centrix_organization_right_panel_visible";

export const OrganizationAccountDetailPage: React.FC = () => {
  const { organizationId = "" } = useParams();
  const navigate = useNavigate();
  const { locale } = useI18n();
  const isVi = locale === "vi";
  const text = (vi: string, en: string) => isVi ? vi : en;
  const reduceMotion = useReducedMotion();
  const access = useEffectiveAccess();
  const session = getAuthSessionSnapshot();
  const currentMemberId = session?.principal.memberId;
  const currentActorName = session?.principal.displayName || session?.principal.email || "CRM User";
  const members = listWorkspaceMemberDirectory();

  const accounts = useSubscribableSnapshot(getOrganizationAccountsSnapshot, subscribeToOrganizationAccounts);
  const contacts = useSubscribableSnapshot(getContactsSnapshot, subscribeToContacts);
  const deals = useSubscribableSnapshot(getDealsSnapshot, subscribeToDeals);
  useSubscribableSnapshot(getCustomersSnapshot, subscribeToCustomers);
  const quotes = useSubscribableSnapshot(getQuotesSnapshot, subscribeToQuotes);
  const orders = useSubscribableSnapshot(getOrderListSnapshot, subscribeToOrderList);
  const invoices = useSubscribableSnapshot(getInvoicesSnapshot, subscribeToInvoices);
  const payments = useSubscribableSnapshot(getPaymentsSnapshot, subscribeToPayments);
  const shipping = useSubscribableSnapshot(getShippingSnapshot, subscribeToShipping);
  const returns = useSubscribableSnapshot(getReturnsSnapshot, subscribeToReturns);
  const support = useSubscribableSnapshot(getSupportCasesSnapshot, subscribeToSupportCases);
  const taskSnapshot = useSubscribableSnapshot(getTaskActivitySnapshot, subscribeToTaskActivity);

  const [activeTab, setActiveTab] = useState<OrganizationDetailTab>("overview");
  const [relationshipView, setRelationshipView] = useState<"profile" | "people" | "notes">("people");
  const [salesView, setSalesView] = useState<"opportunities" | "quotations">("opportunities");
  const [transactionView, setTransactionView] = useState<"orders" | "invoices" | "payments" | "shipping" | "returns" | "products" | "history">("orders");
  const [workView, setWorkView] = useState<"tasks" | "activities">("tasks");
  const [showEdit, setShowEdit] = useState(false);
  const [showAddRepresentative, setShowAddRepresentative] = useState(false);
  const [dealOpen, setDealOpen] = useState(false);
  const [quickAction, setQuickAction] = useState<OrganizationQuickAction | null>(null);
  const [taskOpen, setTaskOpen] = useState(false);
  const [isRightPanelVisible, setIsRightPanelVisible] = useState(() =>
    organizationPresentationPreferences.get(RIGHT_PANEL_PREFERENCE_KEY, true),
  );
  const [attachmentsByRecord, setAttachmentsByRecord] = useState<Record<string, RecordAttachmentItem[]>>({});
  const attachmentTargetId = organizationId ?? "";
  const attachments = attachmentsByRecord[attachmentTargetId] ?? [];
  const setAttachments = (update: (current: RecordAttachmentItem[]) => RecordAttachmentItem[]) => setAttachmentsByRecord(current => ({ ...current, [attachmentTargetId]: update(current[attachmentTargetId] ?? []) }));
  const [message, setMessage] = useState<string | null>(null);
  const [savingActivity, setSavingActivity] = useState(false);
  const [endRepresentativeContactId, setEndRepresentativeContactId] = useState<string | null>(null);

  const [archiveOpen, setArchiveOpen] = useState(false);

  const account = accounts.find((item) => item.id === organizationId);
  const linkedCustomer = account ? findCustomerByRelationshipRefSnapshot({ type: "ORGANIZATION_ACCOUNT", id: account.id }) : undefined;
  const actorId = access.memberId || access.accountId || "current-user";
  const endLifecycle = useBoundFormDraft(Boolean(endRepresentativeContactId), `${account?.id}:${endRepresentativeContactId}`, { account, contactId: endRepresentativeContactId, actorId }, () => ({ reason: "" }), "Organization representative relationship", () => setEndRepresentativeContactId(null));
  const endRepresentativeReason = endLifecycle.draft.reason;
  const setEndRepresentativeReason = (reason: string) => endLifecycle.setDraft({ reason });
  const recordAccess = useEffectiveRecordAccessDecision();
  const canEdit = access.canPerform("organizations", "update")
    && (!isOrganizationConnectedMode() || recordAccess?.canUpdate === true);
  const canArchive = access.canPerform("organizations", "delete")
    && (!isOrganizationConnectedMode() || recordAccess?.canDelete === true)
    && access.getFieldAccess("organizations", "status") === "READ_WRITE";
  const canAddRepresentative = access.canPerform("organizations", "update")
    && access.canPerform("contacts", "create")
    && !isContactOrganizationRelationshipUnavailable();

  const related = useMemo(() => {
    if (!account) return emptyRelated();
    const representatives = getOrganizationContacts(account, contacts);
    const representativeIds = new Set(representatives.map((contact) => contact.id));
    const accountRelationshipKey = relationshipRefKey({ type: "ORGANIZATION_ACCOUNT", id: account.id });
    const customerRelationshipKey = linkedCustomer ? relationshipRefKey(linkedCustomer.relationshipRef) : undefined;
    const customerAliases = new Set(linkedCustomer
      ? [linkedCustomer.id, linkedCustomer.customerCode, ...(linkedCustomer.legacyAliases ?? [])]
      : []);
    const relationshipKeys = new Set([accountRelationshipKey, customerRelationshipKey].filter((key): key is string => Boolean(key)));
    const matchesRelationship = (ref?: RelationshipRef) => Boolean(ref && relationshipKeys.has(relationshipRefKey(ref)));
    const matchesCustomerAlias = (customerId?: string) => Boolean(customerId && customerAliases.has(customerId));
    const relatedDeals = deals.filter((deal) => matchesRelationship(deal.buyerRef) || matchesCustomerAlias(deal.customerId));
    const relatedQuotes = quotes.filter((quote) => matchesRelationship(quote.buyerRef) || matchesCustomerAlias(quote.customerId));
    const relatedOrders = orders.filter((order) => matchesRelationship(order.buyerRef) || matchesCustomerAlias(order.customerId));
    const orderIds = new Set(relatedOrders.map((order) => order.id));
    const relatedInvoices = invoices.invoices.filter((invoice) =>
      matchesRelationship(invoice.buyerRef)
      || Boolean(invoice.sourceLinks.orderId && orderIds.has(invoice.sourceLinks.orderId)),
    );
    const invoiceIds = new Set(relatedInvoices.map((invoice) => invoice.id));
    const relatedReceivables = getReceivablesSnapshot().filter((entry) => invoiceIds.has(entry.invoiceId));
    const relatedPayments = payments.transactions.filter((transaction) => matchesRelationship(transaction.buyerRef) || orderIds.has(transaction.orderId));
    const relatedReturns = returns.requests.filter((request) => matchesRelationship(request.buyerRef) || orderIds.has(request.orderId));
    const returnIds = new Set(relatedReturns.map((request) => request.id));
    const relatedShipping = shipping.filter((booking) => (booking.sourceType === "ORDER" && orderIds.has(booking.sourceId)) || (booking.sourceType === "RETURN" && returnIds.has(booking.sourceId)));
    const relatedSupport = support.filter((item) =>
      matchesRelationship(item.relationshipRef)
      || matchesCustomerAlias(item.customerId)
      || orderIds.has(item.relatedOrderId || "")
      || representativeIds.has(item.contactId || ""),
    );
    const relatedTasks = taskSnapshot.tasks.filter((item) =>
      matchesRelationship(item.relationshipRef)
      || matchesCustomerAlias(item.customerId)
      || (item.recordRef?.moduleKey === "organizations" && item.recordRef.recordId === account.id),
    );
    const relatedActivities = taskSnapshot.activities.filter((item) =>
      matchesRelationship(item.relationshipRef)
      || matchesCustomerAlias(item.customerId)
      || (item.recordRef?.moduleKey === "organizations" && item.recordRef.recordId === account.id),
    );
    const productMap = new Map<string, { id: string; title: string; quantity: number; amount: number; orderIds: Set<string> }>();
    for (const order of relatedOrders) {
      for (const line of order.items) {
        const current = productMap.get(line.productId) ?? { id: line.productId, title: line.productNameSnapshot || line.productName || line.name || line.productId, quantity: 0, amount: 0, orderIds: new Set<string>() };
        current.quantity += line.quantity;
        current.amount += line.lineTotal;
        current.orderIds.add(order.id);
        productMap.set(line.productId, current);
      }
    }
    const relatedProducts = [...productMap.values()].map((item) => ({ ...item, orderCount: item.orderIds.size }));
    const relatedHistory = [
      ...relatedOrders.map((order) => ({ id: `orders:${order.id}`, title: `${text("Đơn hàng", "Order")} ${order.orderNumber}`, badge: order.state, value: formatOrganizationCurrency(order.grandTotal ?? order.totalAmount ?? 0), meta: formatDate(order.orderDate), icon: <Package size={15} /> })),
      ...relatedInvoices.map((invoice) => ({ id: `invoices:${invoice.id}`, title: `${text("Hóa đơn", "Invoice")} ${invoice.invoiceNumber || text("nháp", "draft")}`, badge: invoice.lifecycleState, value: formatMoneyDto(invoice.totals.grandTotal, isVi ? "vi-VN" : "en-US"), meta: formatDate(invoice.issueDate || invoice.createdAt), icon: <FileCheck2 size={15} /> })),
      ...relatedPayments.map((payment) => ({ id: `payments:${payment.id}`, title: payment.kind === "REFUND" ? text("Hoàn tiền", "Refund") : text("Thanh toán", "Payment"), badge: payment.status, value: formatOrganizationCurrency(payment.amount), meta: formatDateTime(payment.occurredAt), icon: <WalletCards size={15} /> })),
      ...relatedShipping.map((booking) => ({ id: `shipping:${booking.id}`, title: `${text("Vận đơn", "Shipping")} ${booking.code}`, badge: booking.externalStatus, value: booking.trackingCode, meta: formatDateTime(booking.updatedAt), icon: <Truck size={15} /> })),
      ...relatedReturns.map((request) => ({ id: `returns:${request.id}`, title: `${text("Đổi / Trả", "Return")} ${request.code}`, badge: request.status, value: `${request.items.length} ${text("dòng", "lines")}`, meta: formatDateTime(request.updatedAt), icon: <RotateCcw size={15} /> })),
    ];
    return {
      representatives,
      deals: relatedDeals,
      quotes: relatedQuotes,
      orders: relatedOrders,
      invoices: relatedInvoices,
      receivables: relatedReceivables,
      payments: relatedPayments,
      shipping: relatedShipping,
      returns: relatedReturns,
      support: relatedSupport,
      tasks: relatedTasks,
      activities: relatedActivities,
      products: relatedProducts,
      history: relatedHistory,
    };
  }, [account, contacts, deals, invoices.invoices, linkedCustomer, orders, payments.transactions, quotes, returns.requests, shipping, support, taskSnapshot]);
  const primaryContact = account ? getPrimaryOrganizationContact(account, contacts) : undefined;
  const openDeals = related.deals.filter((deal) => deal.stage !== "WON" && deal.stage !== "LOST");
  const pipelineValue = openDeals.reduce((total, deal) => total + deal.amount, 0);
  const orderValue = related.orders.reduce((total, order) => total + (order.grandTotal ?? order.totalAmount ?? 0), 0);
  const openTasks = related.tasks.filter((task) => task.status === "OPEN");
  const ownerName = resolveWorkspaceMemberName(account?.ownerId) || account?.ownerId || text("Chưa phân công", "Unassigned");
  const communicationPhone = primaryContact?.phone || primaryContact?.mobilePhone || account?.phone || "";
  const communicationEmail = primaryContact?.workEmail || primaryContact?.email || account?.email || "";
  const canCommunicate = account?.status !== "archived" && account?.status !== "inactive";

  const quickOpening = React.useRef<{ account: OrganizationAccount; customerId: string | undefined; workspaceId: string; intentId: string } | undefined>(undefined);
  const quickPending = React.useRef(false);
  const quickMounted = React.useRef(true);
  React.useEffect(() => { quickMounted.current = true; return () => { quickMounted.current = false; }; }, []);
  const quickWasOpen = React.useRef(false);
  const currentQuickTarget = React.useRef(account?.id); currentQuickTarget.current = account?.id;
  React.useEffect(() => {
    if (quickAction && account && !quickWasOpen.current) quickOpening.current = { account: structuredClone(account), customerId: linkedCustomer?.id, workspaceId: getWorkspaceContextSnapshot().workspaceId, intentId: crypto.randomUUID() };
    if (!quickAction) quickOpening.current = undefined;
    quickWasOpen.current = Boolean(quickAction);
  }, [quickAction, account, linkedCustomer?.id]);

  React.useEffect(() => {
    if (quickAction && quickOpening.current && quickOpening.current.account.id !== account?.id && !getDirtyUnsavedWork().some(entry => entry.id.startsWith("organizations-activity:"))) setQuickAction(null);
  }, [quickAction, account?.id]);

  if (!account) {
    return (
      <div className="mx-auto max-w-3xl rounded-2xl border border-slate-200 bg-white p-10 text-center shadow-sm">
        <AlertCircle size={34} className="mx-auto text-rose-500" />
        <h2 className="mt-3 text-sm font-semibold text-slate-900">{text("Không tìm thấy tổ chức", "Organization not found")}</h2>
        <p className="mt-2 text-xs text-slate-500">{text("Hồ sơ tổ chức có thể đã thay đổi hoặc không thuộc workspace hiện tại.", "The organization may have changed or may not belong to the current workspace.")}</p>
        <button type="button" onClick={() => navigate("..")} className="mt-4 rounded-xl bg-violet-600 px-4 py-2.5 text-xs font-semibold text-white hover:bg-violet-700">{text("Quay lại danh sách", "Back to list")}</button>
      </div>
    );
  }

  const setPrimaryRepresentative = async (contactId: string) => {
    // `contact-organization.set-primary-representative` is a BLOCKED canonical command:
    // refuse before the mutation is started.
    if (isContactOrganizationRelationshipUnavailable()) {
      setMessage(backendUnavailableMessage({ locale, action: text("Đổi đại diện chính", "Changing the primary representative") }));
      return;
    }
    try {
      await setPrimaryOrganizationRepresentativeCommand({ organizationAccountId: account.id, contactId, actorId });
      recordOperationalAudit({ moduleKey: "organizations", recordId: account.id, action: "PrimaryRepresentativeChanged", actorId, actorName: currentActorName, before: { primaryContactId: account.primaryContactId }, after: { primaryContactId: contactId } });
      setMessage(text("Đã cập nhật cá nhân đại diện chính.", "Primary representative updated."));
    } catch (caught) {
      setMessage(formatApplicationError(caught, { locale }));
    }
  };

  const requestEndRepresentativeRelationship = (contactId: string) => {
    if (endLifecycle.pending || endLifecycle.dirty) return;
    setEndRepresentativeContactId(contactId);
  };

  const confirmEndRepresentativeRelationship = async (): Promise<boolean> => {
    const opening = endLifecycle.opening.current;
    if (!opening.account || !opening.contactId || !endRepresentativeReason.trim()) {
      document.getElementById("organization-end-representative-reason")?.focus(); return false;
    }
    if (isContactOrganizationRelationshipUnavailable()) {
      setMessage(backendUnavailableMessage({ locale, action: text("Kết thúc quan hệ đại diện", "Ending the representative relationship") })); return false;
    }
    const operation = endLifecycle.begin(); if (!operation) return false;
    try {
      await endContactOrganizationRelationshipCommand({ contactId: opening.contactId, organizationAccountId: opening.account.id, actorId: opening.actorId, reason: endRepresentativeReason.trim() });
      if (!operation.isCurrent()) return false;
      operation.complete(); setMessage(text("Đã kết thúc quan hệ đại diện và giữ lịch sử.", "The representative relationship ended and remains in history."));
      setEndRepresentativeContactId(null); return true;
    } catch (caught) {
      if (operation.isCurrent()) setMessage(formatApplicationError(caught, { locale })); return false;
    } finally { operation.finish(); }
  };
  endLifecycle.bindSave(confirmEndRepresentativeRelationship);

  const toggleRightPanel = () => {
    setIsRightPanelVisible((current) => {
      const next = !current;
      organizationPresentationPreferences.set(RIGHT_PANEL_PREFERENCE_KEY, next);
      return next;
    });
  };

  const openTaskModal = () => setTaskOpen(true);

  const saveQuickActivity = async (draft: OrganizationQuickActivityDraft): Promise<boolean> => {
    const opening = quickOpening.current;
    if (!quickMounted.current || quickPending.current || currentQuickTarget.current !== opening?.account.id) return false;
    if (!opening) throw new Error("ORGANIZATION_ACTIVITY_TARGET_REQUIRED");
    if (opening.workspaceId !== getWorkspaceContextSnapshot().workspaceId) throw new Error("WORKSPACE_CHANGED");
    const account = opening.account;
    if (!currentMemberId) {
      setMessage(text("Phiên đăng nhập chưa có member hợp lệ.", "The active session has no valid member."));
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
        customerId: opening.customerId,
        relationshipRef: { type: "ORGANIZATION_ACCOUNT", id: account.id },
        recordRef: { moduleKey: "organizations", recordId: account.id, label: account.displayName },
        sourceRef: { type: "ORGANIZATION_DETAIL", id: account.id },
      }, { idempotencyKey: `activity:account:${account.id}:${opening.intentId}` });
      if (opening.workspaceId !== getWorkspaceContextSnapshot().workspaceId || quickOpening.current !== opening || currentQuickTarget.current !== account.id || !quickMounted.current
        || outcome.data.recordRef?.moduleKey !== "organizations" || outcome.data.recordRef.recordId !== account.id || outcome.data.type !== draft.type
        || (draft.occurredAt !== undefined && outcome.data.occurredAt !== draft.occurredAt)
        || !["COMMITTED", "REPLAYED", "DEMO_COMMITTED"].includes(outcome.outcome ?? "")) return false;

      setMessage(text("Đã ghi hoạt động vào timeline tổ chức.", "Activity added to the organization timeline."));
      return true;
    } catch (error) {
      throw error;
    } finally {
      quickPending.current = false;
      if (quickMounted.current) setSavingActivity(false);
    }
  };

  const uploadAttachment = (data: RecordAttachmentUploadData) => {
    setAttachments((current) => [{
      id: `organization_attachment_${crypto.randomUUID()}`,
      name: data.name,
      size: data.size || "—",
      date: new Date().toLocaleDateString(isVi ? "vi-VN" : "en-US"),
      category: data.category,
      description: data.description,
      file: data.file,
    }, ...current]);
    setMessage(text("Đã thêm tài liệu vào phiên làm việc này.", "Attachment added to this session."));
  };

  return (
    <RecordDetailFrame id="organization-account-detail-page" className="min-w-0 space-y-4 overflow-x-hidden rounded-2xl bg-slate-50 p-1 text-[11px] text-slate-700">
      {isOrganizationConnectedMode() ? <OrganizationConnectedWorkspace key={account.id} account={account} canEdit={canEdit} onEdit={() => setShowEdit(true)} message={message} onArchive={account.status !== "archived" && canArchive ? () => setArchiveOpen(true) : undefined}/> : <>
      <OrganizationRecordHeader
        account={account}
        primaryContact={primaryContact}
        representativeCount={related.representatives.length}
        customerId={linkedCustomer?.id}
        onOpenCustomer={() => linkedCustomer && navigate(`/customers/${linkedCustomer.id}`)}
        onBack={() => navigate("..")}
        onEdit={() => setShowEdit(true)}
        onAddRepresentative={() => setShowAddRepresentative(true)}
        canEdit={canEdit}
        canAddRepresentative={canAddRepresentative}
        onArchive={isOrganizationConnectedMode() && account.status !== "archived" && canArchive ? () => setArchiveOpen(true) : undefined}
      />

      {message && <div className="rounded-xl border border-violet-100 bg-violet-50 px-4 py-3 text-xs font-semibold text-violet-800">{message}</div>}

      <div className="relative min-w-0 xl:min-h-[calc(100vh-170px)]">
        <div className="flex min-w-0 flex-col gap-3 xl:min-h-[calc(100vh-170px)] xl:flex-row xl:items-start">
        <main className="w-full min-w-0 xl:flex-1">
          <div className="min-h-[580px] overflow-visible rounded-xl border border-slate-200 bg-white shadow-sm xl:min-h-[calc(100vh-170px)]">
            <OrganizationDetailTabs
              activeTab={activeTab}
              onChange={setActiveTab}
              isRightPanelVisible={isRightPanelVisible}
              onToggleRightPanel={toggleRightPanel}
              counts={{
                relationship: related.representatives.length + related.activities.filter((activity) => activity.type === "NOTE").length + (linkedCustomer ? 1 : 0),
                sales: related.deals.length + related.quotes.length,
                transactions: related.orders.length + related.invoices.length + related.payments.length + related.receivables.length + related.shipping.length + related.returns.length + related.products.length,
                service: related.support.length,
                work: related.tasks.length + related.activities.length,
                attachments: attachments.length,
              }}
            />
            <div className="p-4 sm:p-5">
              <RecordTabTransition transitionKey={activeTab} axis="y" minHeightClassName="min-h-[460px]">
                {activeTab === "overview" && (
                  <OrganizationOverviewTab
                    account={account}
                    primaryContact={primaryContact}
                    ownerName={ownerName}
                    representativeCount={related.representatives.length}
                    openDealsCount={openDeals.length}
                    pipelineValue={pipelineValue}
                    orderValue={orderValue}
                    quotesCount={related.quotes.length}
                    openTasksCount={openTasks.length}
                    supportCount={related.support.length}
                    onOpenPrimaryContact={() => primaryContact && navigate(`/contacts/${primaryContact.id}`)}
                    onAddRepresentative={() => setShowAddRepresentative(true)}
                    onSelectTab={(tab, subTab) => {
                      setActiveTab(tab);
                      if (tab === "relationship" && subTab) setRelationshipView(subTab === "representatives" ? "people" : subTab as "profile" | "people" | "notes");
                      if (tab === "sales" && subTab) setSalesView(subTab === "deals" ? "opportunities" : subTab === "quotes" ? "quotations" : subTab as "opportunities" | "quotations");
                      if (tab === "transactions" && subTab) setTransactionView(subTab as "orders" | "invoices" | "payments" | "shipping" | "returns" | "products" | "history");
                      if (tab === "work" && subTab) setWorkView(subTab as "tasks" | "activities");
                    }}
                  />
                )}

                {activeTab === "relationship" && (
                  <RelationshipWorkspace
                    workspaceKey="organization-relationship"
                    activeId={relationshipView}
                    onChange={(id) => setRelationshipView(id as "profile" | "people" | "notes")}
                    items={[
                      { id: "profile", label: relationshipWorkspaceLabel("profile", isVi) },
                      { id: "people", label: relationshipWorkspaceLabel("people", isVi), count: related.representatives.length },
                      { id: "notes", label: relationshipWorkspaceLabel("notes", isVi), count: related.activities.filter((activity) => activity.type === "NOTE").length },
                    ]}
                  >
                    {relationshipView === "profile" ? (
                      <OrganizationProfilePanel account={account} linkedCustomerName={linkedCustomer ? account.displayName : undefined} ownerName={ownerName} onOpenCustomer={linkedCustomer ? () => navigate(`/customers/${linkedCustomer.id}`) : undefined} />
                    ) : relationshipView === "people" ? (
                      <OrganizationRepresentativesTab
                        contacts={related.representatives}
                        organizationAccountId={account.id}
                        primaryContactId={primaryContact?.id}
                        canEdit={canEdit && !isContactOrganizationRelationshipUnavailable()}
                        onOpenContact={(id) => navigate(`/contacts/${id}`)}
                        onSetPrimary={setPrimaryRepresentative}
                        onEndRelationship={requestEndRepresentativeRelationship}
                        onAddRepresentative={() => setShowAddRepresentative(true)}
                      />
                    ) : (
                      <WorkCollection
                        title={text("Ghi chú quan hệ", "Relationship notes")}
                        empty={text("Chưa có ghi chú ở cấp tổ chức.", "No organization-level notes yet.")}
                        rows={related.activities.filter((activity) => activity.type === "NOTE").map((activity) => ({ id: activity.id, title: activity.subject, badge: text("Ghi chú", "Note"), meta: formatDateTime(activity.occurredAt) }))}
                        action={<RelationshipModuleActions primaryLabel={text("Thêm ghi chú", "Add note")} onPrimary={() => setQuickAction("note")} />}
                      />
                    )}
                  </RelationshipWorkspace>
                )}

                {activeTab === "sales" && (
                  <RelationshipWorkspace
                    workspaceKey="organization-sales"
                    activeId={salesView}
                    onChange={(id) => setSalesView(id as "opportunities" | "quotations")}
                    items={[
                      { id: "opportunities", label: relationshipWorkspaceLabel("opportunities", isVi), count: related.deals.length },
                      { id: "quotations", label: relationshipWorkspaceLabel("quotations", isVi), count: related.quotes.length },
                    ]}
                  >
                    {salesView === "opportunities" ? (
                      <RecordCollection
                        title={text("Cơ hội của tổ chức", "Organization opportunities")}
                        action={<RelationshipModuleActions secondaryLabel={text("Mở pipeline", "Open pipeline")} primaryLabel={text("Tạo cơ hội", "Create opportunity")} onSecondary={() => navigate("/deals")} onPrimary={() => setDealOpen(true)} />}
                        emptyTitle={text("Chưa có cơ hội B2B", "No B2B opportunities")}
                        emptyDescription={text("Tạo cơ hội từ hồ sơ tổ chức để giữ buyerRef và người đại diện xuyên suốt.", "Create the opportunity from this organization so buyerRef and representatives remain connected.")}
                        rows={related.deals.map((deal) => ({ id: deal.id, title: deal.name, badge: String(deal.stage), value: formatOrganizationCurrency(deal.amount), meta: `${text("Xác suất", "Probability")} ${deal.opportunityScore}% · ${text("Dự kiến chốt", "Expected close")} ${formatDate(deal.expectedCloseDate)}`, icon: <BriefcaseBusiness size={15} /> }))}
                        onOpen={(id) => navigate(`/deals/${id}`)}
                      />
                    ) : (
                      <RecordCollection
                        title={text("Báo giá của tổ chức", "Organization quotes")}
                        action={<RelationshipModuleActions secondaryLabel={text("Mở Báo giá", "Open Quotes")} primaryLabel={text("Tạo báo giá", "Create quote")} onSecondary={() => navigate("/quotes")} onPrimary={() => navigate(`/quotes/new?organizationId=${account.id}`)} />}
                        emptyTitle={text("Chưa có báo giá cho tổ chức", "No organization quotes")}
                        emptyDescription={text("Báo giá được nối với cơ hội và buyerRef của tổ chức.", "Quotes are connected to opportunities and the organization's buyerRef.")}
                        rows={related.quotes.map((quote) => ({ id: quote.id, title: `${quote.quoteNumber} · ${quote.title}`, badge: quote.status, value: formatOrganizationCurrency(quote.grandTotal), meta: `${text("Phiên bản", "Version")} ${quote.version}${quote.validUntil ? ` · ${text("Hiệu lực đến", "Valid until")} ${formatDate(quote.validUntil)}` : ""}`, icon: <FileText size={15} /> }))}
                        onOpen={(id) => navigate(`/quotes/${id}`)}
                      />
                    )}
                  </RelationshipWorkspace>
                )}

                {activeTab === "transactions" && (
                  <RelationshipWorkspace
                    workspaceKey="organization-transactions"
                    activeId={transactionView}
                    onChange={(id) => setTransactionView(id as "orders" | "invoices" | "payments" | "shipping" | "returns" | "products" | "history")}
                    items={[
                      { id: "orders", label: relationshipWorkspaceLabel("orders", isVi), count: related.orders.length },
                      { id: "invoices", label: relationshipWorkspaceLabel("invoices", isVi), count: related.invoices.length },
                      { id: "payments", label: relationshipWorkspaceLabel("payments", isVi), count: related.payments.length + related.receivables.length },
                      { id: "shipping", label: relationshipWorkspaceLabel("shipping", isVi), count: related.shipping.length },
                      { id: "returns", label: relationshipWorkspaceLabel("returns", isVi), count: related.returns.length },
                      { id: "products", label: relationshipWorkspaceLabel("products", isVi), count: related.products.length },
                      { id: "history", label: relationshipWorkspaceLabel("history", isVi), count: related.history.length },
                    ]}
                  >
                    {transactionView === "orders" && <RecordCollection title={text("Đơn hàng của tổ chức", "Organization orders")} action={<RelationshipModuleActions secondaryLabel={text("Mở Đơn hàng", "Open Orders")} primaryLabel={text("Tạo đơn hàng", "Create order")} onSecondary={() => navigate("/orders")} onPrimary={() => navigate(`/orders/new?organizationId=${account.id}`)} />} emptyTitle={text("Chưa có đơn hàng B2B", "No B2B orders")} emptyDescription={text("Đơn hàng thuộc tổ chức khi buyerRef trỏ tới hồ sơ này.", "Orders belong to this organization through buyerRef.")} rows={related.orders.map((order) => ({ id: order.id, title: order.orderNumber, badge: order.state, value: formatOrganizationCurrency(order.grandTotal ?? order.totalAmount ?? 0), meta: `${order.items.length} ${text("dòng hàng", "lines")} · ${formatDate(order.orderDate)}`, icon: <Package size={15} /> }))} onOpen={(id) => navigate(`/orders/${id}`)} />}
                    {transactionView === "invoices" && <RecordCollection title={text("Hóa đơn của tổ chức", "Organization invoices")} action={<RelationshipModuleActions secondaryLabel={text("Mở Hóa đơn", "Open Invoices")} primaryLabel={text("Tạo hóa đơn", "Create invoice")} onSecondary={() => navigate("/invoices")} onPrimary={() => navigate(`/invoices/new?organizationId=${account.id}`)} />} emptyTitle={text("Chưa có hóa đơn cho tổ chức", "No organization invoices")} emptyDescription={text("Hóa đơn chính thức được lấy từ module Hóa đơn theo buyerRef hoặc đơn hàng của tổ chức.", "Authoritative invoices come from the Invoice module through buyerRef or organization orders.")} rows={related.invoices.map((invoice) => ({ id: invoice.id, title: invoice.invoiceNumber || text("Hóa đơn nháp", "Draft invoice"), badge: invoice.lifecycleState, value: formatMoneyDto(invoice.totals.grandTotal, isVi ? "vi-VN" : "en-US"), meta: `${invoice.issueDate ? formatDate(invoice.issueDate) : formatDate(invoice.createdAt)}${invoice.dueDate ? ` · ${text("Hạn", "Due")} ${formatDate(invoice.dueDate)}` : ""}`, icon: <FileCheck2 size={15} /> }))} onOpen={(id) => navigate(`/invoices/${id}`)} />}
                    {transactionView === "payments" && <RecordCollection title={text("Thanh toán & công nợ", "Payments & receivables")} action={<RelationshipModuleActions secondaryLabel={text("Mở Thanh toán", "Open Payments")} primaryLabel={text("Mở Công nợ", "Open Receivables")} onSecondary={() => navigate("/payments")} onPrimary={() => navigate("/receivables")} />} emptyTitle={text("Chưa có thanh toán hoặc công nợ", "No payments or receivables")} emptyDescription={text("Dữ liệu được nối qua đơn hàng và hóa đơn của tổ chức.", "Data is connected through the organization's orders and invoices.")} rows={[...related.receivables.map((entry) => ({ id: `receivable:${entry.invoiceId}`, title: `${text("Công nợ", "Receivable")} · ${entry.invoiceNumber}`, badge: entry.settlementState, value: formatMoneyDto(entry.outstandingAmount, isVi ? "vi-VN" : "en-US"), meta: entry.agingBucket, icon: <WalletCards size={15} /> })), ...related.payments.map((payment) => ({ id: `payment:${payment.id}`, title: `${payment.kind} · ${payment.id}`, badge: payment.status, value: formatOrganizationCurrency(payment.amount), meta: formatDateTime(payment.occurredAt), icon: <WalletCards size={15} /> }))]} onOpen={(id) => id.startsWith("receivable:") ? navigate(`/receivables/${id.slice(11)}`) : navigate("/payments")} />}
                    {transactionView === "shipping" && <RecordCollection title={text("Vận đơn liên quan", "Related shipping bookings")} action={<RelationshipModuleActions secondaryLabel={text("Mở Vận đơn", "Open Shipping")} primaryLabel={text("Xem đơn đủ điều kiện", "Review eligible orders")} onSecondary={() => navigate("/shipping")} onPrimary={() => setTransactionView("orders")} />} emptyTitle={text("Chưa có vận đơn", "No shipping bookings")} emptyDescription={text("Vận đơn được nối qua đơn hàng hoặc yêu cầu đổi/trả của tổ chức.", "Shipping bookings are connected through organization orders or returns.")} rows={related.shipping.map((booking) => ({ id: booking.id, title: booking.code, badge: booking.externalStatus, value: booking.trackingCode, meta: `${booking.purpose} · ${booking.providerNameSnapshot}`, icon: <Truck size={15} /> }))} onOpen={(id) => navigate(`/shipping/${id}`)} />}
                    {transactionView === "returns" && <RecordCollection title={text("Yêu cầu đổi / trả", "Return requests")} action={<RelationshipModuleActions secondaryLabel={text("Mở Đổi / Trả", "Open Returns")} primaryLabel={text("Xem đơn hàng", "Review orders")} onSecondary={() => navigate("/returns")} onPrimary={() => setTransactionView("orders")} />} emptyTitle={text("Chưa có yêu cầu đổi / trả", "No return requests")} emptyDescription={text("Yêu cầu đổi/trả giữ liên kết tới đơn hàng và buyerRef của tổ chức.", "Returns retain links to the order and organization buyerRef.")} rows={related.returns.map((request) => ({ id: request.id, title: request.code, badge: request.status, value: `${request.items.length} ${text("dòng", "lines")}`, meta: `${request.reason} · ${formatDate(request.requestedAt)}`, icon: <RotateCcw size={15} /> }))} onOpen={(id) => navigate(`/returns/${id}`)} />}
                    {transactionView === "products" && <RecordCollection title={text("Hàng hóa đã mua", "Purchased items")} action={<RelationshipModuleActions secondaryLabel={text("Mở Sản phẩm", "Open Products")} primaryLabel={text("Xem đơn hàng", "Review orders")} onSecondary={() => navigate("/products")} onPrimary={() => setTransactionView("orders")} />} emptyTitle={text("Chưa có hàng hóa đã mua", "No purchased items")} emptyDescription={text("Hàng hóa được tổng hợp từ các dòng đơn hàng thuộc tổ chức.", "Items are aggregated from order lines belonging to the organization.")} rows={related.products.map((product) => ({ id: product.id, title: product.title, badge: `${product.quantity} ${text("sản phẩm", "items")}`, value: formatOrganizationCurrency(product.amount), meta: `${product.orderCount} ${text("đơn hàng", "orders")}`, icon: <Package size={15} /> }))} onOpen={(id) => navigate(`/products/${id}`)} />}
                    {transactionView === "history" && <RecordCollection title={text("Lịch sử giao dịch xuyên module", "Cross-module transaction history")} action={<Button size="sm" variant="secondary" onClick={() => navigate("/orders")}>{text("Mở trung tâm Đơn hàng", "Open Orders workspace")}</Button>} emptyTitle={text("Chưa có lịch sử giao dịch", "No transaction history")} emptyDescription={text("Lịch sử tự tổng hợp từ Đơn hàng, Hóa đơn, Thanh toán, Vận đơn và Đổi / Trả.", "History is assembled from Orders, Invoices, Payments, Shipping, and Returns.")} rows={related.history} onOpen={(id) => { const [moduleKey, recordId] = id.split(":"); navigate(relationshipRecordPath(moduleKey, recordId)); }} />}
                  </RelationshipWorkspace>
                )}

                {activeTab === "service" && (
                  <RelationshipWorkspace
                    workspaceKey="organization-service"
                    activeId="support"
                    onChange={() => undefined}
                    items={[{ id: "support", label: relationshipWorkspaceLabel("support", isVi), count: related.support.length }]}
                  >
                    <RecordCollection title={text("Phiếu hỗ trợ của tổ chức", "Organization support cases")} action={<RelationshipModuleActions secondaryLabel={text("Mở Hỗ trợ", "Open Support")} primaryLabel={text("Tạo phiếu hỗ trợ", "Create support case")} onSecondary={() => navigate("/support/cases")} onPrimary={() => navigate(`/support/cases/new?organizationId=${account.id}${linkedCustomer ? `&customerId=${linkedCustomer.id}` : ""}${primaryContact ? `&contactId=${primaryContact.id}` : ""}`)} />} emptyTitle={text("Chưa có phiếu hỗ trợ liên quan", "No related support tickets")} emptyDescription={text("Phiếu hỗ trợ được nối qua tổ chức, Customer 360, đơn hàng hoặc đại diện.", "Support cases connect through the organization, Customer 360, orders, or representatives.")} rows={related.support.map((item) => ({ id: item.id, title: `${item.caseNumber} · ${item.title}`, badge: item.status, value: priorityLabel(item.priority), meta: `${item.category} · ${item.slaStatus}`, icon: <Headphones size={15} /> }))} onOpen={(id) => navigate(`/support/cases/${id}`)} />
                  </RelationshipWorkspace>
                )}

                {activeTab === "work" && (
                  <RelationshipWorkspace workspaceKey="organization-work" activeId={workView} onChange={(id) => setWorkView(id as "tasks" | "activities")} items={[{ id: "tasks", label: relationshipWorkspaceLabel("tasks", isVi), count: related.tasks.length }, { id: "activities", label: relationshipWorkspaceLabel("activities", isVi), count: related.activities.length }]}>
                    {workView === "tasks" ? <WorkCollection title={text("Công việc", "Tasks")} empty={text("Chưa có công việc ở cấp quan hệ tổ chức.", "No organization-level tasks yet.")} rows={related.tasks.map((task) => ({ id: task.id, title: task.title, badge: task.status, meta: `${priorityLabel(task.priority)} · ${text("Hạn", "Due")} ${formatDate(task.dueAt)}` }))} onOpen={(id) => navigate(`/tasks/${id}`)} action={<Button size="sm" variant="primary" icon={<Plus size={13} />} onClick={openTaskModal}>{text("Tạo công việc", "Create task")}</Button>} /> : <WorkCollection title={text("Hoạt động", "Activities")} empty={text("Chưa có hoạt động ở cấp tổ chức.", "No organization-level activity yet.")} rows={related.activities.map((activity) => ({ id: activity.id, title: activity.subject, badge: activityTypeLabel(activity.type), meta: formatDateTime(activity.occurredAt) }))} action={<Button size="sm" variant="secondary" onClick={() => setQuickAction("note")}>{text("Ghi hoạt động", "Log activity")}</Button>} />}
                  </RelationshipWorkspace>
                )}

                {activeTab === "attachments" && (
                  <RecordAttachmentsTab idPrefix="organization" recordId={account.id} attachments={attachments} onUploadAttachment={isOrganizationConnectedMode() ? undefined : uploadAttachment} onDeleteAttachment={isOrganizationConnectedMode() ? undefined : (id) => setAttachments((current) => current.filter((item) => item.id !== id))} onDownloadAttachment={(id) => { const attachment = attachments.find((item) => item.id === id); if (!attachment?.file) { setMessage(text("Tệp chưa có dữ liệu trên thiết bị hiện tại.", "The file is not available on this device.")); return; } const url = URL.createObjectURL(attachment.file); const anchor = document.createElement("a"); anchor.href = url; anchor.download = attachment.name; document.body.appendChild(anchor); anchor.click(); anchor.remove(); window.setTimeout(() => URL.revokeObjectURL(url), 0); }} isArchived={account.status === "archived"} title={text("Tài liệu của tổ chức", "Organization attachments")} />
                )}
              </RecordTabTransition>
            </div>
          </div>
        </main>

        <AnimatePresence initial={false} mode="popLayout">
          {isRightPanelVisible ? (
            <motion.aside
              layout="position"
              key="organization-interaction-panel"
              initial={reduceMotion ? false : { opacity: 0, x: 30, scale: 0.985, filter: "blur(3px)" }}
              animate={{ opacity: 1, x: 0, scale: 1, filter: "blur(0px)" }}
              exit={reduceMotion ? { opacity: 0 } : { opacity: 0, x: 22, scale: 0.99, filter: "blur(2px)" }}
              transition={reduceMotion ? { duration: 0 } : { type: "spring", stiffness: 330, damping: 34, mass: 0.78 }}
              className="relative z-10 w-full min-w-0 xl:sticky xl:top-4 xl:h-[calc(100vh-140px)] xl:w-[350px] xl:shrink-0"
            >
              <OrganizationInsightPanel
                activities={related.activities}
                tasks={related.tasks}
                canCommunicate={canCommunicate}
                hasEmail={Boolean(communicationEmail)}
                hasPhone={Boolean(communicationPhone)}
                onQuickAction={setQuickAction}
                onCreateTask={openTaskModal}
              />
            </motion.aside>
          ) : null}
        </AnimatePresence>
        </div>
      </div>

      </>}

      <OrganizationEditModal isOpen={showEdit} onClose={() => setShowEdit(false)} account={account} representatives={isOrganizationConnectedMode() ? [] : related.representatives} actorId={actorId} onSaved={(next) => setMessage(text(`Đã cập nhật ${next.displayName}.`, `${next.displayName} updated.`))} />
      <ConfirmDialog isOpen={archiveOpen} onClose={() => setArchiveOpen(false)} title={text("Lưu trữ tổ chức", "Archive organization")} message={text("Tổ chức sẽ được lưu trữ trên backend.", "The organization will be archived by the backend.")} confirmText={text("Lưu trữ", "Archive")} variant="danger" onConfirm={async (isCurrent) => { try { if (account.resourceVersion === undefined) throw new Error("ORGANIZATION_VERSION_REQUIRED"); await archiveOrganizationViaApi({ organizationId: account.id, expectedVersion: account.resourceVersion }); if (!isCurrent()) return; setArchiveOpen(false); setMessage(text("Đã lưu trữ tổ chức.", "Organization archived.")); } catch (caught) { setMessage(formatApplicationError(caught, { locale })); } }} />
      {!isContactOrganizationRelationshipUnavailable() && <OrganizationRepresentativeModal isOpen={showAddRepresentative} onClose={() => setShowAddRepresentative(false)} account={account} actorId={actorId} onCreated={(contact) => { setMessage(text(`Đã liên kết ${contact.fullName || contact.name} làm cá nhân đại diện.`, `${contact.fullName || contact.name} linked as a representative.`)); setActiveTab("relationship"); setRelationshipView("people"); }} />}
      <OrganizationQuickActivityModal targetId={quickAction && !quickWasOpen.current ? account.id : quickOpening.current?.account.id ?? account.id} action={quickAction} email={communicationEmail} phone={communicationPhone} onClose={() => setQuickAction(null)} onSave={saveQuickActivity} />
      <ConfirmDialog
        isOpen={Boolean(endRepresentativeContactId)}
        onClose={endLifecycle.requestClose}
        editable onConfirm={confirmEndRepresentativeRelationship}
        title={text("Kết thúc quan hệ đại diện", "End representative relationship")}
        message={<div className="space-y-3 text-left"><p>{text("Quan hệ sẽ được chuyển vào lịch sử; Contact và Organization không bị xóa.", "The relationship moves to history; neither Contact nor Organization is deleted.")}</p><Textarea id="organization-end-representative-reason" disabled={endLifecycle.pending} label={text("Lý do *", "Reason *")} value={endRepresentativeReason} onChange={(event) => setEndRepresentativeReason(event.target.value)} /></div>}
        confirmText={text("Kết thúc quan hệ", "End relationship")}
        cancelText={text("Quay lại", "Go back")}
        variant="danger"
      />

      <ConfirmDialog isOpen={endLifecycle.confirmOpen} onClose={() => endLifecycle.setConfirmOpen(false)} onConfirm={endLifecycle.discard}
        title={text("Bỏ thay đổi chưa lưu?", "Discard unsaved changes?")} message={text("Các thay đổi chưa được lưu.", "Your changes have not been saved.")}
        confirmText={text("Bỏ thay đổi", "Discard changes")} cancelText={text("Tiếp tục chỉnh sửa", "Keep editing")} type="warning" />

      <OrganizationCreateOpportunityModal
        isOpen={dealOpen}
        account={account}
        primaryContact={primaryContact}
        linkedCustomerId={linkedCustomer?.id}
        actorName={currentActorName}
        onClose={() => setDealOpen(false)}
        onCreated={(deal) => {
          setDealOpen(false);
          setActiveTab("sales");
          setSalesView("opportunities");
          navigate(`/deals/${deal.id}`);
        }}
      />

      <TaskCreateModal
        isOpen={taskOpen}
        onClose={() => setTaskOpen(false)}
        title={text("Tạo công việc cho tổ chức", "Create organization task")}
        context={{
          customerId: linkedCustomer?.id,
          relationshipRef: { type: "ORGANIZATION_ACCOUNT", id: account.id },
          recordRef: { moduleKey: "organizations", recordId: account.id, label: account.displayName },
          sourceRef: { type: "ORGANIZATION_DETAIL", id: account.id },
          label: account.displayName,
        }}
        defaults={{ assigneeId: account.ownerId || currentMemberId || members[0]?.memberId || "" }}
        actorId={currentMemberId}
        actorName={currentActorName}
        onCreated={() => {
          setTaskOpen(false);
          setActiveTab("work");
          setWorkView("tasks");
          setMessage(text("Đã tạo công việc cho tổ chức.", "Organization task created."));
        }}
        onError={(error) => setMessage(formatApplicationError(error, { locale }))}
      />
    </RecordDetailFrame>
  );
};

interface RecordRow {
  id: string;
  title: string;
  badge: string;
  value?: string;
  meta: string;
  icon: React.ReactNode;
}

const RecordCollection: React.FC<{
  title?: string;
  action?: React.ReactNode;
  emptyTitle: string;
  emptyDescription: string;
  rows: RecordRow[];
  onOpen?: (id: string) => void;
}> = ({ title, action, emptyTitle, emptyDescription, rows, onOpen }) => (
  <section className="space-y-4">
    {(title || action) ? <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">{title ? <h3 className="text-sm font-semibold text-slate-900">{title}</h3> : <span />}{action}</div> : null}
    {rows.length === 0 ? (
      <div className="rounded-2xl border border-dashed border-slate-200 bg-slate-50 p-8 text-center">
        <Quote size={26} className="mx-auto text-slate-300" />
        <h3 className="mt-3 text-sm font-semibold text-slate-800">{emptyTitle}</h3>
        <p className="mx-auto mt-2 max-w-xl text-[11px] leading-5 text-slate-500">{emptyDescription}</p>
      </div>
    ) : (
      <div className="grid gap-3 lg:grid-cols-2">
        {rows.map((row) => (
          <button key={row.id} type="button" onClick={() => onOpen?.(row.id)} className="flex items-start gap-3 rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:border-violet-200 hover:bg-violet-50/30">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-violet-50 text-violet-600">{row.icon}</span>
            <span className="min-w-0 flex-1">
              <span className="flex flex-wrap items-start justify-between gap-2"><span className="min-w-0 flex-1 crm-text-wrap text-xs font-semibold text-slate-900">{row.title}</span><span className="shrink-0 rounded-full bg-slate-100 px-2 py-1 text-[9px] font-medium text-slate-600">{row.badge}</span></span>
              <span className="mt-2 flex flex-wrap items-center justify-between gap-2"><span className="text-[10px] text-slate-500">{row.meta}</span>{row.value ? <span className="text-[11px] font-semibold text-violet-700">{row.value}</span> : null}</span>
            </span>
          </button>
        ))}
      </div>
    )}
  </section>
);

const WorkCollection: React.FC<{
  title: string;
  empty: string;
  rows: Array<{ id: string; title: string; badge: string; meta: string }>;
  onOpen?: (id: string) => void;
  action?: React.ReactNode;
}> = ({ title, empty, rows, onOpen, action }) => (
  <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <div className="flex items-center gap-2"><CalendarClock size={14} className="text-violet-600" /><h3 className="text-xs font-semibold text-slate-800">{title}</h3><span className="rounded-full bg-slate-100 px-2 py-0.5 text-[9px] font-medium text-slate-600">{rows.length}</span></div>
      {action}
    </div>
    <div className="space-y-2">
      {rows.length === 0 ? <div className="rounded-xl border border-dashed border-slate-200 p-4 text-[10px] text-slate-400">{empty}</div> : rows.map((row) => <button key={row.id} type="button" onClick={() => onOpen?.(row.id)} className="flex w-full items-start gap-3 rounded-xl border border-slate-100 bg-slate-50 px-3 py-3 text-left hover:border-violet-200 hover:bg-violet-50/50"><span className={`mt-0.5 ${row.badge === "COMPLETED" ? "text-emerald-500" : "text-violet-500"}`}>{row.badge === "COMPLETED" ? <CheckCircle2 size={14} /> : <CalendarClock size={14} />}</span><span className="min-w-0 flex-1"><span className="block crm-text-wrap text-[11px] font-medium text-slate-800">{row.title}</span><span className="mt-1 block text-[10px] text-slate-500">{row.meta}</span></span><span className="rounded-full bg-white px-2 py-1 text-[9px] font-medium text-slate-500 shadow-sm">{row.badge}</span></button>)}
    </div>
  </section>
);

const OrganizationProfilePanel: React.FC<{
  account: OrganizationAccount;
  linkedCustomerName?: string;
  ownerName: string;
  onOpenCustomer?: () => void;
}> = ({ account, linkedCustomerName, ownerName, onOpenCustomer }) => {
  const { locale } = useI18n();
  const isVi = locale === "vi";
  const text = (vi: string, en: string) => isVi ? vi : en;
  const fields = [
    [text("Tên pháp lý", "Legal name"), account.legalName || account.displayName],
    [text("Mã số thuế", "Tax ID"), account.taxCode],
    [text("Ngành nghề", "Industry"), account.industry],
    [text("Quy mô", "Company size"), account.sizeBand],
    [text("Người phụ trách", "Owner"), ownerName],
    [text("Nguồn", "Source"), account.source],
    ["Email", account.email],
    [text("Điện thoại", "Phone"), account.phone],
    [text("Website", "Website"), account.website || account.domain],
    [text("Địa chỉ", "Address"), account.address],
  ];
  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1.2fr)_minmax(280px,0.8fr)]">
      <section className="rounded-2xl border border-slate-200 bg-white p-4 sm:p-5">
        <div className="flex items-center gap-2"><Building2 size={16} className="text-violet-600" /><h3 className="text-sm font-semibold text-slate-900">{text("Hồ sơ tổ chức", "Organization profile")}</h3></div>
        <div className="mt-4 grid gap-x-6 gap-y-4 sm:grid-cols-2">{fields.map(([label, value]) => <div key={String(label)}><div className="text-[10px] font-medium uppercase tracking-wide text-slate-400">{label}</div><div className="mt-1 crm-text-wrap text-sm font-medium text-slate-800">{value || "—"}</div></div>)}</div>
      </section>
      <section className="rounded-2xl border border-slate-200 bg-white p-4 sm:p-5">
        <h3 className="text-sm font-semibold text-slate-900">Customer 360</h3>
        {linkedCustomerName ? <><div className="mt-4 text-sm font-medium text-slate-800">{linkedCustomerName}</div><p className="mt-2 text-xs leading-5 text-slate-500">{text("Hồ sơ này điều phối dữ liệu giao dịch, công nợ, dịch vụ và lịch sử quan hệ của tổ chức.", "This profile coordinates transaction, receivable, service, and relationship history for the organization.")}</p>{onOpenCustomer ? <Button className="mt-4" size="sm" variant="primary" onClick={onOpenCustomer}>{text("Mở Customer 360", "Open Customer 360")}</Button> : null}</> : <p className="mt-4 text-xs leading-5 text-slate-500">{text("Chưa có Customer 360 liên kết với tổ chức này.", "No Customer 360 profile is linked to this organization.")}</p>}
      </section>
    </div>
  );
};

function emptyRelated() {
  return {
    representatives: [] as Contact[],
    deals: [] as ReturnType<typeof getDealsSnapshot>,
    quotes: [] as ReturnType<typeof getQuotesSnapshot>,
    orders: [] as ReturnType<typeof getOrderListSnapshot>,
    invoices: [] as ReturnType<typeof getInvoicesSnapshot>["invoices"],
    receivables: [] as ReturnType<typeof getReceivablesSnapshot>,
    payments: [] as ReturnType<typeof getPaymentsSnapshot>["transactions"],
    shipping: [] as ReturnType<typeof getShippingSnapshot>,
    returns: [] as ReturnType<typeof getReturnsSnapshot>["requests"],
    support: [] as ReturnType<typeof getSupportCasesSnapshot>,
    tasks: [] as ReturnType<typeof getTaskActivitySnapshot>["tasks"],
    activities: [] as ReturnType<typeof getTaskActivitySnapshot>["activities"],
    products: [] as Array<{ id: string; title: string; quantity: number; amount: number; orderIds: Set<string>; orderCount: number }>,
    history: [] as RecordRow[],
  };
}

function formatDate(value: string): string { return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" }).format(new Date(value)); }
function formatDateTime(value: string): string { return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(new Date(value)); }
function taskStatusLabel(value: string): string { return value === "COMPLETED" ? "Hoàn thành" : value === "CANCELLED" ? "Đã hủy" : "Đang mở"; }
function priorityLabel(value: string): string { switch (value) { case "URGENT": return "Khẩn cấp"; case "HIGH": return "Cao"; case "NORMAL": return "Bình thường"; case "LOW": return "Thấp"; default: return value; } }
function activityTypeLabel(value: string): string { switch (value) { case "CALL": return "Cuộc gọi"; case "EMAIL": return "Email"; case "MEETING": return "Cuộc họp"; case "NOTE": return "Ghi chú"; case "MESSAGE": return "Tin nhắn"; case "SYSTEM": return "Hệ thống"; default: return value; } }

