export function Loading({ text = 'Loading…' }: { text?: string }) {
  return (
    <div className="muted" style={{ padding: 'var(--s-6)', textAlign: 'center' }} aria-busy="true">
      {text}
    </div>
  )
}
