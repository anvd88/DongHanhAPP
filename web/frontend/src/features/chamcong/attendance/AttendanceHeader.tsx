import { GlassLogo } from "./GlassLogo";
import { LoginButton } from "./LoginButton";

/** Header kiosk: logo + tiêu đề/mô tả bên trái, nút Đăng nhập bên phải. */
export function AttendanceHeader() {
  return (
    <header className="att-header">
      <div className="att-brand">
        <div className="att-brand-row">
          <GlassLogo />
          <h1 className="att-title">Chấm công</h1>
        </div>
        <p className="att-subtitle">Nhìn vào camera để ghi nhận giờ vào / ra</p>
      </div>
      <LoginButton />
    </header>
  );
}
