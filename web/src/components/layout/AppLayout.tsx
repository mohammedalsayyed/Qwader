import { Suspense, useState } from 'react';
import { Spinner } from '@/components/ui/Feedback';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { BellRing, Download, KeyRound, LogOut, Menu, UserCircle2 } from 'lucide-react';
import { IosInstallSheet, OfflineBanner, useInstall } from '@/app/pwa';
import { Credit } from './Credit';
import { useAuth } from '@/auth/AuthProvider';
import { cn } from '@/lib/cn';
import { Sheet } from '@/components/ui/Sheet';
import { BOTTOM_TABS, NAV_GROUP_LABELS, NAV_ITEMS, type NavItem } from './navigation';
import { GlobalSearchButton, NotificationsButton } from './TopBarTools';

const STORE_NAME = 'معرض قويدر';

function useVisibleNav() {
  const { can } = useAuth();
  const visible = (anyOf: NavItem['anyOf']) => anyOf.length === 0 || can(...anyOf);
  return {
    items: NAV_ITEMS.filter((i) => visible(i.anyOf)),
    tabs: BOTTOM_TABS.filter((t) => visible(t.anyOf)),
  };
}

function groupItems(items: NavItem[]) {
  return (Object.keys(NAV_GROUP_LABELS) as NavItem['group'][])
    .map((g) => ({ group: g, label: NAV_GROUP_LABELS[g], items: items.filter((i) => i.group === g) }))
    .filter((g) => g.items.length > 0);
}

export function AppLayout() {
  const [moreOpen, setMoreOpen] = useState(false);
  const { items, tabs } = useVisibleNav();
  const location = useLocation();
  const groups = groupItems(items);

  const tabPaths = new Set(tabs.map((t) => t.to));
  const moreActive = !tabPaths.has(location.pathname) && !tabs.some((t) => t.to !== '/' && location.pathname.startsWith(t.to));

  return (
    <div className="min-h-dvh lg:flex">
      {/* Desktop: fixed sidebar */}
      <aside className="sticky top-0 hidden h-dvh w-64 shrink-0 flex-col border-e border-line bg-surface lg:flex print:hidden">
        <Link to="/" className="flex h-16 items-center gap-3 border-b border-line px-5">
          <img src="/brand/mark.png" alt="" className="h-9 w-auto" width={400} height={200} />
          <span className="leading-tight">
            <span className="block font-bold">{STORE_NAME}</span>
            <span className="block text-xs text-muted">للأدوات والأجهزة المنزلية</span>
          </span>
        </Link>
        <nav className="flex-1 overflow-y-auto px-3 py-3" aria-label="القائمة الرئيسية">
          {groups.map((g) => (
            <div key={g.group} className="mb-4">
              <p className="px-3 pb-1 text-xs font-semibold text-muted">{g.label}</p>
              {g.items.map((item) => (
                <SideLink key={item.to} item={item} />
              ))}
            </div>
          ))}
        </nav>
        <Credit className="border-t border-line px-3 py-3 text-muted" />
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <OfflineBanner />
        <TopBar />
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 pt-4 pb-page lg:px-8 lg:pb-10 print:p-0">
          <Suspense fallback={<Spinner />}>
            <Outlet />
          </Suspense>
        </main>
      </div>

      {/* Phone/tablet: bottom tab bar */}
      <nav
        className="pb-safe fixed inset-x-0 bottom-0 z-40 border-t border-line bg-surface/95 backdrop-blur lg:hidden print:hidden"
        aria-label="التنقل السريع"
      >
        <div className="mx-auto grid max-w-lg" style={{ gridTemplateColumns: `repeat(${tabs.length + 1}, minmax(0, 1fr))` }}>
          {tabs.map((t) => (
            <NavLink
              key={t.to}
              to={t.to}
              end={t.to === '/'}
              className={({ isActive }) =>
                cn('flex h-16 flex-col items-center justify-center gap-1 text-xs font-medium', isActive ? 'text-brand-700' : 'text-muted')
              }
            >
              <t.icon className="size-6" aria-hidden />
              {t.label}
            </NavLink>
          ))}
          <button
            type="button"
            onClick={() => setMoreOpen(true)}
            className={cn('flex h-16 flex-col items-center justify-center gap-1 text-xs font-medium', moreActive ? 'text-brand-700' : 'text-muted')}
          >
            <Menu className="size-6" aria-hidden />
            المزيد
          </button>
        </div>
      </nav>

      <Sheet open={moreOpen} onClose={() => setMoreOpen(false)} title="كل الأقسام" size="lg">
        {groups.map((g) => (
          <section key={g.group} className="mb-4">
            <h3 className="mb-2 text-sm font-semibold text-muted">{g.label}</h3>
            <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
              {g.items.map((item) => (
                <Link
                  key={item.to}
                  to={item.to}
                  onClick={() => setMoreOpen(false)}
                  className="flex min-h-22 flex-col items-center justify-center gap-2 rounded-2xl border border-line p-2 text-center text-[13px] font-medium leading-tight hover:bg-canvas"
                >
                  <item.icon className="size-6 text-brand-700" aria-hidden />
                  {item.label}
                </Link>
              ))}
            </div>
          </section>
        ))}
        <Credit className="mt-2 border-t border-line pt-4 text-muted" />
      </Sheet>
    </div>
  );
}

