const taipeiCalendar = new Intl.DateTimeFormat('en', {
  timeZone: 'Asia/Taipei', calendar: 'gregory', numberingSystem: 'latn',
  year: 'numeric', month: '2-digit', day: '2-digit',
})

// 日曆日只用字串比較，不把手機所在的時區帶進家族日期。
export function taipeiToday(now = new Date()): string {
  const parts = taipeiCalendar.formatToParts(now)
  const part = (key: 'year' | 'month' | 'day') => parts.find(({ type }) => type === key)?.value
  return `${part('year')}-${part('month')}-${part('day')}`
}

export function addCivilDays(date: string, days: number): string {
  const utc = new Date(`${date}T00:00:00Z`)
  utc.setUTCDate(utc.getUTCDate() + days)
  return utc.toISOString().slice(0, 10)
}
