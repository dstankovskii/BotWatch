import { CartesianGrid, ComposedChart, Line, ResponsiveContainer, Scatter, Tooltip, XAxis, YAxis } from 'recharts'
import type { CheckResult } from '../api/types'

interface Point {
  time: number
  ms: number
  failed: number | null
}

function toPoints(results: CheckResult[]): Point[] {
  return results
    .map((x) => ({
      time: new Date(x.finishedAt).getTime(),
      ms: x.durationMs,
      failed: x.success ? null : x.durationMs,
    }))
    .sort((a, b) => a.time - b.time)
}

const timeFormat = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit' })
const fullFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' })

// время ответа по последним прогонам, неуспешные - красными точками поверх линии
export function ResponseChart({ results }: { results: CheckResult[] }) {
  const points = toPoints(results)

  if (points.length < 2) {
    return <p className="muted">Not enough runs yet to draw a chart.</p>
  }

  return (
    <ResponsiveContainer width="100%" height={220}>
      <ComposedChart data={points} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
        <CartesianGrid stroke="var(--hairline)" vertical={false} />
        <XAxis
          dataKey="time"
          type="number"
          domain={['dataMin', 'dataMax']}
          tickFormatter={(x: number) => timeFormat.format(x)}
          stroke="var(--muted)"
          fontSize={11}
          tickLine={false}
        />
        <YAxis unit=" ms" stroke="var(--muted)" fontSize={11} tickLine={false} axisLine={false} width={64} />
        <Tooltip
          contentStyle={{ background: 'var(--card)', border: '1px solid var(--hairline-strong)', borderRadius: 8 }}
          labelFormatter={(x) => fullFormat.format(Number(x))}
          formatter={(value, name) => [`${value} ms`, name === 'failed' ? 'Failed run' : 'Response time']}
        />
        <Line dataKey="ms" stroke="var(--accent)" strokeWidth={1.5} dot={false} isAnimationActive={false} />
        <Scatter dataKey="failed" fill="var(--red)" isAnimationActive={false} />
      </ComposedChart>
    </ResponsiveContainer>
  )
}

