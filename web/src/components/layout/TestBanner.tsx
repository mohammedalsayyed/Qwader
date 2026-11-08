import { FlaskConical } from 'lucide-react';

// The trial copy (Cloudflare worker, its backend site, a developer's machine). Chosen by the address the app is
// opened from, not by the build, so the customer's site can never show it even if this code is deployed there.
const TEST_HOSTS = ['workers.dev', 'runasp.net', 'localhost', '127.0.0.1'];

export const isTestCopy = () => TEST_HOSTS.some((h) => window.location.hostname === h || window.location.hostname.endsWith(`.${h}`));

/** A strip above everything on the trial copy, so nobody mistakes it for the real store. */
export function TestBanner() {
  if (!isTestCopy()) return null;
  return (
    <div role="note" className="flex items-center justify-center gap-2 bg-violet-700 px-4 py-1.5 text-center text-sm font-semibold text-white print:hidden">
      <FlaskConical className="size-4 shrink-0" aria-hidden />
      نسخة تجريبية — للتجربة فقط، والبيانات هنا ليست حقيقية
    </div>
  );
}
