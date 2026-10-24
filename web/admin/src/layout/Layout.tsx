import { NavLink, Outlet, useNavigate } from 'react-router'
import { useAuthMutation, useAuthState, useIncidents } from '../api/queries'
import { useTheme, type ThemeChoice } from '../theme'
import { Button } from '../ui/Button'
import { Seg } from '../ui/Seg'
import styles from './Layout.module.css'

const themes: { value: ThemeChoice; label: string }[] = [
  { value: 'system', label: 'Auto' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]

function linkClass({ isActive }: { isActive: boolean }) {
  return isActive ? `${styles.link} ${styles.active}` : styles.link
}

export function Layout() {
  const { data: auth } = useAuthState()
  const { data: openIncidents } = useIncidents(true)
  const logout = useAuthMutation('logout')
  const navigate = useNavigate()
  const [theme, setTheme] = useTheme()

  return (
    <div className={styles.shell}>
      <aside className={styles.side}>
        <NavLink to="/" className={styles.brand}>
          <img src="/favicon.svg" alt="" />
          BotWatch
        </NavLink>

        <nav className={styles.nav}>
          <NavLink to="/" end className={linkClass}>
            Overview
          </NavLink>
          <NavLink to="/incidents" className={linkClass}>
            Incidents
            {!!openIncidents?.length && <span className={styles.count}>{openIncidents.length}</span>}
          </NavLink>
          <NavLink to="/accounts" className={linkClass}>
            Telegram accounts
          </NavLink>
          <NavLink to="/channels" className={linkClass}>
            Alert channels
          </NavLink>
          <NavLink to="/settings" className={linkClass}>
            Settings
          </NavLink>
          <a href="/status" target="_blank" rel="noreferrer" className={styles.link}>
            Status page ↗
          </a>
        </nav>

        <div className={styles.footer}>
          <Seg label="Theme" value={theme} options={themes} onChange={setTheme} />
          <div className={styles.user}>
            <span>{auth?.userName}</span>
            <span className="spacer" />
            <Button
              variant="quiet"
              size="sm"
              busy={logout.isPending}
              onClick={() => logout.mutate(undefined, { onSuccess: () => navigate('/login') })}
            >
              Sign out
            </Button>
          </div>
        </div>
      </aside>

      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  )
}
