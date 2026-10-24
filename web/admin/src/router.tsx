import { createBrowserRouter } from 'react-router'
import { Layout } from './layout/Layout'
import { RequireAuth } from './layout/RequireAuth'
import { AccountsPage } from './pages/AccountsPage'
import { ChannelsPage } from './pages/ChannelsPage'
import { IncidentsPage } from './pages/IncidentsPage'
import { LoginPage, SetupPage } from './pages/AuthPages'
import { MonitorPage } from './pages/MonitorPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { OverviewPage } from './pages/OverviewPage'
import { SettingsPage } from './pages/SettingsPage'
import { StatusPage } from './pages/StatusPage'

export const router = createBrowserRouter([
  { path: '/status', element: <StatusPage /> },
  { path: '/login', element: <LoginPage /> },
  { path: '/setup', element: <SetupPage /> },
  {
    element: (
      <RequireAuth>
        <Layout />
      </RequireAuth>
    ),
    children: [
      { index: true, element: <OverviewPage /> },
      { path: 'monitors/:id', element: <MonitorPage /> },
      { path: 'incidents', element: <IncidentsPage /> },
      { path: 'accounts', element: <AccountsPage /> },
      { path: 'channels', element: <ChannelsPage /> },
      { path: 'settings', element: <SettingsPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
