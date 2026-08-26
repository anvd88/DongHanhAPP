import { Component, type ErrorInfo, type ReactNode } from "react";
import { isChunkLoadError, reloadForNewBuild } from "../lib/lazyPage";
import { BootSkeleton } from "./PageSkeleton";

/**
 * RANH GIỚI LỖI — không bao giờ để người dùng nhìn màn hình trắng nữa.
 *
 * React 19 gỡ TOÀN BỘ cây component khi một lần render/effect ném lỗi mà không có ranh giới nào
 * bắt. Trước đây cả ứng dụng không có ranh giới nào, nên bất kỳ lỗi nào (hay gặp nhất: chunk lazy
 * của bản build cũ đã bị xoá, xảy ra ngay sau khi đăng nhập vì lúc đó mới điều hướng sang trang
 * đích) đều biến thành một trang trắng trơn, không thông báo, không lối thoát ngoài F5.
 *
 * Ở đây tách hai tình huống:
 *  - Lỗi tải chunk ⇒ máy chủ đã có bản mới ⇒ tự tải lại một lần (xem lazyPage.ts).
 *  - Lỗi khác ⇒ hiện thông báo kèm nội dung lỗi để báo lại, và nút tải lại.
 */
export class AppErrorBoundary extends Component<
  { children: ReactNode; label?: string },
  { error: Error | null; reloading: boolean }
> {
  state: { error: Error | null; reloading: boolean } = { error: null, reloading: false };

  static getDerivedStateFromError(error: Error) {
    return { error, reloading: isChunkLoadError(error) };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`[${this.props.label ?? "app"}] lỗi giao diện:`, error, info.componentStack);
    if (isChunkLoadError(error) && !reloadForNewBuild()) this.setState({ reloading: false });
  }

  render() {
    const { error, reloading } = this.state;
    if (!error) return this.props.children;
    // Đang tải lại để lấy bản build mới: giữ skeleton, đừng doạ người dùng bằng màn hình lỗi.
    if (reloading) return <BootSkeleton />;

    return (
      <div className="km-crash" role="alert" data-login-route-ready="true">
        <h1>Giao diện gặp lỗi</h1>
        <p>
          Một phần ứng dụng vừa dừng đột ngột. Dữ liệu của bạn trên máy chủ không bị ảnh hưởng — hãy
          tải lại trang. Nếu lỗi lặp lại, gửi dòng chi tiết bên dưới cho người phụ trách.
        </p>
        <pre>{error.message || String(error)}</pre>
        <div className="km-crash-actions">
          <button type="button" className="km-btn" onClick={() => window.location.reload()}>
            Tải lại trang
          </button>
          <button type="button" className="km-btn" onClick={() => this.setState({ error: null, reloading: false })}>
            Thử hiển thị lại
          </button>
        </div>
      </div>
    );
  }
}
