// Selling without internet: the device keeps a copy of products, customers and accounts, and a queue of sales
// made while offline. The queue is sent, oldest first, as soon as the connection (and a valid session) returns.
// Each queued sale keeps its idempotency key, so a sale the server already saved is never saved twice.

import { useSyncExternalStore } from 'react';
import { ApiError, type CurrentUser } from '@/api/client';
import { lookupsApi, settingsApi, type Lookups, type Settings } from '@/api/endpoints';
import { salesApi, type CreateSaleInput, type OfflineCustomer, type OfflineData, type OfflineProduct } from '@/api/sales';
import { normalizeArabic } from '@/lib/arabic';
import { idb } from '@/lib/idb';

export interface OfflineSnapshot extends OfflineData {
  user: CurrentUser;
  lookups: Lookups;
  settings: Settings;
  /** When the whole copy (accounts and settings included) was last downloaded; the 5-second changes do not move it. */
  fullAt?: string;
}

export interface QueuedSale {
  /** The sale's idempotency key. */
  key: string;
  userId: number;
  /** Shown on the receipt until the server gives the real number. */
  number: string;
  input: CreateSaleInput;
  total: number;
  customerName: string;
  createdAt: string;
  /** Set when the server refused it: it stays here until retried or discarded. */
  error?: string;
}

interface State {
  loaded: boolean;
  snapshot: OfflineSnapshot | null;
  queue: QueuedSale[];
}

let state: State = { loaded: false, snapshot: null, queue: [] };
const listeners = new Set<() => void>();

function setState(patch: Partial<State>) {
  state = { ...state, ...patch };
  listeners.forEach((l) => l());
}

/** Resolves once the stored copy and queue are read from the device. */
export const offlineReady: Promise<State> = (async () => {
  try {
    const [snapshot, queue] = await Promise.all([idb.get<OfflineSnapshot>('snapshot'), idb.get<QueuedSale[]>('queue')]);
    setState({ loaded: true, snapshot: snapshot ?? null, queue: queue ?? [] });
  } catch {
    setState({ loaded: true }); // storage unavailable (private mode): offline selling is simply not offered
  }
  return state;
})();

export function useOfflineState() {
  return useSyncExternalStore(
    (l) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    () => state,
  );
}

async function saveSnapshot(snapshot: OfflineSnapshot | null) {
  setState({ snapshot });
  try {
    if (snapshot) await idb.set('snapshot', snapshot);
    else await idb.del('snapshot');
  } catch {
    /* kept in memory for this session */
  }
}

async function saveQueue(queue: QueuedSale[]) {
  setState({ queue });
  try {
    await idb.set('queue', queue);
  } catch {
    /* kept in memory for this session */
  }
}

/** Downloads a fresh copy for this user. Sales still queued are taken off the stock again, since the server has not seen them. */
export async function refreshOfflineData(user: CurrentUser) {
  const [data, lookups, settings] = await Promise.all([salesApi.offlineData(), lookupsApi.get(), settingsApi.get()]);
  await saveSnapshot(withQueuedSales({ ...data, user, lookups, settings, fullAt: data.generatedAt }, state.queue.filter((q) => q.userId === user.id)));
}

/**
 * Brings the copy up to date with only what changed (stock, prices, balances) — small enough to ask every few seconds,
 * so a collection or a sale on another device shows here before the connection drops. Falls back to a full download.
 */
let changesInFlight: Promise<void> | null = null;
let changesAt = 0;

export function applyOfflineChanges(user: CurrentUser): Promise<void> {
  // One request at a time, and none within 2 s of the last: opening a screen, returning to the tab and the timer often
  // asked together (measured: the same request twice within 3 s).
  if (changesInFlight) return changesInFlight;
  if (Date.now() - changesAt < 2000) return Promise.resolve();
  changesAt = Date.now();
  changesInFlight = fetchChanges(user).finally(() => { changesInFlight = null; });
  return changesInFlight;
}

async function fetchChanges(user: CurrentUser) {
  const current = state.snapshot;
  if (!current?.cursor || current.user.id !== user.id) return refreshOfflineData(user);
  const changes = await salesApi.offlineChanges(current.cursor);
  const snapshot = state.snapshot;
  if (snapshot !== current) return; // replaced while we were asking
  const nothingChanged = changes.products.length === 0 && changes.removedProductIds.length === 0
    && changes.customers.length === 0 && changes.removedCustomerIds.length === 0;
  if (nothingChanged && changes.maxHours === snapshot.maxHours) {
    // Only the cursor and the time move; the lists stay as they are (no re-render of every screen that reads them).
    await saveSnapshot({ ...snapshot, generatedAt: changes.generatedAt, cursor: changes.cursor });
    return;
  }
  const queued = state.queue.filter((q) => q.userId === user.id);
  const fresh = withQueuedSales({ ...snapshot, products: changes.products }, queued).products;
  await saveSnapshot({
    ...snapshot,
    generatedAt: changes.generatedAt,
    maxHours: changes.maxHours,
    cursor: changes.cursor,
    products: merge(snapshot.products, fresh, changes.removedProductIds),
    customers: merge(snapshot.customers, changes.customers, changes.removedCustomerIds),
  });
}

