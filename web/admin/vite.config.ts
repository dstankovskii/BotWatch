import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// в dev ходим в gateway, поднятый через compose: cookie и antiforgery работают как в проде, один origin
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': process.env.GATEWAY_URL ?? 'http://localhost:8080',
    },
  },
  build: {
    // react и прочее, что меняется редко, отдельным чанком: между релизами админки кэш браузера не сбрасывается
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [{ name: 'vendor', test: /[\\/]node_modules[\\/](react|react-dom|react-router|scheduler|@tanstack)[\\/]/ }],
        },
      },
    },
  },
})
