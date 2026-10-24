import type { ReactNode } from 'react'
import type { MonitorStatus } from '../pages/status'
import styles from './Badge.module.css'

export type Tone = 'green' | 'red' | 'amber' | 'grey'

export function Badge({ tone, pulse, children }: { tone: Tone; pulse?: boolean; children: ReactNode }) {
  return <span className={[styles.badge, styles[tone], pulse && styles.pulse].filter(Boolean).join(' ')}>{children}</span>
}

const statusTone: Record<MonitorStatus, Tone> = {
  Up: 'green',
  Down: 'red',
  Stale: 'amber',
  Unknown: 'grey',
  Disabled: 'grey',
}

// Disabled - не статус с бэка, а выключенная проверка: показываем серым, чтобы не путать с Unknown
export function StatusBadge({ status }: { status: MonitorStatus }) {
  return (
    <Badge tone={statusTone[status]} pulse={status === 'Down'}>
      {status}
    </Badge>
  )
}
