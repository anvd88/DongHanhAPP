import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Font toàn cục cho cả app (hiện tại và mai sau): Be Vietnam Pro.
import '@fontsource/be-vietnam-pro/400.css'
import '@fontsource/be-vietnam-pro/500.css'
import '@fontsource/be-vietnam-pro/600.css'
import '@fontsource/be-vietnam-pro/700.css'
import '@fontsource/be-vietnam-pro/800.css'
import './index.css'
import App from './App.tsx'
import { applyPerfMode } from './lib/perfMode'

// Áp chế độ hiệu năng (nhẹ/đầy đủ) TRƯỚC khi render để tránh nháy giao diện nặng trên máy yếu.
applyPerfMode()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
