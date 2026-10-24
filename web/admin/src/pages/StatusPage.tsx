import { useEffect } from 'react'
import { useStatusPage } from '../api/queries'
import type { StatusLevel } from '../api/types'
import { UptimeBars } from '../charts/UptimeBars'
import { Loading } from '../ui/Loading'
import { Note } from '../ui/Note'
import styles from './StatusPage.module.css'

const headline: Record<StatusLevel, string> = {
  Up: 'All systems operational',
  Down: 'Some systems are down',
  Unknown: 'Status is being checked',
}

const label: Record<StatusLevel, string> = {
  Up: 'Operational',
  Down: 'Down',
  Unknown: 'No data',
}

const updatedFormat = new Intl.DateTimeFormat(undefined, { timeStyle: 'short' })

// единственная страница без логина. данные собирает шлюз, ошибок и внутренних имён тут нет
export function StatusPage() {
  const { data, isPending, isError } = useStatusPage()

  useEffect(() => {
    document.title = 'Status · BotWatch'
  }, [])

  if (isPending) return <Loading />

  return (
    <div className={styles.page}>
      <header className={styles.top}>
        <h1>Status</h1>
        {data && <span className="muted">Updated {updatedFormat.format(new Date(data.updatedAt))}</span>}
      </header>

      {/* если обновление упало, а старые данные есть, показываем их, но честно говорим, что они не свежие */}
      {isError && (
        <Note tone="bad">
          {data
            ? 'Could not refresh the status. Showing the last known state.'
            : 'Status is temporarily unavailable. Try again in a minute.'}
        </Note>
      )}

      {data && (
        <>
          <div className={styles.banner} data-status={data.status}>
            {data.monitors.length === 0 ? 'Nothing is published here yet' : headline[data.status]}
          </div>

          <ul className={styles.list}>
            {data.monitors.map((monitor) => (
              <li key={monitor.id} className={styles.item}>
                <div className={styles.itemHead}>
                  <span className={styles.name}>{monitor.name}</span>
                  <span className={styles.state} data-status={monitor.status}>
                    {label[monitor.status]}
                  </span>
                </div>
                <UptimeBars days={monitor.days.map((x) => ({ day: x.day, uptime: x.uptime ?? null }))} />
                <div className={styles.legend}>
                  <span>90 days ago</span>
                  <span>{monitor.uptime === null || monitor.uptime === undefined ? 'no data' : `${monitor.uptime}% uptime`}</span>
                  <span>today</span>
                </div>
              </li>
            ))}
          </ul>
        </>
      )}

      <footer className={styles.footer}>Powered by BotWatch</footer>
    </div>
  )
}
