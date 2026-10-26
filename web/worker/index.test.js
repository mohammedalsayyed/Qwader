import { describe, expect, it, vi } from 'vitest';
import worker from './index.js';

const env = { ASSETS: { fetch: vi.fn(async () => new Response('app')) }, API_ORIGIN: 'http://api.example', PROXY_SECRET: 's' };

describe('worker', () => {
  it('redirects plain http to https, keeping the path, query and method (308)', async () => {
    const res = await worker.fetch(new Request('http://qwader.example/sales/5?x=1', { method: 'POST', body: '{}' }), env);
    expect(res.status).toBe(308);
    expect(res.headers.get('Location')).toBe('https://qwader.example/sales/5?x=1');
  });

  it('serves the app over https', async () => {
    const res = await worker.fetch(new Request('https://qwader.example/pos'), env);
    expect(await res.text()).toBe('app');
  });

  it('leaves local development on http alone', async () => {
    const res = await worker.fetch(new Request('http://localhost:8787/'), env);
    expect(res.status).toBe(200);
  });
});
