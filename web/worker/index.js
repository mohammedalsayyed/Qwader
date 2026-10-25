// Cloudflare Worker entry: /api/* goes to the backend; everything else is the built React app
// (static assets, with index.html as the single-page-app fallback).
import { proxyToApi } from './proxy.js';

export default {
  async fetch(request, env) {
    const { pathname } = new URL(request.url);
    if (pathname === '/api' || pathname.startsWith('/api/')) return proxyToApi(request, env);
    return env.ASSETS.fetch(request);
  },
};
