import { useEffect, useState, useSyncExternalStore } from 'react';
import { useRegisterSW } from 'virtual:pwa-register/react';
import { Download, RefreshCw, Share, SquarePlus, WifiOff, X } from 'lucide-react';
import { Button } from '@/components/ui/Button';
import { Sheet } from '@/components/ui/Sheet';

// ---------- Install prompt ----------

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

let deferredPrompt: BeforeInstallPromptEvent | null = null;
const installListeners = new Set<() => void>();
const notifyInstall = () => installListeners.forEach((l) => l());

/** Called once before React mounts: Chrome/Edge fire this event early, sometimes before the first render. */
export function captureInstallPrompt() {
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e as BeforeInstallPromptEvent;
    notifyInstall();
  });
  window.addEventListener('appinstalled', () => {
    deferredPrompt = null;
    notifyInstall();
  });
}

export const isStandalone = () =>
  window.matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true;

/** iPhone/iPad Safari has no install prompt; users add the app from the Share menu. */
const isIos = () => /iphone|ipad|ipod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

export function useInstall() {
  const canPrompt = useSyncExternalStore(
    (cb) => {
      installListeners.add(cb);
      return () => installListeners.delete(cb);
    },
    () => deferredPrompt !== null,
  );
  const installed = isStandalone();
  return {
    /** True when an install action is available on this device/browser. */
    available: !installed && (canPrompt || isIos()),
    needsIosInstructions: !installed && !canPrompt && isIos(),
    async prompt() {
      if (!deferredPrompt) return false;
      await deferredPrompt.prompt();
      const { outcome } = await deferredPrompt.userChoice;
      deferredPrompt = null;
      notifyInstall();
      return outcome === 'accepted';
    },
  };
}

export function IosInstallSheet({ onClose }: { onClose: () => void }) {
  return (
    <Sheet open onClose={onClose} title="تثبيت التطبيق على الآيفون">
      <ol className="space-y-4 text-[15px] leading-7">
        <li className="flex items-start gap-3">
          <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-brand-50 font-bold text-brand-700">1</span>
          <span>افتح الموقع في متصفح <b>Safari</b>، ثم اضغط زر المشاركة <Share className="inline size-5 text-brand-700" /> أسفل الشاشة.</span>
        </li>
        <li className="flex items-start gap-3">
          <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-brand-50 font-bold text-brand-700">2</span>
          <span>اختر <b>«إضافة إلى الشاشة الرئيسية»</b> <SquarePlus className="inline size-5 text-brand-700" />.</span>
        </li>
        <li className="flex items-start gap-3">
          <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-brand-50 font-bold text-brand-700">3</span>
          <span>اضغط <b>«إضافة»</b>، وسيظهر تطبيق «معرض قويدر» بين تطبيقاتك.</span>
        </li>
      </ol>
    </Sheet>
  );
}

const DISMISS_KEY = 'qwader.installBanner.dismissedAt';

/** Dismissible invitation on the dashboard; hidden for two weeks after «لاحقًا». */
export function InstallBanner() {
  const install = useInstall();
  const [iosOpen, setIosOpen] = useState(false);
  const [dismissed, setDismissed] = useState(() => {
    try {
      const at = Number(localStorage.getItem(DISMISS_KEY));
      return at > 0 && Date.now() - at < 14 * 86_400_000;
    } catch {
      return false;
    }
  });

  if (!install.available || dismissed) return null;

  const dismiss = () => {
    setDismissed(true);
    try {
      localStorage.setItem(DISMISS_KEY, String(Date.now()));
    } catch {
      /* private mode: banner just returns next visit */
    }
  };

  return (
    <div className="flex items-center gap-3 rounded-card border border-brand-200 bg-brand-50 p-4">
      <img src="/icons/icon-192.png" alt="" className="size-12 shrink-0 rounded-xl" />
      <div className="min-w-0 flex-1">
        <p className="font-bold">ثبّت التطبيق على جهازك</p>
        <p className="text-sm text-muted">يفتح من الشاشة الرئيسية بضغطة واحدة وبملء الشاشة.</p>
      </div>
      <div className="flex shrink-0 flex-col gap-1">
        <Button size="sm" onClick={() => (install.needsIosInstructions ? setIosOpen(true) : void install.prompt())}>
          <Download className="size-4" /> تثبيت
        </Button>
        <button type="button" onClick={dismiss} className="h-8 text-xs text-muted">لاحقًا</button>
      </div>
      {iosOpen && <IosInstallSheet onClose={() => setIosOpen(false)} />}
    </div>
  );
}

// ---------- Updates & connectivity ----------

/** Shows «تحديث» when a new version has been downloaded; checks for one every 30 minutes. */
export function UpdatePrompt() {
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      if (registration) setInterval(() => void registration.update(), 30 * 60_000);
    },
  });

  if (!needRefresh) return null;
  return (
    <div role="status" className="fixed inset-x-0 top-0 z-[70] flex justify-center px-4 pt-[calc(env(safe-area-inset-top)+0.5rem)]">
      <div className="flex w-full max-w-md items-center gap-3 rounded-2xl bg-ink px-4 py-3 text-white shadow-xl">
        <RefreshCw className="size-5 shrink-0" />
        <p className="flex-1 text-sm font-medium">يتوفر إصدار جديد من النظام.</p>
        <button type="button" onClick={() => void updateServiceWorker(true)} className="h-9 rounded-lg bg-white px-3 text-sm font-bold text-ink">
          تحديث
        </button>
        <button type="button" aria-label="لاحقًا" onClick={() => setNeedRefresh(false)} className="flex size-9 items-center justify-center rounded-lg">
          <X className="size-4" />
        </button>
      </div>
    </div>
  );
}

function useOnline() {
  const [online, setOnline] = useState(() => navigator.onLine);
  useEffect(() => {
    const on = () => setOnline(true);
    const off = () => setOnline(false);
    window.addEventListener('online', on);
    window.addEventListener('offline', off);
    return () => {
      window.removeEventListener('online', on);
      window.removeEventListener('offline', off);
    };
  }, []);
  return online;
}

/** Saving needs the server (stock and money must stay consistent), so offline is announced clearly. */
export function OfflineBanner() {
  const online = useOnline();
  if (online) return null;
  return (
    <div role="alert" className="flex items-center justify-center gap-2 bg-amber-500 px-4 py-2 text-sm font-semibold text-black print:hidden">
      <WifiOff className="size-4 shrink-0" />
      لا يوجد اتصال بالإنترنت — يمكنك التصفح، لكن لن تُحفظ أي عملية حتى يعود الاتصال.
    </div>
  );
}
