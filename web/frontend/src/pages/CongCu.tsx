import { useState } from "react";
import { Calculator } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { TinhToan } from "./TinhToan";
import "./cong-cu.css";

type ToolTab = "sancuon";

export function CongCu() {
  const [tab, setTab] = useState<ToolTab>("sancuon");

  return (
    <div className="tools-page">
      <PageHeader
        title="Công cụ"
        subtitle="Các tiện ích tính toán nhanh dùng trực tiếp trong hệ thống"
      />

      <div className="tools-tabs" role="tablist" aria-label="Danh sách công cụ">
        <button
          type="button"
          role="tab"
          aria-selected={tab === "sancuon"}
          className="tools-tab"
          data-on={tab === "sancuon"}
          onClick={() => setTab("sancuon")}
        >
          <Calculator className="h-4 w-4" />
          San cuộn
        </button>
      </div>

      <section className="tools-content" role="tabpanel">
        {tab === "sancuon" && <TinhToan embedded />}
      </section>
    </div>
  );
}
