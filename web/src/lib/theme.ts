import { useEffect, useState } from 'react';

/** «الوضع الليلي / النهاري»: saved on this device only; applied before the app draws by /theme-init.js. */
export type Theme = 'light' | 'dark';
const KEY = 'qwader-theme';

export function currentTheme(): Theme {
  return document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light';
}

export function applyTheme(theme: Theme) {
  const root = document.documentElement;
  if (theme === 'dark') root.dataset.theme = 'dark';
  else delete root.dataset.theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#0e1524' : '#073060');
  try { localStorage.setItem(KEY, theme); } catch { /* private mode: for this visit only */ }
  window.dispatchEvent(new CustomEvent('qwader-theme', { detail: theme }));
}

export function useTheme(): [Theme, (theme: Theme) => void] {
  const [theme, setTheme] = useState<Theme>(currentTheme);
  useEffect(() => {
    const on = (e: Event) => setTheme((e as CustomEvent<Theme>).detail);
    window.addEventListener('qwader-theme', on);
    return () => window.removeEventListener('qwader-theme', on);
  }, []);
  return [theme, applyTheme];
}
