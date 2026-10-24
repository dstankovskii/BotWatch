import type { components as Gateway } from './gateway'
import type { components as Incidents } from './incidents'
import type { components as Monitoring } from './monitoring'

type M = Monitoring['schemas']
type I = Incidents['schemas']
type G = Gateway['schemas']

export type Monitor = M['MonitorDto']
export type MonitorKind = M['MonitorKind']
export type Check = M['CheckDto']
export type HttpCheckRequest = M['HttpCheckRequest']
export type TelegramCheckRequest = M['TelegramCheckRequest']
export type ScenarioStep = M['ScenarioStep']
export type StepDto = M['StepDto']
export type StepKind = M['StepKind']
export type ReplyCondition = M['ReplyCondition']
export type Account = M['AccountDto']
export type AccountStatus = M['AccountStatus']

export type CheckState = I['CheckStateDto']
export type CheckStatus = I['CheckStatus']
export type CheckResult = I['CheckResultDto']
export type Incident = I['IncidentDto']
export type UptimeDay = I['UptimeDayDto']
export type Channel = I['ChannelDto']

export type AuthState = G['AuthStateDto']
export type User = G['UserDto']
export type StatusPage = G['StatusPageDto']
export type StatusMonitor = G['StatusMonitorDto']
export type StatusLevel = G['StatusLevel']
