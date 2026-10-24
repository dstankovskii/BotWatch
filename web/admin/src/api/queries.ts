import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { gatewayApi, incidentsApi, monitoringApi, refreshAntiforgery, unwrap } from './client'
import type { HttpCheckRequest, TelegramCheckRequest } from './types'

// проверки идут не чаще раза в минуту, чаще опрашивать смысла нет.
// в фоне react-query по умолчанию не опрашивает, поэтому закрытая вкладка сервер не грузит
const LIVE = 15_000

export const keys = {
  auth: ['auth'] as const,
  monitors: ['monitors'] as const,
  monitor: (id: string) => ['monitors', id] as const,
  states: ['states'] as const,
  results: (checkId: string) => ['results', checkId] as const,
  uptime: (days: number) => ['uptime', days] as const,
  incidents: (openOnly: boolean) => ['incidents', openOnly] as const,
  accounts: ['accounts'] as const,
  channels: ['channels'] as const,
  users: ['users'] as const,
  status: ['status'] as const,
}

export function useAuthState() {
  return useQuery({
    queryKey: keys.auth,
    queryFn: () => unwrap(gatewayApi.GET('/api/auth/state')),
    staleTime: Infinity,
  })
}

export function useAuthMutation(action: 'login' | 'setup' | 'logout') {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body?: { userName: string; password: string }) => {
      if (action === 'logout') {
        await unwrap(gatewayApi.POST('/api/auth/logout'))
      } else {
        await unwrap(gatewayApi.POST(`/api/auth/${action}`, { body: body! }))
      }
      // antiforgery-токен привязан к пользователю, после смены пользователя нужен новый
      await refreshAntiforgery()
    },
    onSuccess: () => queryClient.resetQueries(),
  })
}

export function useMonitors() {
  return useQuery({ queryKey: keys.monitors, queryFn: () => unwrap(monitoringApi.GET('/api/monitors')) })
}

export function useMonitor(id: string) {
  return useQuery({
    queryKey: keys.monitor(id),
    queryFn: () => unwrap(monitoringApi.GET('/api/monitors/{id}', { params: { path: { id } } })),
  })
}

export function useStates() {
  return useQuery({
    queryKey: keys.states,
    queryFn: () => unwrap(incidentsApi.GET('/api/states')),
    refetchInterval: LIVE,
  })
}

export function useResults(checkId: string, limit = 200) {
  return useQuery({
    queryKey: keys.results(checkId),
    queryFn: () => unwrap(incidentsApi.GET('/api/checks/{checkId}/results', { params: { path: { checkId }, query: { limit } } })),
    refetchInterval: LIVE,
  })
}

export function useUptime(days = 30) {
  return useQuery({
    queryKey: keys.uptime(days),
    queryFn: () => unwrap(incidentsApi.GET('/api/uptime', { params: { query: { days } } })),
    refetchInterval: 60_000,
  })
}

export function useIncidents(openOnly = false) {
  return useQuery({
    queryKey: keys.incidents(openOnly),
    queryFn: () => unwrap(incidentsApi.GET('/api/incidents', { params: { query: { openOnly, limit: 200 } } })),
    refetchInterval: LIVE,
  })
}

export function useAccounts() {
  return useQuery({
    queryKey: keys.accounts,
    queryFn: () => unwrap(monitoringApi.GET('/api/accounts')),
    // пока кто-то ждёт входа по QR, опрашиваем чаще: QR живёт полминуты
    refetchInterval: (query) => (query.state.data?.some((x) => x.status === 'PendingLogin') ? 3_000 : LIVE),
  })
}

export function useChannels() {
  return useQuery({ queryKey: keys.channels, queryFn: () => unwrap(incidentsApi.GET('/api/channels')) })
}

export function useUsers() {
  return useQuery({ queryKey: keys.users, queryFn: () => unwrap(gatewayApi.GET('/api/users')) })
}

export function useStatusPage() {
  return useQuery({
    queryKey: keys.status,
    queryFn: () => unwrap(gatewayApi.GET('/api/status')),
    refetchInterval: 60_000,
  })
}

