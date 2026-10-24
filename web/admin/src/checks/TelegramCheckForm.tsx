import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { errorText } from '../api/client'
import { useAccounts, useSaveTelegramCheck } from '../api/queries'
import type { Account, Check } from '../api/types'
import {
  fileToBase64,
  fromCheck,
  hourlyLoad,
  maxDurationSeconds,
  messagesPerRun,
  describeIssue,
  parseScenario,
  toRequestSteps,
  type Attachments,
} from '../scenario/scenario'
import { ScenarioEditor } from '../scenario/ScenarioEditor'
import { Button } from '../ui/Button'
import { Checkbox, Input, Select } from '../ui/Field'
import { Note } from '../ui/Note'
import { formatInterval, telegramIntervals, withCurrent } from './intervals'
import styles from './TelegramCheckForm.module.css'

const MAX_VOICE_BYTES = 1024 * 1024

const schema = z.object({
  name: z.string().trim().min(1, 'Required').max(100),
  intervalSeconds: z.number().int(),
  accountId: z.string().min(1, 'Pick an account'),
  enabled: z.boolean(),
})

type Values = z.infer<typeof schema>

interface Props {
  monitorId: string
  check?: Check
  onDone: () => void
}

export function TelegramCheckForm({ monitorId, check, onDone }: Props) {
  const save = useSaveTelegramCheck(monitorId, check?.id)
  const { data: accounts } = useAccounts()

  const [json, setJson] = useState(() => JSON.stringify(fromCheck(check), null, 2))
  const [attachments, setAttachments] = useState<Attachments>({})
  const [attachError, setAttachError] = useState<string>()
  const parsed = useMemo(() => parseScenario(json, attachments), [json, attachments])

  const { register, handleSubmit, formState, control } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: check?.name ?? 'Bot answers /start',
      intervalSeconds: check?.intervalSeconds ?? 900,
      accountId: check?.telegram?.accountId ?? '',
      enabled: check?.enabled ?? true,
    },
  })
  const [intervalSeconds, accountId, enabled] = useWatch({ control, name: ['intervalSeconds', 'accountId', 'enabled'] })

  const account = accounts?.find((x) => x.id === accountId)

  async function attach(file: File | undefined) {
    setAttachError(undefined)
    if (!file) return
    if (file.size > MAX_VOICE_BYTES) {
      setAttachError(`${file.name} is larger than 1 MB`)
      return
    }
    const base64 = await fileToBase64(file)
    if (!base64.startsWith('T2dnUw')) {
      setAttachError(`${file.name} is not an ogg file. Telegram voice messages are ogg/opus`)
      return
    }
    setAttachments((x) => ({ ...x, [file.name]: base64 }))
  }

  const onSubmit = handleSubmit((values) => {
    if (!parsed.steps) return
    save.mutate(
      { ...values, steps: toRequestSteps(parsed.steps, attachments) },
      { onSuccess: onDone },
    )
  })

  return (
    <form className="stack" onSubmit={onSubmit} noValidate>
      <div className="row" style={{ alignItems: 'flex-start' }}>
        <div style={{ flex: '2 1 240px' }}>
          <Input label="Name" {...register('name')} error={formState.errors.name?.message} />
        </div>
        <div style={{ flex: '1 1 140px' }}>
          <Select label="Every" {...register('intervalSeconds', { valueAsNumber: true })}>
            {withCurrent(telegramIntervals, check?.intervalSeconds).map((x) => (
              <option key={x} value={x}>
                {formatInterval(x)}
              </option>
            ))}
          </Select>
        </div>
        <div style={{ flex: '2 1 220px' }}>
          <Select label="Send from account" {...register('accountId')} error={formState.errors.accountId?.message}>
            <option value="">Pick an account…</option>
            {accounts?.map((x) => (
              <option key={x.id} value={x.id}>
                {x.displayName}
                {x.status !== 'Active' ? ` (${x.status})` : ''}
              </option>
            ))}
          </Select>
        </div>
      </div>

      <div className={styles.editorHead}>
        <span className="eyebrow">Scenario, JSON</span>
        <span className="muted">Ctrl+Space for suggestions</span>
      </div>
      <ScenarioEditor value={json} attachments={attachments} onChange={setJson} />

      <div className={styles.attachments}>
        <label className={styles.attach}>
          <input type="file" accept=".ogg,.oga,audio/ogg" onChange={(e) => attach(e.target.files?.[0])} />
          Attach voice (.ogg)
        </label>
        {Object.keys(attachments).map((name) => (
          <code key={name} className={styles.file}>
            file:{name}
          </code>
        ))}
        {check?.telegram?.steps.some((x) => x.kind === 'SendVoice') && (
          <span className="muted">"voice": null keeps the saved file</span>
        )}
      </div>
      {attachError && <Note tone="bad">{attachError}</Note>}

      {parsed.issues.length > 0 ? (
        <Note tone="bad">
          {parsed.issues.map(describeIssue).map((x) => (
            <div key={x}>{x}</div>
          ))}
        </Note>
      ) : (
        parsed.steps && (
          <LoadSummary
            account={account}
            check={check}
            enabled={enabled}
            intervalSeconds={intervalSeconds}
            messages={messagesPerRun(parsed.steps)}
            durationSeconds={maxDurationSeconds(parsed.steps)}
          />
        )
      )}

      <Checkbox label="Enabled" {...register('enabled')} />
      {save.isError && <Note tone="bad">{errorText(save.error)}</Note>}
      <div className="row">
        <span className="spacer" />
        <Button variant="ghost" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" busy={save.isPending} disabled={!parsed.steps}>
          {check ? 'Save' : 'Add check'}
        </Button>
      </div>
    </form>
  )
}

interface LoadProps {
  account: Account | undefined
  check: Check | undefined
  enabled: boolean
  intervalSeconds: number
  messages: number
  durationSeconds: number
}

// в messagesPerHour аккаунта уже сидит старая версия этой проверки, её вычитаем и прибавляем новую
function LoadSummary({ account, check, enabled, intervalSeconds, messages, durationSeconds }: LoadProps) {
  const own = enabled ? hourlyLoad(messages, intervalSeconds) : 0
  const previous =
    check?.enabled && check.telegram && check.telegram.accountId === account?.id
      ? hourlyLoad(check.telegram.messagesPerRun, check.intervalSeconds)
      : 0
  const total = account ? Math.max(0, account.messagesPerHour - previous) + own : own
  const limit = account?.messagesPerHourLimit
  const share = limit ? Math.min(100, (total / limit) * 100) : 0
  const tone = limit && total > limit ? 'bad' : share > 75 ? 'warn' : undefined

  return (
    <div className={styles.load}>
      <div className="row">
        <span>
          {messages} message{messages === 1 ? '' : 's'} per run, ≈{own.toFixed(1)} per hour
        </span>
        <span className="spacer" />
        {limit !== undefined && (
          <span className={tone === 'bad' ? styles.over : undefined}>
            Account total ≈{total.toFixed(1)} / {limit} per hour
          </span>
        )}
      </div>
      {limit !== undefined && (
        <div className={styles.bar} data-tone={tone}>
          <div style={{ width: `${share}%` }} />
        </div>
      )}
      {tone === 'bad' && (
        <Note tone="bad">Over the account limit. Increase the interval, remove steps or use another account.</Note>
      )}
      {durationSeconds >= intervalSeconds && (
        <Note tone="bad">
          The scenario can take up to {durationSeconds} s, which is not shorter than the interval.
        </Note>
      )}
    </div>
  )
}
