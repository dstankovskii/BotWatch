import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { errorText } from '../api/client'
import { useAccounts, useCreateAccount, useDeleteAccount, useRequestLogin } from '../api/queries'
import type { Account, AccountStatus } from '../api/types'
import { Badge, type Tone } from '../ui/Badge'
import { Button } from '../ui/Button'
import { Card } from '../ui/Card'
import { Input } from '../ui/Field'
import { Modal } from '../ui/Modal'
import { Note } from '../ui/Note'
import { Table } from '../ui/Table'
import styles from './Page.module.css'
import { PageHead } from './PageHead'
import { timeAgo } from './status'

const statusTone: Record<AccountStatus, Tone> = {
  Active: 'green',
  PendingLogin: 'amber',
  LoginFailed: 'red',
  Revoked: 'red',
}

const statusText: Record<AccountStatus, string> = {
  Active: 'Active',
  PendingLogin: 'Waiting for login',
  LoginFailed: 'Login failed',
  Revoked: 'Session revoked',
}

export function AccountsPage() {
  const { data: accounts } = useAccounts()
  const [adding, setAdding] = useState(false)
  const [loginFor, setLoginFor] = useState<string>()
  const requestLogin = useRequestLogin()
  const remove = useDeleteAccount()

  const loginAccount = accounts?.find((x) => x.id === loginFor)

  function startLogin(account: Account) {
    requestLogin.mutate(account.id, { onSuccess: () => setLoginFor(account.id) })
  }

  return (
    <>
      <PageHead
        title="Telegram accounts"
        sub="Userbot accounts that talk to your bots. Use a spare account, not your personal one."
        actions={<Button onClick={() => setAdding(true)}>Add account</Button>}
      />

      <Note tone="warn">
        Telegram may limit or ban accounts that send too much. BotWatch caps each account at a fixed number of messages
        per hour and refuses to save checks that would go over it.
      </Note>

      {(requestLogin.isError || remove.isError) && <Note tone="bad">{errorText(requestLogin.error ?? remove.error)}</Note>}

      <div style={{ marginTop: 'var(--s-5)' }}>
        <Card flush>
          <Table
            rows={accounts}
            rowKey={(x) => x.id}
            empty="No accounts yet. Add one and sign in with a QR code from the Telegram app."
            columns={[
              {
                header: 'Account',
                cell: (x) => (
                  <div>
                    <div className={styles.name}>{x.displayName}</div>
                    <div className="muted">{x.telegramName ?? 'not signed in'}</div>
                  </div>
                ),
              },
              {
                header: 'Status',
                cell: (x) => (
                  <div>
                    <Badge tone={x.throttledUntil ? 'amber' : statusTone[x.status]}>
                      {x.throttledUntil ? 'Flood wait' : statusText[x.status]}
                    </Badge>
                    {x.throttledUntil && <div className="muted">until {timeAgo(x.throttledUntil)}</div>}
                    {x.lastError && x.status !== 'Active' && (
                      <div className="muted" style={{ fontSize: 'var(--fs-xs)' }}>
                        {x.lastError}
                      </div>
                    )}
                  </div>
                ),
              },
              { header: 'Checks', cell: (x) => x.checksCount },
              {
                header: 'Load',
                cell: (x) => (
                  <span className="mono">
                    {x.messagesPerHour} / {x.messagesPerHourLimit} per hour
                  </span>
                ),
              },
              {
                header: '',
                align: 'right',
                cell: (x) => (
                  <div className="row" style={{ justifyContent: 'flex-end' }}>
                    {x.status !== 'Active' && (
                      <Button size="sm" variant="ghost" onClick={() => startLogin(x)}>
                        Sign in with QR
                      </Button>
                    )}
                    <Button
                      size="sm"
                      variant="quiet"
                      onClick={() => confirm(`Remove ${x.displayName}?`) && remove.mutate(x.id)}
                    >
                      Remove
                    </Button>
                  </div>
                ),
              },
            ]}
          />
        </Card>
      </div>

      <Modal title="Add Telegram account" open={adding} onClose={() => setAdding(false)}>
        <AddAccountForm
          onDone={(id) => {
            setAdding(false)
            setLoginFor(id)
          }}
        />
      </Modal>

      <Modal title="Sign in with QR" open={Boolean(loginAccount)} onClose={() => setLoginFor(undefined)}>
        {loginAccount && <QrLogin account={loginAccount} onDone={() => setLoginFor(undefined)} />}
      </Modal>
    </>
  )
}

function AddAccountForm({ onDone }: { onDone: (id: string) => void }) {
  const create = useCreateAccount()
  const requestLogin = useRequestLogin()
  const { register, handleSubmit } = useForm<{ displayName: string }>()

  const onSubmit = handleSubmit(async ({ displayName }) => {
    try {
      const account = (await create.mutateAsync(displayName.trim())) as Account
      await requestLogin.mutateAsync(account.id)
      onDone(account.id)
    } catch {
      // показываем ниже
    }
  })

  return (
    <form className="stack" onSubmit={onSubmit}>
      <Input label="Name" hint="Only for you, e.g. “spare SIM” or “checker 1”" autoFocus required {...register('displayName')} />
      {(create.isError || requestLogin.isError) && <Note tone="bad">{errorText(create.error ?? requestLogin.error)}</Note>}
      <div className="row">
        <span className="spacer" />
        <Button type="submit" busy={create.isPending || requestLogin.isPending}>
          Add and sign in
        </Button>
      </div>
    </form>
  )
}

// проба выдаёт QR через несколько секунд после запроса и перевыпускает его, пока не отсканируют.
// картинку перезапрашиваем, список аккаунтов сам опрашивается раз в 3 секунды
function QrLogin({ account, onDone }: { account: Account; onDone: () => void }) {
  const [tick, setTick] = useState(0)

  useEffect(() => {
    const timer = setInterval(() => setTick((x) => x + 1), 5000)
    return () => clearInterval(timer)
  }, [])

  useEffect(() => {
    if (account.status === 'Active') {
      const timer = setTimeout(onDone, 1500)
      return () => clearTimeout(timer)
    }
  }, [account.status, onDone])

  if (account.status === 'Active') {
    return <Note tone="good">Signed in as {account.telegramName}. You can use this account in checks now.</Note>
  }

  if (account.status === 'LoginFailed') {
    return <Note tone="bad">{account.lastError ?? 'Login failed'}. Close this window and try again.</Note>
  }

  return (
    <div className="stack">
      <ol className="muted" style={{ margin: 0, paddingLeft: 'var(--s-5)' }}>
        <li>Open Telegram on the phone with the checker account.</li>
        <li>Settings → Devices → Link Desktop Device.</li>
        <li>Scan the code below.</li>
      </ol>
      {account.qrReady ? (
        <img className={styles.qr} src={`/api/accounts/${account.id}/qr.png?t=${tick}`} alt="Telegram login QR code" />
      ) : (
        <p className="muted" style={{ textAlign: 'center' }}>
          Waiting for the probe to issue a code…
        </p>
      )}
      <p className="muted" style={{ fontSize: 'var(--fs-xs)' }}>
        If the account has two-step verification enabled, disable it for the login or use another account: BotWatch
        does not ask for cloud passwords.
      </p>
    </div>
  )
}
