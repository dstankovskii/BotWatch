import { forwardRef, useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes } from 'react'
import styles from './Field.module.css'

interface FieldProps {
  label: string
  hint?: ReactNode
  error?: string
  children: (id: string, invalid: boolean) => ReactNode
}

export function Field({ label, hint, error, children }: FieldProps) {
  const id = useId()
  return (
    <div className={styles.field}>
      <label htmlFor={id} className={styles.label}>
        {label}
      </label>
      {children(id, Boolean(error))}
      {error ? <span className={styles.error}>{error}</span> : hint && <span className={styles.hint}>{hint}</span>}
    </div>
  )
}

type InputProps = InputHTMLAttributes<HTMLInputElement> & { label: string; hint?: ReactNode; error?: string }

// forwardRef нужен react-hook-form: register() отдаёт ref
export const Input = forwardRef<HTMLInputElement, InputProps>(({ label, hint, error, ...rest }, ref) => (
  <Field label={label} hint={hint} error={error}>
    {(id, invalid) => <input ref={ref} id={id} className={styles.control} aria-invalid={invalid} {...rest} />}
  </Field>
))

type SelectProps = SelectHTMLAttributes<HTMLSelectElement> & { label: string; hint?: ReactNode; error?: string }

export const Select = forwardRef<HTMLSelectElement, SelectProps>(({ label, hint, error, children, ...rest }, ref) => (
  <Field label={label} hint={hint} error={error}>
    {(id, invalid) => (
      <select ref={ref} id={id} className={styles.control} aria-invalid={invalid} {...rest}>
        {children}
      </select>
    )}
  </Field>
))

type CheckboxProps = InputHTMLAttributes<HTMLInputElement> & { label: ReactNode }

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(({ label, ...rest }, ref) => (
  <label className={styles.check}>
    <input ref={ref} type="checkbox" {...rest} />
    {label}
  </label>
))
