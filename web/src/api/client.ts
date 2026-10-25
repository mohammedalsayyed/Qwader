// Thin fetch wrapper: attaches the in-memory access token, refreshes it once on 401,
// and turns every failure into an ApiError with an Arabic message.

export type FieldErrors = Record<string, string[]>;

export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors?: FieldErrors;

  constructor(status: number, message: string, fieldErrors?: FieldErrors) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
  }

  get isNetwork() {
    return this.status === 0;
  }
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: CurrentUser;
}

export interface CurrentUser {
  id: number;
  userName: string;
  fullName: string;
  roles: string[];
  permissions: string[];
  mustChangePassword: boolean;
}

type SessionListener = (session: AuthResponse | null) => void;

let accessToken: string | null = null;
let refreshInFlight: Promise<AuthResponse | null> | null = null;
const listeners = new Set<SessionListener>();

export const session = {
  set(response: AuthResponse | null) {
    accessToken = response?.accessToken ?? null;
    listeners.forEach((l) => l(response));
  },
  subscribe(listener: SessionListener) {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};

const NETWORK_MESSAGE = 'تعذر الاتصال بالخادم، تحقق من اتصال الإنترنت وحاول مجددًا';

async function postRefresh(): Promise<Response> {
  return fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin' });
}

/** Single-flight refresh shared by all concurrent requests. Resolves null when the session has ended. */
export function refreshSession(): Promise<AuthResponse | null> {
  refreshInFlight ??= (async () => {
    try {
      let res = await postRefresh();
      if (res.status === 401) {
        // Another tab may have just rotated the cookie; one short retry picks up the new one.
        await new Promise((r) => setTimeout(r, 400));
        res = await postRefresh();
      }
      if (!res.ok) {
        session.set(null);
        return null;
      }
      const data = (await res.json()) as AuthResponse;
      session.set(data);
      return data;
    } catch {
      throw new ApiError(0, NETWORK_MESSAGE);
    } finally {
      refreshInFlight = null;
    }
  })();
  return refreshInFlight;
}

type Query = Record<string, string | number | boolean | null | undefined>;

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  query?: Query;
  signal?: AbortSignal;
  headers?: Record<string, string>;
}

function buildUrl(path: string, query?: Query) {
  const url = `/api${path}`;
  if (!query) return url;
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== '') params.set(k, String(v));
  }
  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

async function toError(res: Response): Promise<ApiError> {
  let title: string | undefined;
  let errors: FieldErrors | undefined;
  try {
    const body = await res.json();
    title = body?.title;
    errors = body?.errors;
  } catch {
    /* non-JSON body */
  }
  const fallback =
    res.status === 403
      ? 'ليست لديك صلاحية لتنفيذ هذه العملية'
      : res.status === 404
        ? 'العنصر المطلوب غير موجود'
        : res.status >= 500
          ? 'حدث خطأ في الخادم، حاول مرة أخرى'
          : 'تعذر تنفيذ الطلب';
  return new ApiError(res.status, title ?? fallback, errors);
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const send = () => {
    const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    return fetch(buildUrl(path, options.query), {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
      credentials: 'same-origin',
    });
  };

  let res: Response;
  try {
    res = await send();
    if (res.status === 401 && accessToken) {
      const refreshed = await refreshSession();
      if (refreshed) res = await send();
    }
  } catch (e) {
    if (e instanceof ApiError) throw e;
    if (e instanceof DOMException && e.name === 'AbortError') throw e;
    throw new ApiError(0, NETWORK_MESSAGE);
  }

  if (!res.ok) throw await toError(res);
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Sends multipart form data (file uploads) with the same auth/refresh/error handling as `api`. */
export async function apiUpload<T>(path: string, form: FormData, query?: Query): Promise<T> {
  const send = () =>
    fetch(buildUrl(path, query), {
      method: 'POST',
      headers: accessToken ? { Authorization: `Bearer ${accessToken}`, Accept: 'application/json' } : { Accept: 'application/json' },
      body: form,
      credentials: 'same-origin',
    });
  let res: Response;
  try {
    res = await send();
    if (res.status === 401 && accessToken && (await refreshSession())) res = await send();
  } catch (e) {
    if (e instanceof ApiError) throw e;
    throw new ApiError(0, NETWORK_MESSAGE);
  }
  if (!res.ok) throw await toError(res);
  return (await res.json()) as T;
}

/** Downloads a file response (Excel exports) and saves it under the server-provided name. */
export async function apiDownload(path: string, query?: Query, fallbackName = 'download'): Promise<void> {
  const send = () =>
    fetch(buildUrl(path, query), {
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {},
      credentials: 'same-origin',
    });
  let res: Response;
  try {
    res = await send();
    if (res.status === 401 && accessToken && (await refreshSession())) res = await send();
  } catch (e) {
    if (e instanceof ApiError) throw e;
    throw new ApiError(0, NETWORK_MESSAGE);
  }
  if (!res.ok) throw await toError(res);
  const disposition = res.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;
  const url = URL.createObjectURL(await res.blob());
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Loads a protected file (transfer proof, attachment) with the auth header and returns an object URL for <img>/<a>. */
export async function apiBlobUrl(path: string): Promise<string> {
  const send = () => fetch(buildUrl(path), { headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {}, credentials: 'same-origin' });
  let res: Response;
  try {
    res = await send();
    if (res.status === 401 && accessToken && (await refreshSession())) res = await send();
  } catch {
    throw new ApiError(0, NETWORK_MESSAGE);
  }
  if (!res.ok) throw await toError(res);
  return URL.createObjectURL(await res.blob());
}
