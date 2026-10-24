import createClient, { type Middleware } from 'openapi-fetch'
import type { paths as GatewayPaths } from './gateway'
import type { paths as IncidentsPaths } from './incidents'
import type { paths as MonitoringPaths } from './monitoring'

const XSRF_COOKIE = 'XSRF-TOKEN'
const XSRF_HEADER = 'X-XSRF-TOKEN'

// ProblemDetails из ASP.NET: title/detail для общих ошибок, errors - для ошибок валидации по полям
export interface Problem {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: Problem

  constructor(status: number, problem: Problem) {
    super(problem.detail ?? problem.title ?? `Request failed with ${status}`)
    this.status = status
    this.problem = problem
  }
}

function readCookie(name: string) {
  const match = document.cookie.split('; ').find((x) => x.startsWith(name + '='))
  return match ? decodeURIComponent(match.slice(name.length + 1)) : undefined
}

export async function refreshAntiforgery() {
  await fetch('/api/auth/antiforgery', { credentials: 'same-origin' })
}

// 401 на любом запросе = сессия кончилась, App слушает и уводит на логин
export const UNAUTHORIZED_EVENT = 'botwatch:unauthorized'

const middleware: Middleware = {
  async onRequest({ request }) {
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      const token = readCookie(XSRF_COOKIE)
      if (token) {
        request.headers.set(XSRF_HEADER, token)
      }
    }
    return request
  },
  async onResponse({ response, request }) {
    if (response.status === 401 && !request.url.includes('/api/auth/')) {
      window.dispatchEvent(new Event(UNAUTHORIZED_EVENT))
    }
    return response
  },
}

function create<Paths extends object>() {
  const client = createClient<Paths>({ baseUrl: window.location.origin, credentials: 'same-origin' })
  client.use(middleware)
  return client
}

export const monitoringApi = create<MonitoringPaths>()
export const incidentsApi = create<IncidentsPaths>()
export const gatewayApi = create<GatewayPaths>()

// openapi-fetch не бросает исключений, а react-query ждёт именно их
export async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call
  if (!response.ok) {
    const problem = typeof error === 'object' && error !== null ? (error as Problem) : { title: String(error ?? '') }
    throw new ApiError(response.status, problem)
  }
  return data as T
}

export function errorText(error: unknown) {
  if (error instanceof ApiError) {
    const fields = Object.values(error.problem.errors ?? {}).flat()
    return fields.length > 0 ? fields.join(' ') : error.message
  }
  return error instanceof Error ? error.message : 'Something went wrong'
}
