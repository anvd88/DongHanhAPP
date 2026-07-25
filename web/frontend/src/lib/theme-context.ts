import { createContext, useContext } from "react";

/**
 * Context + hook của chủ đề sáng/tối, tách khỏi ThemeProvider (theme.tsx) để file kia chỉ còn export
 * COMPONENT. Đó là điều kiện để Fast Refresh của Vite hoán đổi nóng được: file vừa export component vừa
 * export hook/hằng thì mỗi lần sửa sẽ tải lại cả trang thay vì giữ nguyên state.
 */
export type Theme = "light" | "dark";

export const ThemeCtx = createContext<{ theme: Theme; toggle: () => void }>(null!);

export const useTheme = () => useContext(ThemeCtx);
