import type { ReactNode } from 'react'
import styles from './Note.module.css'

export function Note({ tone, children }: { tone?: 'bad' | 'warn' | 'good'; children: ReactNode }) {
  return (
    <div className={tone ? `${styles.note} ${styles[tone]}` : styles.note} role={tone === 'bad' ? 'alert' : undefined}>
      {children}
    </div>
  )
}
