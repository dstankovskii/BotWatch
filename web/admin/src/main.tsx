import './zod'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { router } from './router'
import { applyTheme, readTheme } from './theme'
import './styles/tokens.css'
import './styles/global.css'

// до первого рендера, иначе тёмная тема мигнёт светлой
applyTheme(readTheme())

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 10_000,
      // 401/403/404 повторять бессмысленно, остальное - пару раз
      retry: (count, error) => count < 2 && !('status' in error && [401, 403, 404].includes(error.status as number)),
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
