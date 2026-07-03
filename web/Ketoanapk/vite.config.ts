import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => ({
  plugins: [react(), tailwindcss()],
  server: {
    host: true, // cho phép truy cập dev server qua LAN
    proxy: {
      // Chuyển tiếp gọi API sang backend ASP.NET Core khi chạy dev.
      '/api': { target: 'https://localhost:5443', secure: false },
      // Hub SignalR (WebSocket) khi chạy dev.
      '/hubs': { target: 'https://localhost:5443', ws: true, secure: false },
    },
  },
  build: {
    // Project Ketoanapk chạy độc lập: build web ra dist, build APK ra .android-wwwroot.
    outDir: mode === 'android' ? '.android-wwwroot' : 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1000,
    rolldownOptions: {
      checks: {
        // @microsoft/signalr emits PURE annotations on function declarations.
        // Rolldown ignores them safely, so hide this dependency-only warning.
        invalidAnnotation: false,
      },
    },
  },
}))
