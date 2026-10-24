import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { errorText, refreshAntiforgery } from '../api/client'
import { useAuthMutation, useAuthState } from '../api/queries'
import { credentialsSchema, type Credentials } from '../forms'
import { Button } from '../ui/Button'
import { Card } from '../ui/Card'
import { Input } from '../ui/Field'
import { Loading } from '../ui/Loading'
import { Note } from '../ui/Note'
import styles from './AuthPages.module.css'

function AuthForm({ mode }: { mode: 'login' | 'setup' }) {
  const auth = useAuthMutation(mode)
  const navigate = useNavigate()
  const location = useLocation()
  const { register, handleSubmit, formState } = useForm<Credentials>({ resolver: zodResolver(credentialsSchema) })

  // без antiforgery-cookie шлюз отклонит POST, берём её заранее
  useEffect(() => {
    refreshAntiforgery()
  }, [])

  const onSubmit = handleSubmit((values) =>
    auth.mutate(values, {
      onSuccess: () => navigate((location.state as { from?: string } | null)?.from ?? '/', { replace: true }),
    }),
  )

  return (
    <div className={styles.page}>
      <div className={styles.box}>
        <div className={styles.brand}>
          <img src="/favicon.svg" alt="" />
          BotWatch
        </div>
        <Card title={mode === 'setup' ? 'Create the first admin' : 'Sign in'}>
          <form className="stack" onSubmit={onSubmit} noValidate>
            {mode === 'setup' && (
              <p className="muted">
                There are no users yet. Whoever opens this page first becomes the admin, so finish setup right after
                deploying.
              </p>
            )}
            <Input label="User name" autoComplete="username" autoFocus {...register('userName')} error={formState.errors.userName?.message} />
            <Input
              label="Password"
              type="password"
              autoComplete={mode === 'setup' ? 'new-password' : 'current-password'}
              {...register('password')}
              error={formState.errors.password?.message}
            />
            {auth.isError && <Note tone="bad">{errorText(auth.error)}</Note>}
            <Button type="submit" busy={auth.isPending}>
              {mode === 'setup' ? 'Create admin' : 'Sign in'}
            </Button>
          </form>
        </Card>
      </div>
    </div>
  )
}

export function LoginPage() {
  const { data, isPending } = useAuthState()
  if (isPending) return <Loading />
  if (data?.setupRequired) return <Navigate to="/setup" replace />
  if (data?.userName) return <Navigate to="/" replace />
  return <AuthForm mode="login" />
}

export function SetupPage() {
  const { data, isPending } = useAuthState()
  if (isPending) return <Loading />
  if (data && !data.setupRequired) return <Navigate to="/login" replace />
  return <AuthForm mode="setup" />
}
