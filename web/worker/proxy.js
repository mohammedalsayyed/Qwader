// Forwards /api/* to the ASP.NET backend so the browser talks to a single HTTPS origin. One origin keeps
// the httpOnly refresh cookie first-party (SameSite=Strict works, and Safari's tracking prevention does not block it).

const HOP_BY_HOP = ['connection', 'keep-alive', 'transfer-encoding', 'upgrade', 'host'];

export async function proxyToApi(request, env) {
  const origin = (env.API_ORIGIN || 'http://qwader.runasp.net').replace(/\/+$/, '');
  const incoming = new URL(request.url);
  const target = origin + incoming.pathname + incoming.search;

  const headers = new Headers(request.headers);
  for (const h of HOP_BY_HOP) headers.delete(h);
  // The backend rate-limits logins per client IP and marks cookies Secure for HTTPS clients.
  const clientIp = request.headers.get('CF-Connecting-IP');
  if (clientIp) headers.set('X-Forwarded-For', clientIp);
  headers.set('X-Forwarded-Proto', 'https');
  headers.set('X-Forwarded-Host', incoming.host);

  let response;
  try {
    response = await fetch(target, {
      method: request.method,
      headers,
      redirect: 'manual',
      body: ['GET', 'HEAD'].includes(request.method) ? undefined : request.body,
    });
  } catch {
    return new Response(JSON.stringify({ title: 'تعذر الوصول إلى الخادم، حاول بعد قليل', status: 502 }), {
      status: 502,
      headers: { 'Content-Type': 'application/problem+json; charset=utf-8' },
    });
  }

  // Pass the response through unchanged (including Set-Cookie for the refresh token).
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers: response.headers });
}