// изменение проверки меняет и нагрузку аккаунтов, и список состояний, поэтому сбрасываем всё связанное
function useMonitorMutation<T>(mutationFn: (input: T) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.monitors })
      queryClient.invalidateQueries({ queryKey: keys.accounts })
      queryClient.invalidateQueries({ queryKey: keys.states })
    },
  })
}

export function useCreateMonitor() {
  return useMonitorMutation((body: { name: string; kind: 'Http' | 'TelegramBot'; target: string }) =>
    unwrap(monitoringApi.POST('/api/monitors', { body })),
  )
}

export function useUpdateMonitor() {
  return useMonitorMutation(
    ({ id, ...body }: { id: string; name: string; target: string; enabled: boolean; isPublic: boolean }) =>
      unwrap(monitoringApi.PUT('/api/monitors/{id}', { params: { path: { id } }, body })),
  )
}

export function useDeleteMonitor() {
  return useMonitorMutation((id: string) => unwrap(monitoringApi.DELETE('/api/monitors/{id}', { params: { path: { id } } })))
}

export function useSaveHttpCheck(monitorId: string, checkId?: string) {
  return useMonitorMutation((body: HttpCheckRequest) =>
    checkId
      ? unwrap(monitoringApi.PUT('/api/monitors/{id}/checks/http/{checkId}', { params: { path: { id: monitorId, checkId } }, body }))
      : unwrap(monitoringApi.POST('/api/monitors/{id}/checks/http', { params: { path: { id: monitorId } }, body })),
  )
}

export function useSaveTelegramCheck(monitorId: string, checkId?: string) {
  return useMonitorMutation((body: TelegramCheckRequest) =>
    checkId
      ? unwrap(monitoringApi.PUT('/api/monitors/{id}/checks/telegram/{checkId}', { params: { path: { id: monitorId, checkId } }, body }))
      : unwrap(monitoringApi.POST('/api/monitors/{id}/checks/telegram', { params: { path: { id: monitorId } }, body })),
  )
}

export function useDeleteCheck(monitorId: string) {
  return useMonitorMutation((checkId: string) =>
    unwrap(monitoringApi.DELETE('/api/monitors/{id}/checks/{checkId}', { params: { path: { id: monitorId, checkId } } })),
  )
}

function useInvalidating<T>(key: readonly unknown[], mutationFn: (input: T) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn, onSuccess: () => queryClient.invalidateQueries({ queryKey: key }) })
}

export function useCreateAccount() {
  return useInvalidating(keys.accounts, (displayName: string) =>
    unwrap(monitoringApi.POST('/api/accounts', { body: { displayName } })),
  )
}

export function useRequestLogin() {
  return useInvalidating(keys.accounts, (id: string) =>
    unwrap(monitoringApi.POST('/api/accounts/{id}/login', { params: { path: { id } } })),
  )
}

export function useDeleteAccount() {
  return useInvalidating(keys.accounts, (id: string) =>
    unwrap(monitoringApi.DELETE('/api/accounts/{id}', { params: { path: { id } } })),
  )
}

export function useSaveChannel() {
  return useInvalidating(
    keys.channels,
    (input: { id?: string; name: string; botToken: string | null; chatId: string; enabled: boolean }) =>
      input.id
        ? unwrap(incidentsApi.PUT('/api/channels/{id}', { params: { path: { id: input.id } }, body: input }))
        : unwrap(incidentsApi.POST('/api/channels', { body: { ...input, botToken: input.botToken ?? '' } })),
  )
}

export function useDeleteChannel() {
  return useInvalidating(keys.channels, (id: string) =>
    unwrap(incidentsApi.DELETE('/api/channels/{id}', { params: { path: { id } } })),
  )
}

export function useTestChannel() {
  return useMutation({
    mutationFn: (id: string) => unwrap(incidentsApi.POST('/api/channels/{id}/test', { params: { path: { id } } })),
  })
}

export function useCreateUser() {
  return useInvalidating(keys.users, (body: { userName: string; password: string }) =>
    unwrap(gatewayApi.POST('/api/users', { body })),
  )
}

export function useDeleteUser() {
  return useInvalidating(keys.users, (id: string) =>
    unwrap(gatewayApi.DELETE('/api/users/{id}', { params: { path: { id } } })),
  )
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      unwrap(gatewayApi.POST('/api/auth/password', { body })),
  })
}