function SideLink({ item }: { item: NavItem }) {
  return (
    <NavLink
      to={item.to}
      end={item.to === '/'}
      className={({ isActive }) =>
        cn(
          'flex h-11 items-center gap-3 rounded-xl px-3 text-[15px] font-medium transition',
          isActive ? 'bg-brand-50 text-brand-800' : 'text-ink hover:bg-canvas',
        )
      }
    >
      <item.icon className="size-5 shrink-0" aria-hidden />
      {item.label}
    </NavLink>
  );
}

function TopBar() {
  const { user, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const [iosHelp, setIosHelp] = useState(false);
  const install = useInstall();
  const navigate = useNavigate();

  return (
    <header className="sticky top-0 z-30 flex h-[calc(4rem+env(safe-area-inset-top))] items-center pt-[env(safe-area-inset-top)] justify-between gap-2 border-b border-line bg-surface/95 px-4 backdrop-blur lg:px-8 print:hidden">
      <Link to="/" className="flex min-w-0 items-center gap-2 lg:hidden">
        <img src="/brand/mark.png" alt="" className="h-9 w-auto" width={400} height={200} />
        <span className="font-bold">{STORE_NAME}</span>
      </Link>

      <div className="hidden lg:block"><GlobalSearchButton /></div>
      <div className="flex items-center gap-1">
      <div className="lg:hidden"><GlobalSearchButton /></div>
      <NotificationsButton />
      <button
        type="button"
        onClick={() => setMenuOpen(true)}
        className="flex h-11 items-center gap-2 rounded-xl px-2 hover:bg-black/5"
        aria-label="الحساب الشخصي"
      >
        <UserCircle2 className="size-7 text-brand-700" aria-hidden />
        <span className="hidden max-w-40 truncate text-sm font-medium sm:block">{user?.fullName}</span>
      </button>
      </div>

      <Sheet open={menuOpen} onClose={() => setMenuOpen(false)} title="الحساب الشخصي">
        <div className="mb-4 rounded-2xl bg-canvas p-4">
          <p className="font-bold">{user?.fullName}</p>
          <p className="ltr text-sm text-muted">{user?.userName}</p>
        </div>
        <button
          type="button"
          className="flex h-12 w-full items-center gap-3 rounded-xl px-3 text-start hover:bg-canvas"
          onClick={() => {
            setMenuOpen(false);
            navigate('/account/change-password');
          }}
        >
          <KeyRound className="size-5 text-muted" /> تغيير كلمة المرور
        </button>
        <button
          type="button"
          className="flex h-12 w-full items-center gap-3 rounded-xl px-3 text-start hover:bg-canvas"
          onClick={() => {
            setMenuOpen(false);
            navigate('/account/notifications');
          }}
        >
          <BellRing className="size-5 text-muted" /> الإشعارات على هذا الجهاز
        </button>
        {install.available && (
          <button
            type="button"
            className="flex h-12 w-full items-center gap-3 rounded-xl px-3 text-start hover:bg-canvas"
            onClick={() => {
              setMenuOpen(false);
              if (install.needsIosInstructions) setIosHelp(true);
              else void install.prompt();
            }}
          >
            <Download className="size-5 text-muted" /> تثبيت التطبيق على هذا الجهاز
          </button>
        )}
        <button
          type="button"
          className="flex h-12 w-full items-center gap-3 rounded-xl px-3 text-start text-danger hover:bg-red-50"
          onClick={() => void logout()}
        >
          <LogOut className="size-5" /> تسجيل الخروج
        </button>
      </Sheet>
      {iosHelp && <IosInstallSheet onClose={() => setIosHelp(false)} />}
    </header>
  );
}
