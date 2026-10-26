// Cloudflare Worker entry: /api/* goes to the backend; everything else is the built React app
// (static assets, with index.html as the single-page-app fallback).
import { proxyToApi } from './proxy.js';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    // Never serve the app or the API over plain http: passwords and the session cookie must stay encrypted.
    // 308 keeps the method and body, so an http POST is repeated over https rather than turned into a GET.
    if (url.protocol === 'http:' && url.hostname !== 'localhost' && url.hostname !== '127.0.0.1') {
      url.protocol = 'https:';
      return Response.redirect(url.toString(), 308);
    }
    const { pathname } = url;
    if (pathname === '/api' || pathname.startsWith('/api/')) return proxyToApi(request, env);
    return env.ASSETS.fetch(request);
  },
};
