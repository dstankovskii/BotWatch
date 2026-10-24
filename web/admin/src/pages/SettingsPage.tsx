import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { errorText } from '../api/client'
import { useAuthState, useChangePassword, useCreateUser, useDeleteUser, useUsers } from '../api/queries'
import { Button } from '../ui/Button'
import { Card } from '../ui/Card'
import { Input } from '../ui/Field'
import { Note } from '../ui/Note'
import { Table } from '../ui/Table'
import { credentialsSchema } from '../forms'
import styles from './Page.module.css'
import { PageHead } from './PageHead'

export function SettingsPage() {
  return (
    <>
      <PageHead title="Settings" sub="People who can sign in to this BotWatch. Everyone has the same rights." />
      <div className={styles.grid2}>
        <Users />
        <ChangePassword />
      </div>
    </>
  )
}

function Users() {
  const { data: users } = useUsers()
  const { data: auth } = useAuthState()
  const create = useCreateUser()
  const remove = useDeleteUser()
  const { register, handleSubmit, reset, formState } = useForm<z.infer<typeof credentialsSchema>>({
    resolver: zodResolver(credentialsSchema),
  })

  const onSubmit = handleSubmit((values) => create.mutate(values, { onSuccess: () => reset() }))

  return (
    <Card flush title="Users">
      <Table
        rows={users}
        rowKey={(x) => x.id}
        columns={[
          {
            header: 'Name',
            cell: (x) => (
              <span className={styles.name}>
                {x.userName}
                {x.userName === auth?.userName && <span className="muted"> (you)</span>}
              </span>
            ),
          },
          {
            header: '',
            align: 'right',
            cell: (x) =>
              x.userName !== auth?.userName && (
                <Button size="sm" variant="quiet" onClick={() => confirm(`Delete ${x.userName}?`) && remove.mutate(x.id)}>
                  Delete
                </Button>
              ),
          },
        ]}
      />
      <form className="stack" style={{ padding: 'var(--s-5)', borderTop: '1px solid var(--hairline)' }} onSubmit={onSubmit} noValidate>
        <div className="eyebrow">Add user</div>
        <Input label="User name" autoComplete="off" {...register('userName')} error={formState.errors.userName?.message} />
        <Input label="Password" type="password" autoComplete="new-password" {...register('password')} error={formState.errors.password?.message} />
        {(create.isError || remove.isError) && <Note tone="bad">{errorText(create.error ?? remove.error)}</Note>}
        <div className="row">
          <span className="spacer" />
          <Button type="submit" busy={create.isPending}>
            Add user
          </Button>
        </div>
      </form>
    </Card>
  )
}

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Required'),
    newPassword: z.string().min(10, 'At least 10 characters'),
    repeat: z.string(),
  })
  .refine((x) => x.newPassword === x.repeat, { path: ['repeat'], message: 'Passwords do not match' })

function ChangePassword() {
  const change = useChangePassword()
  const { register, handleSubmit, reset, formState } = useForm<z.infer<typeof passwordSchema>>({
    resolver: zodResolver(passwordSchema),
  })
  const errors = formState.errors

  const onSubmit = handleSubmit(({ currentPassword, newPassword }) =>
    change.mutate({ currentPassword, newPassword }, { onSuccess: () => reset() }),
  )

  return (
    <Card title="Change password">
      <form className="stack" onSubmit={onSubmit} noValidate>
        <Input label="Current password" type="password" autoComplete="current-password" {...register('currentPassword')} error={errors.currentPassword?.message} />
        <Input label="New password" type="password" autoComplete="new-password" {...register('newPassword')} error={errors.newPassword?.message} />
        <Input label="Repeat new password" type="password" autoComplete="new-password" {...register('repeat')} error={errors.repeat?.message} />
        {change.isError && <Note tone="bad">{errorText(change.error)}</Note>}
        {change.isSuccess && <Note tone="good">Password changed. Other sessions of this user will be signed out within a minute.</Note>}
        <div className="row">
          <span className="spacer" />
          <Button type="submit" busy={change.isPending}>
            Change password
          </Button>
        </div>
      </form>
    </Card>
  )
}
