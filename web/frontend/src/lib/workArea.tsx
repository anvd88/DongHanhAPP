import { useCallback, useMemo, useState } from "react";
import { useLocation } from "react-router-dom";
import { NAV, hasArea, type NavArea } from "../components/nav";
import { useAccess, type Permission } from "./access";

/**
 * KHÔNG GIAN LÀM VIỆC đang xem: "work" (việc của chính mình) hay "admin" (quản trị & nghiệp vụ).
 *
 * ĐỌC KỸ: đây THUẦN TÚY là chuyện hiển thị. Người có cả hai không gian bấm chuyển qua lại cho gọn
 * menu — chuyển KHÔNG cấp thêm quyền nào. Người chỉ có một không gian sẽ không thấy nút chuyển, và
 * dù có tự gọi setArea("admin") trong console cũng chỉ nhận về một sidebar rỗng: mục menu vẫn lọc
 * theo quyền, còn API thì backend chốt lại theo CSDL ở mỗi request.
 *
 * Cố ý KHÔNG lưu lựa chọn xuống localStorage: mỗi lần mở lại, không gian mặc định được tính lại từ
 * hồ sơ truy cập MỚI NHẤT, nên người vừa bị thu quyền quản trị không mở ra đã thấy menu quản trị.
 */
export function useWorkArea() {
  const { can, profile, uiProfile } = useAccess();
  const location = useLocation();

  const canWork = useMemo(() => hasArea("work", can), [can]);
  const canAdmin = useMemo(() => hasArea("admin", can), [can]);
  const canSwitch = canWork && canAdmin;

  // Không gian mặc định theo hồ sơ truy cập; người dùng bấm chuyển thì theo lựa chọn của họ.
  const preferred: NavArea = uiProfile === "workspace" || uiProfile === "kiosk" ? "work" : "admin";

  // Lựa chọn tay được lưu KÈM danh tính hồ sơ. Đổi tài khoản hoặc bị đổi quyền ⇒ khóa đổi ⇒ lựa chọn
  // cũ tự hết hiệu lực, quay về không gian mặc định của hồ sơ MỚI (không cần effect dọn dẹp). Lưu thêm
  // TRANG lúc bấm (fromPath) để phân biệt "vừa bấm chuyển, chưa đi đâu" với "đã điều hướng đi nơi khác".
  const profileKey = `${profile?.username ?? ""}#${profile?.authorizationVersion ?? 0}`;
  const [picked, setPicked] = useState<{ key: string; area: NavArea; fromPath: string } | null>(null);
  const chosen = picked?.key === profileKey ? picked : null;

  // Không gian của TRANG đang mở (nếu trang đó nằm trong menu). Dùng để menu bám theo trang khi
  // deep-link / dán URL / điều hướng chéo không gian.
  const areaOfPath = useMemo<NavArea | null>(() => {
    const path = (location.pathname.split(/[?#]/)[0] || "/").replace(/\/+$/, "") || "/";
    const match = NAV.flatMap((s) => s.items)
      .filter((it) => path === it.path || path.startsWith(`${it.path}/`))
      .sort((a, b) => b.path.length - a.path.length)[0];
    return match?.area ?? null;
  }, [location.pathname]);

  // LỰA CHỌN TAY THẮNG trang đang mở — nếu không, đứng ở trang quản trị mà bấm "Làm việc" sẽ không có
  // tác dụng vì areaOfPath cứ ghì lại "admin". NHƯNG một khi người dùng ĐIỀU HƯỚNG sang một trang thuộc
  // không gian KHÁC (không phải trang lúc bấm), lựa chọn cũ hết hiệu lực để menu bám theo trang mới —
  // giữ đúng hành vi deep-link/điều hướng chéo cũ. setState-trong-render (không dùng effect) theo đúng
  // khuôn mẫu quanh dự án để hợp React Compiler; điều kiện hội tụ nên không lặp vô hạn.
  const pickStale =
    chosen !== null && areaOfPath !== null && areaOfPath !== chosen.area && location.pathname !== chosen.fromPath;
  if (pickStale) setPicked(null);
  const chosenArea = pickStale ? null : chosen?.area ?? null;

  let area: NavArea = chosenArea ?? areaOfPath ?? preferred;
  // Không có mục nào trong không gian đó thì rơi về không gian còn lại (tránh sidebar trống trơn).
  if (area === "admin" && !canAdmin) area = "work";
  if (area === "work" && !canWork) area = "admin";

  const setArea = useCallback(
    (next: NavArea) => setPicked({ key: profileKey, area: next, fromPath: location.pathname }),
    [profileKey, location.pathname],
  );

  return { area, setArea, canSwitch, can: can as (p: Permission) => boolean };
}
