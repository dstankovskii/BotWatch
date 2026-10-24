// те же границы, что в Check на бэке: HTTP от 30 секунд, Telegram от 5 минут, всё до суток
export const httpIntervals = [30, 60, 300, 900, 1800, 3600]
export const telegramIntervals = [300, 900, 1800, 3600, 3 * 3600, 6 * 3600, 12 * 3600, 24 * 3600]

export function formatInterval(seconds: number) {
  if (seconds < 60) return `${seconds} s`
  if (seconds < 3600) return `${seconds / 60} min`
  const hours = seconds / 3600
  return hours === 1 ? '1 hour' : `${hours} hours`
}

// если интервал сохранён не из списка (например, через API), всё равно показываем его в селекте
export function withCurrent(options: number[], current: number | undefined) {
  return current && !options.includes(current) ? [...options, current].sort((a, b) => a - b) : options
}
