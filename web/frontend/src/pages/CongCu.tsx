import { PageHeader } from "../components/Layout";
import { TinhToan } from "./TinhToan";
import "./cong-cu.css";

export function CongCu() {
  return (
    <div className="tools-page">
      <PageHeader
        title="Công cụ"
        subtitle="Tiện ích tính toán san cuộn inox dùng trực tiếp trong hệ thống"
      />

      <section className="tools-content">
        <TinhToan embedded />
      </section>
    </div>
  );
}
