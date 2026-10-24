import type { ButtonHTMLAttributes } from 'react'
import styles from './Button.module.css'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'ghost' | 'quiet' | 'danger'
  size?: 'md' | 'sm'
  busy?: boolean
}

export function Button({ variant = 'primary', size = 'md', busy, className, disabled, type = 'button', ...rest }: Props) {
  const classes = [styles.button, styles[variant], size === 'sm' && styles.sm, busy && styles.busy, className]
    .filter(Boolean)
    .join(' ')

  return <button type={type} className={classes} disabled={disabled || busy} {...rest} />
}
