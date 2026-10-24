import { useEffect, useState } from 'react'

export type ThemeChoice = 'system' | 'light' | 'dark'

const KEY = 'botwatch.theme'

export function readTheme(): ThemeChoice {
  const saved = localStorage.getItem(KEY)
  return saved === 'light' || saved === 'dark' ? saved : 'system'
}

// system = атрибута нет, работает prefers-color-scheme из tokens.css
export function applyTheme(choice: ThemeChoice) {
  if (choice === 'system') {
    document.documentElement.removeAttribute('data-theme')
  } else {
    document.documentElement.setAttribute('data-theme', choice)
  }
}

export function useTheme() {
  const [theme, setTheme] = useState(readTheme)

  useEffect(() => {
    applyTheme(theme)
    if (theme === 'system') {
      localStorage.removeItem(KEY)
    } else {
      localStorage.setItem(KEY, theme)
    }
  }, [theme])

  return [theme, setTheme] as const
}
