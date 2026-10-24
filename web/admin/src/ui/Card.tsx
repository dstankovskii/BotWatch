import type { ReactNode } from 'react'
import styles from './Card.module.css'

interface Props {
  title?: ReactNode
  actions?: ReactNode
  flush?: boolean
  children: ReactNode
}

export function Card({ title, actions, flush, children }: Props) {
  return (
    <section className={flush ? `${styles.card} ${styles.flush}` : styles.card}>
      {(title || actions) && (
        <header className={styles.header}>
          {title && <h3 className={styles.title}>{title}</h3>}
          {actions && <div className={styles.actions}>{actions}</div>}
        </header>
      )}
      {children}
    </section>
  )
}
