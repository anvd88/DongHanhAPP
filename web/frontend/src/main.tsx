import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/theme.css'
import { App } from './app/App'
import { readTheme, applyTheme, watchSystemTheme } from './lib/theme'

// Chủ đề đã được /theme-boot.js đặt trước khi tải mã này để tránh nháy nền. Gọi lại một lần ở đây
// làm phương án dự phòng nếu tệp đó không chạy được, sau đó theo dõi thay đổi sáng/tối của hệ điều
// hành cho chế độ "theo máy".
applyTheme(readTheme())
watchSystemTheme(() => applyTheme(readTheme()))

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
