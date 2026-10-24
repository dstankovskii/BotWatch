import type { CheckState, Monitor, UptimeDay } from '../api/types'

export type MonitorStatus = 'Up' | 'Down' | 'Unknown' | 'Disabled'

// статус монитора = худший из его включённых проверок
export function monitorStatus(monitor: Monitor, states: Map<string, CheckState>): MonitorStatus {
  if (!monitor.enabled) return 'Disabled'

  const enabled = monitor.checks.filter((x) => x.enabled)
  if (enabled.length === 0) return 'Unknown'

  const statuses = enabled.map((x) => states.get(x.id)?.status ?? 'Unknown')
  if (statuses.includes('Down')) return 'Down'
  return statuses.every((x) => x === 'Up') ? 'Up' : 'Unknown'
}

export function checkStatus(enabled: boolean, state: CheckState | undefined): MonitorStatus {
  return enabled ? (state?.status ?? 'Unknown') : 'Disabled'
}

// uptime по дням для набора проверок: складываем прогоны всех проверок за день
export function uptimeByDay(days: UptimeDay[] | undefined, checkIds: string[]) {
  const ids = new Set(checkIds)
  const totals = new Map<string, { total: number; failed: number }>()

  for (const x of days ?? []) {
    if (!ids.has(x.checkId)) continue
    const day = totals.get(x.day) ?? { total: 0, failed: 0 }
    day.total += x.total
    day.failed += x.failed
    totals.set(x.day, day)
  }

  const byDay = new Map<string, number | null>()
  let total = 0
  let failed = 0
  for (const [day, x] of totals) {
    byDay.set(day, x.total > 0 ? round(((x.total - x.failed) / x.total) * 100) : null)
    total += x.total
    failed += x.failed
  }

  return { byDay, overall: total > 0 ? round(((total - failed) / total) * 100) : null }
}

function round(value: number) {
  return Math.round(value * 100) / 100
}

const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })

export function timeAgo(iso: string | null | undefined) {
  if (!iso) return '—'
  const seconds = Math.round((new Date(iso).getTime() - Date.now()) / 1000)
  const abs = Math.abs(seconds)
  if (abs < 60) return relative.format(seconds, 'second')
  if (abs < 3600) return relative.format(Math.round(seconds / 60), 'minute')
  if (abs < 86400) return relative.format(Math.round(seconds / 3600), 'hour')
  return relative.format(Math.round(seconds / 86400), 'day')
}

export function formatDuration(fromIso: string, toIso: string | null | undefined) {
  const ms = (toIso ? new Date(toIso).getTime() : Date.now()) - new Date(fromIso).getTime()
  const minutes = Math.max(1, Math.round(ms / 60000))
  if (minutes < 60) return `${minutes} min`
  const hours = Math.floor(minutes / 60)
  return hours < 24 ? `${hours} h ${minutes % 60} min` : `${Math.floor(hours / 24)} d ${hours % 24} h`
}
