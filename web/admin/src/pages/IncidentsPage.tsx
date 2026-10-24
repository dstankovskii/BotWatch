import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { useIncidents, useStates } from '../api/queries'
import { Badge } from '../ui/Badge'
import { Card } from '../ui/Card'
import { Seg } from '../ui/Seg'
import { Table } from '../ui/Table'
import { PageHead } from './PageHead'
import { formatDuration, timeAgo } from './status'

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

export function IncidentsPage() {
  const [filter, setFilter] = useState<'open' | 'all'>('all')
  const { data: incidents } = useIncidents(filter === 'open')
  const { data: states } = useStates()

  // в инциденте только id, имена берём из состояний проверок
  const names = useMemo(() => new Map(states?.map((x) => [x.checkId, x])), [states])

  return (
    <>
      <PageHead
        title="Incidents"
        sub="An incident opens after two failed runs in a row and closes on the first successful one."
        actions={
          <Seg
            label="Filter"
            value={filter}
            onChange={setFilter}
            options={[
              { value: 'all', label: 'All' },
              { value: 'open', label: 'Open' },
            ]}
          />
        }
      />
      <Card flush>
        <Table
          rows={incidents}
          rowKey={(x) => x.id}
          empty={filter === 'open' ? 'No open incidents. All good.' : 'No incidents yet.'}
          columns={[
            {
              header: 'State',
              cell: (x) => (x.resolvedAt ? <Badge tone="green">Resolved</Badge> : <Badge tone="red" pulse>Open</Badge>),
            },
            {
              header: 'Check',
              cell: (x) => {
                const state = names.get(x.checkId)
                return state ? (
                  <Link to={`/monitors/${x.monitorId}`}>
                    {state.monitorName} · {state.checkName}
                  </Link>
                ) : (
                  <span className="muted">deleted check</span>
                )
              },
            },
            {
              header: 'Cause',
              cell: (x) => <span className="mono" style={{ fontSize: 'var(--fs-xs)' }}>{x.cause ?? '—'}</span>,
            },
            { header: 'Started', cell: (x) => <span title={dateFormat.format(new Date(x.openedAt))}>{timeAgo(x.openedAt)}</span> },
            { header: 'Duration', align: 'right', cell: (x) => formatDuration(x.openedAt, x.resolvedAt) },
          ]}
        />
      </Card>
    </>
  )
}
