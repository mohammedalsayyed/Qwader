import { api } from '@/api/client';

export interface PushTopics {
  transfers: boolean;
  stock: boolean;
  sales: boolean;
}

interface PushStatus {
  publicKey: string;
  /** Null when this device is not subscribed for the signed-in user. */
  topics: PushTopics | null;
}

export type PushSupport = 'supported' | 'needs-install' | 'unsupported';

const isIos = () => /iphone|ipad|ipod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
const isStandalone = () =>
  window.matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true;

/** iPhone only allows notifications for apps added to the home screen (iOS 16.4+). */
export function pushSupport(): PushSupport {
  if (isIos() && !isStandalone()) return 'needs-install';
  return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window ? 'supported' : 'unsupported';
}

async function currentSubscription() {
  const registration = await navigator.serviceWorker.getRegistration();
  return (await registration?.pushManager.getSubscription()) ?? null;
}

function keyBytes(base64Url: string) {
  const base64 = (base64Url + '='.repeat((4 - (base64Url.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/');
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

const sameBytes = (a: Uint8Array, b: Uint8Array) => a.length === b.length && a.every((v, i) => v === b[i]);

const deviceName = () => {
  const ua = navigator.userAgent;
  const os = /iphone|ipad/i.test(ua) ? 'iPhone' : /android/i.test(ua) ? 'Android' : /windows/i.test(ua) ? 'Windows' : /mac/i.test(ua) ? 'Mac' : 'جهاز';
  return `${os}${isStandalone() ? ' (التطبيق)' : ''}`;
};

export const pushApi = {
  async status(): Promise<PushStatus> {
    const sub = pushSupport() === 'supported' ? await currentSubscription() : null;
    return api<PushStatus>(`/push${sub ? `?endpoint=${encodeURIComponent(sub.endpoint)}` : ''}`);
  },

  /** Asks permission (must follow a tap), subscribes this device and saves its choices on the server. */
  async enable(publicKey: string, topics: PushTopics) {
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') throw new Error(permission === 'denied' ? 'denied' : 'dismissed');
    const registration = await navigator.serviceWorker.ready;
    const key = keyBytes(publicKey);
    let subscription = await registration.pushManager.getSubscription();
    // A subscription made with another server key would be rejected by the push service: replace it.
    const existingKey = subscription?.options.applicationServerKey;
    if (subscription && (!existingKey || !sameBytes(new Uint8Array(existingKey), key))) {
      await subscription.unsubscribe();
      subscription = null;
    }
    subscription ??= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
    const json = subscription.toJSON();
    await api('/push/subscription', {
      method: 'PUT',
      body: { endpoint: subscription.endpoint, p256dh: json.keys?.p256dh, auth: json.keys?.auth, topics, device: deviceName() },
    });
  },

  async disable() {
    const sub = await currentSubscription();
    if (!sub) return;
    await api(`/push/subscription?endpoint=${encodeURIComponent(sub.endpoint)}`, { method: 'DELETE' });
    await sub.unsubscribe();
  },

  test: () => api('/push/test', { method: 'POST' }),
};

/** On sign-out the device stops receiving the previous user's notifications. Best effort: never blocks the logout. */
export async function forgetDeviceOnLogout() {
  try {
    if (pushSupport() === 'supported') await pushApi.disable();
  } catch {
    // offline or already gone
  }
}
