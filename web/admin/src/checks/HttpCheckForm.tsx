import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { errorText } from '../api/client'
import { useSaveHttpCheck } from '../api/queries'
import type { Check } from '../api/types'
import { Button } from '../ui/Button'
import { Checkbox, Input, Select } from '../ui/Field'
import { Note } from '../ui/Note'
import { formatInterval, httpIntervals, withCurrent } from './intervals'

const schema = z.object({
  name: z.string().trim().min(1, 'Required').max(100),
  intervalSeconds: z.number().int(),
  path: z.string().trim().max(500).refine((x) => x === '' || x.startsWith('/'), 'Must start with /'),
  method: z.enum(['GET', 'HEAD']),
  expectedStatus: z.number({ error: 'Required' }).int().min(100).max(599),
  bodyContains: z.string().max(500),
  timeoutSeconds: z.number({ error: 'Required' }).int().min(1).max(60),
  enabled: z.boolean(),
})

type Values = z.infer<typeof schema>

interface Props {
  monitorId: string
  target: string
  check?: Check
  onDone: () => void
}

export function HttpCheckForm({ monitorId, target, check, onDone }: Props) {
  const save = useSaveHttpCheck(monitorId, check?.id)
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: check?.name ?? 'Home page',
      intervalSeconds: check?.intervalSeconds ?? 60,
      path: check?.http?.path ?? '/',
      method: (check?.http?.method as 'GET' | 'HEAD' | undefined) ?? 'GET',
      expectedStatus: check?.http?.expectedStatus ?? 200,
      bodyContains: check?.http?.bodyContains ?? '',
      timeoutSeconds: check?.http?.timeoutSeconds ?? 10,
      enabled: check?.enabled ?? true,
    },
  })
  const errors = formState.errors

  const onSubmit = handleSubmit((values) =>
    save.mutate({ ...values, bodyContains: values.bodyContains || null }, { onSuccess: onDone }),
  )

  return (
    <form className="stack" onSubmit={onSubmit} noValidate>
      <Input label="Name" {...register('name')} error={errors.name?.message} />
      <div className="row" style={{ alignItems: 'flex-start' }}>
        <div style={{ flex: '1 1 140px' }}>
          <Select label="Method" {...register('method')}>
            <option>GET</option>
            <option>HEAD</option>
          </Select>
        </div>
        <div style={{ flex: '3 1 260px' }}>
          <Input label="Path" hint={`Relative to ${target}`} {...register('path')} error={errors.path?.message} />
        </div>
      </div>
      <div className="row" style={{ alignItems: 'flex-start' }}>
        <div style={{ flex: 1 }}>
          <Select label="Every" {...register('intervalSeconds', { valueAsNumber: true })}>
            {withCurrent(httpIntervals, check?.intervalSeconds).map((x) => (
              <option key={x} value={x}>
                {formatInterval(x)}
              </option>
            ))}
          </Select>
        </div>
        <div style={{ flex: 1 }}>
          <Input
            label="Expected status"
            type="number"
            {...register('expectedStatus', { valueAsNumber: true })}
            error={errors.expectedStatus?.message}
          />
        </div>
        <div style={{ flex: 1 }}>
          <Input
            label="Timeout, s"
            type="number"
            {...register('timeoutSeconds', { valueAsNumber: true })}
            error={errors.timeoutSeconds?.message}
          />
        </div>
      </div>
      <Input
        label="Body contains"
        hint="Optional. The check fails if the response body does not contain this text"
        {...register('bodyContains')}
        error={errors.bodyContains?.message}
      />
      <Checkbox label="Enabled" {...register('enabled')} />
      {save.isError && <Note tone="bad">{errorText(save.error)}</Note>}
      <div className="row">
        <span className="spacer" />
        <Button variant="ghost" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" busy={save.isPending}>
          {check ? 'Save' : 'Add check'}
        </Button>
      </div>
    </form>
  )
}
