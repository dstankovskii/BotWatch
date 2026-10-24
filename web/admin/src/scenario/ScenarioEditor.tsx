import { useEffect, useRef } from 'react'
import { basicSetup, EditorView } from 'codemirror'
import { EditorState } from '@codemirror/state'
import { json, jsonLanguage } from '@codemirror/lang-json'
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { forceLinting, linter } from '@codemirror/lint'
import { tags } from '@lezer/highlight'
import { scenarioCompletion, scenarioLinter } from './editorSupport'
import type { Attachments } from './scenario'
import styles from './ScenarioEditor.module.css'

interface Props {
  value: string
  attachments: Attachments
  onChange: (value: string) => void
}

// цвета берём из токенов, поэтому редактор сам переключается со светлой темы на тёмную
const theme = EditorView.theme({
  '&': {
    backgroundColor: 'var(--field)',
    color: 'var(--text)',
    fontSize: 'var(--fs-sm)',
  },
  '.cm-content': { fontFamily: 'var(--mono)', caretColor: 'var(--text)' },
  '.cm-gutters': { backgroundColor: 'var(--surface)', color: 'var(--muted)', border: 'none' },
  '.cm-activeLine': { backgroundColor: 'var(--grey-soft)' },
  '.cm-activeLineGutter': { backgroundColor: 'var(--grey-soft)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': { backgroundColor: 'var(--accent-soft)' },
  '.cm-tooltip': { backgroundColor: 'var(--card)', border: '1px solid var(--hairline-strong)', color: 'var(--text)' },
  '.cm-tooltip-autocomplete ul li[aria-selected]': { backgroundColor: 'var(--accent-soft)', color: 'var(--text)' },
})

// стандартная подсветка рассчитана на светлый фон, в тёмной теме тёмно-красные строки почти не видно
const highlight = HighlightStyle.define([
  { tag: tags.propertyName, color: 'var(--text)' },
  { tag: tags.string, color: 'var(--accent)' },
  { tag: [tags.number, tags.bool, tags.null], color: 'var(--amber)' },
  { tag: [tags.brace, tags.squareBracket, tags.separator], color: 'var(--muted)' },
])

export function ScenarioEditor({ value, attachments, onChange }: Props) {
  const host = useRef<HTMLDivElement>(null)
  const view = useRef<EditorView | null>(null)
  const onChangeRef = useRef(onChange)
  const attachmentsRef = useRef(attachments)

  useEffect(() => {
    onChangeRef.current = onChange
  }, [onChange])

  // приложили файл - ошибка "not attached" должна пропасть сразу, а не после следующего нажатия
  useEffect(() => {
    attachmentsRef.current = attachments
    if (view.current) forceLinting(view.current)
  }, [attachments])

  // редактор создаём один раз, дальше он сам хранит текст и только сообщает об изменениях
  useEffect(() => {
    const editor = new EditorView({
      parent: host.current!,
      state: EditorState.create({
        doc: value,
        extensions: [
          basicSetup,
          json(),
          jsonLanguage.data.of({ autocomplete: scenarioCompletion }),
          linter(scenarioLinter(() => attachmentsRef.current), { delay: 300 }),
          theme,
          syntaxHighlighting(highlight),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) onChangeRef.current(update.state.doc.toString())
          }),
        ],
      }),
    })
    view.current = editor
    return () => editor.destroy()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return <div ref={host} className={styles.editor} />
}
