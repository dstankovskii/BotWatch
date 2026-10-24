import type { DayUptime } from './days'
import styles from './UptimeBars.module.css'

// полоска на день, как на статус-страницах: без библиотеки, это просто div'ы
export function UptimeBars({ days }: { days: DayUptime[] }) {
  return (
    <div className={styles.bars} role="img" aria-label="Daily uptime">
      {days.map((x) => (
        <span
          key={x.day}
          className={styles.bar}
          data-tone={tone(x.uptime)}
          title={`${x.day}: ${x.uptime === null ? 'no data' : `${x.uptime}%`}`}
        />
      ))}
    </div>
  )
}

function tone(uptime: number | null) {
  if (uptime === null) return 'none'
  if (uptime >= 99.5) return 'good'
  if (uptime >= 95) return 'warn'
  return 'bad'
}
