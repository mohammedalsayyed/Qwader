import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { refreshSession, session, type CurrentUser } from '@/api/client';
import { authApi } from '@/api/endpoints';
import type { Permission } from './permissions';
import { forgetDeviceOnLogout } from '@/lib/push';

type Status = 'loading' | 'authenticated' | 'anonymous' | 'offline';

interface AuthContextValue {
  status: Status;
  user: CurrentUser | null;
  login: (userName: string, password: string) => Promise<CurrentUser>;
  logout: () => Promise<void>;
  /** Re-reads the session (e.g. after the password was changed, so the restricted token is replaced). */
  reload: () => Promise<void>;
  can: (...anyOf: Permission[]) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<CurrentUser | null>(null);
  const queryClient = useQueryClient();

  useEffect(
    () =>
      session.subscribe((s) => {
        setUser(s?.user ?? null);
        setStatus(s ? 'authenticated' : 'anonymous');
      }),
    [],
  );

  const reload = useCallback(async () => {
    try {
      await refreshSession(); // updates state through the session listener
    } catch {
      setStatus('offline');
    }
  }, []);

  // Restore the session from the httpOnly refresh cookie on page load.
  useEffect(() => {
    void reload();
  }, [reload]);

  // Opened without internet (installed app, poor signal): retry by itself as soon as the connection returns.
  useEffect(() => {
    if (status !== 'offline') return;
    const retry = () => void reload();
    window.addEventListener('online', retry);
    const timer = setInterval(() => navigator.onLine && retry(), 15_000);
    return () => {
      window.removeEventListener('online', retry);
      clearInterval(timer);
    };
  }, [status, reload]);

  const login = useCallback(async (userName: string, password: string) => {
    const res = await authApi.login(userName, password);
    session.set(res);
    return res.user;
  }, []);

  const logout = useCallback(async () => {
    try {
      await forgetDeviceOnLogout();
      await authApi.logout();
    } finally {
      session.set(null);
      queryClient.clear();
    }
  }, [queryClient]);

  const can = useCallback(
    (...anyOf: Permission[]) => !!user && anyOf.some((p) => user.permissions.includes(p)),
    [user],
  );

  const value = useMemo(() => ({ status, user, login, logout, reload, can }), [status, user, login, logout, reload, can]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