function merge<T extends { id: number }>(rows: T[], changed: T[], removed: number[]): T[] {
  if (changed.length === 0 && removed.length === 0) return rows;
  const byId = new Map(changed.map((r) => [r.id, r]));
  const gone = new Set(removed);
  const kept = rows.filter((r) => !gone.has(r.id)).map((r) => byId.get(r.id) ?? r);
  const known = new Set(rows.map((r) => r.id));
  return [...kept, ...changed.filter((r) => !known.has(r.id))];
}

function withQueuedSales(snapshot: OfflineSnapshot, queue: QueuedSale[]): OfflineSnapshot {
  if (queue.length === 0) return snapshot;
  const sold = new Map<number, number>();
  const factorOf = (productId: number, unitId?: number | null) =>
    unitId ? snapshot.products.find((p) => p.id === productId)?.units?.find((u) => u.id === unitId)?.factor ?? 1 : 1;
  // Stock is kept in the base unit: a queued dozen holds 12 pieces.
  for (const q of queue) for (const l of q.input.lines)
    sold.set(l.productId, (sold.get(l.productId) ?? 0) + l.quantity * factorOf(l.productId, l.productUnitId));
  return { ...snapshot, products: snapshot.products.map((p) => (sold.has(p.id) ? { ...p, quantity: p.quantity - sold.get(p.id)! } : p)) };
}

/** Forgets the copy on this device (logout). Queued sales stay: they are sent when their seller logs in again. */
export const clearOfflineData = () => saveSnapshot(null);

/** Hours since the copy was downloaded, and whether it may still be sold from. */
export function snapshotAge(snapshot: OfflineSnapshot | null, now = Date.now()) {
  if (!snapshot) return { usable: false, hours: Infinity };
  const hours = (now - Date.parse(snapshot.generatedAt)) / 3_600_000;
  return { usable: hours < snapshot.maxHours, hours };
}

/** Each product's text without spaces, worked out once per copy of the product rather than at every letter typed. */
const searchKeys = new WeakMap<OfflineProduct, string>();
function searchKey(p: OfflineProduct): string {
  let key = searchKeys.get(p);
  if (key === undefined) {
    key = p.search.replace(/\s+/g, '');
    searchKeys.set(p, key);
  }
  return key;
}

export function searchProducts(snapshot: OfflineSnapshot, term: string, limit = 8): OfflineProduct[] {
  const raw = term.trim().toLowerCase();
  const words = normalizeArabic(term).split(' ').filter(Boolean);
  if (words.length === 0) return [];
  const isCode = (p: OfflineProduct) => p.sku.toLowerCase() === raw || p.barcode?.toLowerCase() === raw;
  // As on the server: the words are looked up in the product's text with its spaces removed; an exact code first, then newest.
  const found = snapshot.products.filter((p) => {
    if (isCode(p)) return true;
    const key = searchKey(p);
    return words.every((w) => key.includes(w));
  });
  return found.sort((a, b) => Number(isCode(b)) - Number(isCode(a)) || b.id - a.id).slice(0, limit);
}

export function findByCode(snapshot: OfflineSnapshot, code: string): OfflineProduct | undefined {
  const c = code.trim().toLowerCase();
  return snapshot.products.find((p) => p.sku.toLowerCase() === c || p.barcode?.toLowerCase() === c);
}

export function searchCustomers(snapshot: OfflineSnapshot, term: string, limit = 20): OfflineCustomer[] {
  const words = normalizeArabic(term).split(' ').filter(Boolean);
  const digits = term.replace(/\D/g, '').replace(/^0/, '');
  return snapshot.customers
    .filter((c) => (digits.length >= 4 && c.phone?.includes(digits)) || (words.length > 0 && words.every((w) => c.search.includes(w))) || c.code === term.trim())
    .slice(0, limit);
}

/** Keeps a sale made without internet until it can be sent. Its goods come off the device's stock at once. */
export async function queueSale(sale: Omit<QueuedSale, 'number' | 'createdAt'>): Promise<QueuedSale> {
  const queued: QueuedSale = { ...sale, number: `OFF-${Date.now().toString(36).toUpperCase().slice(-6)}`, createdAt: new Date().toISOString() };
  await saveQueue([...state.queue.filter((q) => q.key !== sale.key), queued]);
  if (state.snapshot) await saveSnapshot(withQueuedSales(state.snapshot, [queued]));
  return queued;
}

let syncing: Promise<number> | null = null;

/**
 * Sends this user's queued sales, oldest first. Stops at the first connection problem (the rest wait for the next try);
 * a sale the server refuses keeps its reason and waits for a decision. Resolves with how many were sent.
 */
export function syncQueue(userId: number): Promise<number> {
  syncing ??= (async () => {
    let sent = 0;
    try {
      for (const q of state.queue.filter((x) => x.userId === userId && !x.error)) {
        try {
          await salesApi.create(q.input);
          await saveQueue(state.queue.filter((x) => x.key !== q.key));
          sent++;
        } catch (e) {
          if (!(e instanceof ApiError) || e.isNetwork || e.status === 401 || e.status >= 500) break;
          await saveQueue(state.queue.map((x) => (x.key === q.key ? { ...x, error: e.message } : x)));
        }
      }
    } finally {
      syncing = null;
    }
    return sent;
  })();
  return syncing;
}

export async function retryQueued(key: string) {
  await saveQueue(state.queue.map((x) => (x.key === key ? { ...x, error: undefined } : x)));
}

export async function discardQueued(key: string) {
  await saveQueue(state.queue.filter((x) => x.key !== key));
}
