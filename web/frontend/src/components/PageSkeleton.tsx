/**
 * Skeleton loading dùng chung khi CHUNK của trang (lazy-load) đang được tải về.
 *  - PageSkeleton: khung nội dung BÊN TRONG Layout (đầu trang + thẻ số liệu + bảng). Vỏ app (sidebar,
 *    header) vẫn đứng yên, chỉ vùng nội dung hiển thị skeleton khi chuyển sang trang chưa tải.
 *  - BootSkeleton: khung TOÀN MÀN HÌNH cho lần tải đầu / trang đăng nhập (lúc chưa dựng Layout).
 *
 * Tất cả xây từ class .km-skeleton nên tự có hiệu ứng shimmer, tự đổi màu ở nền tối, và tự TẮT
 * animation ở chế độ nhẹ (perf-lite) lẫn khi người dùng chọn giảm chuyển động — không cần lo thêm.
 */

const CARD_TONES = ["blue", "mint", "amber", "violet"] as const;

/** Khung nội dung khi trang trong Layout đang tải (giữ nguyên sidebar/header). */
export function PageSkeleton() {
  return (
    <div className="km-page-skeleton" aria-hidden="true">
      {/* Đầu trang: tiêu đề + phụ đề + nút hành động */}
      <div className="km-page-header">
        <div style={{ flex: 1 }}>
          <span className="km-skeleton" style={{ width: 220, height: 26, maxWidth: "60%" }} />
          <span className="km-skeleton" style={{ width: 320, height: 14, maxWidth: "80%", marginTop: 10 }} />
        </div>
        <span className="km-skeleton" style={{ width: 120, height: 38, borderRadius: 12 }} />
      </div>

      {/* Hàng thẻ số liệu (đúng khung .km-stat-card như thật) */}
      <div className="km-stats-grid km-skel-stats">
        {CARD_TONES.map((tone) => (
          <div key={tone} className={`km-stat-card km-stat-card-${tone}`}>
            <span className="km-skeleton" style={{ width: 44, height: 44, borderRadius: 14 }} />
            <div className="min-w-0" style={{ display: "grid", gap: 8 }}>
              <span className="km-skeleton" style={{ width: "55%", height: 13 }} />
              <span className="km-skeleton" style={{ width: "75%", height: 22 }} />
              <span className="km-skeleton" style={{ width: "45%", height: 12 }} />
            </div>
          </div>
        ))}
      </div>

      {/* Bảng / danh sách */}
      <div className="km-dashboard km-skel-panel">
        <span className="km-skeleton" style={{ width: 180, height: 18 }} />
        <div className="km-skel-rows">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="km-skel-row">
              <span className="km-skeleton" style={{ flex: 1, height: 14 }} />
              <span className="km-skeleton" style={{ width: 140, height: 14 }} />
              <span className="km-skeleton" style={{ width: 90, height: 14 }} />
              <span className="km-skeleton" style={{ width: 64, height: 28, borderRadius: 8 }} />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/** Khung toàn màn hình cho lần tải đầu / trang đăng nhập (chưa có Layout). */
export function BootSkeleton() {
  return (
    <div className="km-boot-skeleton" aria-hidden="true">
      <div className="km-boot-skeleton-card">
        <span className="km-skeleton" style={{ width: 52, height: 52, borderRadius: 16 }} />
        <span className="km-skeleton" style={{ width: "60%", height: 22, marginTop: 22 }} />
        <span className="km-skeleton" style={{ width: "85%", height: 14, marginTop: 12 }} />
        <span className="km-skeleton" style={{ width: "38%", height: 12, marginTop: 26 }} />
        <span className="km-skeleton" style={{ width: "100%", height: 44, marginTop: 8, borderRadius: 12 }} />
        <span className="km-skeleton" style={{ width: "38%", height: 12, marginTop: 16 }} />
        <span className="km-skeleton" style={{ width: "100%", height: 44, marginTop: 8, borderRadius: 12 }} />
        <span className="km-skeleton" style={{ width: "100%", height: 48, marginTop: 24, borderRadius: 12 }} />
      </div>
    </div>
  );
}
