import { Link } from "react-router-dom";
import { ArrowLeft } from "lucide-react";

/** Nút kính dạng viên thuốc ở góc phải header → quay về trang đăng nhập. */
export function LoginButton() {
  return (
    <Link to="/" className="att-login-btn" aria-label="Đăng nhập">
      <ArrowLeft className="att-login-arrow" aria-hidden="true" />
      <span>Đăng nhập</span>
    </Link>
  );
}
