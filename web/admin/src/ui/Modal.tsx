import { useEffect, useRef, type ReactNode } from 'react'
import styles from './Modal.module.css'

interface Props {
  title: ReactNode
  open: boolean
  onClose: () => void
  wide?: boolean
  children: ReactNode
}

// нативный <dialog>: фокус, Esc и затемнение фона браузер делает сам
export function Modal({ title, open, onClose, wide, children }: Props) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog ref={ref} className={wide ? `${styles.dialog} ${styles.wide}` : styles.dialog} onClose={onClose}>
      {open && (
        <>
          <header className={styles.header}>
            <h2 className={styles.title}>{title}</h2>
            <button type="button" className={styles.close} onClick={onClose} aria-label="Close">
              ×
            </button>
          </header>
          <div className={styles.body}>{children}</div>
        </>
      )}
    </dialog>
  )
}
