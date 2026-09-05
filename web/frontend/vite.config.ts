import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Backend phục vụ SPA từ chính wwwroot của nó → web và API cùng một origin, cookie km_auth đi thẳng.
const backend = 'https://localhost:5443'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    host: true,
    proxy: {
      '/api': { target: backend, secure: false, changeOrigin: false },
    },
  },
  build: {
    outDir: '../backend/KetoanMini.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1200,
  },
})
