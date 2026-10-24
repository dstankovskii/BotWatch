import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { errorText } from '../api/client'
import { useCreateMonitor, useUpdateMonitor } from '../api/queries'
import type { Monitor } from '../api/types'
import { Button } from '../ui/Button'
import { Checkbox, Input, Select } from '../ui/Field'
import { Note } from '../ui/Note'

const schema = z
  .object({
    name: z.string().trim().min(1, 'Required').max(100),
    kind: z.enum(['Http', 'TelegramBot']),
    target: z.string().trim().min(1, 'Required').max(500),
    enabled: z.boolean(),
    isPublic: z.boolean(),
  })
  .superRefine((x, ctx) => {
    if (x.kind === 'Http' && !/^https?:\/\/\S+$/.test(x.target)) {
      ctx.addIssue({ code: 'custom', path: ['target'], message: 'Full URL, e.g. https://example.com' })
    }
    if (x.kind === 'TelegramBot' && !/^@?\w{5,32}$/.test(x.target)) {
      ctx.addIssue({ code: 'custom', path: ['target'], message: 'Bot username, e.g. @my_bot' })
    }
  })

type Values = z.infer<typeof schema>

export function MonitorForm({ monitor, onDone }: { monitor?: Monitor; onDone: (id: string) => void }) {
  const create = useCreateMonitor()
  const update = useUpdateMonitor()
  const failed = create.error ?? update.error

  const { register, handleSubmit, formState, control } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: monitor?.name ?? '',
      kind: monitor?.kind ?? 'TelegramBot',
      target: monitor?.target ?? '',
      enabled: monitor?.enabled ?? true,
      isPublic: monitor?.isPublic ?? false,
    },
  })
  const kind = useWatch({ control, name: 'kind' })
  const errors = formState.errors

  const onSubmit = handleSubmit(async (values) => {
    try {
      if (monitor) {
        await update.mutateAsync({ id: monitor.id, ...values })
        onDone(monitor.id)
        return
      }

      // при создании API публичность не принимает, она ставится вторым запросом
      const created = (await create.mutateAsync(values)) as Monitor
      if (values.isPublic) {
        await update.mutateAsync({ id: created.id, ...values, enabled: true })
      }
      onDone(created.id)
    } catch {
      // ошибку показывает Note ниже из состояния мутации
    }
  })

  return (
    <form className="stack" onSubmit={onSubmit} noValidate>
      <Input label="Name" autoFocus {...register('name')} error={errors.name?.message} />
      {/* тип у существующего монитора не меняется: проверки у HTTP и бота разные */}
      <Select label="Type" disabled={Boolean(monitor)} {...register('kind')}>
        <option value="TelegramBot">Telegram bot</option>
        <option value="Http">Website / HTTP API</option>
      </Select>
      <Input
        label={kind === 'Http' ? 'Base URL' : 'Bot username'}
        placeholder={kind === 'Http' ? 'https://example.com' : '@my_bot'}
        {...register('target')}
        error={errors.target?.message}
      />
      {monitor && <Checkbox label="Enabled" {...register('enabled')} />}
      <Checkbox label="Show on the public status page" {...register('isPublic')} />
      {failed && <Note tone="bad">{errorText(failed)}</Note>}
      <div className="row">
        <span className="spacer" />
        <Button type="submit" busy={formState.isSubmitting}>
          {monitor ? 'Save' : 'Create monitor'}
        </Button>
      </div>
    </form>
  )
}
