import { z } from 'zod'
import type { Check, ScenarioStep } from '../api/types'

// повторяет правила TelegramScenario на бэке: ошибки видно сразу в редакторе, а не после сохранения.
// последнее слово всё равно за сервером
export const MAX_STEPS = 10
const MAX_TEXT = 4096
const PAUSE_BETWEEN_SENDS = 3

const text = z.string().trim().min(1, 'Must not be empty').max(MAX_TEXT)

const stepSchema = z.discriminatedUnion('kind', [
  z.strictObject({
    kind: z.literal('SendText'),
    text: text.describe('Message to send'),
  }),
  z.strictObject({
    kind: z.literal('SendCommand'),
    text: text.describe('Command, e.g. /status. Leading slash is added if missing'),
  }),
  z.strictObject({
    kind: z.literal('SendRandom'),
    phrases: z.array(text).min(1).max(50).describe('One of these is sent at random each run'),
  }),
  z.strictObject({
    kind: z.literal('SendVoice'),
    // сам файл в JSON не кладём: мегабайт base64 в редакторе неудобен, прикладывается кнопкой под ним
    voice: z
      .string()
      .regex(/^file:.+/, 'Use "file:<name>" and attach the file below the editor')
      .nullable()
      .describe('"file:<name>" of an attached .ogg (opus, up to 1 MB). null keeps the file that is already saved'),
  }),
  z.strictObject({
    kind: z.literal('ClickButton'),
    text: text.describe('Button caption to click'),
  }),
  z.strictObject({
    kind: z.literal('ExpectReply'),
    condition: z
      .enum(['Any', 'Contains', 'Regex', 'HasButton', 'IsVoice', 'HasFile'])
      .optional()
      .describe('What the reply must look like. Any by default'),
    text: z.string().optional().describe('Substring for Contains, pattern for Regex, button caption for HasButton'),
    timeoutSeconds: z.number().int().min(5).max(300).optional().describe('How long to wait for the reply, 60 by default'),
  }),
])

export type EditorStep = z.infer<typeof stepSchema>

export const scenarioSchema = z
  .array(stepSchema)
  .min(1, 'Scenario needs at least one step')
  .max(MAX_STEPS, `At most ${MAX_STEPS} steps`)
  .superRefine((steps, ctx) => {
    const first = steps[0]
    if (first && (first.kind === 'ExpectReply' || first.kind === 'ClickButton')) {
      ctx.addIssue({ code: 'custom', path: [0, 'kind'], message: 'Scenario must start with sending a message' })
    }

    let replySeen = false
    steps.forEach((step, i) => {
      if (step.kind === 'ClickButton' && !replySeen) {
        ctx.addIssue({ code: 'custom', path: [i, 'kind'], message: 'ClickButton needs an ExpectReply before it' })
      }
      if (step.kind === 'ExpectReply') {
        replySeen = true
        if ((step.condition === 'Contains' || step.condition === 'Regex') && !step.text?.trim()) {
          ctx.addIssue({ code: 'custom', path: [i, 'text'], message: `${step.condition} needs text` })
        }
        if (step.condition === 'Regex' && step.text) {
          try {
            new RegExp(step.text)
          } catch {
            ctx.addIssue({ code: 'custom', path: [i, 'text'], message: 'Invalid regex' })
          }
        }
      }
    })

    if (!replySeen) {
      ctx.addIssue({ code: 'custom', path: [], message: 'Add at least one ExpectReply, otherwise the check checks nothing' })
    }
  })

export type IssuePath = (string | number)[]

// path нужен редактору, чтобы подчеркнуть ошибку на месте, а не только списком под ним
export interface Issue {
  path: IssuePath
  message: string
}

export interface ParseResult {
  steps?: EditorStep[]
  issues: Issue[]
}

// имя файла -> base64
export type Attachments = Record<string, string>

export function parseScenario(json: string, attachments: Attachments): ParseResult {
  let value: unknown
  try {
    value = JSON.parse(json)
  } catch (e) {
    return { issues: [{ path: [], message: `Invalid JSON: ${(e as Error).message}` }] }
  }

  const result = scenarioSchema.safeParse(value)
  if (!result.success) {
    return {
      issues: result.error.issues.map((x) => ({ path: x.path.filter((p) => typeof p !== 'symbol'), message: x.message })),
    }
  }

  const missing = result.data.flatMap((step, i): Issue[] =>
    step.kind === 'SendVoice' && step.voice && !(fileName(step.voice) in attachments)
      ? [{ path: [i, 'voice'], message: `${fileName(step.voice)} is not attached` }]
      : [],
  )

  return missing.length > 0 ? { issues: missing } : { steps: result.data, issues: [] }
}

export function describeIssue({ path, message }: Issue) {
  const [index, field] = path
  const where = typeof index === 'number' ? `Step ${index + 1}${field !== undefined ? ` (${String(field)})` : ''}: ` : ''
  return where + message
}

function fileName(voice: string) {
  return voice.slice('file:'.length)
}

export function toRequestSteps(steps: EditorStep[], attachments: Attachments): ScenarioStep[] {
  return steps.map((step) =>
    step.kind === 'SendVoice'
      ? { kind: 'SendVoice', voice: step.voice ? attachments[fileName(step.voice)] : null }
      : ({ ...step } as ScenarioStep),
  )
}

// что лежит на сервере -> что показываем в редакторе. файл голосового сервер не отдаёт, только размер
export function fromCheck(check: Check | undefined): EditorStep[] {
  if (!check?.telegram) {
    return [
      { kind: 'SendCommand', text: '/start' },
      { kind: 'ExpectReply', condition: 'Any', timeoutSeconds: 60 },
    ]
  }

  return check.telegram.steps.map((step): EditorStep => {
    switch (step.kind) {
      case 'SendText':
      case 'SendCommand':
      case 'ClickButton':
        return { kind: step.kind, text: step.text ?? '' }
      case 'SendRandom':
        return { kind: 'SendRandom', phrases: step.phrases ?? [] }
      case 'SendVoice':
        return { kind: 'SendVoice', voice: null }
      case 'ExpectReply':
        return {
          kind: 'ExpectReply',
          condition: step.condition ?? 'Any',
          ...(step.text ? { text: step.text } : {}),
          timeoutSeconds: step.timeoutSeconds ?? 60,
        }
    }
  })
}

export function messagesPerRun(steps: EditorStep[]) {
  return steps.filter((x) => x.kind !== 'ExpectReply').length
}

export function hourlyLoad(messages: number, intervalSeconds: number) {
  return intervalSeconds > 0 ? (messages * 3600) / intervalSeconds : 0
}

// худший случай: все ожидания до таймаута. если дольше интервала, прогоны будут наезжать друг на друга
export function maxDurationSeconds(steps: EditorStep[]) {
  const waits = steps.reduce((sum, x) => sum + (x.kind === 'ExpectReply' ? (x.timeoutSeconds ?? 60) : 0), 0)
  return waits + messagesPerRun(steps) * PAUSE_BETWEEN_SENDS
}

export async function fileToBase64(file: File) {
  const bytes = new Uint8Array(await file.arrayBuffer())
  let binary = ''
  for (let i = 0; i < bytes.length; i += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  }
  return btoa(binary)
}
