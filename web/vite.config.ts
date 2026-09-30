/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// 개발: API(dotnet run, 5080)를 같은 출처로 프록시 → 쿠키(SameSite=Strict)가 그대로 동작
// 운영: 빌드 결과를 API wwwroot 로 내보내 API 가 같은 출처로 제공 (설계 §18)
const apiTarget = process.env.JIMES_API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      '/hubs': { target: apiTarget, changeOrigin: false, ws: true },
    },
  },
  build: {
    outDir: '../api/src/JiMes.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1000,   // 사내망 전용 — antd 공통 청크(약 650kB) 경고 제외
  },
  test: {
    environment: 'jsdom',
  },
})
