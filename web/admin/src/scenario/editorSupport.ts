import { snippetCompletion, type Completion, type CompletionContext, type CompletionResult } from '@codemirror/autocomplete'
import { syntaxTree } from '@codemirror/language'
import type { Diagnostic } from '@codemirror/lint'
import type { EditorState } from '@codemirror/state'
import type { SyntaxNode } from '@lezer/common'
import { parseScenario, type Attachments, type IssuePath } from './scenario'

// подсказки по шагам. codemirror-json-schema умеет то же из JSON Schema, но тянет shiki и markdown-it на ~600 КБ
const stepKinds: Record<string, { info: string; fields: Record<string, string>; snippet: string }> = {
  SendCommand: {
    info: 'Send a bot command',
    fields: { text: 'Command, e.g. /status' },
    snippet: '{ "kind": "SendCommand", "text": "${/start}" }',
  },
  SendText: {
    info: 'Send a text message',
    fields: { text: 'Message text' },
    snippet: '{ "kind": "SendText", "text": "${Hello}" }',
  },
  SendRandom: {
    info: 'Send one random phrase from a pool',
    fields: { phrases: 'Pool of phrases, one is picked each run' },
    snippet: '{ "kind": "SendRandom", "phrases": ["${How are you?}", "What can you do?"] }',
  },
  SendVoice: {
    info: 'Send a voice message from an attached .ogg',
    fields: { voice: '"file:<name>" of an attached file, null keeps the saved one' },
    snippet: '{ "kind": "SendVoice", "voice": "file:${hello.ogg}" }',
  },
  ClickButton: {
    info: 'Click an inline or reply keyboard button from the last reply',
    fields: { text: 'Button caption' },
    snippet: '{ "kind": "ClickButton", "text": "${Status}" }',
  },
  ExpectReply: {
    info: 'Wait for the bot to answer',
    fields: {
      condition: 'Any | Contains | Regex | HasButton | IsVoice | HasFile',
      text: 'Substring, regex or button caption, depending on condition',
      timeoutSeconds: 'How long to wait, 5-300 s, 60 by default',
    },
    snippet: '{ "kind": "ExpectReply", "condition": "${Any}", "timeoutSeconds": 60 }',
  },
}

const conditions: Record<string, string> = {
  Any: 'Any reply counts',
  Contains: 'Reply text contains "text"',
  Regex: 'Reply text matches the "text" regex',
  HasButton: 'Reply has a button, with caption "text" if set',
  IsVoice: 'Reply is a voice message',
  HasFile: 'Reply has a file or a photo',
}

const VALUE_NODES = new Set(['Object', 'Array', 'String', 'Number', 'True', 'False', 'Null'])

function text(state: EditorState, node: SyntaxNode) {
  return state.sliceDoc(node.from, node.to)
}

function unquote(raw: string) {
  try {
    return JSON.parse(raw) as unknown
  } catch {
    return undefined
  }
}

function propertyName(state: EditorState, property: SyntaxNode) {
  const name = property.getChild('PropertyName')
  return name ? (unquote(text(state, name)) as string | undefined) : undefined
}

function kindOf(state: EditorState, object: SyntaxNode) {
  for (const property of object.getChildren('Property')) {
    if (propertyName(state, property) === 'kind') {
      const value = property.lastChild
      return value?.name === 'String' ? (unquote(text(state, value)) as string) : undefined
    }
  }
  return undefined
}

export function scenarioCompletion(context: CompletionContext): CompletionResult | null {
  const { state, pos } = context
  let node: SyntaxNode | null = syntaxTree(state).resolveInner(pos, -1)
  const word = context.matchBefore(/"?[\w/]*/)
  const from = word?.from ?? pos

  // значение свойства: "kind": "Se|"  или  "condition": "|"
  if (node.name === 'String' && node.parent?.name === 'Property' && node !== node.parent.firstChild) {
    const name = propertyName(state, node.parent)
    const options =
      name === 'kind' ? Object.entries(stepKinds).map(([value, x]) => ({ value, info: x.info }))
      : name === 'condition' ? Object.entries(conditions).map(([value, info]) => ({ value, info }))
      : []
    return options.length === 0
      ? null
      : { from: node.from, to: node.to, options: options.map((x) => ({ label: JSON.stringify(x.value), info: x.info, type: 'enum' })) }
  }

  // имя свойства или пустое место внутри объекта шага
  while (node && !['Object', 'Array', 'JsonText'].includes(node.name)) {
    node = node.parent
  }
  if (!node) return null

  if (node.name === 'Object') {
    const kind = kindOf(state, node)
    const used = new Set(node.getChildren('Property').map((x) => propertyName(state, x)))
    const fields = kind && stepKinds[kind] ? { kind: 'Step type', ...stepKinds[kind].fields } : { kind: 'Step type' }
    const options: Completion[] = Object.entries(fields)
      .filter(([name]) => !used.has(name))
      .map(([name, info]) => ({ label: `"${name}"`, apply: `"${name}": `, info, type: 'property' }))
    return { from, options }
  }

  // между шагами: целые шаги-шаблоны
  if (!context.explicit && !word?.text) return null
  return {
    from,
    options: Object.entries(stepKinds).map(([label, x]) =>
      snippetCompletion(x.snippet, { label, detail: 'step', info: x.info, type: 'class' }),
    ),
  }
}

// от корня по пути из zod: [2, "text"] -> третий элемент массива, его свойство text
function findNode(state: EditorState, path: IssuePath): SyntaxNode | null {
  let node = syntaxTree(state).topNode.firstChild
  for (const key of path) {
    if (!node) return null
    if (typeof key === 'number' && node.name === 'Array') {
      const items: SyntaxNode[] = []
      for (let child = node.firstChild; child; child = child.nextSibling) {
        if (VALUE_NODES.has(child.name)) items.push(child)
      }
      node = items[key] ?? node
      if (!items[key]) return node
    } else if (typeof key === 'string' && node.name === 'Object') {
      const property = node.getChildren('Property').find((x) => propertyName(state, x) === key)
      if (!property) return node
      node = property.lastChild ?? property
    } else {
      return node
    }
  }
  return node
}

export function scenarioLinter(getAttachments: () => Attachments) {
  return (view: { state: EditorState }): Diagnostic[] => {
    const { state } = view
    const diagnostics: Diagnostic[] = []

    syntaxTree(state).iterate({
      enter: (node) => {
        if (node.type.isError) {
          diagnostics.push({ from: node.from, to: Math.max(node.to, node.from + 1), severity: 'error', message: 'Invalid JSON' })
        }
      },
    })
    if (diagnostics.length > 0) return diagnostics

    const { issues } = parseScenario(state.doc.toString(), getAttachments())
    for (const issue of issues) {
      const node = findNode(state, issue.path)
      const from = node?.from ?? 0
      const to = node?.to ?? Math.min(state.doc.length, 1)
      diagnostics.push({ from, to: Math.min(to, from + 400), severity: 'error', message: issue.message })
    }

    return diagnostics
  }
}
