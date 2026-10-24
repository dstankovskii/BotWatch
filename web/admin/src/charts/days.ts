export interface DayUptime {
  day: string
  uptime: number | null
}

// дни без данных тоже рисуем, иначе полоска молодого монитора была бы короче остальных
export function lastDays(count: number, byDay: Map<string, number | null>): DayUptime[] {
  const today = new Date()
  return Array.from({ length: count }, (_, i) => {
    const date = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() - (count - 1 - i)))
    const day = date.toISOString().slice(0, 10)
    return { day, uptime: byDay.get(day) ?? null }
  })
}
