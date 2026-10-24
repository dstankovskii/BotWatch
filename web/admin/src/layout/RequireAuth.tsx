import { useEffect, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Navigate, useLocation } from 'react-router'
import { UNAUTHORIZED_EVENT } from '../api/client'
import { keys, useAuthState } from '../api/queries'
import { Loading } from '../ui/Loading'

export function RequireAuth({ children }: { children: ReactNode }) {
  const { data, isPending, isError } = useAuthState()
  const location = useLocation()
  const queryClient = useQueryClient()

  // сессия могла кончиться в любой момент: любой 401 перечитывает состояние входа
  useEffect(() => {
    const onUnauthorized = () => queryClient.invalidateQueries({ queryKey: keys.auth })
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
  }, [queryClient])

  if (isPending) return <Loading />
  if (isError) return <Loading text="Gateway is unavailable, retrying…" />
  if (data.setupRequired) return <Navigate to="/setup" replace />
  if (!data.userName) return <Navigate to="/login" replace state={{ from: location.pathname }} />

  return children
}
