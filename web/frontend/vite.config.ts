import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    host: true, // cho phép truy cập dev server qua LAN
    proxy: {
      // Chuyển tiếp gọi API sang backend ASP.NET Core khi chạy dev.
      '/api': 'http://localhost:5239',
      // Hub SignalR (WebSocket) khi chạy dev.
      '/hubs': { target: 'http://localhost:5239', ws: true },
    },
  },
  build: {
    // Build thẳng vào wwwroot của backend để backend phục vụ luôn (1 cổng, cùng origin).
    outDir: '../backend/KetoanMini.Api/wwwroot',
    emptyOutDir: true,
  },
})
