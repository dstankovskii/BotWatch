import { lazy, Suspense, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { errorText } from '../api/client'
import { useAccounts, useDeleteCheck, useDeleteMonitor, useMonitor, useResults, useStates, useUptime } from '../api/queries'
import type { Check } from '../api/types'
import { lastDays } from '../charts/days'
import { UptimeBars } from '../charts/UptimeBars'
import { HttpCheckForm } from '../checks/HttpCheckForm'
import { formatInterval } from '../checks/intervals'
import { Button } from '../ui/Button'
import { StatusBadge } from '../ui/Badge'
import { Card } from '../ui/Card'
import { Loading } from '../ui/Loading'
import { Modal } from '../ui/Modal'
import { Note } from '../ui/Note'
import { MonitorForm } from './MonitorForm'
import { NotFoundPage } from './NotFoundPage'
import styles from './Page.module.css'
import { PageHead } from './PageHead'
import { checkStatus, monitorStatus, timeAgo, uptimeByDay } from './status'

// CodeMirror и Recharts - самые тяжёлые куски, грузим их только на этой странице и только когда нужны
const TelegramCheckForm = lazy(() => import('../checks/TelegramCheckForm').then((m) => ({ default: m.TelegramCheckForm })))
const ResponseChart = lazy(() => import('../charts/ResponseChart').then((m) => ({ default: m.ResponseChart })))

type Editing = { kind: 'monitor' } | { kind: 'check'; check?: Check } | null

export function MonitorPage() {
  const { id = '' } = useParams()
  const { data: monitor, isPending, error } = useMonitor(id)
  const { data: states } = useStates()
  const [editing, setEditing] = useState<Editing>(null)
  const [selectedId, setSelectedId] = useState<string>()
  const deleteMonitor = useDeleteMonitor()
  const deleteCheck = useDeleteCheck(id)
  const navigate = useNavigate()

  const stateById = useMemo(() => new Map(states?.map((x) => [x.checkId, x])), [states])

  if (isPending) return <Loading />
  if (error || !monitor) return <NotFoundPage />

  const selected = monitor.checks.find((x) => x.id === selectedId) ?? monitor.checks[0]
  const close = () => setEditing(null)

  function removeMonitor() {
    if (confirm(`Delete monitor "${monitor!.name}" with all its checks and history?`)) {
      deleteMonitor.mutate(monitor!.id, { onSuccess: () => navigate('/') })
    }
  }

  function removeCheck(check: Check) {
    if (confirm(`Delete check "${check.name}"?`)) {
      deleteCheck.mutate(check.id)
    }
  }

  return (
    <>
      <PageHead
        title={
          <span className="row">
            {monitor.name} <StatusBadge status={monitorStatus(monitor, stateById)} />
          </span>
        }
        sub={
          <>
            <span className="mono">{monitor.target}</span>
            {monitor.isPublic && <span className="muted"> · on the public status page</span>}
          </>
        }
        actions={
          <>
            <Button variant="ghost" onClick={() => setEditing({ kind: 'monitor' })}>
              Edit
            </Button>
            <Button variant="danger" busy={deleteMonitor.isPending} onClick={removeMonitor}>
              Delete
            </Button>
          </>
        }
      />

      {(deleteMonitor.isError || deleteCheck.isError) && (
        <Note tone="bad">{errorText(deleteMonitor.error ?? deleteCheck.error)}</Note>
      )}

      <div className={styles.sections}>
        <Card
          flush
          title="Checks"
          actions={
            <Button size="sm" onClick={() => setEditing({ kind: 'check' })}>
              Add check
            </Button>
          }
        >
          {monitor.checks.length === 0 ? (
            <p className="muted" style={{ padding: 'var(--s-5)' }}>
              {monitor.kind === 'TelegramBot'
                ? 'Add a scenario: what to send to the bot and what reply to expect.'
                : 'Add a check: which path to request and what response to expect.'}
            </p>
          ) : (
            <div className={styles.checkList} role="listbox" aria-label="Checks">
              {monitor.checks.map((check) => (
                <CheckRow
                  key={check.id}
                  check={check}
                  selected={check.id === selected?.id}
                  status={checkStatus(check.enabled, stateById.get(check.id))}
                  lastResultAt={stateById.get(check.id)?.lastResultAt}
                  lastError={stateById.get(check.id)?.lastError}
                  onSelect={() => setSelectedId(check.id)}
                  onEdit={() => setEditing({ kind: 'check', check })}
                  onDelete={() => removeCheck(check)}
                />
              ))}
            </div>
          )}
        </Card>

        {selected && <CheckCharts check={selected} />}
      </div>

      <Modal title="Edit monitor" open={editing?.kind === 'monitor'} onClose={close}>
        <MonitorForm monitor={monitor} onDone={close} />
      </Modal>

      <Modal
        title={editing?.kind === 'check' && editing.check ? `Edit “${editing.check.name}”` : 'New check'}
        open={editing?.kind === 'check'}
        onClose={close}
        wide={monitor.kind === 'TelegramBot'}
      >
        {editing?.kind === 'check' &&
          (monitor.kind === 'TelegramBot' ? (
            <Suspense fallback={<Loading />}>
              <TelegramCheckForm monitorId={monitor.id} check={editing.check} onDone={close} />
            </Suspense>
          ) : (
            <HttpCheckForm monitorId={monitor.id} target={monitor.target} check={editing.check} onDone={close} />
          ))}
      </Modal>
    </>
  )
}

interface CheckRowProps {
  check: Check
  selected: boolean
  status: ReturnType<typeof checkStatus>
  lastResultAt?: string | null
  lastError?: string | null
  onSelect: () => void
  onEdit: () => void
  onDelete: () => void
}

function CheckRow({ check, selected, status, lastResultAt, lastError, onSelect, onEdit, onDelete }: CheckRowProps) {
  const { data: accounts } = useAccounts()
  const account = check.telegram && accounts?.find((x) => x.id === check.telegram!.accountId)

  return (
    <div className={styles.checkRow} role="option" aria-selected={selected} onClick={onSelect}>
      <StatusBadge status={status} />
      <div>
        <div className={styles.name}>{check.name}</div>
        <div className={styles.checkMeta}>
          <span>every {formatInterval(check.intervalSeconds)}</span>
          {check.http && (
            <span className="mono">
              {check.http.method} {check.http.path} → {check.http.expectedStatus}
            </span>
          )}
          {check.telegram && (
            <span>
              {check.telegram.steps.length} steps, {check.telegram.messagesPerRun} msg/run
              {account && ` · from ${account.displayName}`}
            </span>
          )}
          <span>last run {timeAgo(lastResultAt)}</span>
        </div>
      </div>
      <div className="row" onClick={(e) => e.stopPropagation()}>
        <Button variant="quiet" size="sm" onClick={onEdit}>
          Edit
        </Button>
        <Button variant="quiet" size="sm" onClick={onDelete}>
          Delete
        </Button>
      </div>
      {status === 'Down' && lastError && <div className={styles.error}>{lastError}</div>}
    </div>
  )
}

function CheckCharts({ check }: { check: Check }) {
  const { data: results } = useResults(check.id)
  const { data: uptime } = useUptime(30)
  const { byDay, overall } = uptimeByDay(uptime, [check.id])

  return (
    <div className={styles.grid2}>
      <Card title={`Uptime · ${check.name}`} actions={<strong>{overall === null ? '—' : `${overall}%`}</strong>}>
        <UptimeBars days={lastDays(30, byDay)} />
        <div className="row muted" style={{ marginTop: 'var(--s-2)', fontSize: 'var(--fs-xs)' }}>
          <span>30 days ago</span>
          <span className="spacer" />
          <span>today</span>
        </div>
      </Card>
      <Card title="Response time, last runs">
        <Suspense fallback={<Loading />}>{results ? <ResponseChart results={results} /> : <Loading />}</Suspense>
      </Card>
    </div>
  )
}
