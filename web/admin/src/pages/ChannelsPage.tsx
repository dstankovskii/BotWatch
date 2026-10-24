import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { errorText } from '../api/client'
import { useChannels, useDeleteChannel, useSaveChannel, useTestChannel } from '../api/queries'
import type { Channel } from '../api/types'
import { Badge } from '../ui/Badge'
import { Button } from '../ui/Button'
import { Card } from '../ui/Card'
import { Checkbox, Input } from '../ui/Field'
import { Modal } from '../ui/Modal'
import { Note } from '../ui/Note'
import { Table } from '../ui/Table'
import styles from './Page.module.css'
import { PageHead } from './PageHead'

export function ChannelsPage() {
  const { data: channels } = useChannels()
  const [editing, setEditing] = useState<Channel | 'new' | null>(null)
  const test = useTestChannel()
  const remove = useDeleteChannel()
  const [tested, setTested] = useState<string>()

  return (
    <>
      <PageHead
        title="Alert channels"
        sub="Where BotWatch sends alerts: a Telegram chat through your own bot from @BotFather."
        actions={<Button onClick={() => setEditing('new')}>Add channel</Button>}
      />

      {test.isError && <Note tone="bad">Test message failed: {errorText(test.error)}</Note>}
      {tested && !test.isError && <Note tone="good">Test message sent to {tested}. Check the chat.</Note>}

      <div style={{ marginTop: 'var(--s-4)' }}>
        <Card flush>
          <Table
            rows={channels}
            rowKey={(x) => x.id}
            empty="No channels. Without one, incidents are only visible here in the admin."
            columns={[
              { header: 'Name', cell: (x) => <span className={styles.name}>{x.name}</span> },
              { header: 'Bot token', cell: (x) => <span className="mono">{x.botToken}</span> },
              { header: 'Chat', cell: (x) => <span className="mono">{x.chatId}</span> },
              {
                header: 'State',
                cell: (x) => (x.enabled ? <Badge tone="green">Enabled</Badge> : <Badge tone="grey">Disabled</Badge>),
              },
              {
                header: '',
                align: 'right',
                cell: (x) => (
                  <div className="row" style={{ justifyContent: 'flex-end' }}>
                    <Button
                      size="sm"
                      variant="ghost"
                      busy={test.isPending && test.variables === x.id}
                      onClick={() => {
                        setTested(undefined)
                        test.mutate(x.id, { onSuccess: () => setTested(x.name) })
                      }}
                    >
                      Send test
                    </Button>
                    <Button size="sm" variant="quiet" onClick={() => setEditing(x)}>
                      Edit
                    </Button>
                    <Button size="sm" variant="quiet" onClick={() => confirm(`Delete ${x.name}?`) && remove.mutate(x.id)}>
                      Delete
                    </Button>
                  </div>
                ),
              },
            ]}
          />
        </Card>
      </div>

      <Modal title={editing === 'new' ? 'New channel' : 'Edit channel'} open={editing !== null} onClose={() => setEditing(null)}>
        {editing !== null && (
          <ChannelForm channel={editing === 'new' ? undefined : editing} onDone={() => setEditing(null)} />
        )}
      </Modal>
    </>
  )
}

function ChannelForm({ channel, onDone }: { channel?: Channel; onDone: () => void }) {
  const save = useSaveChannel()

  // у существующего канала токен можно не трогать: пустое поле = оставить как есть
  const schema = z.object({
    name: z.string().trim().min(1, 'Required').max(100),
    botToken: channel
      ? z.string().trim()
      : z.string().trim().regex(/^\d+:[\w-]{30,}$/, 'Token from @BotFather, like 123456:ABC-DEF…'),
    chatId: z.string().trim().min(1, 'Required').max(100),
    enabled: z.boolean(),
  })

  const { register, handleSubmit, formState } = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: channel?.name ?? 'Me', botToken: '', chatId: channel?.chatId ?? '', enabled: channel?.enabled ?? true },
  })
  const errors = formState.errors

  const onSubmit = handleSubmit((values) =>
    save.mutate({ id: channel?.id, ...values, botToken: values.botToken || null }, { onSuccess: onDone }),
  )

  return (
    <form className="stack" onSubmit={onSubmit} noValidate>
      <Input label="Name" {...register('name')} error={errors.name?.message} />
      <Input
        label="Bot token"
        type="password"
        autoComplete="off"
        placeholder={channel ? `Current: ${channel.botToken}` : '123456:ABC-DEF…'}
        hint={channel ? 'Leave empty to keep the current token' : 'Stored encrypted, never shown again in full'}
        {...register('botToken')}
        error={errors.botToken?.message}
      />
      <Input
        label="Chat id"
        hint="Your user id, a group id like -100123… or @channel. Write to the bot first, otherwise it cannot message you"
        {...register('chatId')}
        error={errors.chatId?.message}
      />
      {channel && <Checkbox label="Enabled" {...register('enabled')} />}
      {save.isError && <Note tone="bad">{errorText(save.error)}</Note>}
      <div className="row">
        <span className="spacer" />
        <Button type="submit" busy={save.isPending}>
          Save
        </Button>
      </div>
    </form>
  )
}
