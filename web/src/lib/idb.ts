// A tiny key-value store on IndexedDB: room for thousands of products, unlike localStorage, and it survives reloads.

const DB_NAME = 'qwader';
const STORE = 'kv';

let opening: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
  opening ??= new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => {
      opening = null;
      reject(req.error);
    };
  });
  return opening;
}

async function run<T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest): Promise<T> {
  const db = await open();
  return new Promise<T>((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const req = action(tx.objectStore(STORE));
    tx.oncomplete = () => resolve(req.result as T);
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

export const idb = {
  get: <T>(key: string) => run<T | undefined>('readonly', (s) => s.get(key)),
  set: (key: string, value: unknown) => run<void>('readwrite', (s) => s.put(value, key)),
  del: (key: string) => run<void>('readwrite', (s) => s.delete(key)),
};
