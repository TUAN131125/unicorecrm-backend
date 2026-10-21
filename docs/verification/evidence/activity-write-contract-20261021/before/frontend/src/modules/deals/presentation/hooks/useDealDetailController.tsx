import { formatApplicationError } from "@/shared/operations";
import { normalizeApplicationError } from "@/shared/domain";
import { useDealDetailInteraction } from "./useDealDetailInteraction";
import React, { useState } from "react";
import { useParams, useNavigate, useLocation } from "react-router-dom";
import { formatCurrency } from "@/shared/lib/format/currency";
import type { Deal, DealActivity } from "../../domain/model/deal.types";
import { DealStage } from "../../domain/model/deal.types";
import { getStageLabel, getNextStage, isWonStage, isLostStage } from "../../domain/rules/dealStages";
import { useDeals } from "../hooks/useDeals";
import { useDealStages } from "../hooks/useDealStages";
import type { Contact } from "@/modules/contacts";
import type { CustomerDisplay as Customer } from "@/modules/customers";
import { getCustomerDisplayName, getCustomerSecondaryInfo } from "@/modules/customers";
import type { CrmWorkspaceConfig } from "@/platform/workspace-config";
import { getQuoteDocumentLabel } from "@/platform/workspace-config";


import { QuoteApprovalStatus, QuoteStatus, getQuoteDealId, getQuotesSnapshot, isQuoteAccepted, subscribeToQuotes, updateQuotes, transitionQuoteStatusCommand, type Quote } from "@/modules/quotes";
import { DEFAULT_CRM_WORKSPACE_CONFIG } from "@/platform/workspace-config/workspaceConfigDefaults";
import { useI18n } from "@/i18n";
import { getOrdersForDeal } from "@/modules/orders";
import type { CustomerOrder } from "@/modules/orders";
import { logActivityViaApi, type ActivityType, type NoteActivityDraft, resolveActivityRecordingDate } from "@/modules/tasks";
import { closeDealLostCommand, closeDealWonCommand, isDealConnectedMode, transitionDealStageCommand, updateDealCommand } from "../../public/deals";
import { invalidateModuleQueries } from "@/shared/application";
import type { DealLineItem } from "../../domain/model/deal.types";
import { acceptQuoteAndCloseDealCommand } from "@/workflows/quote-acceptance";
import { findCustomerByRelationshipRefSnapshot } from "@/modules/customers";
import { getAuthSessionSnapshot } from "@/platform/identity-auth";
import { createDurableId } from "@/shared/ids";

export interface DealDetailPageProps {
  customers: Customer[];
  setCustomers: React.Dispatch<React.SetStateAction<Customer[]>>;
  contacts: Contact[];
  setContacts?: React.Dispatch<React.SetStateAction<Contact[]>>;
  crmConfig?: CrmWorkspaceConfig;
  orders?: Record<string, CustomerOrder[]>;
}

