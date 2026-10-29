import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useI18n } from "@/i18n";
import { Button } from "@/shared/components/ui";
import type { Customer360Projection } from "../../application/ports/CustomerApiRuntime";
import { CustomerDetailTabs, type CustomerDetailTab } from "./CustomerDetailTabs";
import { CustomerConnectedOverview } from "./CustomerConnectedOverview";
import { CustomerRelationshipPanel } from "./CustomerRelationshipPanel";

export function CustomerConnectedWorkspace({ projection }: { projection: Customer360Projection }) {
  const { locale } = useI18n();
  const text = (a: string, b: string) => locale === "vi" ? a : b;
  const navigate = useNavigate();
  const [tab, setTab] = useState<CustomerDetailTab>("overview");
  const [visible, setVisible] = useState(true);
  return <div className="min-w-0 space-y-4" data-customer-connected-workspace data-guidance-id="customers.detail.workspace">
    <header className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white p-4">
      <div className="min-w-0"><h1 className="text-lg font-semibold [overflow-wrap:anywhere]">{projection.identity.displayName}</h1><p className="text-xs text-slate-500 [overflow-wrap:anywhere]">{projection.customer.customerCode} · {projection.customer.status}</p></div>
      <Button variant="secondary" onClick={() => navigate("/customers")}>{text("Danh sách khách hàng", "Customer list")}</Button>
    </header>
    <div className="flex min-w-0 flex-col items-start gap-3 xl:flex-row">
      <main className="w-full min-w-0 flex-1 rounded-xl border border-slate-200 bg-white">
        <CustomerDetailTabs activeTab={tab} setActiveTab={setTab} counts={{}} isRightPanelVisible={visible} onToggleRightPanel={() => setVisible(value => !value)}/>
        <div className="p-4 sm:p-5">{tab === "overview" || tab === "relationship" ? <CustomerConnectedOverview projection={projection}/> : tab === "work" ? <div className="space-y-3"><p>{text("Xem Công việc và Lịch sử trong bảng quan hệ.", "Use Work and History in the relationship panel.")}</p><Button onClick={() => setVisible(true)}>{text("Hiện bảng quan hệ", "Show relationship panel")}</Button></div> : <p role="status">{text("Chưa có dữ liệu liên kết được xác minh cho mục này.", "A verified related-data projection is unavailable for this section.")}</p>}</div>
      </main>
      <CustomerRelationshipPanel customerId={projection.customer.id} status={projection.customer.status} visible={visible}/>
    </div>
  </div>;
}
