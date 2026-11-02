import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Το ASP.NET Core backend (dotnet run --launch-profile https)
const backend = process.env.BACKEND_URL ?? 'https://localhost:7222'

// https://vite.dev/config/
export default defineConfig(({ command }) => ({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  // Το build πάει στο wwwroot/spa του backend, που το σερβίρει ως fallback για τα React routes
  base: command === 'build' ? '/spa/' : '/',
  build: {
    outDir: '../Panoptes/wwwroot/spa',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: Object.fromEntries(
      ['/api', '/img', '/media'].map((p) => [p, { target: backend, changeOrigin: true, secure: false }]),
    ),
  },
}))