export function useDealDetailController({ 
  customers, 
  setCustomers, 
  contacts,
  setContacts,
  crmConfig = DEFAULT_CRM_WORKSPACE_CONFIG,
  orders = {}
}: DealDetailPageProps) {

  const { deals, setDeals } = useDeals({ loadAuthoritative: false });
  const [quotes, setQuoteState] = useState<Quote[]>(() => getQuotesSnapshot());
  React.useEffect(() => subscribeToQuotes(setQuoteState), []);
  const setQuotes: React.Dispatch<React.SetStateAction<Quote[]>> = (updater) => { updateQuotes(updater); };
  const { stageConfigs } = useDealStages();
  const { dealId } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const { t, locale } = useI18n();

  const screenDeal = deals.find(d => d.id === dealId);
  const [lostReason, setLostReason] = useState("");
  const [lostNotes, setLostNotes] = useState("");
  const [lostRecycleDecision, setLostRecycleDecision] = useState<"RECYCLE" | "CONDITIONAL" | "DO_NOT_RECYCLE">("DO_NOT_RECYCLE");
  const [lostRevisitAt, setLostRevisitAt] = useState("");
  const [lostError, setLostError] = useState("");
  const lostDirty = Boolean(lostReason || lostNotes.trim() || lostRevisitAt || lostRecycleDecision !== "DO_NOT_RECYCLE");
  const { intentId, openingDeal, pendingMutation, interactionPending, resetInteraction, runBoundMutation,
    isWonModalOpen, setIsWonModalOpen, isLostModalOpen, setIsLostModalOpen,
    isProductPickerOpen, setIsProductPickerOpen, isDirectNoteModalOpen, setIsDirectNoteModalOpen,
  } = useDealDetailInteraction(screenDeal, dealId, lostDirty, locale,
    () => { setLostReason(""); setLostNotes(""); setLostRecycleDecision("DO_NOT_RECYCLE"); setLostRevisitAt(""); setLostError(""); },
    () => handleConfirmLost(),
    error => { const message = formatApplicationError(error, { locale }); setLostError(message); triggerToast("error", message);
      const fields = normalizeApplicationError(error).fieldErrors;
      document.getElementById(fields?.reason ? "deal-lost-reason" : fields?.revisitAt ? "deal-lost-revisit" : "deal-lost-notes")?.focus();
    },
  );
  const deal = openingDeal ?? screenDeal;


  const [toast, setToast] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const triggerToast = (type: "success" | "error", text: string) => {
    setToast({ type, text });
    setTimeout(() => setToast(null), 3500);
  };

  const logDealTimelineActivity = async (input: {
    activityType: DealActivity["type"];
    taskType: ActivityType;
    title: string;
    description: string;
    occurredAt?: string;
    metadata?: DealActivity["metadata"];
    sourceType: string;
    sourceId: string;
    idempotencyKey?: string;
  }): Promise<boolean> => {
    if (!deal) return false;
    const session = getAuthSessionSnapshot();
    if (!session) throw new Error(locale === "vi" ? "Phiên đăng nhập không còn hợp lệ." : "The authenticated session is no longer available.");
    const outcome = await logActivityViaApi({
      id: createDurableId("deal_activity"),
      type: input.taskType,
      subject: input.title,
      body: input.description,
      actorId: session.principal.memberId,
      actorName: session.principal.displayName,
      occurredAt: input.occurredAt,
      recordRef: { moduleKey: "deals", recordId: deal.id, label: deal.name },
      sourceRef: { type: input.sourceType, id: input.sourceId },
    }, input.idempotencyKey ? { idempotencyKey: input.idempotencyKey } : {});
    const saved = outcome.data.activity;
    if (saved.recordRef?.moduleKey !== "deals" || saved.recordRef.recordId !== deal.id || saved.type !== input.taskType
      || (input.occurredAt !== undefined && saved.occurredAt !== input.occurredAt)
      || !["COMMITTED", "REPLAYED", "DEMO_COMMITTED"].includes(outcome.outcome ?? "")) return false;
    // `task.log-activity` declares readModelRefreshRequirement ["tasks"], so the Tasks
    // module owns this activity and the backend does not record it on the Deal read
    // model. Connected mode must not fabricate a Deal activity locally: it refreshes the
    // authoritative Deal instead, so whatever the backend did record surfaces from
    // authority. Demo mode keeps its local Deal timeline, which is demo-authoritative.
    if (isDealConnectedMode()) {
      await invalidateModuleQueries({
        moduleKeys: ["deals"],
        commandType: "task.log-activity",
        aggregateId: deal.id,
        occurredAt: saved.occurredAt,
      });
      return true;
    }
    const projection: DealActivity = {
      id: saved.id,
      type: input.activityType,
      title: saved.subject,
      description: saved.body ?? "",
      createdAt: saved.occurredAt,
      author: session.principal.displayName,
      metadata: input.metadata,
    };
    setDeals((current) => current.map((item) => item.id === deal.id ? {
      ...item,
      activities: [projection, ...(item.activities || [])],
      updatedAt: saved.occurredAt,
    } : item));
    return true;
  };

  // New note state
  const [newNoteText, setNewNoteText] = useState("");
  const [noteError, setNoteError] = useState("");
  const [activeFilter, setActiveFilter] = useState<"all" | "note" | "stage" | "quote" | "outcome">("all");
  const [activeTab, setActiveTab] = useState<"overview" | "products" | "quotes" | "activity" | "notes">("overview");

  // Quotes helper states and handlers
  const relatedQuotes = quotes.filter(q => getQuoteDealId(q) === deal?.id);
  const hasAcceptedQuote = quotes.some(q => getQuoteDealId(q) === deal?.id && isQuoteAccepted(q.status));
  const acceptedQuote = quotes.find(q => getQuoteDealId(q) === deal?.id && isQuoteAccepted(q.status));

  const linkedOrders = React.useMemo(() => {
    if (!deal || !isWonStage(deal.stage, stageConfigs)) return [];
    return getOrdersForDeal(orders || {}, deal.id, quotes);
  }, [deal, orders, quotes]);

  const hasLinkedOrder = linkedOrders.length > 0;
  const latestLinkedOrder = linkedOrders[0];

  const getOpportunityQuoteState = (d: Deal, qs: Quote[]): "none" | "draft" | "review" | "sent" | "accepted" | "rejected" | "expired" => {
    const related = qs.filter(q => getQuoteDealId(q) === d.id);
    if (related.length === 0) return "none";

    if (related.some(q => isQuoteAccepted(q.status))) {
      return "accepted";
    }
    if (related.some(q => q.status === QuoteStatus.SENT)) {
      return "sent";
    }
    if (related.some(q => q.status === QuoteStatus.REVIEW || (q.approvalRequired && q.approvalStatus === QuoteApprovalStatus.PENDING && q.approvalRequestedAt))) {
      return "review";
    }
    if (related.some(q => q.status === QuoteStatus.DRAFT)) {
      return "draft";
    }
    if (related.every(q => q.status === QuoteStatus.REJECTED)) {
      return "rejected";
    }
    if (related.every(q => q.status === QuoteStatus.EXPIRED || q.status === QuoteStatus.REJECTED)) {
      return "expired";
    }
    return "none";
  };

  const quoteState = deal ? getOpportunityQuoteState(deal, quotes) : "none";

  const handleUpdateQuoteStatus = async (quoteId: string, newStatus: QuoteStatus) => {
    const targetQuote = quotes.find((quote) => quote.id === quoteId);
    if (!targetQuote) return;

    const now = new Date().toISOString();
    try {
      const updatedQuote = newStatus === QuoteStatus.ACCEPTED
        ? (await acceptQuoteAndCloseDealCommand(
          { quoteId },
          targetQuote.resourceVersion === undefined ? {} : { expectedVersion: targetQuote.resourceVersion },
        ), targetQuote)
        : (await transitionQuoteStatusCommand(quoteId, newStatus, now)).data;
      if (!updatedQuote) return;

      let activityTitle = "";
      let activityDescription = "";
      if (newStatus === QuoteStatus.REVIEW) {
        activityTitle = locale === "vi" ? "Báo giá chuyển sang duyệt" : "Quote submitted for review";
        activityDescription = `#${targetQuote.quoteNumber} v${targetQuote.version}`;
      } else if (newStatus === QuoteStatus.SENT) {
        activityTitle = t("deals.activities.quoteSentTitle");
        activityDescription = t("deals.activities.quoteSentDescription", { quoteNumber: targetQuote.quoteNumber });
      } else if (newStatus === QuoteStatus.ACCEPTED) {
        activityTitle = t("deals.activities.quoteAcceptedTitle");
        activityDescription = t("deals.activities.quoteAcceptedDescription", { quoteNumber: targetQuote.quoteNumber });
      } else if (newStatus === QuoteStatus.REJECTED) {
        activityTitle = t("deals.activities.quoteRejectedTitle");
        activityDescription = t("deals.activities.quoteRejectedDescription", { quoteNumber: targetQuote.quoteNumber });
      }

      if (deal && activityTitle) {
        await logDealTimelineActivity({
          activityType: "quote",
          taskType: "SYSTEM",
          title: activityTitle,
          description: activityDescription,
          occurredAt: now,
          sourceType: "QUOTE_STATUS",
          sourceId: targetQuote.id,
          metadata: {
            quoteId: targetQuote.id,
            quoteNumber: targetQuote.quoteNumber,
            quoteStatus: newStatus,
          },
        });
      }

      if (newStatus === QuoteStatus.ACCEPTED && deal) {
        triggerToast("success", locale === "vi"
          ? "Quote Accepted đã cung cấp commercial commitment evidence; Deal được đóng WON."
          : "Quote Accepted supplied commercial commitment evidence; the Deal is now WON.");
      }
    } catch (error) {
      triggerToast("error", formatApplicationError(error, { locale }));
    }
  };

  // Stage configuration is subscribed through the Deal module hook.

  const getDealStageLabel = (stage: string | DealStage) => {
    let label = getStageLabel(stage, stageConfigs, locale);
    if (stage === "PROPOSAL" && locale === "vi") {
      const hasSentOrAccepted = relatedQuotes.some(q => q.status === QuoteStatus.SENT || isQuoteAccepted(q.status));
      if (hasSentOrAccepted) {
        label = "Đã báo giá";
      }
    }
    return label;
  };

  if (!deal) return null;

  // Find related customer & contact
  const projectedCustomer = findCustomerByRelationshipRefSnapshot(deal.buyerRef);
  const customer = customers.find(c => c.id === deal.customerId || c.id === projectedCustomer?.id);
  const contact = contacts.find(c => c.id === deal.contactId);
  const isQuoteEnabled = crmConfig.modules.quotes && crmConfig.workflow.quoteUsageMode !== "DISABLED";
  const quoteDocumentLabel = getQuoteDocumentLabel(crmConfig, t);
  const customerDisplayName = getCustomerDisplayName(customer) || t("common.notAvailable");
  const customerSecondaryInfo = customer ? getCustomerSecondaryInfo(customer, t("common.notAvailable")) : t("common.notAvailable");
  const customerTypeLabel = customer?.type === "INDIVIDUAL"
    ? t("settings.crmConfig.customerTypes.individual")
    : t("settings.crmConfig.customerTypes.company");

  // Next deal stages helper
  const getNextDealStage = (stage: string | DealStage): string | null => {
    return getNextStage(stage, stageConfigs);
  };

  // Check terminal locking status
  const isTerminal = isWonStage(deal.stage, stageConfigs) || isLostStage(deal.stage, stageConfigs);

  // Handles moving through open canonical stages only.
  const handleNextStage = async () => {
    const nextStage = getNextDealStage(deal.stage);
    if (!nextStage) return;
    const now = new Date().toISOString();
    await transitionDealStageCommand(deal.id, nextStage, {
      id: createDurableId("deal_activity"),
      type: "stage",
      title: t("deals.activities.stageChangedTitle"),
      description: t("deals.activities.stageChangedDescription", {
        from: getDealStageLabel(deal.stage),
        to: getDealStageLabel(nextStage),
      }),
      createdAt: now,
      author: t("common.system"),
      metadata: { fromStage: deal.stage, toStage: nextStage },
    });
  };

  // Handles updating line items
  const handleUpdateLineItem = async (itemId: string, field: "quantity" | "discountPercent", value: number) => {
    if (isTerminal) return;

    const updatedLineItems = deal.lineItems.map(item => item.id === itemId ? { ...item, [field]: value } : item);
    const newAmount = updatedLineItems.reduce((sum, item) => {
      const discountedPrice = (item.unitPrice ?? item.unitPriceSnapshot ?? 0) * (1 - item.discountPercent / 100);
      return sum + (discountedPrice * item.quantity);
    }, 0);

    try {
      await updateDealCommand(deal.id, { lineItems: updatedLineItems, amount: newAmount });
    } catch (error) {
      triggerToast("error", formatApplicationError(error, { locale }));
    }
  };

  // Applying a Product Picker selection replaces the Deal commercial lines. It is a
  // business mutation and must use the canonical `deal.update` command, never a local
  // Deal projection write.
  const handleApplyLineItems = async (nextLines: DealLineItem[]): Promise<boolean> => {
    if (isTerminal) return false;
    const newAmount = nextLines.reduce((sum, item) => {
      const discountedPrice = (item.unitPrice ?? item.unitPriceSnapshot ?? 0) * (1 - item.discountPercent / 100);
      return sum + (discountedPrice * item.quantity);
    }, 0);
    return runBoundMutation(() => updateDealCommand(deal.id, { lineItems: nextLines, amount: newAmount }));
  };

  // Opens Product Picker modal instead of inserting random placeholder products
  const handleAddSampleItem = () => {
    if (isTerminal) return;
    triggerToast("error", locale === "vi" ? "Dòng thương mại chưa được hỗ trợ. Hãy thêm sản phẩm quan tâm trong biểu mẫu cơ hội." : "Commercial lines are unavailable. Add interested products in the opportunity form.");
  };

  // Deletes line item
  const handleDeleteLineItem = async (itemId: string) => {
    if (isTerminal) return;

    const updatedLineItems = deal.lineItems.filter(item => item.id !== itemId);
    const newAmount = updatedLineItems.reduce((sum, item) => {
      const discountedPrice = (item.unitPrice ?? item.unitPriceSnapshot ?? 0) * (1 - item.discountPercent / 100);
      return sum + (discountedPrice * item.quantity);
    }, 0);

    try {
      await updateDealCommand(deal.id, { lineItems: updatedLineItems, amount: newAmount });
    } catch (error) {
      triggerToast("error", formatApplicationError(error, { locale }));
    }
  };

  const getLostReasonLabel = (reasonCode?: string) => {
    if (!reasonCode) return "";
    const code = reasonCode.includes(" - Note: ") ? reasonCode.split(" - Note: ")[0] : reasonCode;

    switch (code) {
      case "PRICE_TOO_HIGH":
        return t("deals.lostReasons.priceTooHigh");
      case "NO_BUDGET":
        return t("deals.lostReasons.noBudget");
      case "COMPETITOR_SELECTED":
        return t("deals.lostReasons.competitorSelected");
      case "NO_DECISION":
        return t("deals.lostReasons.noDecision");
      case "NOT_A_FIT":
        return t("deals.lostReasons.notAFit");
      case "OTHER":
        return t("deals.lostReasons.other");
      default:
        return code || t("common.notAvailable");
    }
  };

  const getLostReasonNoteHelper = (dealCheck: Deal) => {
    if (dealCheck.lostReasonNote) return dealCheck.lostReasonNote;
    if (dealCheck.lostReason && dealCheck.lostReason.includes(" - Note: ")) {
      return dealCheck.lostReason.split(" - Note: ")[1];
    }
    return undefined;
  };

  // Action: Mark Won. The terminal transition requires commitment evidence.
  const handleConfirmWon = async () => {
    const now = new Date().toISOString();
    const quoteEvidence = relatedQuotes
      .filter((quote) => quote.status === QuoteStatus.ACCEPTED)
      .sort((a, b) => (b.acceptedAt || b.updatedAt || b.createdAt).localeCompare(a.acceptedAt || a.updatedAt || a.createdAt))[0];
    const orderEvidence = linkedOrders.find((item) => item.state === "CONFIRMED" || item.state === "COMPLETED");

    const evidence = quoteEvidence
      ? { type: "QUOTE_ACCEPTED" as const, sourceId: quoteEvidence.id, occurredAt: quoteEvidence.acceptedAt || now }
      : orderEvidence
        ? { type: "ORDER_CONFIRMED" as const, sourceId: orderEvidence.id, occurredAt: orderEvidence.confirmedAt || orderEvidence.updatedAt || now }
        : undefined;

    if (!evidence) {
      triggerToast("error", locale === "vi"
        ? "Không thể đánh dấu WON: cần Quote Accepted hoặc Order Confirmed."
        : "Cannot mark WON: Quote Accepted or Order Confirmed evidence is required.");
      return;
    }

    await closeDealWonCommand(deal.id, evidence, {
      id: createDurableId("deal_activity_won"),
      type: "won",
      title: t("deals.activities.markedWonTitle"),
      description: t("deals.activities.markedWonDescription", { deal: deal.name, amount: formatCurrency(deal.amount, deal.currency || "VND", locale) }),
      createdAt: now,
      author: t("common.system"),
      metadata: { toStage: DealStage.WON },
    });

    triggerToast("success", t("orders.toast.dealWon"));
    setIsWonModalOpen(false);
  };

  // Action: Mark Lost with explicit recycle semantics.
  const handleConfirmLost = async (): Promise<boolean> => {
    if (pendingMutation.current) return false;
    if (!lostReason.trim()) {
      setLostError(t("deals.lostModal.reasonRequired"));
      document.getElementById("deal-lost-reason")?.focus();
      return false;
    }
    if (lostRecycleDecision !== "DO_NOT_RECYCLE" && !lostRevisitAt) {
      setLostError(locale === "vi" ? "Cơ hội có thể tái khai thác phải có ngày xem xét lại." : "A recyclable Deal requires a revisit date.");
      document.getElementById("deal-lost-revisit")?.focus();
      return false;
    }
    setLostError("");

    const now = new Date().toISOString();
    const saved = await runBoundMutation(() => closeDealLostCommand(deal.id, {
      reason: lostReason,
      note: lostNotes.trim() || undefined,
      recycleDecision: lostRecycleDecision,
      revisitAt: lostRecycleDecision === "DO_NOT_RECYCLE" ? undefined : new Date(`${lostRevisitAt}T09:00:00.000Z`).toISOString(),
      occurredAt: now,
    }, {
      id: createDurableId("deal_activity_lost"),
      type: "lost",
      title: t("deals.activities.markedLostTitle"),
      description: t("deals.activities.markedLostDescription", { deal: deal.name, reason: getLostReasonLabel(lostReason) }),
      createdAt: now,
      author: t("common.system"),
      metadata: {
        toStage: DealStage.LOST,
        lostReason,
        lostReasonNote: lostNotes.trim(),
        recycleDecision: lostRecycleDecision,
      },
    }, { idempotencyKey: `${intentId}:lost` }));

    if (!saved) return false;
    resetInteraction();
    return true;
  };

  // Handle adding direct notes in the details workspace
  const handleAddDirectNote = async (draft: NoteActivityDraft): Promise<boolean> => {
    try {
      const saved = await runBoundMutation(() => logDealTimelineActivity({
        activityType: "note",
        taskType: "NOTE",
        title: draft.title,
        description: `[${draft.category}] ${draft.body}`,
        occurredAt: resolveActivityRecordingDate(draft),
        sourceType: "DEAL_NOTE",
        sourceId: deal.id,
        metadata: { noteCategory: draft.category, pinned: draft.pinned },
        idempotencyKey: `${intentId}:note`,
      }), true);
      if (!saved) throw new Error("DEAL_NOTE_NOT_SAVED");
      return true;
    } catch (error) {
      throw error;
    }
  };

  // Back state router compatibility
  const state = location.state as { returnTo?: string } | null;
  const returnTo = state?.returnTo || "/deals";

  // Timeline list setup with filtering and newest-first sorting by parsed Date
  const rawActivities = deal.activities || [];
  const filteredActivities = rawActivities.filter(act => {
    switch (activeFilter) {
      case "note":
        return act.type === "note";
      case "stage":
        return act.type === "stage";
      case "quote":
        return act.type === "quote";
      case "outcome":
        return act.type === "won" || act.type === "lost" || (act.type === "system" && (act.metadata?.toStage === DealStage.WON || act.metadata?.toStage === DealStage.LOST));
      default:
        return true;
    }
  });

  const activitiesList = [...filteredActivities].sort((a, b) => {
    return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
  });

  return {
    customers,
    setCustomers,
    contacts,
    setContacts,
    quotes,
    setQuotes,
    crmConfig,
    orders,
    deals,
    setDeals,
    stageConfigs,
    dealId,
    navigate,
    location,
    t,
    locale,
    deal,
    isWonModalOpen,
    setIsWonModalOpen,
    isProductPickerOpen,
    setIsProductPickerOpen,
    interactionPending,
    isLostModalOpen,
    setIsLostModalOpen,
    lostReason,
    setLostReason,
    lostNotes,
    setLostNotes,
    lostRecycleDecision,
    setLostRecycleDecision,
    lostRevisitAt,
    setLostRevisitAt,
    lostError,
    setLostError,
    toast,
    setToast,
    triggerToast,
    newNoteText,
    setNewNoteText,
    noteError,
    setNoteError,
    isDirectNoteModalOpen,
    setIsDirectNoteModalOpen,
    activeFilter,
    setActiveFilter,
    activeTab,
    setActiveTab,
    relatedQuotes,
    hasAcceptedQuote,
    acceptedQuote,
    linkedOrders,
    hasLinkedOrder,
    latestLinkedOrder,
    getOpportunityQuoteState,
    quoteState,
    handleUpdateQuoteStatus,
    getDealStageLabel,
    customer,
    contact,
    isQuoteEnabled,
    quoteDocumentLabel,
    customerDisplayName,
    customerSecondaryInfo,
    customerTypeLabel,
    getNextDealStage,
    isTerminal,
    handleNextStage,
    handleUpdateLineItem,
    handleApplyLineItems,
    handleAddSampleItem,
    handleDeleteLineItem,
    getLostReasonLabel,
    getLostReasonNoteHelper,
    handleConfirmWon,
    handleConfirmLost,
    handleAddDirectNote,
    state,
    returnTo,
    rawActivities,
    filteredActivities,
    activitiesList,
  };
}
