import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useIncidents, useMonitors, useStates, useUptime } from '../api/queries'
import { lastDays } from '../charts/days'
import { UptimeBars } from '../charts/UptimeBars'
import { Button } from '../ui/Button'
import { StatusBadge } from '../ui/Badge'
import { Card } from '../ui/Card'
import { Loading } from '../ui/Loading'
import { Modal } from '../ui/Modal'
import { Table } from '../ui/Table'
import { MonitorForm } from './MonitorForm'
import styles from './Page.module.css'
import { PageHead } from './PageHead'
import { monitorStatus, uptimeByDay } from './status'

export function OverviewPage() {
  const { data: monitors, isPending } = useMonitors()
  const { data: states } = useStates()
  const { data: uptime } = useUptime(30)
  const { data: openIncidents } = useIncidents(true)
  const [creating, setCreating] = useState(false)
  const navigate = useNavigate()

  const stateById = useMemo(() => new Map(states?.map((x) => [x.checkId, x])), [states])

  const rows = useMemo(
    () =>
      monitors?.map((monitor) => {
        const { byDay, overall } = uptimeByDay(uptime, monitor.checks.map((x) => x.id))
        return { monitor, status: monitorStatus(monitor, stateById), byDay, overall }
      }),
    [monitors, uptime, stateById],
  )

  // считаем по проверкам из monitoring: проекция в incidents может отставать на пару секунд после удаления
  const enabledChecks =
    monitors?.flatMap((m) => (m.enabled ? m.checks.filter((c) => c.enabled) : [])).map((c) => stateById.get(c.id)) ?? []
  const down = enabledChecks.filter((x) => x?.status === 'Down').length

  if (isPending) return <Loading />

  return (
    <>
      <PageHead
        title="Overview"
        sub="Everything BotWatch is watching. Refreshes every 15 seconds while this tab is open."
        actions={<Button onClick={() => setCreating(true)}>New monitor</Button>}
      />

      <div className={styles.tiles}>
        <Tile label="Monitors" value={monitors?.length ?? 0} />
        <Tile label="Checks up" value={enabledChecks.filter((x) => x?.status === 'Up').length} tone="good" />
        <Tile label="Checks down" value={down} tone={down > 0 ? 'bad' : undefined} />
        <Tile label="Open incidents" value={openIncidents?.length ?? 0} tone={openIncidents?.length ? 'bad' : undefined} />
      </div>

      <Card flush title="Monitors">
        <Table
          rows={rows}
          rowKey={(x) => x.monitor.id}
          empty="No monitors yet. Create one for each bot or site you want to watch."
          columns={[
            {
              header: 'Name',
              cell: (x) => (
                <div>
                  <Link to={`/monitors/${x.monitor.id}`} className={styles.name}>
                    {x.monitor.name}
                  </Link>
                  <div className="muted mono" style={{ fontSize: 'var(--fs-xs)' }}>
                    {x.monitor.target}
                  </div>
                </div>
              ),
            },
            { header: 'Status', cell: (x) => <StatusBadge status={x.status} /> },
            { header: 'Checks', cell: (x) => x.monitor.checks.length },
            {
              header: 'Last 30 days',
              cell: (x) => (
                <div style={{ minWidth: 180 }}>
                  <UptimeBars days={lastDays(30, x.byDay)} />
                </div>
              ),
            },
            { header: 'Uptime', align: 'right', cell: (x) => (x.overall === null ? '—' : `${x.overall}%`) },
          ]}
        />
      </Card>

      <Modal title="New monitor" open={creating} onClose={() => setCreating(false)}>
        <MonitorForm
          onDone={(id) => {
            setCreating(false)
            navigate(`/monitors/${id}`)
          }}
        />
      </Modal>
    </>
  )
}

function Tile({ label, value, tone }: { label: string; value: number; tone?: 'good' | 'bad' }) {
  return (
    <div className={styles.tile}>
      <div className="eyebrow">{label}</div>
      <div className={styles.tileValue} data-tone={tone}>
        {value}
      </div>
    </div>
  )
}
